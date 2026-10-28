using BookingAPI.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingAPI.DAL.Context
{
    // BookingDbContext maneja la conexion y el mapeo relacional hacia la base de datos PostgreSQL propia del microservicio 
    // (cada microservicio tiene su propia DB)
    public class BookingDbContext : DbContext
    {
        public BookingDbContext(DbContextOptions<BookingDbContext> options) : base(options)
        {
        }
        // Con Set<T>(); aseguramos que sean solo lectura, imposibilitando pisar o reasignar la referencia del DbSet
        public DbSet<Evento> Eventos => Set<Evento>();
        public DbSet<Asiento> Asientos => Set<Asiento>();
        public DbSet<Reserva> Reservas => Set<Reserva>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configuramos como se mappean las clases de C# a las tablas y columnas de PostgreSQL
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Asiento>(entity =>
            {
                // Con HasKey defino las llaves primarias de cada tabla por medio de FluentAPI
                entity.HasKey(a => a.AsientoId);
                entity.Property(a => a.Codigo).IsRequired().HasMaxLength(50);
                entity.Property(a => a.Precio).HasColumnType("numeric(18,2)");
                // Se configura la columna 'Version' de Asiento como Token de Concurrencia en PostgreSQL.
                // Evitando condiciones de carrera
                entity.Property(a => a.Version).IsConcurrencyToken();
            });

            modelBuilder.Entity<Evento>(entity =>
            {
                entity.HasKey(e => e.EventoId);
                entity.Property(e => e.Nombre).IsRequired().HasMaxLength(200);
            });

            modelBuilder.Entity<Reserva>(entity =>
            {
                entity.HasKey(r => r.ReservaId);
                entity.HasIndex(r => r.SolicitudId).IsUnique();
                entity.HasIndex(r => new { r.AsientoId, r.Estado });
            });
        }
    }
}
