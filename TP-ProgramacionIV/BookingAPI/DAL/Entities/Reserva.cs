using BookingAPI.DAL.Enums;

namespace BookingAPI.DAL.Entities
{
    public class Reserva
    {
        // Dominio anemico
        public Guid ReservaId { get; set; }
        public Guid SolicitudId { get; set; }
        public Guid AsientoId { get; set; }
        public Guid EventoId { get; set; }
        public string UsuarioId { get; set; } = string.Empty;
        // Usamos el enum del EstadoReserva y MotivoRechazo para luego mappearlos en el BLL de forma que no haya que manejar strings
        // Si EstadoReserva = 1 ==> 'Retenido' y si MotivoRechazo = 0 ==> 'Ninguno'
        public EstadoReserva Estado { get; set; }
        public MotivoRechazo Motivo { get; set; }
        public DateTime FechaCreacionUtc { get; set; }
        public DateTime FechaVencimientoUtc { get; set; }
    }
}
