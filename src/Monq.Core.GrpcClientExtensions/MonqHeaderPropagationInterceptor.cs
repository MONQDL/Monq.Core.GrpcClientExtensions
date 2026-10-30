using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;

namespace Monq.Core.GrpcClientExtensions;

/// <summary>
/// Propagates <c>X-Smon-Userspace-Id</c> and <c>Accept-Language</c> headers from the current HTTP
/// request to the metadata of outgoing gRPC calls, matching the behavior of the legacy
/// pre-configured clients in Monq.Core.BasicDotNetMicroservice. Headers explicitly set on the
/// call take precedence. Outside of an HTTP request (background or console execution) the
/// interceptor is a no-op.
/// </summary>
public sealed class MonqHeaderPropagationInterceptor : Interceptor
{
    /// <summary>
    /// Header carrying the current userspace identifier.
    /// </summary>
    public const string UserspaceIdHeader = "X-Smon-Userspace-Id";

    /// <summary>
    /// Header carrying the current request culture.
    /// </summary>
    public const string CultureHeader = "Accept-Language";

    readonly IHttpContextAccessor? _httpContextAccessor;

    /// <summary>
    /// Creates the interceptor. <paramref name="httpContextAccessor" /> may be <see langword="null" />
    /// in hosts that do not serve HTTP requests.
    /// </summary>
    public MonqHeaderPropagationInterceptor(IHttpContextAccessor? httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, Propagate(context));

    /// <inheritdoc />
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, Propagate(context));

    /// <inheritdoc />
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, Propagate(context));

    /// <inheritdoc />
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
        => continuation(Propagate(context));

    /// <inheritdoc />
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
        => continuation(Propagate(context));

    ClientInterceptorContext<TRequest, TResponse> Propagate<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        var httpRequest = _httpContextAccessor?.HttpContext?.Request;
        if (httpRequest is null)
            return context;

        var headers = new Metadata();
        if (context.Options.Headers is not null)
            foreach (var header in context.Options.Headers)
                headers.Add(header);

        AddIfMissing(headers, httpRequest, UserspaceIdHeader);
        AddIfMissing(headers, httpRequest, CultureHeader);

        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(headers));
    }

    static void AddIfMissing(Metadata headers, HttpRequest httpRequest, string name)
    {
        if (headers.Any(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)))
            return;

        if (httpRequest.Headers.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
            headers.Add(name, value.ToString());
    }
}
