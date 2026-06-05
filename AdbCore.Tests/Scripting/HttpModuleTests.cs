using System;
using System.Collections.Generic;
using AdbCore.Scripting;
using AdbCore.Tests.Scripting.Fakes;
using Xunit;

namespace AdbCore.Tests.Scripting;

public class HttpModuleTests
{
    private static LuaScriptHost.Result Run(string script, FakeHttpRequester http, IDictionary<string, object> vars)
        => new LuaScriptHost(_ => { }, new FakeFileSystem(), new FakeProcessRunner(), http).Run(script, vars, default);

    [Fact]
    public void Get_ReturnsStatusBodyHeaders()
    {
        var http = new FakeHttpRequester
        {
            OnSend = (m, url, body, h) => new HttpResult(200, "{\"ok\":true}",
                new Dictionary<string, string> { ["Content-Type"] = "application/json" })
        };
        var vars = new Dictionary<string, object>();
        var r = Run("local res = http.get('http://x'); vars.s = res.status; vars.b = res.body; vars.ct = res.headers['Content-Type']", http, vars);
        Assert.True(r.Success);
        Assert.Equal(200d, vars["s"]);
        Assert.Equal("{\"ok\":true}", vars["b"]);
        Assert.Equal("application/json", vars["ct"]);
    }

    [Fact]
    public void Get_SendsMethodAndUrl()
    {
        string? method = null, url = null;
        var http = new FakeHttpRequester { OnSend = (m, u, body, h) => { method = m; url = u; return new HttpResult(200, "", new Dictionary<string, string>()); } };
        Run("http.get('http://example.com/a')", http, new Dictionary<string, object>());
        Assert.Equal("GET", method);
        Assert.Equal("http://example.com/a", url);
    }

    [Fact]
    public void Post_SendsBodyAndHeaders()
    {
        string? sentBody = null;
        IReadOnlyDictionary<string, string>? sentHeaders = null;
        var http = new FakeHttpRequester { OnSend = (m, u, body, h) => { sentBody = body; sentHeaders = h; return new HttpResult(201, "", new Dictionary<string, string>()); } };
        var vars = new Dictionary<string, object>();
        var r = Run("local res = http.post('http://x', 'payload', {Authorization='Bearer t'}); vars.s = res.status", http, vars);
        Assert.True(r.Success);
        Assert.Equal("payload", sentBody);
        Assert.NotNull(sentHeaders);
        Assert.Equal("Bearer t", sentHeaders!["Authorization"]);
        Assert.Equal(201d, vars["s"]);
    }

    [Fact]
    public void Non2xx_IsAValueNotAnError()
    {
        var http = new FakeHttpRequester { OnSend = (m, u, body, h) => new HttpResult(404, "nope", new Dictionary<string, string>()) };
        var vars = new Dictionary<string, object>();
        var r = Run("local res = http.get('http://x'); vars.s = res.status", http, vars);
        Assert.True(r.Success);
        Assert.Equal(404d, vars["s"]);
    }

    [Fact]
    public void TransportFailure_RoutesToFailure()
    {
        var http = new FakeHttpRequester { OnSend = (m, u, body, h) => throw new InvalidOperationException("dns fail") };
        var r = Run("http.get('http://x')", http, new Dictionary<string, object>());
        Assert.False(r.Success);
        Assert.Contains("dns fail", r.Error);
    }
}
