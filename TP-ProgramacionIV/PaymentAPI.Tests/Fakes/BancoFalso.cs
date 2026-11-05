using PaymentAPI.BLL.DTOs;
using PaymentAPI.BLL.Interfaces;

namespace PaymentAPI.Tests.Fakes;

// Banco controlado por el test: cuenta las llamadas y permite frenar antes de decidir.
public sealed class BancoFalso(DecisionBancoDto? decision = null) : IBancoSimulado
{
    private int _llamadas;

    public int Llamadas => _llamadas;

    public Func<CancellationToken, Task>? AntesDeDecidir { get; set; }

    public async Task<DecisionBancoDto> AutorizarAsync(SolicitudPagoDto solicitud, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _llamadas);
        if (AntesDeDecidir is not null)
        {
            await AntesDeDecidir(cancellationToken);
        }
        return decision ?? DecisionBancoDto.Aprobado();
    }
}
