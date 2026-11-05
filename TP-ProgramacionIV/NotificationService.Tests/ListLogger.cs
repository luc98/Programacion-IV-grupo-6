using Microsoft.Extensions.Logging;

namespace NotificationService.Tests;

internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<object> _scopes = [];

    public List<(LogLevel Level, string Message, IReadOnlyList<object> Scopes)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        _scopes.Add(state);
        return new Scope(() => _scopes.Remove(state));
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception), _scopes.ToList()));

    private sealed class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
