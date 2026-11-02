using Microsoft.Extensions.Options;
using PaymentAPI.BLL.DTOs;
using PaymentAPI.BLL.Interfaces;
using PaymentAPI.BLL.Options;

namespace PaymentAPI.BLL.Services;

// Simulación determinística: el mismo pedido siempre da el mismo resultado, así
// los escenarios de la demo se repiten a pedido. No usa azar.
public class BancoSimulado(IOptionsMonitor<BancoSimuladoOptions> opciones) : IBancoSimulado
{
    public async Task<DecisionBancoDto> AutorizarAsync(SolicitudPagoDto solicitud, CancellationToken cancellationToken)
    {
        // IOptionsMonitor lee el valor vigente en cada cobro: con dotnet run se
        // puede cambiar appsettings.json sin reiniciar.
        var config = opciones.CurrentValue;

        if (config.RetardoMs > 0)
        {
            // Si Booking corta por timeout, gRPC cancela este token y el cobro se
            // interrumpe antes de quedar registrado.
            await Task.Delay(config.RetardoMs, cancellationToken);
        }

        if (config.UsuariosSinFondos.Contains(solicitud.UsuarioId, StringComparer.OrdinalIgnoreCase))
        {
            return DecisionBancoDto.FondosInsuficientes("El usuario no tiene fondos (banco simulado)");
        }

        if (config.LimitePorCobroCentavos > 0 && solicitud.MontoCentavos > config.LimitePorCobroCentavos)
        {
            return DecisionBancoDto.FondosInsuficientes(
                $"El monto supera el límite por cobro de {config.LimitePorCobroCentavos / 100m:0.00} (banco simulado)");
        }

        return DecisionBancoDto.Aprobado();
    }
}
