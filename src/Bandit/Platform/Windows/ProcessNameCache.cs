namespace Bandit.Platform.Windows;

using System.Diagnostics;

internal sealed class ProcessNameCache
{
    private readonly Dictionary<int, string> _names = [];
    private readonly Lock _lock = new();

    public string Resolve(int pid)
    {
        lock (_lock)
        {
            if (_names.TryGetValue(pid, out var cached)) return cached;
        }

        string name;
        try
        {
            using var p = Process.GetProcessById(pid);
            name = p.ProcessName;
        }
        catch
        {
            name = $"pid:{pid}";
        }

        lock (_lock)
        {
            _names[pid] = name;
        }
        return name;
    }

    public void Forget(int pid)
    {
        lock (_lock) _names.Remove(pid);
    }
}
