using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Monq.Core.HttpClientExtensions;
using Monq.Core.MicroserviceAddressing.Addressing;

namespace Monq.Core.GrpcClientExtensions.Tests;

public class MonqGrpcClientTests
{
    const string clientName = "test-grpc-client";
    const string applicationName = "sample-api-service";

    static Uri DnsAddress => new("dns:///sample-api-service-grpc.integration.svc.cluster.local:5006");
    static Uri HttpAddress => new("http://sample-api-service.integration.svc.cluster.local:5006");

    [Fact]
    public void AddGrpcPreConfiguredClient_ThrowsWhenAddressingNotConfigured()
    {
        var services = new ServiceCollection();
        services.AddGrpcPreConfiguredClient<TestServiceClient>(clientName, applicationName);

        using var provider = services.BuildServiceProvider();
        var clientFactory = provider.GetRequiredService<GrpcClientFactory>();

        var exception = Assert.Throws<InvalidOperationException>(
            () => clientFactory.CreateClient<TestServiceClient>(clientName));

        Assert.Contains("Monq.Core.MicroserviceAddressing is not configured", exception.Message, StringComparison.Ordinal);
        Assert.Contains(applicationName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_ResolvesAddressLazilyOnFirstCreate()
    {
        var addressProvider = CreateAddressProvider(DnsAddress, EndpointResolutionMode.ServiceDiscovery);

        var services = new ServiceCollection();
        services.AddSingleton(addressProvider.Object);
        services.AddGrpcPreConfiguredClient<TestServiceClient>(
            clientName, applicationName, port: 5006, addressResolver: null);

        using var provider = services.BuildServiceProvider();
        var clientFactory = provider.GetRequiredService<GrpcClientFactory>();

        addressProvider.Verify(
            x => x.Resolve(It.IsAny<string>(), It.IsAny<MicroserviceProtocol>(),
                It.IsAny<int?>(), It.IsAny<IMicroserviceAddressResolver?>()),
            Times.Never);

        var client = clientFactory.CreateClient<TestServiceClient>(clientName);

        Assert.NotNull(client);
        addressProvider.Verify(
            x => x.Resolve(applicationName, MicroserviceProtocol.Grpc, 5006, null),
            Times.Once);
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_SetsInsecureCredentialsForNonHttpScheme()
    {
        var channelOptions = ConfigureChannel(
            services => services.AddGrpcPreConfiguredClient<TestServiceClient>(
                clientName, new Uri("ipv4:///10.42.0.7:5006")));

        Assert.Same(ChannelCredentials.Insecure, channelOptions.Credentials);
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_DoesNotOverrideCredentialsForHttpScheme()
    {
        var channelOptions = ConfigureChannel(
            services => services.AddGrpcPreConfiguredClient<TestServiceClient>(clientName, HttpAddress));

        Assert.Null(channelOptions.Credentials);
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_EnablesRoundRobinForServiceDiscoveryMode()
    {
        var services = new ServiceCollection();
        var addressProvider = CreateAddressProvider(DnsAddress, EndpointResolutionMode.ServiceDiscovery);
        services.AddSingleton(addressProvider.Object);

        services.AddGrpcPreConfiguredClient<TestServiceClient>(clientName, applicationName);

        var channelOptions = CreateChannelOptions(services, clientName);

        Assert.Contains(channelOptions.ServiceConfig?.LoadBalancingConfigs ?? [],
            config => config is RoundRobinConfig);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(Grpc.Net.Client.Balancer.ResolverFactory)
            && descriptor.ImplementationType?.Name == "ConfiguredDnsResolverFactory");
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_EnablesRoundRobinForDnsSchemeInDirectMode()
    {
        var channelOptions = ConfigureChannel(
            services => services.AddGrpcPreConfiguredClient<TestServiceClient>(clientName, DnsAddress));

        Assert.Contains(channelOptions.ServiceConfig?.LoadBalancingConfigs ?? [],
            config => config is RoundRobinConfig);
        Assert.Same(ChannelCredentials.Insecure, channelOptions.Credentials);
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_NoServiceConfigForDirectHttpAddress()
    {
        var channelOptions = ConfigureChannel(
            services => services.AddGrpcPreConfiguredClient<TestServiceClient>(clientName, HttpAddress));

        Assert.Null(channelOptions.ServiceConfig);
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_RegistersDnsResolverFactoryOnceForSeveralClients()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateAddressProvider(DnsAddress).Object);

        services.AddGrpcPreConfiguredClient<TestServiceClient>("client-1", applicationName);
        services.AddGrpcPreConfiguredClient<TestServiceClient>("client-2", applicationName);

        Assert.Equal(1, services.Count(descriptor =>
            descriptor.ServiceType == typeof(Grpc.Net.Client.Balancer.ResolverFactory)));
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_SettingsFactoryResolverTakesPrecedence()
    {
        var customResolver = new FixedMicroserviceAddressResolver(HttpAddress);
        var addressProvider = new Mock<IMicroserviceAddressProvider>();
        addressProvider
            .Setup(provider => provider.Resolve(
                applicationName, MicroserviceProtocol.Grpc, null, customResolver))
            .Returns(new ResolvedMicroserviceAddress(HttpAddress));

        var services = new ServiceCollection();
        services.AddSingleton(addressProvider.Object);
        services.AddGrpcPreConfiguredClient<TestServiceClient>(
            clientName,
            _ => new GrpcClientAddressSettings(applicationName, AddressResolver: customResolver));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<GrpcClientFactory>().CreateClient<TestServiceClient>(clientName);

        addressProvider.VerifyAll();
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_FixedAddressDoesNotConsultAddressProvider()
    {
        var addressProvider = new Mock<IMicroserviceAddressProvider>();

        var channelOptions = ConfigureChannel(
            services =>
            {
                services.AddSingleton(addressProvider.Object);
                services.AddGrpcPreConfiguredClient<TestServiceClient>(clientName, HttpAddress);
            });

        Assert.Null(channelOptions.Credentials);
        addressProvider.Verify(
            x => x.Resolve(It.IsAny<string>(), It.IsAny<MicroserviceProtocol>(),
                It.IsAny<int?>(), It.IsAny<IMicroserviceAddressResolver?>()),
            Times.Never);
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_AppliesUserChannelOptionsWithAutomaticDefaults()
    {
        var options = new GrpcClientOptions
        {
            ChannelOptionsAction = channelOptions => channelOptions.MaxReceiveMessageSize = 1024,
        };

        var channelOptions = ConfigureChannel(
            services => services.AddGrpcPreConfiguredClient<TestServiceClient>(
                clientName, DnsAddress, options: options));

        Assert.Equal(1024, channelOptions.MaxReceiveMessageSize);
        Assert.True(channelOptions.UnsafeUseInsecureChannelCallCredentials);
        Assert.Same(ChannelCredentials.Insecure, channelOptions.Credentials);
    }

    [Fact]
    public void AddGrpcPreConfiguredConsoleClient_RegistersRestHttpClientAndInsecureCallCredentials()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateAddressProvider(HttpAddress).Object);

        services.AddGrpcPreConfiguredConsoleClient<TestServiceClient>(clientName, applicationName);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(RestHttpClient));

        var channelOptions = CreateChannelOptions(services, clientName);

        Assert.True(channelOptions.UnsafeUseInsecureChannelCallCredentials);
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_WithoutName_ResolvesTypedClient()
    {
        var addressProvider = CreateAddressProvider(HttpAddress);

        var services = new ServiceCollection();
        services.AddSingleton(addressProvider.Object);
        services.AddGrpcPreConfiguredClient<TestServiceClient>(applicationName, port: 5006);

        using var provider = services.BuildServiceProvider();
        var clientFactory = provider.GetRequiredService<GrpcClientFactory>();

        // The nameless AddGrpcClient overload registers client options under the type display name.
        var client = clientFactory.CreateClient<TestServiceClient>(nameof(TestServiceClient));

        Assert.NotNull(client);
        addressProvider.Verify(
            x => x.Resolve(applicationName, MicroserviceProtocol.Grpc, 5006, null),
            Times.Once);
    }

    [Fact]
    public void ConfigureGrpcServiceDiscovery_AppliesDnsRefreshInterval()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateAddressProvider(DnsAddress).Object);

        services.ConfigureGrpcServiceDiscovery(
            options => options.DnsRefreshInterval = TimeSpan.FromSeconds(5));
        services.AddGrpcPreConfiguredClient<TestServiceClient>(clientName, applicationName);

        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            TimeSpan.FromSeconds(5),
            provider.GetRequiredService<IOptions<GrpcServiceDiscoveryOptions>>().Value.DnsRefreshInterval);
    }

    [Fact]
    public void ConfigureGrpcServiceDiscovery_InvalidIntervalFailsValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateAddressProvider(DnsAddress).Object);

        services.ConfigureGrpcServiceDiscovery(
            options => options.DnsRefreshInterval = TimeSpan.FromSeconds(-1));
        services.AddGrpcPreConfiguredClient<TestServiceClient>(clientName, applicationName);

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<GrpcServiceDiscoveryOptions>>().Value);
    }

    [Fact]
    public void AddGrpcPreConfiguredConsoleClient_FixedAddressAppliesCredentialsAndCallCredentials()
    {
        var services = new ServiceCollection();
        services.AddGrpcPreConfiguredConsoleClient<TestServiceClient>(clientName, DnsAddress);

        var channelOptions = CreateChannelOptions(services, clientName);

        Assert.Same(ChannelCredentials.Insecure, channelOptions.Credentials);
        Assert.True(channelOptions.UnsafeUseInsecureChannelCallCredentials);
        Assert.Contains(channelOptions.ServiceConfig?.LoadBalancingConfigs ?? [],
            config => config is RoundRobinConfig);
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_AppliesPreConfiguredDefaultsAndOptionsObject()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateAddressProvider(DnsAddress, EndpointResolutionMode.ServiceDiscovery).Object);

        services.AddGrpcPreConfiguredClient<TestServiceClient>(
            clientName,
            applicationName,
            options: new GrpcClientOptions
            {
                ChannelOptionsAction = channelOptions => channelOptions.MaxReceiveMessageSize = 123,
            });

        var channelOptions = CreateChannelOptions(services, clientName);

        Assert.Equal(123L, channelOptions.MaxReceiveMessageSize!.Value);
        Assert.Same(ChannelCredentials.Insecure, channelOptions.Credentials);
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_AppliesLegacyPreConfiguredDefaults()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateAddressProvider(HttpAddress).Object);

        services.AddGrpcPreConfiguredClient<TestServiceClient>(clientName, applicationName);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHttpContextAccessor));

        using var provider = services.BuildServiceProvider();
        var factoryOptions = provider
            .GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>()
            .Get(clientName);

        var channelOptions = new GrpcChannelOptions();

        foreach (var configureChannel in factoryOptions.ChannelOptionsActions)
            configureChannel(channelOptions);

        Assert.True(channelOptions.UnsafeUseInsecureChannelCallCredentials);
        Assert.Equal(51 * 1024 * 1024, channelOptions.MaxReceiveMessageSize!.Value);
        // Call context propagation (deadline/cancellation token) is opt-in, so only the
        // header propagation interceptor is registered by default.
        Assert.Equal(1, factoryOptions.InterceptorRegistrations.Count);
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_ContextPropagationIsOptIn()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateAddressProvider(HttpAddress).Object);

        services.AddGrpcPreConfiguredClient<TestServiceClient>(
            clientName,
            applicationName,
            options: new GrpcClientOptions
            {
                ContextPropagationOptionsAction = propagationOptions =>
                    propagationOptions.SuppressContextNotFoundErrors = true,
            });

        using var provider = services.BuildServiceProvider();
        var factoryOptions = provider
            .GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>()
            .Get(clientName);

        // Header propagation interceptor + explicitly enabled call context propagation interceptor.
        Assert.Equal(2, factoryOptions.InterceptorRegistrations.Count);
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_UserChannelOptionsOverrideDefaults()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateAddressProvider(HttpAddress).Object);

        services.AddGrpcPreConfiguredClient<TestServiceClient>(
            clientName,
            applicationName,
            options: new GrpcClientOptions
            {
                ChannelOptionsAction = channelOptions => channelOptions.MaxReceiveMessageSize = 2048,
            });

        var channelOptions = CreateChannelOptions(services, clientName);

        Assert.Equal(2048L, channelOptions.MaxReceiveMessageSize!.Value);
        Assert.True(channelOptions.UnsafeUseInsecureChannelCallCredentials);
    }

    [Fact]
    public void AddGrpcPreConfiguredClient_RejectsRelativeAddress()
    {
        var services = new ServiceCollection();
        var relativeAddress = new Uri("sample/path", UriKind.Relative);

        Assert.Throws<ArgumentException>(
            () => services.AddGrpcPreConfiguredClient<TestServiceClient>(relativeAddress));
        Assert.Throws<ArgumentException>(
            () => services.AddGrpcPreConfiguredConsoleClient<TestServiceClient>(relativeAddress));
    }

    [Fact]
    public void HeaderPropagation_AddsUserspaceAndCultureFromHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Smon-Userspace-Id"] = "42";
        httpContext.Request.Headers["Accept-Language"] = "ru-RU";

        var interceptor = new HeaderPropagationInterceptor(
            new HttpContextAccessor { HttpContext = httpContext });

        var headers = InterceptUnary(interceptor, new CallOptions());

        Assert.Equal("42", GetHeader(headers, "x-smon-userspace-id"));
        Assert.Equal("ru-RU", GetHeader(headers, "accept-language"));
    }

    [Fact]
    public void HeaderPropagation_KeepsHeadersSetOnCall()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Smon-Userspace-Id"] = "42";

        var interceptor = new HeaderPropagationInterceptor(
            new HttpContextAccessor { HttpContext = httpContext });

        var callOptions = new CallOptions().WithHeaders(new Metadata { { "x-smon-userspace-id", "7" } });

        var headers = InterceptUnary(interceptor, callOptions);

        Assert.NotNull(headers);
        Assert.Single(headers, header => header.Key.Equals("x-smon-userspace-id", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("7", GetHeader(headers, "x-smon-userspace-id"));
    }

    [Fact]
    public void HeaderPropagation_NoHttpContextLeavesCallUntouched()
    {
        var interceptor = new HeaderPropagationInterceptor(httpContextAccessor: null);

        var headers = InterceptUnary(interceptor, new CallOptions());

        Assert.Null(headers);
    }

    static Metadata? InterceptUnary(HeaderPropagationInterceptor interceptor, CallOptions callOptions)
    {
        var marshaller = new Marshaller<string>(
            value => System.Text.Encoding.UTF8.GetBytes(value),
            bytes => System.Text.Encoding.UTF8.GetString(bytes));
        var method = new Method<string, string>(
            MethodType.Unary, "sample.SampleService", "Call", marshaller, marshaller);
        var context = new ClientInterceptorContext<string, string>(method, host: null, callOptions);

        Metadata? captured = null;
        interceptor.BlockingUnaryCall("request", context, (_, modifiedContext) =>
        {
            captured = modifiedContext.Options.Headers;
            return "response";
        });

        return captured;
    }

    static string? GetHeader(Metadata? headers, string key) =>
        headers?.FirstOrDefault(header => header.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;

    static Mock<IMicroserviceAddressProvider> CreateAddressProvider(
        Uri address,
        EndpointResolutionMode mode = EndpointResolutionMode.Direct)
    {
        var addressProvider = new Mock<IMicroserviceAddressProvider>();
        addressProvider
            .Setup(provider => provider.Resolve(
                applicationName, MicroserviceProtocol.Grpc, It.IsAny<int?>(), null))
            .Returns(new ResolvedMicroserviceAddress(address, mode));

        return addressProvider;
    }

    static GrpcChannelOptions ConfigureChannel(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateAddressProvider(DnsAddress).Object);
        configure(services);

        return CreateChannelOptions(services, clientName);
    }

    static GrpcChannelOptions CreateChannelOptions(
        ServiceCollection services,
        string name)
    {
        using var provider = services.BuildServiceProvider();

        // Resolving the named factory options runs the registration callbacks (address resolution,
        // channel configuration) without creating the gRPC channel itself.
        var factoryOptions = provider
            .GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>()
            .Get(name);

        var channelOptions = new GrpcChannelOptions();

        foreach (var configureChannel in factoryOptions.ChannelOptionsActions)
            configureChannel(channelOptions);

        return channelOptions;
    }

    class TestServiceClient : ClientBase<TestServiceClient>
    {
        public TestServiceClient(CallInvoker callInvoker) : base(callInvoker)
        {
        }

        protected TestServiceClient(ClientBaseConfiguration configuration) : base(configuration)
        {
        }

        protected override TestServiceClient NewInstance(ClientBaseConfiguration configuration) =>
            new(configuration);
    }
}
