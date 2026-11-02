using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Common;

namespace AvailabilitySite.Application.Reservations;

public sealed class RequestReservationService(
    ICurrentUserContext currentUserContext,
    IReservationRequestRepository reservationRequestRepository,
    IReservationRequestPublisher reservationRequestPublisher)
    : IRequestReservationUseCase
{
    public async Task<Result<ReservationRequestReceiptDto>> ExecuteAsync(
        CreateReservationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(request);
        if (validationError is not null)
        {
            return Result<ReservationRequestReceiptDto>.Failure(validationError);
        }

        if (!currentUserContext.IsAuthenticated || string.IsNullOrWhiteSpace(currentUserContext.UserId))
        {
            return Result<ReservationRequestReceiptDto>.Failure(
                new ApplicationError(
                    ApplicationErrorType.Unauthenticated,
                    "user_unauthenticated",
                    "El usuario debe estar autenticado para solicitar una reserva."));
        }

        var userId = currentUserContext.UserId;
        var existing = await reservationRequestRepository.FindByIdempotencyKeyAsync(
            userId,
            request.IdempotencyKey,
            cancellationToken);

        if (existing is not null)
        {
            if (existing.EventId != request.EventId || existing.SeatId != request.SeatId)
            {
                return Result<ReservationRequestReceiptDto>.Failure(
                    new ApplicationError(
                        ApplicationErrorType.Validation,
                        "idempotency_key_conflict",
                        "La clave de idempotencia ya fue utilizada para otra solicitud."));
            }

            return Result<ReservationRequestReceiptDto>.Success(
                ToReceipt(existing, isReplay: true));
        }

        var newRequest = new ReservationRequestRecord(
            Guid.NewGuid(),
            request.EventId,
            request.SeatId,
            userId,
            request.IdempotencyKey,
            DateTimeOffset.UtcNow,
            ReservationRequestStatus.Pending);

        await reservationRequestRepository.AddAsync(newRequest, cancellationToken);

        var message = new ReservationRequestedMessage(
            newRequest.RequestId,
            newRequest.EventId,
            newRequest.SeatId,
            newRequest.UserId);

        try
        {
            await reservationRequestPublisher.PublishAsync(message, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // La persistencia y el broker no forman una transacción aquí.
            // Un Outbox será necesario para garantizar publicación eventual.
            return Result<ReservationRequestReceiptDto>.Failure(
                new ApplicationError(
                    ApplicationErrorType.OutcomeUnknown,
                    "reservation_publication_unknown",
                    "No se pudo determinar el resultado de la publicación de la solicitud."));
        }

        return Result<ReservationRequestReceiptDto>.Success(
            ToReceipt(newRequest, isReplay: false));
    }

    private static ApplicationError? Validate(CreateReservationRequestDto request)
    {
        if (request.EventId == Guid.Empty)
        {
            return new ApplicationError(
                ApplicationErrorType.Validation,
                "event_id_required",
                "El identificador del evento es obligatorio.");
        }

        if (request.SeatId == Guid.Empty)
        {
            return new ApplicationError(
                ApplicationErrorType.Validation,
                "seat_id_required",
                "El identificador del asiento es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return new ApplicationError(
                ApplicationErrorType.Validation,
                "idempotency_key_required",
                "La clave de idempotencia es obligatoria.");
        }

        return null;
    }

    private static ReservationRequestReceiptDto ToReceipt(
        ReservationRequestRecord request,
        bool isReplay)
    {
        return new ReservationRequestReceiptDto(
            request.RequestId,
            request.EventId,
            request.SeatId,
            request.ReceivedAt,
            isReplay,
            request.Status);
    }
}
