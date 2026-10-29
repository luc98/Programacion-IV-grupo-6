namespace BookingAPI.BLL.DTOs
{
    // Representa el mensaje de solicitud de reserva recibido desde Azure Service Bus (emitido por Availability Site).
    // Desacopla el contrato de entrada de las entidades anemicas de persistencia (DAL).
    public class SolicitudReservaDto
    {
        public Guid SolicitudId { get; set; }
        public Guid EventoId { get; set; }
        public Guid AsientoId { get; set; }
        public string UsuarioId { get; set; } = string.Empty;
    }
}
