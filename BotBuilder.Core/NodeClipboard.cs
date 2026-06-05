namespace BotBuilder.Core;

internal sealed record NodeClip(
    string TypeKey, string Label, System.Guid? TargetId,
    int RetryMaxAttempts, int RetryDelayMs,
    System.Collections.Generic.Dictionary<string, object> Config, double X, double Y);

internal sealed record ConnectionClip(int SourceIndex, string SourcePort, int TargetIndex, string TargetPort);

internal sealed record NodeClipboard(
    System.Collections.Generic.IReadOnlyList<NodeClip> Nodes,
    System.Collections.Generic.IReadOnlyList<ConnectionClip> Connections);
