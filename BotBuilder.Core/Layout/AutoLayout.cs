namespace BotBuilder.Core.Layout;

/// <summary>Pure layered left-to-right graph layout ("Tidy Up"). Assigns each node a layer by longest path
/// on the back-edge-removed DAG (so cycles are safe), then packs each layer's column top-to-bottom by height.</summary>
public static class AutoLayout
{
    public const double ColGap = 240;
    public const double RowGap = 30;
    public const double OriginX = 40;
    public const double OriginY = 40;

    public static IReadOnlyDictionary<Guid, (double X, double Y)> Arrange(
        IReadOnlyList<(Guid Id, double Height)> nodes,
        IReadOnlyList<(Guid Source, Guid Target)> edges)
    {
        var result = new Dictionary<Guid, (double X, double Y)>();
        if (nodes.Count == 0) return result;

        var ids = nodes.Select(n => n.Id).ToList();
        var idSet = new HashSet<Guid>(ids);
        var height = nodes.ToDictionary(n => n.Id, n => n.Height);
        var order = new Dictionary<Guid, int>();           // stable input order
        for (var i = 0; i < ids.Count; i++) order[ids[i]] = i;

        // adjacency over edges whose endpoints are both real nodes
        var adj = ids.ToDictionary(id => id, _ => new List<Guid>());
        foreach (var (s, t) in edges)
            if (idSet.Contains(s) && idSet.Contains(t) && s != t) adj[s].Add(t);

        // 1) cycle removal: DFS, drop edges that point to a node on the current stack (back-edges)
        var forward = ids.ToDictionary(id => id, _ => new List<Guid>());
        var state = new Dictionary<Guid, int>();           // 0=unvisited,1=on-stack,2=done
        foreach (var id in ids) state[id] = 0;
        void Dfs(Guid u)
        {
            state[u] = 1;
            foreach (var v in adj[u])
            {
                if (state[v] == 1) continue;               // back-edge -> skip for layering
                forward[u].Add(v);
                if (state[v] == 0) Dfs(v);
            }
            state[u] = 2;
        }
        foreach (var id in ids.OrderBy(i => order[i])) if (state[id] == 0) Dfs(id);

        // 2) longest-path layering on the forward DAG (Kahn)
        var indeg = ids.ToDictionary(id => id, _ => 0);
        foreach (var u in ids) foreach (var v in forward[u]) indeg[v]++;
        var layer = ids.ToDictionary(id => id, _ => 0);
        var queue = new Queue<Guid>(ids.Where(id => indeg[id] == 0).OrderBy(i => order[i]));
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            foreach (var v in forward[u])
            {
                if (layer[v] < layer[u] + 1) layer[v] = layer[u] + 1;
                if (--indeg[v] == 0) queue.Enqueue(v);
            }
        }

        // 3) group by layer, stable order within layer; 4) pack columns by height
        var byLayer = ids.GroupBy(id => layer[id]).OrderBy(g => g.Key);
        foreach (var group in byLayer)
        {
            var x = OriginX + group.Key * ColGap;
            var y = OriginY;
            foreach (var id in group.OrderBy(i => order[i]))
            {
                result[id] = (x, y);
                y += height[id] + RowGap;
            }
        }
        return result;
    }
}
