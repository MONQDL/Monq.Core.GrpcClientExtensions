using Grpc.AspNetCore.ClientFactory;
using Grpc.Net.Client;
using Grpc.Net.ClientFactory;

namespace Monq.Core.GrpcClientExtensions;

/// <summary>
/// Options used to configure a gRPC client.
/// </summary>
public sealed class GrpcClientOptions
{
    /// <summary>
    /// A delegate that is used to configure a <see cref="GrpcClientFactoryOptions"/>.
    /// </summary>
    public Action<GrpcClientFactoryOptions>? ClientOptionsAction { get; set; }

    /// <summary>
    /// A delegate that is used to configure a <see cref="GrpcChannelOptions"/>.
    /// Applied in addition to the automatic credentials and load-balancing settings.
    /// </summary>
    public Action<GrpcChannelOptions>? ChannelOptionsAction { get; set; }

    /// <summary>
    /// A delegate that is used to configure a <see cref="GrpcContextPropagationOptions"/>.
    /// </summary>
    /// <value>If <see langword="null"/>, then call context propagation is disabled.</value>
    public Action<GrpcContextPropagationOptions>? ContextPropagationOptionsAction { get; set; }
}
