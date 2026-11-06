using System.Collections.Concurrent;
using System.Net;
using BookingAPI.BLL.DTOs;
using BookingAPI.BLL.Gateways;
using BookingAPI.BLL.Interfaces;
using BookingAPI.BLL.Jobs;
using BookingAPI.BLL.Services;
using BookingAPI.DAL.Context;
using BookingAPI.DAL.Entities;
using BookingAPI.DAL.Enums;
using BookingAPI.DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

int fallos = 0;

async Task EjecutarPruebaAsync(string nombre, Func<Task> prueba)
{
    try
    {
        await prueba();
        Console.WriteLine($"[PASS] {nombre}");
    }
    catch (Exception ex)
    {
        fallos++;
        Console.Error.WriteLine($"[FAIL] {nombre}: {ex.Message}");
    }
}

await EjecutarPruebaAsync(
    "1. Concurrencia simultánea (20 solicitudes sobre el mismo asiento): solo 1 confirma y 19 se rechazan por asiento ocupado",
    async () =>
    {
        var store = new FakeOptimisticPostgresStore();
        Guid eventoId = BookingDbInitializer.EventoConferenciaId;
        Guid asientoId = BookingDbInitializer.AsientoA01Id;
        store.SeedAsiento(asientoId, eventoId, "A-01", 15000m, versionInicial: 1);

        var barreraLectura = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int lectoresListos = 0;
        const int totalCompetidores = 20;

        store.OnAsientoLeido = () =>
        {
            if (Interlocked.Increment(ref lectoresListos) >= totalCompetidores)
            {
                barreraLectura.TrySetResult();
            }
            return barreraLectura.Task;
        };

        var solicitudes = Enumerable.Range(1, totalCompetidores)
            .Select(i => new SolicitudReservaDto
            {
                SolicitudId = Guid.NewGuid(),
                EventoId = eventoId,
                AsientoId = asientoId,
                UsuarioId = $"usuario-{i}",
                TraceId = $"trace-concurrente-{i}"
            })
            .ToList();

        await Task.WhenAll(solicitudes.Select(async solicitud =>
        {
            var repo = new FakeOptimisticReservaRepository(store);
            var payment = new FakePaymentGateway(exitoso: true, servicioCaido: false);
            var publisher = new FakeEventPublisher();
            var service = new BookingService(repo, payment, publisher);
            await service.ProcesarSolicitudReservaAsync(solicitud);
        }));

        List<Reserva> reservas = store.ObtenerTodasLasReservas();
        int confirmadas = reservas.Count(r => r.Estado == EstadoReserva.Confirmada);
        int rechazadasOcupado = reservas.Count(r =>
            r.Estado == EstadoReserva.Rechazada && r.Motivo == MotivoRechazo.AsientoOcupado);

        Verificar(reservas.Count == totalCompetidores, $"Se esperaban {totalCompetidores} reservas persistidas, hubo {reservas.Count}.");
        Verificar(confirmadas == 1, $"Exactamente 1 reserva debía quedar Confirmada, hubo {confirmadas}.");
        Verificar(rechazadasOcupado == totalCompetidores - 1, $"Exactamente {totalCompetidores - 1} debían rechazarse por AsientoOcupado, hubo {rechazadasOcupado}.");
        Verificar(store.ObtenerVersionActual(asientoId) == 2, $"La versión del asiento debía incrementarse exactamente a 2, fue {store.ObtenerVersionActual(asientoId)}.");
    });

await EjecutarPruebaAsync(
    "2. Happy Path y propagación continua de TraceId hacia ReservaCreada y ReservaConfirmada",
    async () =>
    {
        var store = new FakeOptimisticPostgresStore();
        Guid asientoId = BookingDbInitializer.AsientoA02Id;
        store.SeedAsiento(asientoId, BookingDbInitializer.EventoConferenciaId, "A-02", 15000m, 1);

        var repo = new FakeOptimisticReservaRepository(store);
        var payment = new FakePaymentGateway(exitoso: true, servicioCaido: false);
        var publisher = new FakeEventPublisher();
        var service = new BookingService(repo, payment, publisher);

        Guid solicitudId = Guid.NewGuid();
        const string traceEsperado = "trace-origen-availability-001";

        await service.ProcesarSolicitudReservaAsync(new SolicitudReservaDto
        {
            SolicitudId = solicitudId,
            EventoId = BookingDbInitializer.EventoConferenciaId,
            AsientoId = asientoId,
            UsuarioId = "felipe",
            TraceId = traceEsperado
        });

        ReservaResultadoDto? estado = await service.ObtenerEstadoPorSolicitudIdAsync(solicitudId);
        Verificar(estado is not null, "El estado de la solicitud no debe ser nulo.");
        Verificar(estado!.Estado == "Confirmada", $"Se esperaba 'Confirmada', fue '{estado.Estado}'.");
        Verificar(estado.MotivoRechazo == string.Empty, $"No debía haber motivo de rechazo, fue '{estado.MotivoRechazo}'.");
        Verificar(publisher.Eventos.Count == 2, $"Se esperaban 2 eventos publicados, hubo {publisher.Eventos.Count}.");
        Verificar(publisher.Eventos.All(e => e.TraceId == traceEsperado), "Todos los eventos publicados deben conservar el mismo TraceId de origen.");
    });

await EjecutarPruebaAsync(
    "3. Fallback de TraceId a SolicitudId cuando el mensaje entrante no trae TraceId",
    async () =>
    {
        var store = new FakeOptimisticPostgresStore();
        Guid asientoId = BookingDbInitializer.AsientoB01Id;
        store.SeedAsiento(asientoId, BookingDbInitializer.EventoRecitalId, "B-01", 22000m, 1);

        var repo = new FakeOptimisticReservaRepository(store);
        var payment = new FakePaymentGateway(exitoso: true, servicioCaido: false);
        var publisher = new FakeEventPublisher();
        var service = new BookingService(repo, payment, publisher);

        Guid solicitudId = Guid.NewGuid();
        await service.ProcesarSolicitudReservaAsync(new SolicitudReservaDto
        {
            SolicitudId = solicitudId,
            EventoId = BookingDbInitializer.EventoRecitalId,
            AsientoId = asientoId,
            UsuarioId = "roman",
            TraceId = null
        });

        Verificar(
            publisher.Eventos.All(e => e.TraceId == solicitudId.ToString()),
            "Cuando TraceId es nulo, debe usarse SolicitudId como TraceId de respaldo.");
    });

await EjecutarPruebaAsync(
    "4. Idempotencia ante reentrega duplicada de Azure Service Bus (mismo SolicitudId)",
    async () =>
    {
        var store = new FakeOptimisticPostgresStore();
        Guid asientoId = BookingDbInitializer.AsientoA03Id;
        store.SeedAsiento(asientoId, BookingDbInitializer.EventoConferenciaId, "A-03", 15000m, 1);

        var repo = new FakeOptimisticReservaRepository(store);
        var payment = new FakePaymentGateway(exitoso: true, servicioCaido: false);
        var publisher = new FakeEventPublisher();
        var service = new BookingService(repo, payment, publisher);

        var solicitud = new SolicitudReservaDto
        {
            SolicitudId = Guid.NewGuid(),
            EventoId = BookingDbInitializer.EventoConferenciaId,
            AsientoId = asientoId,
            UsuarioId = "usuario-idempotente"
        };

        await service.ProcesarSolicitudReservaAsync(solicitud);
        await service.ProcesarSolicitudReservaAsync(solicitud);

        Verificar(payment.CantidadCobros == 1, $"Solo debía invocarse Payment 1 vez, se invocó {payment.CantidadCobros}.");
        Verificar(store.ObtenerTodasLasReservas().Count == 1, "No debe duplicarse la reserva ante un SolicitudId repetido.");
    });

await EjecutarPruebaAsync(
    "5. Degradación elegante (pago no disponible vs pago fallido) y liberación inmediata del asiento",
    async () =>
    {
        var store = new FakeOptimisticPostgresStore();
        Guid asientoId = BookingDbInitializer.AsientoA04Id;
        store.SeedAsiento(asientoId, BookingDbInitializer.EventoConferenciaId, "A-04", 15000m, 1);

        var repo = new FakeOptimisticReservaRepository(store);
        var publisher = new FakeEventPublisher();

        Guid solCaido = Guid.NewGuid();
        var serviceCaido = new BookingService(repo, new FakePaymentGateway(exitoso: false, servicioCaido: true), publisher);
        await serviceCaido.ProcesarSolicitudReservaAsync(new SolicitudReservaDto
        {
            SolicitudId = solCaido,
            EventoId = BookingDbInitializer.EventoConferenciaId,
            AsientoId = asientoId,
            UsuarioId = "u1"
        });

        ReservaResultadoDto? resCaido = await serviceCaido.ObtenerEstadoPorSolicitudIdAsync(solCaido);
        Verificar(resCaido!.Estado == "Rechazada" && resCaido.MotivoRechazo == "pago no disponible",
            $"Esperado Rechazada/'pago no disponible', fue {resCaido.Estado}/'{resCaido.MotivoRechazo}'.");

        Guid solFallido = Guid.NewGuid();
        var serviceFallido = new BookingService(repo, new FakePaymentGateway(exitoso: false, servicioCaido: false), publisher);
        await serviceFallido.ProcesarSolicitudReservaAsync(new SolicitudReservaDto
        {
            SolicitudId = solFallido,
            EventoId = BookingDbInitializer.EventoConferenciaId,
            AsientoId = asientoId,
            UsuarioId = "u2"
        });

        ReservaResultadoDto? resFallido = await serviceFallido.ObtenerEstadoPorSolicitudIdAsync(solFallido);
        Verificar(resFallido!.Estado == "Rechazada" && resFallido.MotivoRechazo == "pago fallido",
            $"Esperado Rechazada/'pago fallido', fue {resFallido.Estado}/'{resFallido.MotivoRechazo}'.");

        Guid solExito = Guid.NewGuid();
        var serviceExito = new BookingService(repo, new FakePaymentGateway(exitoso: true, servicioCaido: false), publisher);
        await serviceExito.ProcesarSolicitudReservaAsync(new SolicitudReservaDto
        {
            SolicitudId = solExito,
            EventoId = BookingDbInitializer.EventoConferenciaId,
            AsientoId = asientoId,
            UsuarioId = "u3"
        });

        ReservaResultadoDto? resExito = await serviceExito.ObtenerEstadoPorSolicitudIdAsync(solExito);
        Verificar(resExito!.Estado == "Confirmada", "Tras un rechazo de pago el asiento debe quedar disponible para el siguiente usuario.");
    });

await EjecutarPruebaAsync(
    "6. ExpirationWorker marca como Vencida una reserva retenida con TTL expirado y libera el asiento",
    async () =>
    {
        var store = new FakeOptimisticPostgresStore();
        Guid asientoId = BookingDbInitializer.AsientoB02Id;
        store.SeedAsiento(asientoId, BookingDbInitializer.EventoRecitalId, "B-02", 22000m, 1);

        Guid solicitudVencidaId = Guid.NewGuid();
        store.InsertarReservaDirecta(new Reserva
        {
            ReservaId = Guid.NewGuid(),
            SolicitudId = solicitudVencidaId,
            AsientoId = asientoId,
            EventoId = BookingDbInitializer.EventoRecitalId,
            UsuarioId = "usuario-ttl",
            Estado = EstadoReserva.Retenida,
            Motivo = MotivoRechazo.Ninguno,
            FechaCreacionUtc = DateTime.UtcNow.AddMinutes(-6),
            FechaVencimientoUtc = DateTime.UtcNow.AddMinutes(-1)
        });

        var services = new ServiceCollection();
        services.AddScoped<IReservaRepository>(_ => new FakeOptimisticReservaRepository(store));
        ServiceProvider provider = services.BuildServiceProvider();

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ExpirationWorker:IntervalSeconds"] = "5" })
            .Build();

        var worker = new ExpirationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            NullLogger<ExpirationWorker>.Instance);

        await worker.ProcesarReservasVencidasAsync();

        var service = new BookingService(
            new FakeOptimisticReservaRepository(store),
            new FakePaymentGateway(exitoso: true, servicioCaido: false),
            new FakeEventPublisher());

        ReservaResultadoDto? estadoAnterior = await service.ObtenerEstadoPorSolicitudIdAsync(solicitudVencidaId);
        Verificar(estadoAnterior!.Estado == "Vencida", $"La reserva expirada debía pasar a 'Vencida', fue '{estadoAnterior.Estado}'.");

        Guid nuevaSolicitudId = Guid.NewGuid();
        await service.ProcesarSolicitudReservaAsync(new SolicitudReservaDto
        {
            SolicitudId = nuevaSolicitudId,
            EventoId = BookingDbInitializer.EventoRecitalId,
            AsientoId = asientoId,
            UsuarioId = "nuevo-usuario"
        });

        ReservaResultadoDto? nuevoEstado = await service.ObtenerEstadoPorSolicitudIdAsync(nuevaSolicitudId);
        Verificar(nuevoEstado!.Estado == "Confirmada", "El asiento liberado por TTL debía poder confirmarse por otro usuario.");
    });

await EjecutarPruebaAsync(
    "7. TraceIdMiddleware y ReservasController propagan x-trace-id y responden 200 OK / 404 NotFound",
    async () =>
    {
        var middleware = new BookingAPI.Presentation.Middlewares.TraceIdMiddleware(_ => Task.CompletedTask);
        var httpConTrace = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpConTrace.Request.Headers["x-trace-id"] = "trace-http-entrante-99";
        await middleware.InvokeAsync(httpConTrace, NullLogger<BookingAPI.Presentation.Middlewares.TraceIdMiddleware>.Instance);

        Verificar(httpConTrace.Response.Headers["x-trace-id"] == "trace-http-entrante-99",
            "TraceIdMiddleware debe preservar el header x-trace-id entrante.");

        var httpSinTrace = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        await middleware.InvokeAsync(httpSinTrace, NullLogger<BookingAPI.Presentation.Middlewares.TraceIdMiddleware>.Instance);
        Verificar(!string.IsNullOrWhiteSpace(httpSinTrace.Response.Headers["x-trace-id"]),
            "TraceIdMiddleware debe generar un x-trace-id si el cliente no lo envió.");

        var store = new FakeOptimisticPostgresStore();
        Guid asientoId = BookingDbInitializer.AsientoA01Id;
        store.SeedAsiento(asientoId, BookingDbInitializer.EventoConferenciaId, "A-01", 15000m, 1);
        var service = new BookingService(
            new FakeOptimisticReservaRepository(store),
            new FakePaymentGateway(true, false),
            new FakeEventPublisher());

        Guid solicitudExistente = Guid.NewGuid();
        await service.ProcesarSolicitudReservaAsync(new SolicitudReservaDto
        {
            SolicitudId = solicitudExistente,
            EventoId = BookingDbInitializer.EventoConferenciaId,
            AsientoId = asientoId,
            UsuarioId = "web-user"
        });

        var controller = new BookingAPI.Presentation.Controllers.ReservasController(service);
        var res200 = await controller.ObtenerEstadoSolicitud(solicitudExistente);
        Verificar(res200 is Microsoft.AspNetCore.Mvc.OkObjectResult, "Solicitud existente debe devolver 200 OK.");

        var res404 = await controller.ObtenerEstadoSolicitud(Guid.NewGuid());
        Verificar(res404 is Microsoft.AspNetCore.Mvc.NotFoundObjectResult, "Solicitud inexistente debe devolver 404 NotFound.");
    });

string? pgConn = Environment.GetEnvironmentVariable("BOOKING_TEST_POSTGRES");
if (!string.IsNullOrWhiteSpace(pgConn))
{
    await EjecutarPruebaAsync(
        "8. Integración real contra PostgreSQL (MigrateAsync + Seed + 10 reservas simultáneas sobre Npgsql)",
        async () =>
        {
            DbContextOptions<BookingDbContext> CrearOpciones() =>
                new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(pgConn).Options;

            Guid asientoPruebaId = Guid.NewGuid();
            Guid eventoPruebaId = BookingDbInitializer.EventoConferenciaId;

            await using (var setupDb = new BookingDbContext(CrearOpciones()))
            {
                await setupDb.Database.MigrateAsync();
                await BookingDbInitializer.SeedDatosInicialesAsync(setupDb);
                setupDb.Asientos.Add(new Asiento
                {
                    AsientoId = asientoPruebaId,
                    EventoId = eventoPruebaId,
                    Codigo = $"TEST-{asientoPruebaId.ToString()[..6]}",
                    Precio = 10000m,
                    Version = 1
                });
                await setupDb.SaveChangesAsync();
            }

            const int competidoresPg = 10;
            var solicitudesPg = Enumerable.Range(1, competidoresPg)
                .Select(i => new SolicitudReservaDto
                {
                    SolicitudId = Guid.NewGuid(),
                    EventoId = eventoPruebaId,
                    AsientoId = asientoPruebaId,
                    UsuarioId = $"pg-user-{i}"
                })
                .ToList();

            await Task.WhenAll(solicitudesPg.Select(async sol =>
            {
                await using var db = new BookingDbContext(CrearOpciones());
                var repo = new ReservaRepository(db);
                var service = new BookingService(repo, new FakePaymentGateway(true, false), new FakeEventPublisher());
                await service.ProcesarSolicitudReservaAsync(sol);
            }));

            await using (var verifyDb = new BookingDbContext(CrearOpciones()))
            {
                List<Reserva> creadas = await verifyDb.Reservas
                    .AsNoTracking()
                    .Where(r => r.AsientoId == asientoPruebaId)
                    .ToListAsync();

                Verificar(creadas.Count(r => r.Estado == EstadoReserva.Confirmada) == 1, "En PostgreSQL real solo 1 debe quedar Confirmada.");
                Verificar(creadas.Count(r => r.Estado == EstadoReserva.Rechazada && r.Motivo == MotivoRechazo.AsientoOcupado) == competidoresPg - 1,
                    "En PostgreSQL real las demás deben quedar Rechazadas por AsientoOcupado.");
            }
        });
}
else
{
    Console.WriteLine("[SKIP] 8. Integración PostgreSQL en vivo omitida (definir variable BOOKING_TEST_POSTGRES para ejecutar contra BookingDb).");
}

await EjecutarPruebaAsync(
    "9. HttpNotificationPublisher maneja servicio caído y error HTTP 500 sin lanzar excepción",
    async () =>
    {
        var handlerFalso = new HttpHandlerMock((req, ct) =>
        {
            if (req.Headers.TryGetValues("x-trace-id", out var vals) && vals.Contains("trace-caida"))
            {
                throw new HttpRequestException("Simulando falla de red / Notification Service caído");
            }

            if (req.Headers.TryGetValues("x-trace-id", out var vals500) && vals500.Contains("trace-500"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("http://notification-test:5050") };
        var inMemoryConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NotificationService:Endpoint"] = "/api/notifications",
                ["NotificationService:TimeoutSegundos"] = "1"
            })
            .Build();

        var publisher = new HttpNotificationPublisher(httpClient, inMemoryConfig, NullLogger<HttpNotificationPublisher>.Instance);
        var reserva = new Reserva
        {
            ReservaId = Guid.NewGuid(),
            SolicitudId = Guid.NewGuid(),
            AsientoId = Guid.NewGuid(),
            EventoId = Guid.NewGuid(),
            UsuarioId = "test-user",
            Estado = EstadoReserva.Retenida,
            FechaCreacionUtc = DateTime.UtcNow,
            FechaVencimientoUtc = DateTime.UtcNow.AddMinutes(5)
        };

        await publisher.PublicarReservaCreadaAsync(reserva, "trace-caida");
        await publisher.PublicarReservaConfirmadaAsync(reserva, "trace-500");
        await publisher.PublicarReservaConfirmadaAsync(reserva, "trace-ok");
    });

if (fallos > 0)
{
    Console.Error.WriteLine($"\nResultado final: {fallos} prueba(s) fallaron.");
    return 1;
}

Console.WriteLine("\nResultado final: TODAS LAS PRUEBAS PASARON EXITOSAMENTE.");
return 0;

static void Verificar(bool condicion, string mensajeError)
{
    if (!condicion)
    {
        throw new InvalidOperationException(mensajeError);
    }
}

internal sealed class FakePaymentGateway : IPaymentGateway
{
    private readonly bool _exitoso;
    private readonly bool _servicioCaido;
    private int _cantidadCobros;

    public FakePaymentGateway(bool exitoso, bool servicioCaido)
    {
        _exitoso = exitoso;
        _servicioCaido = servicioCaido;
    }

    public int CantidadCobros => _cantidadCobros;

    public Task<ResultadoCobroDto> CobrarReservaAsync(Reserva reserva, decimal monto)
    {
        Interlocked.Increment(ref _cantidadCobros);
        return Task.FromResult(new ResultadoCobroDto
        {
            Exitoso = _exitoso,
            ServicioCaido = _servicioCaido,
            ErrorMensaje = _exitoso ? null : (_servicioCaido ? "Servicio no disponible" : "Fondos insuficientes")
        });
    }
}

internal sealed class FakeEventPublisher : IEventPublisher
{
    public ConcurrentQueue<(string Tipo, Guid ReservaId, string? TraceId)> Eventos { get; } = new();

    public Task PublicarReservaCreadaAsync(Reserva reserva, string? traceId = null)
    {
        Eventos.Enqueue(("ReservaCreada", reserva.ReservaId, traceId));
        return Task.CompletedTask;
    }

    public Task PublicarReservaConfirmadaAsync(Reserva reserva, string? traceId = null)
    {
        Eventos.Enqueue(("ReservaConfirmada", reserva.ReservaId, traceId));
        return Task.CompletedTask;
    }
}

internal sealed class FakeOptimisticPostgresStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Asiento> _asientos = new();
    private readonly Dictionary<Guid, Reserva> _reservas = new();

    public Func<Task>? OnAsientoLeido { get; set; }

    public void SeedAsiento(Guid asientoId, Guid eventoId, string codigo, decimal precio, int versionInicial)
    {
        lock (_sync)
        {
            _asientos[asientoId] = new Asiento
            {
                AsientoId = asientoId,
                EventoId = eventoId,
                Codigo = codigo,
                Precio = precio,
                Version = versionInicial
            };
        }
    }

    public void InsertarReservaDirecta(Reserva reserva)
    {
        lock (_sync)
        {
            _reservas[reserva.ReservaId] = ClonarReserva(reserva);
        }
    }

    public int ObtenerVersionActual(Guid asientoId)
    {
        lock (_sync)
        {
            return _asientos[asientoId].Version;
        }
    }

    public List<Reserva> ObtenerTodasLasReservas()
    {
        lock (_sync)
        {
            return _reservas.Values.Select(ClonarReserva).ToList();
        }
    }

    public async Task<Asiento?> LeerAsientoAsync(Guid asientoId)
    {
        Asiento? copia;
        lock (_sync)
        {
            copia = _asientos.TryGetValue(asientoId, out Asiento? actual)
                ? new Asiento
                {
                    AsientoId = actual.AsientoId,
                    EventoId = actual.EventoId,
                    Codigo = actual.Codigo,
                    Precio = actual.Precio,
                    Version = actual.Version
                }
                : null;
        }

        if (copia is not null && OnAsientoLeido is not null)
        {
            await OnAsientoLeido();
        }

        return copia;
    }

    public bool ExisteReservaActiva(Guid asientoId)
    {
        lock (_sync)
        {
            DateTime ahora = DateTime.UtcNow;
            return _reservas.Values.Any(r =>
                r.AsientoId == asientoId &&
                (r.Estado == EstadoReserva.Confirmada ||
                 (r.Estado == EstadoReserva.Retenida && r.FechaVencimientoUtc > ahora)));
        }
    }

    public void GuardarConcurrente(Reserva reserva, Asiento asientoLeido)
    {
        lock (_sync)
        {
            if (!_asientos.TryGetValue(asientoLeido.AsientoId, out Asiento? actual))
            {
                throw new DbUpdateConcurrencyException("El asiento no existe.");
            }

            if (actual.Version != asientoLeido.Version)
            {
                throw new DbUpdateConcurrencyException("Conflicto de concurrencia optimista: Version modificada por otra transacción.");
            }

            actual.Version++;
            asientoLeido.Version = actual.Version;
            _reservas[reserva.ReservaId] = ClonarReserva(reserva);
        }
    }

    public void GuardarRechazada(Reserva reserva)
    {
        lock (_sync)
        {
            _reservas[reserva.ReservaId] = ClonarReserva(reserva);
        }
    }

    public void ActualizarReserva(Reserva reserva)
    {
        lock (_sync)
        {
            _reservas[reserva.ReservaId] = ClonarReserva(reserva);
        }
    }

    public Reserva? ObtenerPorSolicitudId(Guid solicitudId)
    {
        lock (_sync)
        {
            Reserva? encontrada = _reservas.Values.FirstOrDefault(r => r.SolicitudId == solicitudId);
            return encontrada is null ? null : ClonarReserva(encontrada);
        }
    }

    public List<Reserva> ObtenerVencidas(DateTime ahoraUtc)
    {
        lock (_sync)
        {
            return _reservas.Values
                .Where(r => r.Estado == EstadoReserva.Retenida && r.FechaVencimientoUtc <= ahoraUtc)
                .Select(ClonarReserva)
                .ToList();
        }
    }

    private static Reserva ClonarReserva(Reserva r) => new()
    {
        ReservaId = r.ReservaId,
        SolicitudId = r.SolicitudId,
        AsientoId = r.AsientoId,
        EventoId = r.EventoId,
        UsuarioId = r.UsuarioId,
        Estado = r.Estado,
        Motivo = r.Motivo,
        FechaCreacionUtc = r.FechaCreacionUtc,
        FechaVencimientoUtc = r.FechaVencimientoUtc
    };
}

internal sealed class FakeOptimisticReservaRepository : IReservaRepository
{
    private readonly FakeOptimisticPostgresStore _store;

    public FakeOptimisticReservaRepository(FakeOptimisticPostgresStore store)
    {
        _store = store;
    }

    public Task<Asiento?> ObtenerAsientoPorIdAsync(Guid asientoId) =>
        _store.LeerAsientoAsync(asientoId);

    public Task<bool> ExisteReservaActivaParaAsientoAsync(Guid asientoId) =>
        Task.FromResult(_store.ExisteReservaActiva(asientoId));

    public Task GuardarReservaConcurrenteAsync(Reserva nuevaReserva, Asiento asiento)
    {
        _store.GuardarConcurrente(nuevaReserva, asiento);
        return Task.CompletedTask;
    }

    public Task CrearReservaRechazadaAsync(Reserva reservaRechazada)
    {
        _store.GuardarRechazada(reservaRechazada);
        return Task.CompletedTask;
    }

    public Task ActualizarAsync(Reserva reserva)
    {
        _store.ActualizarReserva(reserva);
        return Task.CompletedTask;
    }

    public Task<Reserva?> ObtenerReservaPorSolicitudIdAsync(Guid solicitudId) =>
        Task.FromResult(_store.ObtenerPorSolicitudId(solicitudId));

    public Task<List<Reserva>> ObtenerReservasRetenidasVencidasAsync(DateTime fechaCorteUtc) =>
        Task.FromResult(_store.ObtenerVencidas(fechaCorteUtc));
}

internal sealed class HttpHandlerMock(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => handler(request, cancellationToken);
}
