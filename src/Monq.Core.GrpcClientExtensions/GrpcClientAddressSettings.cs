using Monq.Core.MicroserviceAddressing.Addressing;

namespace Monq.Core.GrpcClientExtensions;

/// <summary>
/// Describes how to resolve the address of a gRPC service by application name.
/// </summary>
/// <param name="ApplicationName">
/// The Kubernetes application (service) name to resolve, for example <c>sample-api-service</c>.
/// </param>
/// <param name="Port">
/// An explicit port to resolve. When <see langword="null"/>, the port is chosen by the resolver.
/// </param>
/// <param name="AddressResolver">
/// A resolver instance that takes precedence over the host-level resolver for this client.
/// </param>
public sealed record GrpcClientAddressSettings(
    string ApplicationName,
    int? Port = null,
    IMicroserviceAddressResolver? AddressResolver = null);
