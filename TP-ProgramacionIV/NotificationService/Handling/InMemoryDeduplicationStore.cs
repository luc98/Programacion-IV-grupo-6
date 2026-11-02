using System.Collections.Concurrent;

namespace NotificationService.Handling;

public sealed class InMemoryDeduplicationStore : IDeduplicationStore
{
    private readonly ConcurrentDictionary<string, byte> _reserved = new();

    public bool TryBegin(string key) => _reserved.TryAdd(key, 0);

    public void Release(string key) => _reserved.TryRemove(key, out _);
}
