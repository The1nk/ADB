using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;

namespace AdbCore.Execution.ControlFlow;

/// <summary>Engine-native Loop: re-walks the Body sub-path once per iteration (count or for-each), setting the
/// optional index/item variables, then resumes the parent walk at the Done port.</summary>
public sealed class LoopControlFlowExecutor : IControlFlowExecutor
{
    public string TypeKey => LoopAction.LoopTypeKey;

    public async Task<ControlFlowResult> ExecuteAsync(ControlFlowContext context, CancellationToken ct)
    {
        var loop = context.Action;
        var bodyStart = context.Graph.FindNext(loop.Id, LoopAction.BodyPort);
        var mode = ConfigValues.GetString(loop.Config, LoopAction.ModeKey, LoopAction.ModeCount);
        var indexVar = ConfigValues.GetString(loop.Config, LoopAction.IndexVariableKey);
        var itemVar = ConfigValues.GetString(loop.Config, LoopAction.ItemVariableKey);

        IReadOnlyList<string?> items;
        if (string.Equals(mode, LoopAction.ModeForEach, StringComparison.OrdinalIgnoreCase))
        {
            var collectionVar = ConfigValues.GetString(loop.Config, LoopAction.CollectionVariableKey);
            var raw = !string.IsNullOrEmpty(collectionVar)
                && context.RunContext.Variables.TryGetValue(collectionVar, out var v) ? v : null;
            items = SplitItems(raw);
        }
        else
        {
            // Fallback matches LoopAction's "count" ConfigField.DefaultValue (1): a dropped Loop whose Count
            // was never edited has no "count" key in Config, yet should iterate once, not zero times.
            var count = Math.Max(0, ConfigValues.GetInt(loop.Config, LoopAction.CountKey, 1));
            items = new string?[count];
        }

        for (var i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(indexVar))
            {
                context.RunContext.Variables[indexVar] = i;
            }
            if (!string.IsNullOrEmpty(itemVar) && items[i] is not null)
            {
                context.RunContext.Variables[itemVar] = items[i]!;
            }

            var bodyOutcome = await context.WalkAsync(bodyStart, ct);
            if (!bodyOutcome.Success)
            {
                return ControlFlowResult.Halt(bodyOutcome);
            }
        }

        return ControlFlowResult.Continue(context.Graph.FindNext(loop.Id, LoopAction.DonePort));
    }

    /// <summary>For-each item source: a comma-separated string. Empty/whitespace yields no items; each item
    /// is trimmed.</summary>
    private static IReadOnlyList<string?> SplitItems(object? raw)
    {
        var text = ConfigValues.AsString(raw);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string?>();
        }
        return text.Split(',').Select(part => (string?)part.Trim()).ToList();
    }
}
