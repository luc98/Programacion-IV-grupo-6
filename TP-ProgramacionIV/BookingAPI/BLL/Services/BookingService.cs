using System.Diagnostics;
using BookingAPI.BLL.DTOs;
using BookingAPI.BLL.Interfaces;
using BookingAPI.DAL.Entities;
using BookingAPI.DAL.Enums;
using BookingAPI.DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingAPI.BLL.Services
{
    // Toda la logica de negocio, el control de la maquina de estados (Retenida, Confirmada, Rechazada, Vencida)
    // y el manejo de concurrencia residen en este servicio de la capa BLL, manteniendo las entidades de DAL anemicas
    public class BookingService : IBookingService
    {
        private const string Operacion = "ProcesarSolicitudReserva";

        // Es el tiempo maximo que tiene una operacion de confirmar el pago de un asiento
        // hasta que la operacion pase a un estado 'Vencida' (4)
        private static readonly TimeSpan TiempoRetencion = TimeSpan.FromMinutes(5);

        // Patron repository con los metodos que luego van a hacer las operaciones a la DB
        private readonly IReservaRepository _reservaRepository;
        // Comunicacion con PaymentGrpc
        private readonly IPaymentGateway _paymentGateway;
        // Publicacion de las notificaciones hacia el Notification service por medio de Azure Serive Bus
        private readonly IEventPublisher _eventPublisher;
        private readonly ILogger<BookingService> _logger;

        public BookingService(
            IReservaRepository reservaRepository,
            IPaymentGateway paymentGateway,
            IEventPublisher eventPublisher,
            ILogger<BookingService>? logger = null)
        {
            _reservaRepository = reservaRepository;
            _paymentGateway = paymentGateway;
            _eventPublisher = eventPublisher;
            _logger = logger ?? NullLogger<BookingService>.Instance;
        }

        // Implementa el Procesamiento, Cobro y Resultado del flujo de una reserva
        public async Task ProcesarSolicitudReservaAsync(SolicitudReservaDto solicitud)
        {
            string traceId = !string.IsNullOrWhiteSpace(solicitud.TraceId)
                ? solicitud.TraceId
                : solicitud.SolicitudId.ToString();

            var cronometro = Stopwatch.StartNew();

            using (_logger.BeginScope(new Dictionary<string, object> { ["TraceId"] = traceId }))
            {
                _logger.LogInformation(
                    "{Etapa} {Operacion} TraceId={TraceId} SolicitudId={SolicitudId} AsientoId={AsientoId}",
                    "Inicio", Operacion, traceId, solicitud.SolicitudId, solicitud.AsientoId);

                // Verifica que la solicitud no se encuentre en la DB (Idempotencia)
                // Azure Service Bus puede reenviar el mismo mensaje si hubo un microcorte
                // En BookingDbContext, SolicitudId es Unique
                if (await _reservaRepository.ObtenerReservaPorSolicitudIdAsync(solicitud.SolicitudId) is not null)
                {
                    _logger.LogInformation(
                        "{Etapa} {Operacion} TraceId={TraceId} SolicitudId={SolicitudId} Resultado={Resultado} DuracionMs={DuracionMs}",
                        "Fin", Operacion, traceId, solicitud.SolicitudId, "SolicitudDuplicada", cronometro.ElapsedMilliseconds);
                    return;
                }

                Asiento? asiento = await _reservaRepository.ObtenerAsientoPorIdAsync(solicitud.AsientoId);
                // Si Availability fallo y el asiento no existe o es de otro evento, se corta la ejecucion
                if (asiento is null || asiento.EventoId != solicitud.EventoId)
                {
                    await RegistrarRechazoAsync(solicitud, MotivoRechazo.AsientoOcupado);
                    _logger.LogWarning(
                        "{Etapa} {Operacion} TraceId={TraceId} SolicitudId={SolicitudId} Resultado={Resultado} DuracionMs={DuracionMs}",
                        "Fin", Operacion, traceId, solicitud.SolicitudId, "RechazadaAsientoInexistente", cronometro.ElapsedMilliseconds);
                    return;
                }

                // Verifica si el asiento ya se encuentra ocupado (tiene reserva retenida o confirmada)
                bool asientoOcupado = await _reservaRepository.ExisteReservaActivaParaAsientoAsync(solicitud.AsientoId);
                if (asientoOcupado)
                {
                    await RegistrarRechazoAsync(solicitud, MotivoRechazo.AsientoOcupado);
                    _logger.LogInformation(
                        "{Etapa} {Operacion} TraceId={TraceId} SolicitudId={SolicitudId} Resultado={Resultado} DuracionMs={DuracionMs}",
                        "Fin", Operacion, traceId, solicitud.SolicitudId, "RechazadaAsientoOcupado", cronometro.ElapsedMilliseconds);
                    return;
                }

                Reserva reserva;
                try
                {
                    // Buscamos generar la reserva en memoria con sus respectivos atributos, EstadoReserva y MotivoRechazo
                    DateTime ahoraUtc = DateTime.UtcNow;
                    reserva = new Reserva
                    {
                        ReservaId = Guid.NewGuid(),
                        SolicitudId = solicitud.SolicitudId,
                        AsientoId = solicitud.AsientoId,
                        EventoId = solicitud.EventoId,
                        UsuarioId = solicitud.UsuarioId,
                        Estado = EstadoReserva.Retenida, // 1
                        Motivo = MotivoRechazo.Ninguno, // 0
                        FechaCreacionUtc = ahoraUtc,
                        FechaVencimientoUtc = ahoraUtc.Add(TiempoRetencion)
                    };
                    // Aplicando concurrencia optimista (con 'Version') buscamos apartar el asiento en estado retenido de ser posible
                    await _reservaRepository.GuardarReservaConcurrenteAsync(reserva, asiento);

                    // Se publica el evento informando que la reserva fue creada en estado retenida al Notification Service
                    await _eventPublisher.PublicarReservaCreadaAsync(reserva, traceId);
                }
                // Manejo de la condicion de carrera
                catch (DbUpdateConcurrencyException)
                {
                    // Cuando otra operacion de Booking incremento el 'Version' del mismo asiento
                    // Se rechaza la solicitud con motivo AsientoOcupado (1)
                    await RegistrarRechazoAsync(solicitud, MotivoRechazo.AsientoOcupado);

                    _logger.LogWarning(
                        "{Etapa} {Operacion} TraceId={TraceId} SolicitudId={SolicitudId} Resultado={Resultado} DuracionMs={DuracionMs}",
                        "Fin", Operacion, traceId, solicitud.SolicitudId, "RechazadaConflictoConcurrencia", cronometro.ElapsedMilliseconds);
                    return;
                }

                decimal montoCobro = asiento.Precio;

                // Hacemos el llamado a Payment gRPC para procesar el cobro
                ResultadoCobroDto resultadoCobro = await _paymentGateway.CobrarReservaAsync(reserva, montoCobro, traceId);

                // Segun la respuesta del Payment gRPC, se actualiza el estado y motivo de la reserva
                string resultadoFinal;
                if (resultadoCobro.Exitoso)
                {
                    reserva.Estado = EstadoReserva.Confirmada;
                    reserva.Motivo = MotivoRechazo.Ninguno;
                    await _reservaRepository.ActualizarAsync(reserva);

                    // Como el pago se proceso con exito, se publica el evento para que el Servicio de Notificaciones
                    // informe al usuario que su reserva esta creada y confirmada.
                    await _eventPublisher.PublicarReservaConfirmadaAsync(reserva, traceId);
                    resultadoFinal = "Confirmada";
                }
                else
                {
                    // Si la reserva se rechaza, el asiento vuelve a estar disponible
                    reserva.Estado = EstadoReserva.Rechazada;

                    // Degradacion elegante
                    // Se distingue si el fallo fue por caida/timeout del componente Payment ("pago no disponible")
                    // o por rechazo del medio de pago ("pago fallido").
                    reserva.Motivo = resultadoCobro.ServicioCaido
                        ? MotivoRechazo.PagoNoDisponible
                        : MotivoRechazo.PagoFallido;

                    await _reservaRepository.ActualizarAsync(reserva);
                    resultadoFinal = resultadoCobro.ServicioCaido ? "RechazadaPagoNoDisponible" : "RechazadaPagoFallido";
                }

                _logger.LogInformation(
                    "{Etapa} {Operacion} TraceId={TraceId} SolicitudId={SolicitudId} ReservaId={ReservaId} Resultado={Resultado} DuracionMs={DuracionMs}",
                    "Fin", Operacion, traceId, solicitud.SolicitudId, reserva.ReservaId, resultadoFinal, cronometro.ElapsedMilliseconds);
            }
        }

        // Implementa la consulta de seguimiento del estado de una solicitud (GET expuesto para Availability Site)
        // Booking es el dueño de las reservas: Availability le pregunta qué asientos están ocupados
        public Task<List<Guid>> ObtenerAsientosOcupadosAsync(Guid eventoId) =>
            _reservaRepository.ObtenerAsientosOcupadosPorEventoAsync(eventoId);

        public async Task<ReservaResultadoDto?> ObtenerEstadoPorSolicitudIdAsync(Guid solicitudId)
        {
            Reserva? reserva = await _reservaRepository.ObtenerReservaPorSolicitudIdAsync(solicitudId);
            if (reserva is null)
            {
                return null;
            }

            return new ReservaResultadoDto
            {
                ReservaId = reserva.ReservaId,
                SolicitudId = reserva.SolicitudId,
                EventoId = reserva.EventoId,
                AsientoId = reserva.AsientoId,
                Estado = FormatearEstado(reserva.Estado),
                MotivoRechazo = FormatearMotivoRechazo(reserva.Motivo),
                FechaCreacionUtc = reserva.FechaCreacionUtc,
                FechaVencimientoUtc = reserva.FechaVencimientoUtc
            };
        }

        private async Task RegistrarRechazoAsync(SolicitudReservaDto solicitud, MotivoRechazo motivo)
        {
            DateTime ahoraUtc = DateTime.UtcNow;
            var reservaRechazada = new Reserva
            {
                ReservaId = Guid.NewGuid(),
                SolicitudId = solicitud.SolicitudId,
                AsientoId = solicitud.AsientoId,
                EventoId = solicitud.EventoId,
                UsuarioId = solicitud.UsuarioId,
                Estado = EstadoReserva.Rechazada,
                Motivo = motivo,
                FechaCreacionUtc = ahoraUtc,
                FechaVencimientoUtc = ahoraUtc
            };
            // Se mapea el asiento rechazado junto a su motivo y se lo persiste en la DB
            await _reservaRepository.CrearReservaRechazadaAsync(reservaRechazada);
        }

        private static string FormatearEstado(EstadoReserva estado) => estado switch
        {
            // Mapeamos el enum del estado de una reserva a un string
            EstadoReserva.Retenida => "Retenida",
            EstadoReserva.Confirmada => "Confirmada",
            EstadoReserva.Rechazada => "Rechazada",
            EstadoReserva.Vencida => "Vencida",
            _ => estado.ToString()
        };

        private static string FormatearMotivoRechazo(MotivoRechazo motivo) => motivo switch
        {
            // Mapeamos el enum del motivo de rechazo de una reserva a un string
            MotivoRechazo.Ninguno => string.Empty,
            MotivoRechazo.AsientoOcupado => "asiento ocupado",
            MotivoRechazo.PagoFallido => "pago fallido",
            MotivoRechazo.PagoNoDisponible => "pago no disponible",
            _ => motivo.ToString()
        };
    }
}
