using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Configuration;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Monq.Core.GrpcClientExtensions;
using Monq.Core.HttpClientExtensions;
using Monq.Core.MicroserviceAddressing.Addressing;
using Monq.Core.MicroserviceAddressing.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering gRPC clients that resolve their addresses through
/// <see cref="IMicroserviceAddressProvider"/>. Registration applies the behavior of the legacy
/// pre-configured clients from Monq.Core.BasicDotNetMicroservice: scheme-aware channel credentials,
/// round-robin balancing for load-balanced (Service Discovery / <c>dns</c>) addresses, userspace and
/// culture header propagation, insecure-channel call credentials, a 51 MB receive limit, call
/// context (event id) propagation, and <c>Authorization</c> forwarding from the current HTTP
/// request (with a static token fallback for console clients). Channel defaults are applied before
/// user-provided actions, so every default can be overridden through <see cref="GrpcClientOptions"/>.
/// </summary>
public static class MonqGrpcClientServiceCollectionExtensions
{
    /// <summary>
    /// Adds a gRPC client whose address is resolved by application name on the first client creation.
    /// </summary>
    public static IHttpClientBuilder AddMonqGrpcClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        this IServiceCollection services,
        string applicationName,
        int? port = null,
        IMicroserviceAddressResolver? addressResolver = null,
        GrpcClientOptions? options = null)
        where TClient : class =>
        services.AddMonqGrpcClientCore<TClient>(
            name: null,
            _ => new GrpcClientAddressSettings(applicationName, port, addressResolver),
            options,
            consoleClient: false);

    /// <summary>
    /// Adds a named gRPC client whose address is resolved by application name on the first client
    /// creation. Use named registrations when a service registers several clients of the same type.
    /// </summary>
    public static IHttpClientBuilder AddMonqGrpcClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        this IServiceCollection services,
        string name,
        string applicationName,
        int? port = null,
        IMicroserviceAddressResolver? addressResolver = null,
        GrpcClientOptions? options = null)
        where TClient : class =>
        services.AddMonqGrpcClientCore<TClient>(
            name,
            _ => new GrpcClientAddressSettings(applicationName, port, addressResolver),
            options,
            consoleClient: false);

    /// <summary>
    /// Adds a named gRPC client for composed libraries: the settings are resolved lazily from the
    /// service provider, so registration order does not matter.
    /// </summary>
    public static IHttpClientBuilder AddMonqGrpcClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, GrpcClientAddressSettings> settingsFactory,
        GrpcClientOptions? options = null)
        where TClient : class =>
        services.AddMonqGrpcClientCore<TClient>(
            name, settingsFactory, options, consoleClient: false);

    /// <summary>
    /// Adds a gRPC client with a fixed address. The resolvers are not consulted; use it for local
    /// debugging. A <c>dns</c> scheme still enables DNS resolution and round-robin balancing.
    /// </summary>
    public static IHttpClientBuilder AddMonqGrpcClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        this IServiceCollection services,
        Uri address,
        EndpointResolutionMode mode = EndpointResolutionMode.Direct,
        GrpcClientOptions? options = null)
        where TClient : class =>
        services.AddMonqGrpcClientCore<TClient>(
            name: null,
            _ => new ResolvedMicroserviceAddress(address, mode),
            options,
            consoleClient: false);

    /// <summary>
    /// Adds a named gRPC client with a fixed address. See the overload without a name.
    /// </summary>
    public static IHttpClientBuilder AddMonqGrpcClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        this IServiceCollection services,
        string name,
        Uri address,
        EndpointResolutionMode mode = EndpointResolutionMode.Direct,
        GrpcClientOptions? options = null)
        where TClient : class =>
        services.AddMonqGrpcClientCore<TClient>(
            name,
            _ => new ResolvedMicroserviceAddress(address, mode),
            options,
            consoleClient: false);

    /// <summary>
    /// Adds a console/background gRPC client whose address is resolved by application name on the
    /// first client creation. The client forwards the incoming <c>Authorization</c> header and
    /// falls back to a static token via <see cref="RestHttpClient"/> when there is no HTTP context.
    /// </summary>
    public static IHttpClientBuilder AddMonqGrpcConsoleClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        this IServiceCollection services,
        string applicationName,
        int? port = null,
        IMicroserviceAddressResolver? addressResolver = null,
        GrpcClientOptions? options = null)
        where TClient : class =>
        services.AddMonqGrpcClientCore<TClient>(
            name: null,
            _ => new GrpcClientAddressSettings(applicationName, port, addressResolver),
            options,
            consoleClient: true);

    /// <summary>
    /// Adds a named console/background gRPC client. See the overload without a name.
    /// </summary>
    public static IHttpClientBuilder AddMonqGrpcConsoleClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        this IServiceCollection services,
        string name,
        string applicationName,
        int? port = null,
        IMicroserviceAddressResolver? addressResolver = null,
        GrpcClientOptions? options = null)
        where TClient : class =>
        services.AddMonqGrpcClientCore<TClient>(
            name,
            _ => new GrpcClientAddressSettings(applicationName, port, addressResolver),
            options,
            consoleClient: true);

    /// <summary>
    /// Adds a named console/background gRPC client for composed libraries: the settings are resolved
    /// lazily from the service provider. See the console overload by application name.
    /// </summary>
    public static IHttpClientBuilder AddMonqGrpcConsoleClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, GrpcClientAddressSettings> settingsFactory,
        GrpcClientOptions? options = null)
        where TClient : class =>
        services.AddMonqGrpcClientCore<TClient>(
            name, settingsFactory, options, consoleClient: true);

    /// <summary>
    /// Adds a console/background gRPC client with a fixed address. See the console overload
    /// by application name for the credentials behavior.
    /// </summary>
    public static IHttpClientBuilder AddMonqGrpcConsoleClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        this IServiceCollection services,
        Uri address,
        EndpointResolutionMode mode = EndpointResolutionMode.Direct,
        GrpcClientOptions? options = null)
        where TClient : class =>
        services.AddMonqGrpcClientCore<TClient>(
            name: null,
            _ => new ResolvedMicroserviceAddress(address, mode),
            options,
            consoleClient: true);

    /// <summary>
    /// Adds a named console/background gRPC client with a fixed address.
    /// </summary>
    public static IHttpClientBuilder AddMonqGrpcConsoleClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        this IServiceCollection services,
        string name,
        Uri address,
        EndpointResolutionMode mode = EndpointResolutionMode.Direct,
        GrpcClientOptions? options = null)
        where TClient : class =>
        services.AddMonqGrpcClientCore<TClient>(
            name,
            _ => new ResolvedMicroserviceAddress(address, mode),
            options,
            consoleClient: true);

    /// <summary>
    /// Configures the shared DNS refresh interval used by all gRPC clients resolved in
    /// Service Discovery mode.
    /// </summary>
    public static IServiceCollection ConfigureMonqGrpcServiceDiscovery(
        this IServiceCollection services,
        Action<GrpcServiceDiscoveryOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);

        return services;
    }

    static IHttpClientBuilder AddMonqGrpcClientCore<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        this IServiceCollection services,
        string? name,
        Func<IServiceProvider, GrpcClientAddressSettings> settingsFactory,        GrpcClientOptions? options,
        bool consoleClient)
        where TClient : class =>
        services.AddMonqGrpcClientCore<TClient>(
            name,
            provider => ResolveAddress(provider, settingsFactory(provider)),
            options,            consoleClient);

    static IHttpClientBuilder AddMonqGrpcClientCore<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
        this IServiceCollection services,
        string? name,
        Func<IServiceProvider, ResolvedMicroserviceAddress> addressFactory,
        GrpcClientOptions? options,        bool consoleClient)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(addressFactory);

        services.AddMicroserviceAddressing();

        // Registered at registration time (before the provider is built) for every client:
        // name-based addresses are only known to use Service Discovery at resolution time, and
        // the factory replaces just the DNS refresh interval, so registering it unconditionally
        // is harmless for hosts that never resolve dns addresses.
        EnsureServiceDiscovery(services);

        if (consoleClient)
            services.AddHttpClient<RestHttpClient>();

        ResolvedMicroserviceAddress? resolvedAddress = null;

        void ConfigureFactoryOptions(IServiceProvider provider, GrpcClientFactoryOptions factoryOptions)
        {
            options?.ClientOptionsAction?.Invoke(factoryOptions);

            factoryOptions.Address = (resolvedAddress = addressFactory(provider)).Address;
        }

        var builder = string.IsNullOrWhiteSpace(name)
            ? services.AddGrpcClient<TClient>(ConfigureFactoryOptions)
            : services.AddGrpcClient<TClient>(name, ConfigureFactoryOptions);

        // Header propagation was part of the legacy pre-configured clients
        // (AdditionalHeadersInterceptor in Monq.Core.BasicDotNetMicroservice); consumers that
        // register through this library directly must keep receiving it, otherwise services like
        // MyLimits reject the call with "Header x-smon-userspace-id not defined".
        builder = builder.AddInterceptor(static provider =>
            new MonqHeaderPropagationInterceptor(provider.GetService<IHttpContextAccessor>()));

        // Defaults of the legacy Monq.Core.BasicDotNetMicroservice pre-configured clients. They are
        // applied before user-provided actions, so every default can be overridden per client.
        builder = builder.ConfigureChannel(static channelOptions =>
        {
            channelOptions.UnsafeUseInsecureChannelCallCredentials = true;
            channelOptions.MaxReceiveMessageSize = 51 * 1024 * 1024;
        });

        if (options?.ChannelOptionsAction is not null)
            builder = builder.ConfigureChannel(options.ChannelOptionsAction);

        // Interactive clients forward the incoming Authorization header (legacy pre-configured
        // behavior); console clients forward it with a static token fallback.
        if (consoleClient)
            builder = builder.AddConsoleCallCredentials();
        else
            builder = builder.AddCallCredentials(static (context, metadata, provider) =>
            {
                var httpContext = provider.GetService<IHttpContextAccessor>()?.HttpContext;

                if (httpContext?.Request.Headers
                        .TryGetValue(HttpRequestHeader.Authorization.ToString(), out var token) == true
                    && !string.IsNullOrEmpty(token))
                    metadata.Add(HttpRequestHeader.Authorization.ToString(), token!);

                return Task.CompletedTask;
            });

        builder.ConfigureChannel(channelOptions =>
        {
            if (resolvedAddress is null)
                return;

            ConfigureChannelCredentials(channelOptions, resolvedAddress.Address);

            // Round-robin follows the Monq.Core.MicroserviceAddressing contract for gRPC clients
            // (IMicroserviceAddressProvider): load balancing is enabled for Service Discovery mode
            // and for any dns-scheme address. A headless Service exposes pods as A-records and
            // grpc-dotnet defaults to pick_first, which would use only the first one. Direct
            // (ClusterIP) addresses keep kube-proxy balancing, so no service config is applied.
            if (UsesServiceDiscovery(resolvedAddress))
            {
                channelOptions.ServiceConfig = new ServiceConfig
                {
                    LoadBalancingConfigs =
                    {
                        new RoundRobinConfig(),
                    },
                };
            }
        });

        // Event id (call context) propagation was enabled by the legacy pre-configured clients.
        builder = builder.EnableCallContextPropagation(static propagationOptions =>
            propagationOptions.SuppressContextNotFoundErrors = true);

        if (options?.ContextPropagationOptionsAction is not null)
            builder = builder.EnableCallContextPropagation(options.ContextPropagationOptionsAction);

        return builder;
    }

    static ResolvedMicroserviceAddress ResolveAddress(
        IServiceProvider provider,
        GrpcClientAddressSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings.ApplicationName);

        return provider
            .GetRequiredService<IMicroserviceAddressProvider>()
            .Resolve(
                settings.ApplicationName,
                MicroserviceProtocol.Grpc,
                settings.Port,
                settings.AddressResolver);
    }

    static bool UsesServiceDiscovery(ResolvedMicroserviceAddress resolvedAddress) =>
        resolvedAddress.ResolutionMode == EndpointResolutionMode.ServiceDiscovery
        || resolvedAddress.Address.Scheme.Equals("dns", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// grpc-dotnet requires explicit channel credentials for every address scheme other than
    /// http/https: dns:/// (Service Discovery), but also ipv4:///, unix:/ and any custom resolver
    /// scheme. Checking only "dns" would leave such addresses to fail channel creation with
    /// "Credentials must be specified". In-cluster endpoints work without TLS, so Insecure is used.
    /// </summary>
    static void ConfigureChannelCredentials(GrpcChannelOptions channelOptions, Uri address)
    {
        if (!address.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            && !address.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            channelOptions.Credentials = ChannelCredentials.Insecure;
        }
    }

    static void EnsureServiceDiscovery(IServiceCollection services)
    {
        services
            .AddOptions<GrpcServiceDiscoveryOptions>()
            .Validate(
                options => options.DnsRefreshInterval == Timeout.InfiniteTimeSpan
                    || options.DnsRefreshInterval > TimeSpan.Zero,
                $"{nameof(GrpcServiceDiscoveryOptions.DnsRefreshInterval)} must be positive or Timeout.InfiniteTimeSpan.");

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ResolverFactory, ConfiguredDnsResolverFactory>());
    }
}

static class MonqGrpcClientConsoleCredentialsExtensions
{
    /// <summary>
    /// Forwards the incoming <c>Authorization</c> header; when there is no HTTP context, obtains
    /// a token through <see cref="RestHttpClient"/> (the static authentication endpoint must be
    /// configured on the host).
    /// </summary>
    public static IHttpClientBuilder AddConsoleCallCredentials(this IHttpClientBuilder builder) =>
        builder
            .ConfigureChannel(channelOptions =>
                channelOptions.UnsafeUseInsecureChannelCallCredentials = true)
            .AddCallCredentials(async (context, metadata, provider) =>
            {
                var httpContextAccessor = provider.GetService<IHttpContextAccessor>();

                if (httpContextAccessor?.HttpContext?.Request.Headers
                        .TryGetValue(HttpRequestHeader.Authorization.ToString(), out var token) == true
                    && !string.IsNullOrEmpty(token))
                {
                    metadata.Add(HttpRequestHeader.Authorization.ToString(), token!);
                    return;
                }

                var restHttpClient = provider.GetService<RestHttpClient>();

                if (restHttpClient is null)
                    return;

                var tokenResponse = await restHttpClient.GetAccessToken(false);

                if (tokenResponse is null)
                    return;

                const string scheme = "Bearer";
                var authorizationHeaderValue = new AuthenticationHeaderValue(scheme, tokenResponse.AccessToken);
                metadata.Add(HttpRequestHeader.Authorization.ToString(), authorizationHeaderValue.ToString());
            });
}
