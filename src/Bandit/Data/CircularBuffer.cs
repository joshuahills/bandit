namespace Bandit.Data;

public sealed class CircularBuffer<T>
{
    private readonly T[] _buf;
    private int _head;
    private int _count;
    private readonly Lock _lock = new();

    public CircularBuffer(int capacity) => _buf = new T[capacity];

    public int Count { get { lock (_lock) return _count; } }
    public int Capacity => _buf.Length;

    public void Add(T item)
    {
        lock (_lock)
        {
            _buf[_head] = item;
            _head = (_head + 1) % _buf.Length;
            if (_count < _buf.Length) _count++;
        }
    }

    public T[] TailN(int n)
    {
        lock (_lock)
        {
            n = Math.Min(n, _count);
            if (n == 0) return [];
            var result = new T[n];
            int tail = ((_head - n) % _buf.Length + _buf.Length) % _buf.Length;
            for (int i = 0; i < n; i++)
                result[i] = _buf[(tail + i) % _buf.Length];
            return result;
        }
    }

    public T? Latest()
    {
        lock (_lock)
        {
            if (_count == 0) return default;
            return _buf[(_head - 1 + _buf.Length) % _buf.Length];
        }
    }
}
