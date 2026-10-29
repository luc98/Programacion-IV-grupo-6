namespace BookingAPI.BLL.DTOs
{
    // Este DTO va a ser la estructura de respuesta expuesta por el endpoint GET 
    // para el seguimiento de una solicitud
    public class ReservaResultadoDto
    {
        public Guid ReservaId { get; set; }
        public Guid SolicitudId { get; set; }
        public Guid AsientoId { get; set; }
        public Guid EventoId { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string MotivoRechazo { get; set; } = string.Empty;
        public DateTime FechaCreacionUtc { get; set; }
        public DateTime FechaVencimientoUtc { get; set; }
    }
}
