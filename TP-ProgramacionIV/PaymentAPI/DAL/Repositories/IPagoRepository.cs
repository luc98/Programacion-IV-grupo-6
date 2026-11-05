using PaymentAPI.DAL.Entities;

namespace PaymentAPI.DAL.Repositories;

public interface IPagoRepository
{
    Task<Pago?> ObtenerPorReservaAsync(Guid reservaId, CancellationToken cancellationToken);

    // Devuelve false si ya había un pago para esa reserva: otro intento ganó la
    // carrera y chocó con el índice UNIQUE.
    Task<bool> IntentarAgregarAsync(Pago pago, CancellationToken cancellationToken);
}
