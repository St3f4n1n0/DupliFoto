using DupliFoto.Core.Imaging;

namespace DupliFoto.Core.Matching;

/// <summary>
/// BK-tree sulla distanza di Hamming: trova tutti gli hash entro distanza d
/// senza confrontare ogni foto con tutte le altre (da O(n²) a circa O(n log n)).
/// </summary>
public sealed class BkTree<T>
{
    private sealed class Node(ulong hash, T value)
    {
        public readonly ulong Hash = hash;
        public readonly List<T> Values = [value];
        public Dictionary<int, Node>? Children;
    }

    private Node? _root;
    public int Count { get; private set; }

    public void Add(ulong hash, T value)
    {
        Count++;
        if (_root is null) { _root = new Node(hash, value); return; }
        var node = _root;
        while (true)
        {
            int d = PerceptualHash.Distance(hash, node.Hash);
            if (d == 0) { node.Values.Add(value); return; }
            node.Children ??= new Dictionary<int, Node>();
            if (!node.Children.TryGetValue(d, out var child))
            {
                node.Children[d] = new Node(hash, value);
                return;
            }
            node = child;
        }
    }

    /// <summary>Tutti gli elementi con distanza ≤ maxDistance da <paramref name="hash"/>.</summary>
    public List<(T Value, int Distance)> Query(ulong hash, int maxDistance)
    {
        var results = new List<(T, int)>();
        if (_root is null) return results;
        var stack = new Stack<Node>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            int d = PerceptualHash.Distance(hash, node.Hash);
            if (d <= maxDistance)
                foreach (var v in node.Values) results.Add((v, d));
            if (node.Children is null) continue;
            // Disuguaglianza triangolare: solo i figli a distanza [d-max, d+max] possono contenere risultati.
            foreach (var (edge, child) in node.Children)
                if (edge >= d - maxDistance && edge <= d + maxDistance)
                    stack.Push(child);
        }
        return results;
    }
}
