using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Common;
using AvailabilitySite.Application.Events;
using AvailabilitySite.Application.Reservations;
using AvailabilitySite.Domain.Entities;

namespace AvailabilitySite.Infrastructure.Mock;

// MOCKS: implementan las interfaces definidas en Application con datos de ejemplo.
// El backend real reemplaza estas clases; las pantallas no cambian.
//
// - Disponibilidad: se usa el GetEventAvailabilityService REAL de Application;
//   lo único simulado es el repositorio (InMemoryAvailabilityQueryRepository).
// - Los demás casos de uso todavía no tienen implementación en Application, así que se simulan acá.
// - No simulan caídas: los únicos errores que devuelven son de validación, inexistencia,
//   solicitud ajena y falta de usuario.

internal sealed class InMemoryAvailabilityQueryRepository(MockData data) : IAvailabilityQueryRepository
{
    public Task<IReadOnlyList<EventSummaryDto>> ListEventsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<EventSummaryDto>>(data.Events);
    }

    public Task<IReadOnlyList<Seat>> GetSeatsAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;

        IReadOnlyList<Seat> seats = data.Seats.TryGetValue(eventId, out var list)
            ? list.Select(seat => new Seat
            {
                Id = seat.Id,
                EventId = seat.EventId,
                Label = seat.Label,
                // Ocupado si ya lo estaba o si alguien lo pidió en esta ejecución.
                IsAvailable = seat.IsAvailable && !data.Submitted.Values.Any(r => r.SeatId == seat.Id && r.BookingStatusAt(now) is not null),
            }).ToList()
            : [];

        return Task.FromResult(seats);
    }
}

internal sealed class MockListEventsUseCase(MockData data) : IListEventsUseCase
{
    public Task<Result<IReadOnlyList<EventSummaryDto>>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<EventSummaryDto>>.Success(data.Events));
}

internal sealed class MockRequestReservationUseCase(MockData data, ICurrentUserContext currentUser) : IRequestReservationUseCase
{
    public Task<Result<ReservationRequestReceiptDto>> ExecuteAsync(CreateReservationRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            return Fail(ApplicationErrorType.Unauthenticated, "unauthenticated", "Necesitás iniciar sesión.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Fail(ApplicationErrorType.Validation, "idempotency_key_required", "Falta la clave de la solicitud. Volvé a cargar la página.");
        if (data.FindEvent(request.EventId) is null)
            return Fail(ApplicationErrorType.NotFound, "event_not_found", "El evento no existe.");
        if (data.FindSeat(request.EventId, request.SeatId) is null)
            return Fail(ApplicationErrorType.NotFound, "seat_not_found", "El asiento no existe en este evento.");

        // Idempotencia: misma clave del mismo usuario = misma solicitud (no se duplica).
        var existing = data.Submitted.Values.FirstOrDefault(r => r.UserId == userId && r.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null)
            return Ok(Receipt(existing, isReplay: true));

        var created = new MockReservationRequest(Guid.NewGuid(), userId, request.EventId, request.SeatId, request.IdempotencyKey, DateTimeOffset.UtcNow);
        data.Submitted[created.RequestId] = created;
        return Ok(Receipt(created, isReplay: false));
    }

    private static ReservationRequestReceiptDto Receipt(MockReservationRequest r, bool isReplay) =>
        new(r.RequestId, r.EventId, r.SeatId, r.CreatedAt, isReplay, ReservationRequestStatus.Pending);

    private static Task<Result<ReservationRequestReceiptDto>> Ok(ReservationRequestReceiptDto receipt) =>
        Task.FromResult(Result<ReservationRequestReceiptDto>.Success(receipt));

    private static Task<Result<ReservationRequestReceiptDto>> Fail(ApplicationErrorType type, string code, string message) =>
        Task.FromResult(Result<ReservationRequestReceiptDto>.Failure(new ApplicationError(type, code, message)));
}

internal sealed class MockGetReservationRequestStatusUseCase(MockData data, ICurrentUserContext currentUser) : IGetReservationRequestStatusUseCase
{
    public Task<Result<ReservationTrackingDto>> ExecuteAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            return Fail(ApplicationErrorType.Unauthenticated, "unauthenticated", "Necesitás iniciar sesión.");

        var now = DateTimeOffset.UtcNow;
        var request = data.Examples(now).FirstOrDefault(r => r.RequestId == requestId)
            ?? (data.Submitted.TryGetValue(requestId, out var submitted) ? submitted : null);

        if (request is null)
            return Fail(ApplicationErrorType.NotFound, "request_not_found", "No encontramos esa solicitud.");
        if (request.UserId != userId)
            return Fail(ApplicationErrorType.Forbidden, "request_forbidden", "Esta solicitud pertenece a otro usuario.");

        return Task.FromResult(Result<ReservationTrackingDto>.Success(new ReservationTrackingDto(
            ReservaId: Guid.Empty,
            RequestId: request.RequestId,
            EventId: request.EventId,
            SeatId: request.SeatId,
            RequestStatus: ReservationRequestStatus.Pending,
            BookingStatus: request.BookingStatusAt(now),
            RejectionReason: request.RejectionReasonAt(now),
            CreatedAtUtc: request.CreatedAt.UtcDateTime,
            ExpirationAtUtc: now.UtcDateTime)));
    }

    private static Task<Result<ReservationTrackingDto>> Fail(ApplicationErrorType type, string code, string message) =>
        Task.FromResult(Result<ReservationTrackingDto>.Failure(new ApplicationError(type, code, message)));
}

internal sealed class MockListCurrentUserReservationRequestsUseCase(MockData data, ICurrentUserContext currentUser)
    : IListCurrentUserReservationRequestsUseCase
{
    public Task<Result<IReadOnlyList<ReservationRequestSummaryDto>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            return Task.FromResult(Result<IReadOnlyList<ReservationRequestSummaryDto>>.Failure(
                new ApplicationError(ApplicationErrorType.Unauthenticated, "unauthenticated", "Necesitás iniciar sesión.")));

        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<ReservationRequestSummaryDto> mine = data.Submitted.Values
            .Concat(data.Examples(now))
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReservationRequestSummaryDto(
                r.RequestId, r.EventId, r.SeatId, ReservationRequestStatus.Pending, r.BookingStatusAt(now), r.CreatedAt))
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<ReservationRequestSummaryDto>>.Success(mine));
    }
}
