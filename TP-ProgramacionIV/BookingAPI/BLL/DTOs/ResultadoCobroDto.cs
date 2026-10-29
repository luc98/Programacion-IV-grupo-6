namespace BookingAPI.BLL.DTOs
{
    // Encapsula la respuesta del microservicio Payment gRPC API, 
    // diferenciando entre un rechazo de fondos insuficientes / tarjeta rechazada ("pago fallido")
    // y una falla de infraestructura/red capturada por resiliencia ("pago no disponible").
    // Abierto a modificaciones (temporal)
    public class ResultadoCobroDto
    {
        public bool Exitoso { get; set; }
        public bool ServicioCaido { get; set; }
        public string? TransaccionId { get; set; }
        public string? ErrorMensaje { get; set; }
    }
}
