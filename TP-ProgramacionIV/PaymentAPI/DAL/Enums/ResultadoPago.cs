namespace PaymentAPI.DAL.Enums;

// Se guarda como entero. Los valores coinciden con el enum del .proto para que el
// mapeo sea directo, pero son tipos distintos: la base no depende del contrato.
public enum ResultadoPago
{
    Aprobado = 1,
    RechazadoFondosInsuficientes = 2
}
