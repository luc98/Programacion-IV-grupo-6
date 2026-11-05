using BookingAPI.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingAPI.DAL.Context
{
    // Al iniciar el microservicio, este IHostedService aplica automáticamente las migraciones pendientes
    // de Entity Framework Core y carga de manera idempotente el catálogo inicial de Eventos y Asientos
    // sincronizado con los identificadores semilla utilizados por Availability Site.
    public sealed class BookingDbInitializer : IHostedService
    {
        public static readonly Guid EventoConferenciaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid EventoRecitalId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        public static readonly Guid AsientoA01Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
        public static readonly Guid AsientoA02Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
        public static readonly Guid AsientoA03Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3");
        public static readonly Guid AsientoA04Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4");
        public static readonly Guid AsientoB01Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
        public static readonly Guid AsientoB02Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BookingDbInitializer> _logger;

        public BookingDbInitializer(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<BookingDbInitializer> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_configuration.GetValue<bool>("Database:AutoMigrate", true))
            {
                _logger.LogInformation("[DB INITIALIZER] Migración automática deshabilitada por configuración (Database:AutoMigrate=false).");
                return;
            }

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                BookingDbContext dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

                await dbContext.Database.MigrateAsync(cancellationToken);
                await SeedDatosInicialesAsync(dbContext, cancellationToken);

                _logger.LogInformation("[DB INITIALIZER] Migraciones de PostgreSQL aplicadas y datos semilla verificados correctamente.");
            }
            catch (Exception ex)
            {
                if (_configuration.GetValue<bool>("Database:FailFastOnStartup", false))
                {
                    throw;
                }

                _logger.LogWarning(
                    ex,
                    "[DB INITIALIZER] No fue posible conectar con PostgreSQL al iniciar ({Mensaje}). La API continuará levantada.",
                    ex.Message);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public static async Task SeedDatosInicialesAsync(BookingDbContext dbContext, CancellationToken cancellationToken = default)
        {
            HashSet<Guid> eventosExistentes = (await dbContext.Eventos
                .AsNoTracking()
                .Select(e => e.EventoId)
                .ToListAsync(cancellationToken))
                .ToHashSet();

            var eventosSemilla = new[]
            {
                new Evento
                {
                    EventoId = EventoConferenciaId,
                    Nombre = "Conferencia de prueba",
                    FechaEventoUtc = new DateTime(2026, 12, 10, 22, 0, 0, DateTimeKind.Utc)
                },
                new Evento
                {
                    EventoId = EventoRecitalId,
                    Nombre = "Recital de prueba",
                    FechaEventoUtc = new DateTime(2026, 12, 18, 23, 30, 0, DateTimeKind.Utc)
                }
            };

            bool huboCambios = false;
            foreach (Evento evento in eventosSemilla)
            {
                if (!eventosExistentes.Contains(evento.EventoId))
                {
                    dbContext.Eventos.Add(evento);
                    huboCambios = true;
                }
            }

            HashSet<Guid> asientosExistentes = (await dbContext.Asientos
                .AsNoTracking()
                .Select(a => a.AsientoId)
                .ToListAsync(cancellationToken))
                .ToHashSet();

            var asientosSemilla = new[]
            {
                new Asiento { AsientoId = AsientoA01Id, EventoId = EventoConferenciaId, Codigo = "A-01", Precio = 15000.00m, Version = 1 },
                new Asiento { AsientoId = AsientoA02Id, EventoId = EventoConferenciaId, Codigo = "A-02", Precio = 15000.00m, Version = 1 },
                new Asiento { AsientoId = AsientoA03Id, EventoId = EventoConferenciaId, Codigo = "A-03", Precio = 15000.00m, Version = 1 },
                new Asiento { AsientoId = AsientoA04Id, EventoId = EventoConferenciaId, Codigo = "A-04", Precio = 15000.00m, Version = 1 },
                new Asiento { AsientoId = AsientoB01Id, EventoId = EventoRecitalId, Codigo = "B-01", Precio = 22000.00m, Version = 1 },
                new Asiento { AsientoId = AsientoB02Id, EventoId = EventoRecitalId, Codigo = "B-02", Precio = 22000.00m, Version = 1 }
            };

            foreach (Asiento asiento in asientosSemilla)
            {
                if (!asientosExistentes.Contains(asiento.AsientoId))
                {
                    dbContext.Asientos.Add(asiento);
                    huboCambios = true;
                }
            }

            if (huboCambios)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
