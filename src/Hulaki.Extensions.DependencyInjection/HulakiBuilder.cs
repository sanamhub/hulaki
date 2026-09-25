using Microsoft.Extensions.DependencyInjection;

namespace Hulaki.Extensions.DependencyInjection;

internal sealed class HulakiBuilder(IServiceCollection services) : IHulakiBuilder
{
    public IServiceCollection Services { get; } = services;
}
