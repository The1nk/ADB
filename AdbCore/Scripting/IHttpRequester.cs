namespace AdbCore.Scripting;

/// <summary>An HTTP response: status code, body, and response headers.</summary>
public readonly record struct HttpResult(int Status, string Body, IReadOnlyDictionary<string, string> Headers);

/// <summary>Sends an HTTP request. Injectable so the Lua <c>http</c> module is unit-testable without a network.
/// A non-2xx status is a normal <see cref="HttpResult"/> (not an exception); a transport failure throws.</summary>
public interface IHttpRequester
{
    HttpResult Send(string method, string url, string? body, IReadOnlyDictionary<string, string>? headers, CancellationToken ct);
}
