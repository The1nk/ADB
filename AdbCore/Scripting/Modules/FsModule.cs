using MoonSharp.Interpreter;

namespace AdbCore.Scripting.Modules;

/// <summary>Builds the Lua <c>fs</c> table over an <see cref="IFileSystem"/>. Operational failures
/// (missing file, IO error) surface as <see cref="ScriptRuntimeException"/> so a script can <c>pcall</c>
/// them or let them route to onFailure.</summary>
internal static class FsModule
{
    public static Table Build(Script script, IFileSystem fs)
    {
        var t = new Table(script);
        t["read"]   = (Func<string, string>)(p => Guard(() => fs.Read(p)));
        t["write"]  = (Action<string, string>)((p, c) => Guard(() => { fs.Write(p, c); return 0; }));
        t["copy"]   = (Action<string, string>)((s, d) => Guard(() => { fs.Copy(s, d); return 0; }));
        t["move"]   = (Action<string, string>)((s, d) => Guard(() => { fs.Move(s, d); return 0; }));
        t["exists"] = (Func<string, bool>)(p => Guard(() => fs.Exists(p)));
        t["delete"] = (Action<string>)(p => Guard(() => { fs.Delete(p); return 0; }));
        return t;
    }

    private static T Guard<T>(Func<T> op)
    {
        try { return op(); }
        catch (ScriptRuntimeException) { throw; }
        catch (Exception ex) { throw new ScriptRuntimeException(ex.Message); }
    }
}
