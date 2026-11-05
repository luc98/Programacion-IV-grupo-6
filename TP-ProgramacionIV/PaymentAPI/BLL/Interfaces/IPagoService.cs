using PaymentAPI.BLL.DTOs;

namespace PaymentAPI.BLL.Interfaces;

public interface IPagoService
{
    Task<ResultadoPagoDto> ProcesarAsync(SolicitudPagoDto solicitud, CancellationToken cancellationToken);
}
