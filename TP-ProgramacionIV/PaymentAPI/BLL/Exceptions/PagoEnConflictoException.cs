namespace PaymentAPI.BLL.Exceptions;

// Llegó un cobro para una reserva que ya se cobró, pero con otros datos (otro
// monto u otro usuario). No es un reintento: es un error del cliente, y devolver
// el pago guardado como si nada sería mentirle.
public class PagoEnConflictoException(Guid reservaId)
    : Exception($"La reserva {reservaId} ya tiene un pago registrado con otros datos");
