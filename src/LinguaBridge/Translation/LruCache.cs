using System.Collections.Generic;

namespace LinguaBridge.Translation;

/// <summary>A small thread-safe least-recently-used cache: when full, the entry unused for the longest time goes.</summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int capacity;
    private readonly object gate = new();
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> map = new();
    private readonly LinkedList<(TKey Key, TValue Value)> order = new();

    public LruCache(int capacity) => this.capacity = capacity;

    public bool TryGet(TKey key, out TValue value)
    {
        lock (gate)
        {
            if (map.TryGetValue(key, out var node))
            {
                order.Remove(node);
                order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    /// <summary>Adds the value unless the key is already there (the first result stays).</summary>
    public void Add(TKey key, TValue value)
    {
        lock (gate)
        {
            if (map.ContainsKey(key))
                return;
            map[key] = order.AddFirst((key, value));
            if (order.Count > capacity)
            {
                var last = order.Last!;
                order.RemoveLast();
                map.Remove(last.Value.Key);
            }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            map.Clear();
            order.Clear();
        }
    }
}
