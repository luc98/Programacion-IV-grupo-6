using BookingAPI.BLL.DTOs;
using BookingAPI.BLL.Interfaces;
using BookingAPI.DAL.Entities;
using BookingAPI.DAL.Enums;
using BookingAPI.DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BookingAPI.BLL.Services
{
    // Toda la logica de negocio, el control de la maquina de estados (Retenida, Confirmada, Rechazada, Vencida)
    // y el manejo de concurrencia residen en este servicio de la capa BLL, manteniendo las entidades de DAL anemicas
    public class BookingService : IBookingService
    {
        // Es el tiempo maximo que tiene una operacion de confirmar el pago de un asiento
        // hasta que la operacion pase a un estado 'Vencida' (4)
        private static readonly TimeSpan TiempoRetencion = TimeSpan.FromMinutes(5);

        // Patron repository con los metodos que luego van a hacer las operaciones a la DB
        private readonly IReservaRepository _reservaRepository;
        // Comunicacion con PaymentGrpc
        private readonly IPaymentGateway _paymentGateway;
        // Publicacion de las notificaciones hacia el Notification service por medio de Azure Serive Bus
        private readonly IEventPublisher _eventPublisher;

        public BookingService(
            IReservaRepository reservaRepository,
            IPaymentGateway paymentGateway,
            IEventPublisher eventPublisher)
        {
            _reservaRepository = reservaRepository;
            _paymentGateway = paymentGateway;
            _eventPublisher = eventPublisher;
        }

        // Implementa el Procesamiento, Cobro y Resultado del flujo de una reserva
        public async Task ProcesarSolicitudReservaAsync(SolicitudReservaDto solicitud)
        {
            // Verifica si el asiento ya se encuentra ocupado (tiene reserva retenida o confirmada)

            // Verifica que la solicitud no se encuentre en la DB
            // Azure Service Bus puede reenviar el mismo mensaje si hubo un microcorte
            // En BookingDbContext, SolicitudId es Unique
            if (await _reservaRepository.ObtenerReservaPorSolicitudIdAsync(solicitud.SolicitudId) is not null)
            {
                // Registrar log
                return;
            }

            Asiento? asiento = await _reservaRepository.ObtenerAsientoPorIdAsync(solicitud.AsientoId);
            // Si Availability fallo y el asiento no existe, se corta la ejecucion
            if (asiento is null)
            {
                await RegistrarRechazoAsync(solicitud, MotivoRechazo.AsientoOcupado);
                // Registrar log
                return;
            }

            bool asientoOcupado = await _reservaRepository.ExisteReservaActivaParaAsientoAsync(solicitud.AsientoId);
            if (asientoOcupado)
            {
                await RegistrarRechazoAsync(solicitud, MotivoRechazo.AsientoOcupado);

                // Registrar log de rechazo por asiento ocupado
                return;
            }

            // Iniciar el alcance del TraceId / CorrelationId recibido desde el mensaje del broker
            // y registrar el log de auditoria

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
                await _eventPublisher.PublicarReservaCreadaAsync(reserva);
            }
            // Manejo de la condicion de carrera
            catch (DbUpdateConcurrencyException)
            {
                // Cuando otra operacion de Booking incremento el 'Version' del mismo asiento
                // Se rechaza la solicitud con movito AsientoOcupado (1)
                await RegistrarRechazoAsync(solicitud, MotivoRechazo.AsientoOcupado);

                // Registrar log de conflicto de concurrencia
                return;
            }

            decimal montoCobro = asiento.Precio;

            // Antes de invocar a Payment gRPC API, asegurar la propagacion del TraceId en los metadatos gRPC

            // Hacemos el llamado a Payment gRPC para procesar el cobro
            ResultadoCobroDto resultadoCobro = await _paymentGateway.CobrarReservaAsync(reserva, montoCobro);

            // Segun la respuesta del Payment gRPC, se actualiza el estado y motivo de la reserva
            if (resultadoCobro.Exitoso)
            {
                reserva.Estado = EstadoReserva.Confirmada;
                reserva.Motivo = MotivoRechazo.Ninguno;
                await _reservaRepository.ActualizarAsync(reserva);

                // Como el pago se proceso con exito, se publica el evento para que el Servicio de Notificaciones
                // informe al usuario que su reserva esta creada y confirmada.
                await _eventPublisher.PublicarReservaConfirmadaAsync(reserva);
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
            }

            // Registrar el fin del log de auditoria de Booking REST API.
        }

        // Implementa la consulta de seguimiento del estado de una solicitud (GET expuesto para Availability Site)
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
