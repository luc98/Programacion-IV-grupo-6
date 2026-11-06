using System.Diagnostics;
using BookingAPI.BLL.DTOs;
using BookingAPI.BLL.Gateways;
using BookingAPI.BLL.Interfaces;
using BookingAPI.DAL.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Demo de resiliencia del cobro (punto 3.j). Usa el cliente de Payment de Booking registrado igual
// que en Booking (AddClientePayment), pero sin Service Bus ni la base de Booking: muestra los
// reintentos, el circuit breaker y la degradación aislados del resto del sistema.
//
//   dotnet run               Interactivo: cada opción cobra una reserva. Payment se apaga y se
//                            prende desde otra terminal con docker compose stop/start payment.
//   dotnet run -- --guion    Corre solos los escenarios de la demo, apagando y prendiendo Payment.
//
// El secreto de booking-api no va en el repo: se pasa con Authentication__ClientSecret.

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

if (string.IsNullOrWhiteSpace(config["Authentication:ClientSecret"]))
{
    Console.Error.WriteLine("Falta el secreto de booking-api: definí la variable Authentication__ClientSecret");
    return 1;
}

var services = new ServiceCollection();
// En Booking esto lo registra el host web; en una consola hay que hacerlo a mano
services.AddSingleton<IConfiguration>(config);
services.AddLogging(logging => logging
    .AddConfiguration(config.GetSection("Logging"))
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss.fff "; }));
services.AddClientePayment(config);
using var proveedor = services.BuildServiceProvider();
proveedor.ValidarClientePayment();

var resumen = new List<(string Escenario, string Resultado, long Ms)>();

if (args.Contains("--guion"))
{
    await GuionAsync();
    Console.WriteLine("\n===== Resumen");
    foreach (var (escenario, resultado, ms) in resumen)
    {
        Console.WriteLine($"{escenario,-62} {resultado,-32} {ms,6} ms");
    }
}
else
{
    await InteractivoAsync();
}
return 0;

// Cobra una reserva nueva con el gateway de Booking y muestra cómo terminaría la reserva
async Task<ResultadoCobroDto> Cobrar(string escenario, decimal monto)
{
    using var scope = proveedor.CreateScope();
    var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>();
    var reserva = new Reserva { ReservaId = Guid.NewGuid(), SolicitudId = Guid.NewGuid(), UsuarioId = "usuario-demo" };

    Console.WriteLine($"\n===== {escenario}");
    var cronometro = Stopwatch.StartNew();
    var resultado = await gateway.CobrarReservaAsync(reserva, monto);
    var estado = resultado.Exitoso ? "Confirmada"
        : resultado.ServicioCaido ? "Rechazada (PagoNoDisponible)"
        : "Rechazada (PagoFallido)";
    Console.WriteLine($"===== => {estado} en {cronometro.ElapsedMilliseconds} ms {resultado.ErrorMensaje}");
    resumen.Add((escenario, estado, cronometro.ElapsedMilliseconds));
    return resultado;
}

async Task InteractivoAsync()
{
    Console.WriteLine("""

        Demo de resiliencia del cobro
          1  Cobrar una reserva de $1.500 (se aprueba)
          2  Cobrar una reserva de $60.000 (fondos insuficientes)
          3  Cobrar 5 reservas seguidas
          q  Salir
        En otra terminal, desde la raíz del repo:
          docker compose -f docker-keycloak-booking-payment.yaml stop payment
          docker compose -f docker-keycloak-booking-payment.yaml start payment
        """);
    while (true)
    {
        Console.Write("\n> ");
        switch (Console.ReadLine()?.Trim())
        {
            case "1": await Cobrar("Reserva de $1.500", 1500m); break;
            case "2": await Cobrar("Reserva de $60.000", 60000m); break;
            case "3":
                for (var i = 1; i <= 5; i++)
                {
                    await Cobrar($"Reserva {i} de 5", 1500m);
                }
                break;
            case "q" or null: return;
        }
    }
}

async Task GuionAsync()
{
    await Cobrar("1. Happy path", 1500m);
    await Cobrar("2. Fondos insuficientes", 60000m);

    await Compose("stop payment");
    var circuitoAbierto = Stopwatch.StartNew();
    await Cobrar("3. Payment caído: reintentos y circuito abierto", 1500m);
    await Cobrar("4. Circuito abierto: rechazo sin llamar a Payment", 1500m);

    await Compose("start payment");
    await EsperarHasta(circuitoAbierto, TimeSpan.FromSeconds(16), "termine el corte de 15 s");
    await Cobrar("5. Pasó el corte: el circuito prueba y se cierra", 1500m);
    await Cobrar("5b. Circuito cerrado", 1500m);

    await Compose("up -d payment", ("PAYMENT_RETARDO_MS", "3000"));
    await Cobrar("6. Payment lento (3 s): intentos cortados a los 2 s", 1500m);

    await Compose("up -d payment");
    await EsperarHasta(Stopwatch.StartNew(), TimeSpan.FromSeconds(16), "termine el corte del escenario 6");
    await Cobrar("7a. Payment normal otra vez", 1500m);

    var caida = TimeSpan.FromSeconds(config.GetValue("Demo:CaidaLargaSegundos", 60));
    await Compose("stop payment");
    await Cobrar("7b. Comienza una caída larga", 1500m);
    var reloj = Stopwatch.StartNew();
    while (reloj.Elapsed < caida)
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        await Cobrar($"7c. Durante la caída, a los {reloj.Elapsed.TotalSeconds:0} s", 1500m);
    }

    // El reloj arranca antes del start: mide desde que se pide levantar Payment
    var vuelta = Stopwatch.StartNew();
    await Compose("start payment");
    while (vuelta.Elapsed < TimeSpan.FromSeconds(90))
    {
        var resultado = await Cobrar($"7d. Payment levantado hace {vuelta.Elapsed.TotalSeconds:0} s", 1500m);
        if (resultado.Exitoso)
        {
            Console.WriteLine($"\n===== Primera reserva confirmada {vuelta.Elapsed.TotalSeconds:0.0} s después de levantar Payment");
            return;
        }
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
    Console.WriteLine("\n===== El cobro no se recuperó en 90 s");
}

async Task EsperarHasta(Stopwatch desde, TimeSpan total, string motivo)
{
    var falta = total - desde.Elapsed;
    if (falta > TimeSpan.Zero)
    {
        Console.WriteLine($"\n(espero {falta.TotalSeconds:0} s a que {motivo})");
        await Task.Delay(falta);
    }
}

async Task Compose(string accion, params (string Nombre, string Valor)[] variables)
{
    var directorio = Path.GetFullPath(config["Demo:DirectorioCompose"] ?? "../..");
    var proceso = new ProcessStartInfo("docker", $"{config["Demo:Compose"]} {accion}") { WorkingDirectory = directorio };
    foreach (var (nombre, valor) in variables)
    {
        proceso.Environment[nombre] = valor;
    }

    var descripcion = string.Join(" ", variables.Select(v => $"{v.Nombre}={v.Valor}"));
    Console.WriteLine($"\n$ {descripcion} docker compose ... {accion}".Replace("$  ", "$ "));
    using var docker = Process.Start(proceso) ?? throw new InvalidOperationException("No se pudo ejecutar docker");
    await docker.WaitForExitAsync();
    // Le da tiempo a Payment a arrancar antes del próximo cobro
    if (!accion.StartsWith("stop"))
    {
        await Task.Delay(TimeSpan.FromSeconds(6));
    }
}
