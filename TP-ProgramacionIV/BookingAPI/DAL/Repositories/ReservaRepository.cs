using BookingAPI.DAL.Context;
using BookingAPI.DAL.Entities;
using BookingAPI.DAL.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookingAPI.DAL.Repositories
{
    // Implementacion concreta del repositorio de reservas sobre PostgreSQL.
    // ReservaRepository contiene las operaciones SQL concretas 
    public class ReservaRepository : IReservaRepository
    {
        private readonly BookingDbContext _context;

        public ReservaRepository(BookingDbContext context)
        {
            _context = context;
        }

        public async Task<Asiento?> ObtenerAsientoPorIdAsync(Guid asientoId)
        {
            // Debe mantener el tracking de EF Core activo para que funcione Version++
            return await _context.Asientos.FirstOrDefaultAsync(a => a.AsientoId == asientoId);
        }

        public async Task<bool> ExisteReservaActivaParaAsientoAsync(Guid asientoId)
        {
            DateTime ahoraUtc = DateTime.UtcNow;

            // Regla del dominio: Un asiento esta ocupado mientras tenga una reserva Retenida o Confirmada.
            // En cualquier otro estado (Rechazada o Vencida), el asiento vuelve a estar disponible.
            return await _context.Reservas.AnyAsync(r =>
                r.AsientoId == asientoId &&
                (r.Estado == EstadoReserva.Confirmada ||
                 (r.Estado == EstadoReserva.Retenida && r.FechaVencimientoUtc > ahoraUtc)));
        }

        public async Task GuardarReservaConcurrenteAsync(Reserva reserva, Asiento asiento)
        {
            // Control de concurrencia
            // Al retener un asiento libre, se incrementa 'asiento.Version' en +1 (se guarda el contexto con SaveChangesAsync).
            // Al ser un ConcurrencyToken (definido en BookingDbContext) EF Core agrega un "WHERE Version = @versionOriginal" en el UPDATE de PostgreSQL
            // Si otra operacion modifico el asiento en paralelo, SaveChangesAsync lanza DbUpdateConcurrencyException
            asiento.Version++;
            await _context.Reservas.AddAsync(reserva);
            await _context.SaveChangesAsync();
        }

        // Este metodo guarda en DbContext la reserva rechazada (cuando se dispare DbUpdateConcurrencyException) y 
        // limpia el ChangeTracker (operaciones fallidas previas del DbContext) para evitar arrastrar el conflicto anterior
        public async Task CrearReservaRechazadaAsync(Reserva reserva)
        {
            _context.ChangeTracker.Clear();
            await _context.Reservas.AddAsync(reserva);
            await _context.SaveChangesAsync();
        }

        public async Task ActualizarAsync(Reserva reserva)
        {
            _context.Reservas.Update(reserva);
            await _context.SaveChangesAsync();
        }

        public async Task<Reserva?> ObtenerReservaPorSolicitudIdAsync(Guid solicitudId)
        {
            return await _context.Reservas
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.SolicitudId == solicitudId);
        }

        public async Task<List<Reserva>> ObtenerReservasRetenidasVencidasAsync(DateTime ahoraUtc)
        {
            return await _context.Reservas
                .Where(r => r.Estado == EstadoReserva.Retenida && r.FechaVencimientoUtc <= ahoraUtc)
                .ToListAsync();
        }
    }
}
