using System.Collections.Concurrent;

namespace NotificationService.Handling;

// [JUSTIFICACIÓN - Concurrencia] TryAdd reserva la clave de forma atómica: dos llamadas iguales a la vez avisan una sola vez.
// Olvida todo al reiniciar y alcanza para una sola instancia.
public sealed class InMemoryDeduplicationStore : IDeduplicationStore
{
    private readonly ConcurrentDictionary<string, byte> _reserved = new();

    public bool TryBegin(string key) => _reserved.TryAdd(key, 0);

    public void Release(string key) => _reserved.TryRemove(key, out _);
}
