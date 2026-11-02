using PaymentAPI.BLL.DTOs;

namespace PaymentAPI.BLL.Interfaces;

// El "banco" queda detrás de una interfaz: hoy es una simulación y mañana podría
// ser una pasarela real sin tocar PagoService.
public interface IBancoSimulado
{
    Task<DecisionBancoDto> AutorizarAsync(SolicitudPagoDto solicitud, CancellationToken cancellationToken);
}
