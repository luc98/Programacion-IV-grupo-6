using PaymentAPI.DAL.Enums;

namespace PaymentAPI.BLL.DTOs;

public sealed record DecisionBancoDto(ResultadoPago Resultado, string? Motivo)
{
    public static DecisionBancoDto Aprobado() => new(ResultadoPago.Aprobado, null);

    public static DecisionBancoDto FondosInsuficientes(string motivo) =>
        new(ResultadoPago.RechazadoFondosInsuficientes, motivo);
}
