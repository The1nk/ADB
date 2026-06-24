namespace BotBuilder.Core.Layout;

/// <summary>Layered left-to-right graph layout ("Tidy Up"). Assigns each node a layer by longest path
/// on the back-edge-removed DAG (cycles are safe), reduces edge crossings with alternating barycenter
/// sweeps, then packs each layer's column top-to-bottom by height. (Wrapping into stacked rows is added
/// in a later step.)</summary>
public static class AutoLayout
{
    public const double ColGap = 240;
    public const double RowGap = 30;
    public const double BandGap = 80;     // vertical gap between wrapped row-bands (> RowGap: gutter for return wires)
    public const double OriginX = 40;
    public const double OriginY = 40;

    private const int BarycenterPasses = 4;

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

        // 3) group nodes by layer in stable input order
        var maxLayer = layer.Values.Max();
        var layers = new List<List<Guid>>();
        for (var l = 0; l <= maxLayer; l++)
            layers.Add(ids.Where(id => layer[id] == l).OrderBy(i => order[i]).ToList());

        // predecessors over the forward DAG (for up-sweeps)
        var preds = ids.ToDictionary(id => id, _ => new List<Guid>());
        foreach (var u in ids) foreach (var v in forward[u]) preds[v].Add(u);

        var posInLayer = new Dictionary<Guid, int>();
        foreach (var lyr in layers) for (var i = 0; i < lyr.Count; i++) posInLayer[lyr[i]] = i;

        // 4) barycenter crossing reduction: alternate down-sweeps (order by predecessor positions)
        //    and up-sweeps (order by successor positions). Nodes with no neighbors keep their index
        //    (stable); ties break by original input order so the result is deterministic.
        void SortLayer(List<Guid> lyr, IReadOnlyDictionary<Guid, List<Guid>> neighbors)
        {
            double Bary(Guid id)
            {
                var ns = neighbors[id];
                return ns.Count == 0 ? posInLayer[id] : ns.Average(n => (double)posInLayer[n]);
            }
            lyr.Sort((x, y) =>
            {
                var cmp = Bary(x).CompareTo(Bary(y));
                return cmp != 0 ? cmp : order[x].CompareTo(order[y]);
            });
            for (var i = 0; i < lyr.Count; i++) posInLayer[lyr[i]] = i;
        }

        for (var pass = 0; pass < BarycenterPasses; pass++)
        {
            if (pass % 2 == 0)
                for (var l = 1; l <= maxLayer; l++) SortLayer(layers[l], preds);
            else
                for (var l = maxLayer - 1; l >= 0; l--) SortLayer(layers[l], forward);
        }

        // 5) choose band width K (layers per stacked row). Single row for now.
        var L = layers.Count;
        var colHeight = layers
            .Select(lyr => lyr.Sum(id => height[id]) + Math.Max(0, lyr.Count - 1) * RowGap)
            .ToList();
        var k = L;

        // 6) position: row-reset bands. Band b holds layers [b*k, b*k+k); each band restarts at OriginX,
        //    stacked below the previous band by that band's tallest column + BandGap.
        var bands = (int)Math.Ceiling(L / (double)k);
        var bandTop = new double[bands];
        bandTop[0] = OriginY;
        for (var b = 1; b < bands; b++)
        {
            var prevHeight = 0.0;
            for (var l = (b - 1) * k; l < Math.Min(b * k, L); l++)
                prevHeight = Math.Max(prevHeight, colHeight[l]);
            bandTop[b] = bandTop[b - 1] + prevHeight + BandGap;
        }

        for (var l = 0; l < L; l++)
        {
            var band = l / k;
            var localCol = l % k;
            var x = OriginX + localCol * ColGap;
            var y = bandTop[band];
            foreach (var id in layers[l])
            {
                result[id] = (x, y);
                y += height[id] + RowGap;
            }
        }
        return result;
    }
}
