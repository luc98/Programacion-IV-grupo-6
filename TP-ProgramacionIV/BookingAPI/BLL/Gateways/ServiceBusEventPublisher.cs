using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BookingAPI.BLL.DTOs;
using BookingAPI.BLL.Interfaces;
using BookingAPI.DAL.Entities;

namespace BookingAPI.BLL.Gateways
{
    // [JUSTIFICACIÓN TEÓRICA - ARQUITECTURA ORIENTADA A EVENTOS (EDA) & CONSISTENCIA EVENTUAL (PUNTO 3.g)]
    // El patrón de Publicación/Suscripción (Pub/Sub) desacopla temporal y espacialmente a Booking REST API
    // del Notification Service. Booking no necesita conocer la disponibilidad ni ubicación del consumidor;
    // simplemente deposita el evento en el Message Broker (Azure Service Bus), garantizando que el sistema
    // alcance consistencia eventual sin bloquear el flujo transaccional.
    //
    // [JUSTIFICACIÓN TEÓRICA - NOTIFICACIONES AL USUARIO (PUNTO 3.h)]
    // Cumple con el requerimiento de emitir eventos de integración en dos momentos clave del ciclo de vida:
    // 1. 'ReservaCreada': Cuando la reserva ingresa en estado 'Retenida' con su tiempo de vencimiento (TTL).
    // 2. 'ReservaConfirmada': Una vez que el pago fue procesado con éxito por Payment gRPC API.
    public class ServiceBusEventPublisher : IEventPublisher, IAsyncDisposable
    {
        private readonly ServiceBusClient? _serviceBusClient;
        private readonly ServiceBusSender? _sender;
        private readonly ILogger<ServiceBusEventPublisher> _logger;
        private readonly string _topicName;

        public ServiceBusEventPublisher(
            IConfiguration configuration,
            ILogger<ServiceBusEventPublisher> logger,
            ServiceBusClient? serviceBusClient = null)
        {
            _logger = logger;
            string? connectionString = configuration["AzureServiceBus:ConnectionString"];
            _topicName = configuration["AzureServiceBus:NotificacionesTopic"] ?? "notificaciones-reserva";

            // Permite inyectar un cliente mockeado para pruebas unitarias o crear uno a partir de la connection string
            if (serviceBusClient is not null)
            {
                _serviceBusClient = serviceBusClient;
                _sender = _serviceBusClient.CreateSender(_topicName);
            }
            // Si hay connection string valida de Azure, se instancia el sender para publicar al topico
            else if (!string.IsNullOrWhiteSpace(connectionString) && !connectionString.Contains("INGRESAR_"))
            {
                try
                {
                    _serviceBusClient = new ServiceBusClient(connectionString);
                    _sender = _serviceBusClient.CreateSender(_topicName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[SERVICE BUS - PUBLISHER] No fue posible inicializar ServiceBusClient con la cadena configurada.");
                }
            }
            // Modo de resguardo para desarrollo local sin Azure: simula la publicacion sin romper la aplicacion
            else
            {
                _logger.LogWarning("[SERVICE BUS - PUBLISHER] Cadena de conexión a Azure Service Bus no configurada o en modo simulación local.");
            }
        }

        // Emite el evento asincrono al crear la reserva en estado Retenida con su fecha de vencimiento
        public async Task PublicarReservaCreadaAsync(Reserva reserva)
        {
            await PublicarEventoAsync("ReservaCreada", reserva);
        }

        // Emite el evento asincrono cuando el cobro fue completado con exito y la reserva pasa a Confirmada
        public async Task PublicarReservaConfirmadaAsync(Reserva reserva)
        {
            await PublicarEventoAsync("ReservaConfirmada", reserva);
        }

        private async Task PublicarEventoAsync(string tipoEvento, Reserva reserva)
        {
            // [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Genera identificador de correlacion para el broker
            string traceId = Guid.NewGuid().ToString("N");

            // Construye el contrato publico del evento desacoplando la entidad interna de PostgreSQL
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
                TraceId = traceId
            };

            _logger.LogInformation(
                "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Publicando evento '{TipoEvento}' a Azure Service Bus. Topic: {Topic}, SolicitudId: {SolicitudId}, ReservaId: {ReservaId}, TraceId: {TraceId}",
                tipoEvento, _topicName, reserva.SolicitudId, reserva.ReservaId, traceId);

            // Si estamos en entorno local sin conexion a Azure activa, se registra en consola y no se bloquea
            if (_sender is null)
            {
                _logger.LogWarning(
                    "[SERVICE BUS - PUBLISHER] Evento '{TipoEvento}' simulado exitosamente en log (Sender de Service Bus no conectado en entorno actual).",
                    tipoEvento);
                return;
            }

            try
            {
                // Serializa el payload a JSON estandar
                string jsonBody = JsonSerializer.Serialize(eventoDto);

                // Configura metadatos nativos y ApplicationProperties para que el Notification Service filtre por tipo o trace
                var message = new ServiceBusMessage(jsonBody)
                {
                    Subject = tipoEvento,
                    ContentType = "application/json",
                    CorrelationId = reserva.SolicitudId.ToString(),
                    ApplicationProperties =
                    {
                        { "TipoEvento", tipoEvento },
                        { "TraceId", traceId },
                        { "SolicitudId", reserva.SolicitudId.ToString() }
                    }
                };

                // Envia el mensaje de forma asincronica al topic de Service Bus
                await _sender.SendMessageAsync(message);

                _logger.LogInformation(
                    "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Evento '{TipoEvento}' enviado a Azure Service Bus exitosamente. TraceId: {TraceId}",
                    tipoEvento, traceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Error al publicar evento '{TipoEvento}' a Azure Service Bus. TraceId: {TraceId}",
                    tipoEvento, traceId);
            }
        }

        // Cierre y liberacion ordenada de conexiones de red y sockets con Azure
        public async ValueTask DisposeAsync()
        {
            if (_sender is not null)
            {
                await _sender.DisposeAsync();
            }

            if (_serviceBusClient is not null)
            {
                await _serviceBusClient.DisposeAsync();
            }

            GC.SuppressFinalize(this);
        }
    }
}
