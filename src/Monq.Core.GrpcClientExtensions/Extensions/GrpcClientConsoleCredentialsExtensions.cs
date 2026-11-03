using Microsoft.AspNetCore.Http;
using Monq.Core.HttpClientExtensions;
using System.Net;
using System.Net.Http.Headers;

namespace Microsoft.Extensions.DependencyInjection;

static class GrpcClientConsoleCredentialsExtensions
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
