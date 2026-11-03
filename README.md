# Monq.Core.GrpcClientExtensions

`Monq.Core.GrpcClientExtensions` registers gRPC clients that resolve their addresses through
`Monq.Core.MicroserviceAddressing`. It is the gRPC counterpart of `Monq.Core.HttpClientExtensions`
for the HTTP side and centralizes the transport rules that every MONQ service otherwise had to
duplicate:

- scheme-aware channel credentials — grpc-dotnet requires explicit credentials for any address
  scheme other than `http`/`https` (`dns:///` from Service Discovery, but also `ipv4:///`,
  `unix:/` and custom resolver schemes), so non-http addresses get `ChannelCredentials.Insecure`;
- round-robin balancing — enabled exactly for load-balanced addresses (resolution mode
  `ServiceDiscovery` or the `dns` scheme); Direct (ClusterIP) addresses keep kube-proxy balancing;
- client-side service discovery — a DNS resolver with a configurable refresh interval shared by
  all clients;
- static authorization fallback for background/console clients — the incoming `Authorization`
  header is forwarded, and when no HTTP context is present a token is obtained through
  `Monq.Core.HttpClientExtensions.RestHttpClient`.

## Quick start

```csharp
builder.Services.AddMicroserviceAddressing();
builder.Host.UseGrpcMicroserviceAddressResolver(new KubernetesServiceDiscoveryAddressResolver());

// Address resolved by application name on the first client creation.
builder.Services.AddMonqGrpcClient<SampleService.SampleServiceClient>("sample-api-service");

// Named registration for several clients of the same type.
builder.Services.AddMonqGrpcClient<SampleService.SampleServiceClient>(
    name: "secondary",
    applicationName: "sample-api-service",
    port: 5006,
    addressResolver: new FixedMicroserviceAddressResolver(new Uri("http://localhost:5006")));

// Settings resolved lazily — for composed libraries.
builder.Services.AddMonqGrpcClient<SampleService.SampleServiceClient>(
    "sample",
    provider => new GrpcClientAddressSettings("sample-api-service", 5006));

// Fixed address for local debugging.
builder.Services.AddMonqGrpcClient<SampleService.SampleServiceClient>(
    new Uri("http://localhost:5006"));

// Additional client/channel configuration via the options object.
builder.Services.AddMonqGrpcClient<SampleService.SampleServiceClient>(
    "sample-api-service",
    options: new GrpcClientOptions
    {
        ChannelOptionsAction = channelOptions => channelOptions.MaxReceiveMessageSize = null,
    });
```

Background and console clients use the console variants and receive call credentials with
static token fallback:

```csharp
builder.Services.AddMonqGrpcConsoleClient<SampleService.SampleServiceClient>(
    "sample-api-service");
```

## Service discovery options

```csharp
builder.Services.ConfigureMonqGrpcServiceDiscovery(options =>
    options.DnsRefreshInterval = TimeSpan.FromSeconds(15));
```

## Behavior summary

| Address | Credentials | Load balancing |
|---|---|---|
| `http`/`https` (Direct, ClusterIP) | inferred from scheme | kube-proxy (no service config) |
| `dns:///` (Service Discovery, headless Service) | `Insecure` | round-robin over resolved pods |
| `ipv4:///`, `unix:/`, custom schemes | `Insecure` | per resolver semantics |
| Fixed URI (`Uri` overloads) | scheme-aware | round-robin only for the `dns` scheme |

Every registered client also receives the defaults of the legacy pre-configured clients from
`Monq.Core.BasicDotNetMicroservice`:

- propagation of the `X-Smon-Userspace-Id` and `Accept-Language` headers from the current HTTP
  request to the outgoing gRPC metadata (`MonqHeaderPropagationInterceptor`);
- `MaxReceiveMessageSize` = 51 MB and `UnsafeUseInsecureChannelCallCredentials` = `true`;
- call context (event id) propagation (`EnableCallContextPropagation`);
- `Authorization` forwarding from the current HTTP request — interactive clients forward it as is,
  console clients additionally fall back to a static token via `RestHttpClient`.

Defaults are applied before the actions of `GrpcClientOptions`, so each default can be overridden
per client.

## Roadmap

MONQ service frameworks move their gRPC registration internals onto this package without
changing their public APIs; composed server libraries are expected to use it directly.
