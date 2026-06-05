using System;
using System.Collections.Generic;
using System.Threading;
using AdbCore.Scripting;

namespace AdbCore.Tests.Scripting.Fakes;

/// <summary>Configurable fake http requester. Default returns 200 / empty body / no headers.</summary>
public sealed class FakeHttpRequester : IHttpRequester
{
    public Func<string, string, string?, IReadOnlyDictionary<string, string>?, HttpResult> OnSend { get; set; }
        = (_, _, _, _) => new HttpResult(200, "", new Dictionary<string, string>());
    public HttpResult Send(string method, string url, string? body, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return OnSend(method, url, body, headers);
    }
}
