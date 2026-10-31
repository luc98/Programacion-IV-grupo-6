using BookingAPI.DAL.Entities;
using BookingAPI.DAL.Enums;
using BookingAPI.DAL.Repositories;

namespace BookingAPI.BLL.Jobs
{
    // [JUSTIFICACIÓN TEÓRICA - EXPIRACIÓN TTL Y LIBERACIÓN DE ASIENTO (PUNTOS 1, 7 Y 8 DEL TP)]
    // En el dominio de reserva de asientos, una reserva retenida aparta temporalmente el asiento mientras
    // se procesa el cobro o hasta que se alcanza su fecha de vencimiento (TTL = 5 minutos).
    // Si la reserva no llega a confirmarse antes de su vencimiento, este BackgroundService periódico
    // detecta las reservas retenidas cuya FechaVencimientoUtc <= ahoraUtc y transiciona su estado a 'Vencida'.
    //
    // [JUSTIFICACIÓN TEÓRICA - CONSISTENCIA EVENTUAL EN SEGUNDO PLANO (PUNTO 3.g)]
    // Al pasar la reserva a 'Vencida', la regla de negocio del repositorio (ExisteReservaActivaParaAsientoAsync)
    // deja de considerar ocupado el asiento (solo Retenida no vencida y Confirmada bloquean el asiento).
    // De este modo, el sistema alcanza consistencia eventual autónomamente liberando el asiento para nuevas solicitudes
    // sin requerir una acción manual del usuario ni intervención sincrónica.
    //
    // [JUSTIFICACIÓN TEÓRICA - N-LAYER & SCOPE MANAGEMENT]
    // Al ser un BackgroundService (Singleton), crea un IServiceScope por cada iteración para resolver
    // IReservaRepository y el BookingDbContext de forma acotada y segura en concurrencia.
    public class ExpirationWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ExpirationWorker> _logger;
        private readonly TimeSpan _intervalo;

        public ExpirationWorker(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<ExpirationWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;

            // Lee el intervalo de ejecucion desde appsettings (por defecto cada 10 segundos)
            int segundosIntervalo = _configuration.GetValue<int>("ExpirationWorker:IntervalSeconds", 10);
            _intervalo = TimeSpan.FromSeconds(segundosIntervalo <= 0 ? 10 : segundosIntervalo);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "[EXPIRATION WORKER] Iniciando proceso de segundo plano para expiración TTL de reservas (Intervalo: {Intervalo}s)...",
                _intervalo.TotalSeconds);

            // Bucle principal continuo mientras la aplicacion este corriendo
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Ejecuta la logica de busqueda y actualizacion de reservas vencidas
                    await ProcesarReservasVencidasAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Detención ordenada solicitada por el host
                    break;
                }
                catch (Exception ex)
                {
                    // Captura errores para que una excepcion aislada de base de datos no mate el worker
                    _logger.LogError(
                        ex,
                        "[EXPIRATION WORKER] Ocurrió una excepción no controlada en la iteración de expiración de reservas.");
                }

                try
                {
                    // Pausa asincronica entre ciclos para no saturar la CPU ni la base de datos
                    await Task.Delay(_intervalo, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("[EXPIRATION WORKER] Proceso de segundo plano finalizado.");
        }

        private async Task ProcesarReservasVencidasAsync(CancellationToken cancellationToken)
        {
            // Crea un ambito scoped para instanciar el repositorio y el DbContext de forma limpia
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                IReservaRepository reservaRepository = scope.ServiceProvider.GetRequiredService<IReservaRepository>();

                DateTime ahoraUtc = DateTime.UtcNow;

                // Consulta en PostgreSQL todas las reservas Retenidas cuya fecha de vencimiento ya expiro
                List<Reserva> reservasRetenidasVencidas = await reservaRepository.ObtenerReservasRetenidasVencidasAsync(ahoraUtc);

                if (reservasRetenidasVencidas.Count == 0)
                {
                    return;
                }

                _logger.LogInformation(
                    "[EXPIRATION WORKER] Se detectaron {Cantidad} reserva(s) retenida(s) expiradas para procesar.",
                    reservasRetenidasVencidas.Count);

                // Itera y actualiza individualmente cada reserva expirada
                foreach (Reserva reserva in reservasRetenidasVencidas)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Trazabilidad de la transición a 'Vencida'
                    string traceId = Guid.NewGuid().ToString("N");

                    // Cambia el estado a Vencida (4): esto libera de inmediato el asiento en la base de datos
                    reserva.Estado = EstadoReserva.Vencida;
                    await reservaRepository.ActualizarAsync(reserva);

                    _logger.LogInformation(
                        "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Reserva {ReservaId} marcada como VENCIDA por expiración de TTL ({VencimientoUtc}). Asiento {AsientoId} liberado. SolicitudId: {SolicitudId}, TraceId: {TraceId}",
                        reserva.ReservaId, reserva.FechaVencimientoUtc, reserva.AsientoId, reserva.SolicitudId, traceId);
                }
            }
        }
    }
}
