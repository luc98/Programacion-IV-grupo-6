namespace PaymentAPI.BLL.DTOs;

// Lo que la BLL necesita para cobrar, ya validado y sin tipos del contrato gRPC:
// la BLL no sabe que del otro lado hay gRPC.
public sealed record SolicitudPagoDto(
    Guid ReservaId,
    Guid SolicitudId,
    string UsuarioId,
    long MontoCentavos,
    string TraceId);
