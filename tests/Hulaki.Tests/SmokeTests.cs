using Xunit;

namespace Hulaki.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Core_assembly_is_named_hulaki()
    {
        Assert.Equal("Hulaki", typeof(Hulaki.Message).Assembly.GetName().Name);
    }
}
