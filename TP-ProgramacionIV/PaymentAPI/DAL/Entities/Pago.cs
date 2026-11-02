using PaymentAPI.DAL.Enums;

namespace PaymentAPI.DAL.Entities;

// Dominio anémico: la entidad solo tiene datos, sin métodos de negocio. Las reglas
// (idempotencia, decisión del banco) viven en BLL/Services.
// Cada fila es un intento de cobro con su resultado, aprobado o rechazado: la
// tabla es el historial de pagos.
public class Pago
{
    public Guid PagoId { get; set; }

    // Índice UNIQUE (ver PaymentDbContext): una reserva se cobra una sola vez
    public Guid ReservaId { get; set; }

    public Guid SolicitudId { get; set; }
    public string UsuarioId { get; set; } = string.Empty;
    public long MontoCentavos { get; set; }
    public ResultadoPago Resultado { get; set; }

    // Motivo legible cuando el pago no se aprueba
    public string? Motivo { get; set; }

    // El TraceId de la cadena (SolicitudId), para cruzar esta fila con los logs
    public string TraceId { get; set; } = string.Empty;

    public DateTime FechaUtc { get; set; }
}
