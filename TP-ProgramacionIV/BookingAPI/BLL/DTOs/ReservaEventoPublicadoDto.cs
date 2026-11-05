namespace BookingAPI.BLL.DTOs
{
    // [JUSTIFICACIÓN TEÓRICA - DTO DE INTEGRACIÓN Y CONTRATO DE EVENTOS (PUNTO 3.h)]
    // Este DTO representa el payload del mensaje de integración publicado hacia Azure Service Bus
    // para ser consumido asincrónicamente por el microservicio Notification Service.
    // Desacopla las entidades internas de persistencia de BookingAPI del contrato público de mensajería.
    public class ReservaEventoPublicadoDto
    {
        // Identifica la accion: 'ReservaCreada' (estado Retenida) o 'ReservaConfirmada' (cobro exitoso)
        public string TipoEvento { get; set; } = string.Empty;

        // Identificador unico de la reserva persistida en Booking
        public Guid ReservaId { get; set; }

        // Identificador original de la solicitud generado por Availability Site (usado para correlacion)
        public Guid SolicitudId { get; set; }

        // Asiento afectado por la operacion de reserva
        public Guid AsientoId { get; set; }

        // Evento (recital/conferencia) al que pertenece el asiento
        public Guid EventoId { get; set; }

        // Usuario destinatario de la notificacion por email o push
        public string UsuarioId { get; set; } = string.Empty;

        // Estado actual de la reserva en formato legible (Retenida o Confirmada)
        public string Estado { get; set; } = string.Empty;

        // Momento en que la reserva fue creada en la base de datos
        public DateTime FechaCreacionUtc { get; set; }

        // Fecha limite de retencion (TTL): permite al usuario saber hasta cuando puede pagar
        public DateTime? FechaVencimientoUtc { get; set; }

        // Marca de tiempo de emision del evento hacia el broker
        public DateTime FechaEventoUtc { get; set; }

        // Identificador para correlacionar logs de auditoria entre microservicios (Dupla 4)
        public string TraceId { get; set; } = string.Empty;
    }
}
