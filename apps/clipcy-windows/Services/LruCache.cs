using System.Collections.Generic;

namespace Clipcy.Services;

public sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<TKey>> _nodes = new();
    private readonly Dictionary<TKey, TValue> _values = new();
    private readonly LinkedList<TKey> _lru = new();
    private readonly object _lock = new();

    public LruCache(int capacity)
    {
        _capacity = capacity > 0 ? capacity : 1;
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_values.TryGetValue(key, out value!))
            {
                Touch(key);
                return true;
            }
        }

        value = default!;
        return false;
    }

    public void Add(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_values.ContainsKey(key))
            {
                _values[key] = value;
                Touch(key);
                return;
            }

            _values[key] = value;
            var node = _lru.AddFirst(key);
            _nodes[key] = node;

            if (_values.Count > _capacity)
            {
                var last = _lru.Last;
                if (last != null)
                {
                    var removeKey = last.Value;
                    _lru.RemoveLast();
                    _nodes.Remove(removeKey);
                    _values.Remove(removeKey);
                }
            }
        }
    }

    private void Touch(TKey key)
    {
        if (_nodes.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
        }
        else
        {
            var newNode = _lru.AddFirst(key);
            _nodes[key] = newNode;
        }
    }
}
