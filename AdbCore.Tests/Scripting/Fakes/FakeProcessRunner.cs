using System;
using System.Collections.Generic;
using System.Threading;
using AdbCore.Scripting;

namespace AdbCore.Tests.Scripting.Fakes;

/// <summary>Configurable fake process runner. Default returns exit 0 / empty output.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public Func<string, IReadOnlyList<string>?, ProcessResult> OnRun { get; set; } = (_, _) => new ProcessResult(0, "", "");
    public ProcessResult Run(string command, IReadOnlyList<string>? arguments, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return OnRun(command, arguments);
    }
}
