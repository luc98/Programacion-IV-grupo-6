namespace BookingAPI.DAL.Entities
{
    // Al ser un Dominio Anemico, las entidades solo contienen propiedades de datos (get, set) sin metodos de negocio
    // Toda la logica de validacion y transicion de estados reside en la capa BLL
    public class Asiento
    {
        public Guid AsientoId { get; set; }
        public Guid EventoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public decimal Precio { get; set; }

        // Se usa un entero 'Version' para lograr una Concurrencia Optimista en PostreSQL
        // (Configurado en BookingDbContext y explicacion en ReservaRepository)
        public int Version { get; set; }
    }
}
