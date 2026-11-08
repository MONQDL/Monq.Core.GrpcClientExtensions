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
    /// Propagation copies the deadline and the cancellation token of the current server call
    /// onto outgoing calls. Applied after the default <c>SuppressContextNotFoundErrors = true</c>
    /// on the same options instance, so properties it sets win; unset properties keep the default.
    /// Ignored when <see cref="EnableContextPropagation"/> is <see langword="false"/>.
    /// </summary>
    public Action<GrpcContextPropagationOptions>? ContextPropagationOptionsAction { get; set; }

    /// <summary>
    /// Enables call context propagation: outgoing calls inherit the deadline and cancellation token
    /// of the current server call and are cancelled together with the incoming request. Matches the
    /// Monq.Core.BasicDotNetMicroservice default. Set to <see langword="false"/> for technical
    /// clients (configuration or reference-data lookups) that must survive the caller.
    /// </summary>
    /// <value>
    /// The default <see langword="true"/> keeps propagation enabled; <see langword="false"/> disables it.
    /// </value>
    public bool EnableContextPropagation { get; set; } = true;
}
