using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PaymentAPI.BLL.DTOs;
using PaymentAPI.BLL.Services;
using PaymentAPI.DAL.Context;
using PaymentAPI.DAL.Entities;
using PaymentAPI.DAL.Enums;
using PaymentAPI.DAL.Repositories;
using PaymentAPI.Tests.Fakes;

namespace PaymentAPI.Tests.Integracion;

// Corre solo si existe la variable PAYMENT_TEST_PG con la cadena de conexión a un
// Postgres de prueba. Crea una base temporal, aplica las migraciones reales y la
// borra al terminar. Ejemplo:
//   PAYMENT_TEST_PG="Host=localhost;Port=55432;Username=postgres;Password=..." dotnet test
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PAYMENT_TEST_PG")))
        {
            Skip = "Definí PAYMENT_TEST_PG para correr los tests contra Postgres";
        }
    }
}

public sealed class PagoRepositoryPostgresTests : IAsyncLifetime
{
    private readonly string _conexion;

    public PagoRepositoryPostgresTests()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PAYMENT_TEST_PG") ?? string.Empty)
        {
            Database = $"payment_test_{Guid.NewGuid():N}"
        };
        _conexion = builder.ConnectionString;
    }

    private PaymentDbContext Contexto() =>
        new(new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql(_conexion).Options);

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PAYMENT_TEST_PG")))
        {
            await using var contexto = Contexto();
            await contexto.Database.MigrateAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PAYMENT_TEST_PG")))
        {
            await using var contexto = Contexto();
            await contexto.Database.EnsureDeletedAsync();
        }
    }

    private static Pago NuevoPago(Guid reservaId) => new()
    {
        PagoId = Guid.NewGuid(),
        ReservaId = reservaId,
        SolicitudId = Guid.NewGuid(),
        UsuarioId = "ana",
        MontoCentavos = 1_500,
        Resultado = ResultadoPago.Aprobado,
        TraceId = "trace",
        FechaUtc = DateTime.UtcNow
    };

    [PostgresFact]
    public async Task ElIndiceUnico_FrenaElSegundoPagoDeLaMismaReserva()
    {
        var reservaId = Guid.NewGuid();
        await using var contexto = Contexto();
        var repositorio = new PagoRepository(contexto);

        Assert.True(await repositorio.IntentarAgregarAsync(NuevoPago(reservaId), CancellationToken.None));
        Assert.False(await repositorio.IntentarAgregarAsync(NuevoPago(reservaId), CancellationToken.None));

        // El contexto quedó usable después del choque
        Assert.True(await repositorio.IntentarAgregarAsync(NuevoPago(Guid.NewGuid()), CancellationToken.None));
        Assert.Equal(1, await contexto.Pagos.CountAsync(p => p.ReservaId == reservaId));
    }

    [PostgresFact]
    public async Task DosIntentosSimultaneos_DejanUnSoloPago()
    {
        // Los dos intentos pasan la verificación inicial antes de que ninguno guarde:
        // el banco los retiene hasta que llegaron los dos. Así se fuerza la carrera.
        var llegaron = 0;
        var ambos = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var banco = new BancoFalso
        {
            AntesDeDecidir = async ct =>
            {
                if (Interlocked.Increment(ref llegaron) == 2)
                {
                    ambos.SetResult();
                }
                await ambos.Task.WaitAsync(ct);
            }
        };
        var solicitud = new SolicitudPagoDto(Guid.NewGuid(), Guid.NewGuid(), "ana", 1_500, "trace");

        async Task<ResultadoPagoDto> Intento()
        {
            await using var contexto = Contexto();
            var servicio = new PagoService(new PagoRepository(contexto), banco, TimeProvider.System, NullLogger<PagoService>.Instance);
            return await servicio.ProcesarAsync(solicitud, CancellationToken.None);
        }

        var resultados = await Task.WhenAll(Intento(), Intento()).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(resultados[0].PagoId, resultados[1].PagoId);
        Assert.Single(resultados, r => r.EraRepetido);
        await using var verificacion = Contexto();
        Assert.Equal(1, await verificacion.Pagos.CountAsync(p => p.ReservaId == solicitud.ReservaId));
    }
}
