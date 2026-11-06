using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookingAPI.BLL.DTOs;
using BookingAPI.BLL.Interfaces;
using BookingAPI.DAL.Entities;

namespace BookingAPI.BLL.Gateways
{
    // Emite avisos al Notification Service cuando:
    // 1. La reserva fue creada y se retiene con su TLL de vencimiento ('ReservaCreada')
    // 2. Una vez el pago fue confirmado por payment gRPC ('ReservaConfirmada')
    public class HttpNotificationPublisher : IEventPublisher
    {
        private readonly HttpClient _httpClient;
        private readonly ITokenProvider _tokenProvider;
        private readonly ILogger<HttpNotificationPublisher> _logger;
        private readonly string _endpoint;
        private readonly int _timeoutSegundos;

        public HttpNotificationPublisher(
            HttpClient httpClient,
            ITokenProvider tokenProvider,
            IConfiguration configuration,
            ILogger<HttpNotificationPublisher> logger)
        {
            _httpClient = httpClient;
            _tokenProvider = tokenProvider;
            _logger = logger;
            // Carga la ruta y establece un timeout estricto (2 segundos) para no retener recursos
            _endpoint = configuration["NotificationService:Endpoint"] ?? "/api/notifications";

            if (!int.TryParse(configuration["NotificationService:TimeoutSegundos"], out _timeoutSegundos) || _timeoutSegundos <= 0)
            {
                _timeoutSegundos = 2;
            }
        }

        public async Task PublicarReservaCreadaAsync(Reserva reserva, string? traceId = null)
        {
            await PublicarEventoAsync("ReservaCreada", reserva, traceId);
        }

        public async Task PublicarReservaConfirmadaAsync(Reserva reserva, string? traceId = null)
        {
            await PublicarEventoAsync("ReservaConfirmada", reserva, traceId);
        }

        private async Task PublicarEventoAsync(string tipoEvento, Reserva reserva, string? traceId)
        {
            // Propaga el TraceId original para vincular los logs entre servicios
            string traceIdEfectivo = !string.IsNullOrWhiteSpace(traceId)
                ? traceId
                : reserva.SolicitudId.ToString();

            // Desacopla la entidad interna de PostreSQL en un contrato DTO de integracion
            var eventoDto = new ReservaEventoPublicadoDto
            {
                TipoEvento = tipoEvento,
                ReservaId = reserva.ReservaId,
                SolicitudId = reserva.SolicitudId,
                AsientoId = reserva.AsientoId,
                EventoId = reserva.EventoId,
                UsuarioId = reserva.UsuarioId,
                Estado = reserva.Estado.ToString(),
                FechaCreacionUtc = reserva.FechaCreacionUtc,
                FechaVencimientoUtc = reserva.FechaVencimientoUtc,
                FechaEventoUtc = DateTime.UtcNow,
                TraceId = traceIdEfectivo
            };

            try
            {
                // CancellationTokenSource asegura que si la red se cuelga, corta la operacion a los 2 segundos
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSegundos));
                // Punto 3.e: Booking se autentica ante Notifications con el mismo token de Keycloak que usa con Payment
                string token = await _tokenProvider.ObtenerTokenAsync(cts.Token);

                using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("x-trace-id", traceIdEfectivo);
                request.Headers.Add("x-event-type", tipoEvento);
                request.Content = JsonContent.Create(eventoDto);

                // Al tratarse de un servicio conceptual, la comunicacion HTTP POST esta aislada
                // Si Notification Service esta caido o responde lento, se degrada elegantemente con LogWarning
                // Y la transaccion principal de Booking no se interrumpe ni se cancela
                var response = await _httpClient.SendAsync(request, cts.Token);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // El próximo aviso pide un token nuevo
                    _tokenProvider.Invalidar();
                }

                if (!response.IsSuccessStatusCode)
                {
                    // Notifica el error HTTP sin interrumpir la reserva
                    _logger.LogWarning(
                        "[NOTIFICATION SERVICE - HTTP] Notification Service respondió con código {StatusCode}. TraceId={TraceId} SolicitudId={SolicitudId} TipoEvento={TipoEvento}",
                        (int)response.StatusCode, traceIdEfectivo, reserva.SolicitudId, tipoEvento);
                    return;
                }

                _logger.LogInformation(
                    "[NOTIFICATION SERVICE - HTTP] Notificación '{TipoEvento}' enviada exitosamente. TraceId={TraceId} SolicitudId={SolicitudId}",
                    tipoEvento, traceIdEfectivo, reserva.SolicitudId);
            }
            catch (Exception ex)
            {
                // Sucede cuando Notification Service esta caido o timeout
                // No impacta en la consistencia de la reserva
                _logger.LogWarning(
                    ex,
                    "[NOTIFICATION SERVICE - HTTP] No fue posible enviar notificación '{TipoEvento}' a Notification Service (servicio caído o timeout). La reserva continúa normalmente. TraceId={TraceId} SolicitudId={SolicitudId}",
                    tipoEvento, traceIdEfectivo, reserva.SolicitudId);
            }
        }
    }
}
