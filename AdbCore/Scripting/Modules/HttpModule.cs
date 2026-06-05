using MoonSharp.Interpreter;

namespace AdbCore.Scripting.Modules;

/// <summary>Builds the Lua <c>http</c> table over an <see cref="IHttpRequester"/>. <c>get</c>/<c>post</c> return
/// a result table <c>{ status, body, headers }</c>. A non-2xx status is a value; a transport failure surfaces as
/// a <see cref="ScriptRuntimeException"/> (pcall-able / routes to onFailure). Honors the CancellationToken.</summary>
internal static class HttpModule
{
    public static Table Build(Script script, IHttpRequester http, CancellationToken ct)
    {
        var t = new Table(script);
        t["get"] = DynValue.NewCallback((ctx, args) =>
        {
            var url = args.AsType(0, "http.get", DataType.String).String;
            var headers = HeadersFrom(args, 1);
            return Send(script, http, "GET", url, null, headers, ct);
        });
        t["post"] = DynValue.NewCallback((ctx, args) =>
        {
            var url = args.AsType(0, "http.post", DataType.String).String;
            var body = args.Count > 1 && !args[1].IsNil() ? args[1].CastToString() : null;
            var headers = HeadersFrom(args, 2);
            return Send(script, http, "POST", url, body, headers, ct);
        });
        return t;
    }

    private static IReadOnlyDictionary<string, string>? HeadersFrom(CallbackArguments args, int index)
    {
        if (args.Count <= index || args[index].Type != DataType.Table) return null;
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in args[index].Table.Pairs)
            if (pair.Key.Type == DataType.String)
                d[pair.Key.String] = pair.Value.CastToString();
        return d;
    }

    private static DynValue Send(Script script, IHttpRequester http, string method, string url,
        string? body, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        HttpResult result;
        try { result = http.Send(method, url, body, headers, ct); }
        catch (ScriptRuntimeException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new ScriptRuntimeException(ex.Message); }

        var ret = new Table(script);
        ret["status"] = result.Status;
        ret["body"] = result.Body;
        var h = new Table(script);
        foreach (var kv in result.Headers)
            h[kv.Key] = kv.Value;
        ret["headers"] = h;
        return DynValue.NewTable(ret);
    }
}
