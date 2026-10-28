namespace BookingAPI.DAL.Entities
{
    public class Evento
    {
        // Dominio anemico
        public Guid EventoId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public DateTime FechaEventoUtc { get; set; }
    }
}
