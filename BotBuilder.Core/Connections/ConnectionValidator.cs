namespace BotBuilder.Core.Connections;

/// <summary>Validates a proposed connection against the DAG rules (output→input, no self,
/// no duplicate, no cycle).</summary>
public static class ConnectionValidator
{
    public static ConnectionError Validate(
        IReadOnlyCollection<ConnectionViewModel> existing,
        NodeViewModel source,
        PortViewModel sourcePort,
        NodeViewModel target,
        PortViewModel targetPort)
    {
        if (sourcePort.Direction != PortDirection.Out || targetPort.Direction != PortDirection.In)
        {
            return ConnectionError.NotOutputToInput;
        }

        if (ReferenceEquals(source, target))
        {
            return ConnectionError.SelfConnection;
        }

        if (existing.Any(c =>
                ReferenceEquals(c.Source, source) && c.SourcePort.Name == sourcePort.Name &&
                ReferenceEquals(c.Target, target) && c.TargetPort.Name == targetPort.Name))
        {
            return ConnectionError.Duplicate;
        }

        if (TargetReachesSource(existing, source, target))
        {
            return ConnectionError.WouldCreateCycle;
        }

        return ConnectionError.None;
    }

    // Adding source->target creates a cycle iff target can already reach source.
    private static bool TargetReachesSource(
        IReadOnlyCollection<ConnectionViewModel> existing, NodeViewModel source, NodeViewModel target)
    {
        var visited = new HashSet<NodeViewModel>();
        var stack = new Stack<NodeViewModel>();
        stack.Push(target);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (ReferenceEquals(node, source))
            {
                return true;
            }
            if (!visited.Add(node))
            {
                continue;
            }
            foreach (var edge in existing.Where(c => ReferenceEquals(c.Source, node)))
            {
                stack.Push(edge.Target);
            }
        }

        return false;
    }
}
