using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentAPI.DAL.Context;
using PaymentAPI.DAL.Entities;

namespace PaymentAPI.DAL.Repositories;

public class PagoRepository(PaymentDbContext context) : IPagoRepository
{
    public Task<Pago?> ObtenerPorReservaAsync(Guid reservaId, CancellationToken cancellationToken) =>
        context.Pagos.AsNoTracking().FirstOrDefaultAsync(p => p.ReservaId == reservaId, cancellationToken);

    public async Task<bool> IntentarAgregarAsync(Pago pago, CancellationToken cancellationToken)
    {
        context.Pagos.Add(pago);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Reconocer el código de error de Postgres es un detalle de la base, por
            // eso se resuelve acá y la BLL solo recibe un false. Se suelta la entidad
            // para que el contexto no intente insertarla de nuevo.
            context.Entry(pago).State = EntityState.Detached;
            return false;
        }
    }
}
