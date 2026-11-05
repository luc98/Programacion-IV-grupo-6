using System.Collections.Concurrent;
using PaymentAPI.DAL.Entities;
using PaymentAPI.DAL.Repositories;

namespace PaymentAPI.Tests.Fakes;

// Repositorio en memoria que respeta la misma regla que el índice UNIQUE de la
// base: un solo pago por reserva.
public class RepositorioEnMemoria : IPagoRepository
{
    private readonly ConcurrentDictionary<Guid, Pago> _pagos = new();

    public IReadOnlyCollection<Pago> Pagos => _pagos.Values.ToList();

    // Si se define, IntentarAgregarAsync tira esta excepción en lugar de guardar:
    // simula que la base se cae justo al insertar.
    public Func<Exception>? FallaAlAgregar { get; set; }

    public Task<Pago?> ObtenerPorReservaAsync(Guid reservaId, CancellationToken cancellationToken) =>
        Task.FromResult(_pagos.TryGetValue(reservaId, out var pago) ? pago : null);

    public virtual Task<bool> IntentarAgregarAsync(Pago pago, CancellationToken cancellationToken)
    {
        if (FallaAlAgregar is not null)
        {
            throw FallaAlAgregar();
        }
        return Task.FromResult(_pagos.TryAdd(pago.ReservaId, pago));
    }

    protected bool Agregar(Pago pago) => _pagos.TryAdd(pago.ReservaId, pago);
}

// Simula la carrera: justo antes de guardar, otro intento con la misma reserva
// se guarda primero.
public sealed class RepositorioConCarrera : RepositorioEnMemoria
{
    public Pago? Ganador { get; private set; }

    public override Task<bool> IntentarAgregarAsync(Pago pago, CancellationToken cancellationToken)
    {
        if (Ganador is null)
        {
            Ganador = new Pago
            {
                PagoId = Guid.NewGuid(),
                ReservaId = pago.ReservaId,
                SolicitudId = pago.SolicitudId,
                UsuarioId = pago.UsuarioId,
                MontoCentavos = pago.MontoCentavos,
                Resultado = pago.Resultado,
                TraceId = pago.TraceId,
                FechaUtc = pago.FechaUtc
            };
            Agregar(Ganador);
        }
        return base.IntentarAgregarAsync(pago, cancellationToken);
    }
}
