using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BookingAPI.BLL.DTOs;
using BookingAPI.BLL.Interfaces;

namespace BookingAPI.Presentation.Listeners
{
    // [JUSTIFICACIÓN TEÓRICA - PRESENTATION LAYER & ASYNC MESSAGE CONSUMER]
    // Esta clase pertenece a la capa Presentation y actúa como adaptador de entrada para la mensajería asincrónica.
    // Hereda de BackgroundService para ejecutarse como un proceso daemon continuo dentro de ASP.NET Core.
    // Cumple con el Flujo de Reserva (Punto 4 del TP): una de las réplicas de Booking toma el mensaje publicado
    // por Availability Site en Azure Service Bus y delega su procesamiento a la capa BLL (BookingService).
    // Soporta suscripción tanto a Colas (Queue) como a Tópicos con Suscripciones (Topic/Subscription).
    //
    // [JUSTIFICACIÓN TEÓRICA - SCOPE MANAGEMENT EN BACKGROUND SERVICES]
    // BackgroundService se registra con ciclo de vida Singleton, mientras que los servicios de aplicación
    // (IBookingService) y el DbContext son Scoped (por solicitud). Para evitar problemas de concurrencia y memory leaks,
    // se inyecta IServiceScopeFactory para crear un ámbito (scope) independiente para cada mensaje procesado.
    public class ServiceBusListener : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ServiceBusListener> _logger;
        private ServiceBusClient? _serviceBusClient;
        private ServiceBusProcessor? _processor;

        public ServiceBusListener(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<ServiceBusListener> logger,
            ServiceBusClient? serviceBusClient = null)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
            _serviceBusClient = serviceBusClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Lee los nombres de colas o topicos desde appsettings
            string? connectionString = _configuration["AzureServiceBus:ConnectionString"];
            string? topicName = _configuration["AzureServiceBus:SolicitudesTopic"];
            string? subscriptionName = _configuration["AzureServiceBus:SolicitudesSubscription"];
            string queueName = _configuration["AzureServiceBus:SolicitudesQueue"] ?? "solicitudes-reserva";

            // Si no se proveyo cliente inyectado, se instancia uno con la cadena de conexion
            if (_serviceBusClient is null)
            {
                // Si la cadena no esta configurada o es plantilla, entra en modo espera para no abortar el inicio de la API
                if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("INGRESAR_"))
                {
                    _logger.LogWarning(
                        "[SERVICE BUS - LISTENER] Cadena de conexión no configurada o en modo plantilla. El listener permanecerá inactivo en espera pasiva.");
                    
                    await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
                    return;
                }

                _serviceBusClient = new ServiceBusClient(connectionString);
            }

            // Permite procesar hasta 10 mensajes en paralelo con confirmacion manual (ACK)
            var processorOptions = new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 10,
                AutoCompleteMessages = false // Manejo manual para asegurar procesamiento transaccional
            };

            // Deteccion automatica: si se configuro Topico y Suscripcion se suscribe en modo Pub/Sub; sino, usa Cola directa
            if (!string.IsNullOrWhiteSpace(topicName) && !string.IsNullOrWhiteSpace(subscriptionName))
            {
                _processor = _serviceBusClient.CreateProcessor(topicName, subscriptionName, processorOptions);
                _logger.LogInformation(
                    "[SERVICE BUS - LISTENER] Iniciando escucha en Modo Tópico/Suscripción. Tópico: '{Topic}', Suscripción: '{Subscription}'...",
                    topicName, subscriptionName);
            }
            else
            {
                _processor = _serviceBusClient.CreateProcessor(queueName, processorOptions);
                _logger.LogInformation(
                    "[SERVICE BUS - LISTENER] Iniciando escucha en Modo Cola. Cola: '{QueueName}'...",
                    queueName);
            }

            // Suscribe los callbacks para procesamiento de mensajes y gestion de fallas de conexion
            _processor.ProcessMessageAsync += ProcesarMensajeAsync;
            _processor.ProcessErrorAsync += ProcesarErrorAsync;

            // Inicia la recepcion activa de mensajes del broker
            await _processor.StartProcessingAsync(stoppingToken);

            try
            {
                // Mantiene el worker activo hasta que el host solicite el apagado de la aplicacion
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[SERVICE BUS - LISTENER] Detención solicitada por el host.");
            }
            finally
            {
                // Cierre y liberacion limpia del processor y cliente al detener el servicio
                if (_processor is not null)
                {
                    await _processor.StopProcessingAsync();
                    await _processor.DisposeAsync();
                }

                if (_serviceBusClient is not null)
                {
                    await _serviceBusClient.DisposeAsync();
                }
            }
        }

        private async Task ProcesarMensajeAsync(ProcessMessageEventArgs args)
        {
            string messageBody = args.Message.Body.ToString();

            // [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Captura de trazabilidad desde propiedades del mensaje
            string traceId = args.Message.ApplicationProperties.TryGetValue("TraceId", out var traceIdProp)
                ? traceIdProp?.ToString() ?? Guid.NewGuid().ToString("N")
                : Guid.NewGuid().ToString("N");

            string correlationId = args.Message.CorrelationId ?? args.Message.MessageId;

            _logger.LogInformation(
                "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Mensaje recibido desde Service Bus. TraceId: {TraceId}, CorrelationId: {CorrelationId}, SequenceNumber: {SeqNum}",
                traceId, correlationId, args.Message.SequenceNumber);

            SolicitudReservaDto? solicitud;
            try
            {
                // Deserializa el JSON proveniente de Availability Site hacia el DTO correspondiente
                solicitud = JsonSerializer.Deserialize<SolicitudReservaDto>(messageBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (solicitud is null || solicitud.SolicitudId == Guid.Empty || solicitud.AsientoId == Guid.Empty)
                {
                    throw new JsonException("El payload del mensaje no contiene una SolicitudReserva válida.");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(
                    ex,
                    "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Mensaje malformado o corrupto. Moviendo a Dead Letter Queue (DLQ). TraceId: {TraceId}",
                    traceId);

                // Si el mensaje es invalido, se desvia a la Dead Letter Queue (DLQ) para que no bloquee la cola
                await args.DeadLetterMessageAsync(args.Message, "MensajeCorrupto", ex.Message);
                return;
            }

            try
            {
                // [JUSTIFICACIÓN TEÓRICA - CREACIÓN DE ÁMBITO SCOPED EN BACKGROUND WORKER]
                // Como el listener es Singleton, crea un scope independiente para resolver IBookingService y DbContext
                using (IServiceScope scope = _scopeFactory.CreateScope())
                {
                    IBookingService bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();

                    _logger.LogInformation(
                        "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Delegando solicitud {SolicitudId} a BookingService. TraceId: {TraceId}",
                        solicitud.SolicitudId, traceId);

                    // Ejecuta el flujo transaccional completo: apartar asiento, cobrar gRPC y persistir
                    await bookingService.ProcesarSolicitudReservaAsync(solicitud);
                }

                // Asienta el mensaje en Service Bus (ACK) eliminandolo de la cola tras procesarlo con exito
                await args.CompleteMessageAsync(args.Message);

                _logger.LogInformation(
                    "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Procesamiento completado y mensaje asentado (ACK). TraceId: {TraceId}, SolicitudId: {SolicitudId}",
                    traceId, solicitud.SolicitudId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "// [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: Error durante el procesamiento de la reserva. Liberando mensaje para reintento. TraceId: {TraceId}, SolicitudId: {SolicitudId}",
                    traceId, solicitud.SolicitudId);

                // Ante error transitorio, se libera el mensaje para que Service Bus aplique su politica de reintentos
                await args.AbandonMessageAsync(args.Message);
            }
        }

        // Maneja las fallas de transporte, caida de red o errores de autenticacion con el broker
        private Task ProcesarErrorAsync(ProcessErrorEventArgs args)
        {
            _logger.LogError(
                args.Exception,
                "[SERVICE BUS - LISTENER] Error en el procesamiento del transporte Service Bus. ErrorSource: {ErrorSource}, Namespace: {Namespace}, Entity: {EntityPath}",
                args.ErrorSource, args.FullyQualifiedNamespace, args.EntityPath);

            return Task.CompletedTask;
        }
    }
}
