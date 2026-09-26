using Hulaki.Ntfy;
using Hulaki.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hulaki.Samples;

// README: "With dependency injection".
internal static class DependencyInjectionSample
{
    public static IServiceCollection Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHulaki()
            .AddTelegram("alerts", configuration.GetSection("Hulaki:Channels:alerts"))
            .AddNtfy("ops", o => o.BaseAddress = new Uri("https://ntfy.sh/"));
        return services;
    }
}
