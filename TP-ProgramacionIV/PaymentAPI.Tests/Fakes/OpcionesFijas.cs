using Microsoft.Extensions.Options;

namespace PaymentAPI.Tests.Fakes;

public sealed class OpcionesFijas<T>(T valor) : IOptionsMonitor<T>
{
    public T CurrentValue => valor;

    public T Get(string? name) => valor;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
