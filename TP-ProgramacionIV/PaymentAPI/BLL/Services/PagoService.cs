using PaymentAPI.BLL.DTOs;
using PaymentAPI.BLL.Exceptions;
using PaymentAPI.BLL.Interfaces;
using PaymentAPI.DAL.Entities;
using PaymentAPI.DAL.Repositories;

namespace PaymentAPI.BLL.Services;

// Toda la lógica del cobro vive acá y no en la entidad Pago (dominio anémico).
public class PagoService(
    IPagoRepository repositorio,
    IBancoSimulado banco,
    TimeProvider reloj,
    ILogger<PagoService> logger) : IPagoService
{
    public async Task<ResultadoPagoDto> ProcesarAsync(SolicitudPagoDto solicitud, CancellationToken cancellationToken)
    {
        // 1. Idempotencia. Polly en Booking puede reenviar el mismo cobro, y un
        // timeout puede cortar después de que Payment ya cobró. Si la reserva ya
        // tiene pago, se devuelve ese resultado sin volver a pasar por el banco.
        var existente = await repositorio.ObtenerPorReservaAsync(solicitud.ReservaId, cancellationToken);
        if (existente is not null)
        {
            return DevolverGuardado(existente, solicitud);
        }

        // 2. Decisión del banco simulado
        var decision = await banco.AutorizarAsync(solicitud, cancellationToken);

        // 3. Registro en el historial. En un banco real habría que anotar el
        // intento antes de cobrar; acá el banco no tiene efectos, y el pago existe
        // recién cuando queda guardado.
        var pago = new Pago
        {
            PagoId = Guid.NewGuid(),
            ReservaId = solicitud.ReservaId,
            SolicitudId = solicitud.SolicitudId,
            UsuarioId = solicitud.UsuarioId,
            MontoCentavos = solicitud.MontoCentavos,
            Resultado = decision.Resultado,
            Motivo = decision.Motivo,
            TraceId = solicitud.TraceId,
            FechaUtc = reloj.GetUtcNow().UtcDateTime
        };

        if (await repositorio.IntentarAgregarAsync(pago, cancellationToken))
        {
            logger.LogInformation(
                "Pago {PagoId} registrado para la reserva {ReservaId}: {Resultado}",
                pago.PagoId, pago.ReservaId, pago.Resultado);
            return new ResultadoPagoDto(pago.PagoId, pago.Resultado, pago.Motivo, EraRepetido: false);
        }

        // 4. Carrera: otro intento con la misma reserva se guardó entre el paso 1 y
        // el 3, y el índice UNIQUE frenó el nuestro. Gana el que llegó primero.
        var ganador = await repositorio.ObtenerPorReservaAsync(solicitud.ReservaId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"El índice UNIQUE rechazó el pago de la reserva {solicitud.ReservaId}, pero no se encontró el pago existente");
        return DevolverGuardado(ganador, solicitud);
    }

    private ResultadoPagoDto DevolverGuardado(Pago guardado, SolicitudPagoDto solicitud)
    {
        if (guardado.MontoCentavos != solicitud.MontoCentavos || guardado.UsuarioId != solicitud.UsuarioId)
        {
            throw new PagoEnConflictoException(solicitud.ReservaId);
        }

        logger.LogInformation(
            "La reserva {ReservaId} ya tenía el pago {PagoId} ({Resultado}): se devuelve sin cobrar de nuevo",
            guardado.ReservaId, guardado.PagoId, guardado.Resultado);
        return new ResultadoPagoDto(guardado.PagoId, guardado.Resultado, guardado.Motivo, EraRepetido: true);
    }
}
