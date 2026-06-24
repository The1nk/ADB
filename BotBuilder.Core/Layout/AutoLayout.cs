namespace BotBuilder.Core.Layout;

/// <summary>Layered left-to-right graph layout ("Tidy Up"). Assigns each node a layer by longest path
/// on the back-edge-removed DAG (cycles are safe), reduces edge crossings with alternating barycenter
/// sweeps, packs each layer's column top-to-bottom by height, and wraps long flows into stacked
/// left-to-right bands sized toward a balanced aspect ratio so they don't run off-screen.</summary>
public static class AutoLayout
{
    public const double ColGap = 240;
    public const double RowGap = 30;
    public const double BandGap = 80;     // vertical gap between wrapped row-bands (> RowGap: gutter for return wires)
    public const double OriginX = 40;
    public const double OriginY = 40;

    private const int BarycenterPasses = 4;
    private const double NodeWidth = NodeLayout.CardWidth;   // 160; for aspect estimation only
    private const double TargetAspect = 1.6;                 // desired width:height of the whole block
    private const int NoWrapMaxLayers = 4;                   // flows this short or shorter never wrap

    /// <summary>Back-compat overload: edges without port positions (port Y defaults to 0, so ties fall
    /// back to input order exactly as before).</summary>
    public static IReadOnlyDictionary<Guid, (double X, double Y)> Arrange(
        IReadOnlyList<(Guid Id, double Height)> nodes,
        IReadOnlyList<(Guid Source, Guid Target)> edges)
        => Arrange(nodes, edges.Select(e => (e.Source, e.Target, 0.0, 0.0)).ToList());

    /// <summary>Port-aware layout: each edge carries the source/target port Y (relative to the card top),
    /// so siblings fed from a single parent are ordered by feeding-port height (prevents crossed outputs).</summary>
    public static IReadOnlyDictionary<Guid, (double X, double Y)> Arrange(
        IReadOnlyList<(Guid Id, double Height)> nodes,
        IReadOnlyList<(Guid Source, Guid Target, double SourcePortY, double TargetPortY)> edges)
    {
        var result = new Dictionary<Guid, (double X, double Y)>();
        if (nodes.Count == 0) return result;

        var ids = nodes.Select(n => n.Id).ToList();
        var idSet = new HashSet<Guid>(ids);
        var height = nodes.ToDictionary(n => n.Id, n => n.Height);
        var order = new Dictionary<Guid, int>();           // stable input order
        for (var i = 0; i < ids.Count; i++) order[ids[i]] = i;

        // adjacency over edges whose endpoints are both real nodes, carrying port Y for ordering
        var adj = ids.ToDictionary(id => id, _ => new List<(Guid Node, double SrcY, double TgtY)>());
        foreach (var (s, t, sy, ty) in edges)
            if (idSet.Contains(s) && idSet.Contains(t) && s != t) adj[s].Add((t, sy, ty));

        // 1) cycle removal: DFS, drop edges that point to a node on the current stack (back-edges)
        var forward = ids.ToDictionary(id => id, _ => new List<(Guid Node, double SrcY, double TgtY)>());
        var state = new Dictionary<Guid, int>();           // 0=unvisited,1=on-stack,2=done
        foreach (var id in ids) state[id] = 0;
        void Dfs(Guid u)
        {
            state[u] = 1;
            foreach (var e in adj[u])
            {
                if (state[e.Node] == 1) continue;          // back-edge -> skip for layering
                forward[u].Add(e);
                if (state[e.Node] == 0) Dfs(e.Node);
            }
            state[u] = 2;
        }
        foreach (var id in ids.OrderBy(i => order[i])) if (state[id] == 0) Dfs(id);

        // 2) longest-path layering on the forward DAG (Kahn)
        var indeg = ids.ToDictionary(id => id, _ => 0);
        foreach (var u in ids) foreach (var e in forward[u]) indeg[e.Node]++;
        var layer = ids.ToDictionary(id => id, _ => 0);
        var queue = new Queue<Guid>(ids.Where(id => indeg[id] == 0).OrderBy(i => order[i]));
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            foreach (var e in forward[u])
            {
                if (layer[e.Node] < layer[u] + 1) layer[e.Node] = layer[u] + 1;
                if (--indeg[e.Node] == 0) queue.Enqueue(e.Node);
            }
        }

        // 3) group nodes by layer in stable input order
        var maxLayer = layer.Values.Max();
        var layers = new List<List<Guid>>();
        for (var l = 0; l <= maxLayer; l++)
            layers.Add(ids.Where(id => layer[id] == l).OrderBy(i => order[i]).ToList());

        // predecessors over the forward DAG (for up-sweeps), carrying the edge's port Ys
        var preds = ids.ToDictionary(id => id, _ => new List<(Guid Node, double SrcY, double TgtY)>());
        foreach (var u in ids) foreach (var e in forward[u]) preds[e.Node].Add((u, e.SrcY, e.TgtY));

        var posInLayer = new Dictionary<Guid, int>();
        foreach (var lyr in layers) for (var i = 0; i < lyr.Count; i++) posInLayer[lyr[i]] = i;

        // 4) barycenter crossing reduction: alternate down-sweeps (order layer by predecessor positions)
        //    and up-sweeps (order by successor positions). Primary key = avg neighbor index; ties break by
        //    the average feeding-port Y (so two children of one parent sort by which output port feeds
        //    them — prevents crossed outputs); final tie-break = original input order (determinism).
        //    `down` selects which port end matters: when ordering a layer by its PREDECESSORS we use the
        //    SOURCE port Y (down-sweep, neighbor = parent); by its SUCCESSORS we use the TARGET port Y.
        void SortLayer(List<Guid> lyr, IReadOnlyDictionary<Guid, List<(Guid Node, double SrcY, double TgtY)>> neighbors, bool down)
        {
            double Bary(Guid id)
            {
                var ns = neighbors[id];
                return ns.Count == 0 ? posInLayer[id] : ns.Average(n => (double)posInLayer[n.Node]);
            }
            double PortKey(Guid id)
            {
                var ns = neighbors[id];
                return ns.Count == 0 ? 0.0 : ns.Average(n => down ? n.SrcY : n.TgtY);
            }
            lyr.Sort((x, y) =>
            {
                var cmp = Bary(x).CompareTo(Bary(y));
                if (cmp != 0) return cmp;
                cmp = PortKey(x).CompareTo(PortKey(y));
                return cmp != 0 ? cmp : order[x].CompareTo(order[y]);
            });
            for (var i = 0; i < lyr.Count; i++) posInLayer[lyr[i]] = i;
        }

        for (var pass = 0; pass < BarycenterPasses; pass++)
        {
            if (pass % 2 == 0)
                for (var l = 1; l <= maxLayer; l++) SortLayer(layers[l], preds, down: true);
            else
                for (var l = maxLayer - 1; l >= 0; l--) SortLayer(layers[l], forward, down: false);
        }

        // 5) choose band width K (layers per stacked row): short flows stay one row, longer flows
        //    wrap so the whole block trends toward a balanced (screen-ish) aspect ratio.
        var L = layers.Count;
        var colHeight = layers
            .Select(lyr => lyr.Sum(id => height[id]) + Math.Max(0, lyr.Count - 1) * RowGap)
            .ToList();
        var k = L <= NoWrapMaxLayers ? L : ChooseBandWidth(L, colHeight);

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

    /// <summary>Brute-forces the band width (1..layerCount) whose resulting block aspect (width:height)
    /// is closest to <see cref="TargetAspect"/>. layerCount is small (tens) so the O(n^2) scan is cheap.</summary>
    private static int ChooseBandWidth(int layerCount, IReadOnlyList<double> colHeight)
    {
        var best = layerCount;
        var bestDelta = double.MaxValue;
        for (var k = 1; k <= layerCount; k++)
        {
            var bands = (int)Math.Ceiling(layerCount / (double)k);
            var width = (k - 1) * ColGap + NodeWidth;
            var totalHeight = 0.0;
            for (var b = 0; b < bands; b++)
            {
                var bandHeight = 0.0;
                for (var l = b * k; l < Math.Min((b + 1) * k, layerCount); l++)
                    bandHeight = Math.Max(bandHeight, colHeight[l]);
                totalHeight += bandHeight;
            }
            totalHeight += Math.Max(0, bands - 1) * BandGap;
            var aspect = width / totalHeight;
            var delta = Math.Abs(aspect - TargetAspect);
            if (delta < bestDelta) { bestDelta = delta; best = k; }
        }
        return best;
    }
}
