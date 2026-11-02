using Microsoft.EntityFrameworkCore;
using PaymentAPI.DAL.Entities;

namespace PaymentAPI.DAL.Context;

// Base PostgreSQL propia de Payment: ningún otro servicio la lee ni la escribe.
// Si Booking necesita saber de un pago, lo pregunta por gRPC.
public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Pago> Pagos => Set<Pago>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Pago>(entity =>
        {
            entity.HasKey(p => p.PagoId);

            // La idempotencia la garantiza la base, no el código: si dos intentos
            // del mismo cobro llegan a la vez, el segundo INSERT falla y nunca
            // quedan dos pagos para una reserva.
            entity.HasIndex(p => p.ReservaId).IsUnique();

            entity.Property(p => p.UsuarioId).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Motivo).HasMaxLength(500);
            entity.Property(p => p.TraceId).IsRequired().HasMaxLength(100);
        });
    }
}
