using Grpc.Net.Client.Balancer;
using Microsoft.Extensions.Options;

namespace Monq.Core.GrpcClientExtensions;

/// <summary>
/// A <see cref="ResolverFactory"/> wrapper that applies the DNS refresh interval from
/// <see cref="GrpcServiceDiscoveryOptions"/> to the built-in grpc-dotnet DNS resolver.
/// </summary>
sealed class ConfiguredDnsResolverFactory : ResolverFactory
{
    readonly DnsResolverFactory _factory;

    public ConfiguredDnsResolverFactory(IOptions<GrpcServiceDiscoveryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _factory = new(options.Value.DnsRefreshInterval);
    }

    public override string Name => _factory.Name;

    public override Resolver Create(ResolverOptions options) => _factory.Create(options);
}
