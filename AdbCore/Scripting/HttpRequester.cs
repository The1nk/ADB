using System.Net.Http;

namespace AdbCore.Scripting;

/// <summary>The real <see cref="IHttpRequester"/> backed by a shared <see cref="HttpClient"/> (one per process,
/// per the HttpClient guidance). Blocks synchronously so the Lua host functions stay simple, honoring the token.</summary>
public sealed class HttpRequester : IHttpRequester
{
    private static readonly HttpClient Client = new();

    public HttpResult Send(string method, string url, string? body, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body is not null)
            request.Content = new StringContent(body);
        if (headers is not null)
            foreach (var h in headers)
            {
                if (!request.Headers.TryAddWithoutValidation(h.Key, h.Value))
                    request.Content?.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

        using var response = Client.Send(request, ct);
        var responseBody = response.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
        var responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers)
            responseHeaders[h.Key] = string.Join(", ", h.Value);
        foreach (var h in response.Content.Headers)
            responseHeaders[h.Key] = string.Join(", ", h.Value);

        return new HttpResult((int)response.StatusCode, responseBody, responseHeaders);
    }
}
