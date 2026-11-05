using PaymentAPI.DAL.Enums;

namespace PaymentAPI.BLL.DTOs;

// EraRepetido = true cuando la reserva ya estaba cobrada y se devolvió el pago
// guardado, sin volver a pasar por el banco.
public sealed record ResultadoPagoDto(
    Guid PagoId,
    ResultadoPago Resultado,
    string? Motivo,
    bool EraRepetido);
