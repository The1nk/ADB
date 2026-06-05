using MoonSharp.Interpreter;

namespace AdbCore.Scripting.Modules;

/// <summary>Builds the Lua <c>process</c> table over an <see cref="IProcessRunner"/>. Returns a result table
/// <c>{ exitCode, stdout, stderr }</c>. A non-zero exit code is a value; failure to start the process surfaces
/// as a <see cref="ScriptRuntimeException"/> (pcall-able / routes to onFailure). Honors the CancellationToken.</summary>
internal static class ProcessModule
{
    public static Table Build(Script script, IProcessRunner runner, CancellationToken ct)
    {
        var t = new Table(script);
        t["run"] = DynValue.NewCallback((ctx, args) =>
        {
            var command = args.AsType(0, "process.run", DataType.String).String;
            IReadOnlyList<string>? argList = null;
            if (args.Count > 1 && args[1].Type == DataType.Table)
            {
                var list = new List<string>();
                // Use the array-part index loop to preserve Lua sequence order (1-based).
                for (int i = 1; i <= args[1].Table.Length; i++)
                    list.Add(args[1].Table.Get(i).CastToString());
                argList = list;
            }
            else if (args.Count > 1 && args[1].Type != DataType.Nil)
            {
                throw new ScriptRuntimeException("process.run: second argument must be a table of string arguments");
            }

            ProcessResult result;
            try { result = runner.Run(command, argList, ct); }
            catch (ScriptRuntimeException) { throw; }
            catch (OperationCanceledException) { throw; } // cancellation is not a script failure
            catch (Exception ex) { throw new ScriptRuntimeException(ex.Message); }

            var ret = new Table(script);
            ret["exitCode"] = result.ExitCode;
            ret["stdout"]   = result.StdOut;
            ret["stderr"]   = result.StdErr;
            return DynValue.NewTable(ret);
        });
        return t;
    }
}
