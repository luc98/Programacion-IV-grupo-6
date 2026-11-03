using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Common;

namespace AvailabilitySite.Application.Reservations;

public sealed class RequestReservationService(
    ICurrentUserContext currentUserContext,
    IReservationRequestRepository reservationRequestRepository,
    IAvailabilityQueryRepository availabilityQueryRepository)
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

        var seats = await availabilityQueryRepository.GetSeatsAsync(request.EventId, cancellationToken);
        if (seats.All(seat => seat.Id != request.SeatId))
        {
            return Result<ReservationRequestReceiptDto>.Failure(
                new ApplicationError(
                    ApplicationErrorType.NotFound,
                    "seat_not_found",
                    "El asiento no existe para el evento solicitado."));
        }

        var newRequest = new ReservationRequestRecord(
            Guid.NewGuid(),
            request.EventId,
            request.SeatId,
            userId,
            request.IdempotencyKey,
            DateTimeOffset.UtcNow,
            ReservationRequestStatus.Pending);

        var persisted = await reservationRequestRepository.AddOrGetExistingAsync(newRequest, cancellationToken);
        if (!persisted.Created)
        {
            if (persisted.Request.EventId != request.EventId || persisted.Request.SeatId != request.SeatId)
            {
                return Result<ReservationRequestReceiptDto>.Failure(
                    new ApplicationError(
                        ApplicationErrorType.Validation,
                        "idempotency_key_conflict",
                        "La clave de idempotencia ya fue utilizada para otra solicitud."));
            }

            return Result<ReservationRequestReceiptDto>.Success(ToReceipt(persisted.Request, isReplay: true));
        }

        // AddAsync persiste la solicitud y su mensaje Outbox en una transacción.
        // La publicación la realiza posteriormente el Worker de Outbox.
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
