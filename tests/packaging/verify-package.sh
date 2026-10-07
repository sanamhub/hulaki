#!/usr/bin/env bash
# Consumes the built packages the way a real user would, from a clean project with no reference
# to this repository's source, optionally under Native AOT.
#
# This catches a package that builds, tests and installs cleanly and then fails at the consumer's
# first call, and trim or AOT warnings that only a consumer sees. Adapted from
# sanamhub/ada-csharp tests/packaging/verify-package.sh.
set -euo pipefail

PACKAGE_DIR=""
AOT="false"

while [ $# -gt 0 ]; do
  case "$1" in
    --package-dir) PACKAGE_DIR="$2"; shift 2 ;;
    --aot)         AOT="true";       shift 1 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

[ -n "$PACKAGE_DIR" ] || { echo "--package-dir is required" >&2; exit 2; }
PACKAGE_DIR="$(cd "$PACKAGE_DIR" && pwd)"

# The core package's file name has the version digit right after "Hulaki.". Hulaki.Email and
# Hulaki.Testing do not, so this glob finds the core alone.
PACKAGES=("$PACKAGE_DIR"/Hulaki.[0-9]*.nupkg)
if [ ! -f "${PACKAGES[0]}" ]; then
  echo "no Hulaki package found in $PACKAGE_DIR" >&2
  exit 1
fi
VERSION="$(basename "${PACKAGES[0]}" .nupkg)"
VERSION="${VERSION#Hulaki.}"

# Under Git Bash, pwd returns an MSYS path that .NET cannot read.
NATIVE_PACKAGE_DIR="$PACKAGE_DIR"
if command -v cygpath >/dev/null 2>&1; then
  NATIVE_PACKAGE_DIR="$(cygpath -w "$PACKAGE_DIR")"
fi

echo "consuming Hulaki $VERSION from $NATIVE_PACKAGE_DIR (aot=$AOT)"

# FreeTierMail, which Hulaki.Email depends on, comes from the repository's packages-local/ until
# it is on nuget.org.
FREETIERMAIL_DIR="$(cd "$(dirname "$0")/../../packages-local" && pwd)"
if command -v cygpath >/dev/null 2>&1; then FREETIERMAIL_DIR="$(cygpath -w "$FREETIERMAIL_DIR")"; fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
cd "$WORK"

# Our packages can only come from the local folder, everything else only from nuget.org. A
# local-only feed would starve Native AOT, which restores the ILCompiler packages from nuget.org.
cat > NuGet.Config <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$NATIVE_PACKAGE_DIR" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="Hulaki" />
      <package pattern="Hulaki.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
XML

# Warnings are errors, so a trim or AOT warning (IL2xxx, IL3xxx) from our packages fails here.
cat > consumer.csproj <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <!-- Set explicitly. This project lives in a temp directory on purpose, so it inherits none
         of the repository's Directory.Build.props. -->
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Consumer</RootNamespace>
    <PublishAot>$AOT</PublishAot>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Hulaki" Version="$VERSION" />
  </ItemGroup>
</Project>
XML

cat > Program.cs <<'CS'
using System.Net;
using System.Text;
using System.Text.Json;
using Hulaki;
using Hulaki.Channels;
using Hulaki.Telegram;
using Hulaki.Text;

// Exercises JSON serialisation, markup rendering and grapheme counting, the parts most likely to
// break under trimming.
var handler = new Stub();
var telegram = new TelegramChannel("telegram", new HttpClient(handler),
    new TelegramChannelOptions { BotToken = "123456:TEST-token_0000000000000000000", DisableRateLimiting = true });
var client = new HulakiClient(new IChannel[] { telegram });

var message = new Message("Rain **Red** in <Myagdi>") { Format = TextFormat.Markup, Title = "DHM" };
var result = await client.SendAsync(message, new[] { new Target("telegram", new Recipient("-100123")) });
var outcome = result.Outcomes[0].Outcome;

if (outcome.Status != DeliveryStatus.Delivered) { Console.Error.WriteLine($"FAIL: status was {outcome.Status}"); return 1; }
// System.Text.Json escapes < and > in the wire body, so read the field back rather than search the raw text.
string sent = JsonDocument.Parse(handler.Body).RootElement.GetProperty("text").GetString() ?? "";
if (!sent.Contains("<b>Red</b>", StringComparison.Ordinal)) { Console.Error.WriteLine($"FAIL: text was {sent}"); return 1; }
if (TextCounter.Graphemes.Count("\u0915\u094D\u0937\u093F") != 1) { Console.Error.WriteLine("FAIL: conjunct counted as more than one grapheme"); return 1; }

Console.WriteLine("PASS");
return 0;

sealed class Stub : HttpMessageHandler
{
    public string Body { get; private set; } = "";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Body = await request.Content!.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":42}}", Encoding.UTF8, "application/json"),
        };
    }
}
CS

dotnet restore --verbosity quiet

if [ "$AOT" = "true" ]; then
  dotnet publish -c Release -o out --verbosity quiet
  ./out/consumer
else
  dotnet run -c Release --verbosity quiet
fi

echo "package consumption OK"
