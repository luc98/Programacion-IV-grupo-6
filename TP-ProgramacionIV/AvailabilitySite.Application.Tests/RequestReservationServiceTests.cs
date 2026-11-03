using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Common;
using AvailabilitySite.Application.Reservations;
using AvailabilitySite.Domain.Entities;
using Xunit;

namespace AvailabilitySite.Application.Tests;

public sealed class RequestReservationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ValidRequest_ReturnsPendingReceipt()
    {
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var service = CreateService(availability: new FakeAvailability(eventId, seatId));

        var result = await service.ExecuteAsync(
            new CreateReservationRequestDto(eventId, seatId, "key-1"));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(ReservationRequestStatus.Pending, result.Value.Status);
        Assert.False(result.Value.IsReplay);
    }

    [Fact]
    public async Task ExecuteAsync_UnauthenticatedUser_ReturnsUnauthenticatedError()
    {
        var service = CreateService(userContext: new FakeUserContext(false, null));

        var result = await service.ExecuteAsync(
            new CreateReservationRequestDto(Guid.NewGuid(), Guid.NewGuid(), "key-1"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationErrorType.Unauthenticated, result.Error!.Type);
    }

    [Fact]
    public async Task ExecuteAsync_ReusedIdempotencyKey_ReturnsReplayWithoutPublishing()
    {
        var existing = new ReservationRequestRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "user-1",
            "key-1",
            DateTimeOffset.UtcNow,
            ReservationRequestStatus.Pending);
        var repository = new FakeRepository(existing);
        var service = CreateService(repository);

        var result = await service.ExecuteAsync(
            new CreateReservationRequestDto(existing.EventId, existing.SeatId, "key-1"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsReplay);
        Assert.Equal(existing.RequestId, result.Value.RequestId);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownSeat_ReturnsNotFound()
    {
        var service = CreateService(availability: new FakeAvailability(Guid.NewGuid(), Guid.NewGuid()));

        var result = await service.ExecuteAsync(
            new CreateReservationRequestDto(Guid.NewGuid(), Guid.NewGuid(), "key-1"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationErrorType.NotFound, result.Error!.Type);
        Assert.Equal("seat_not_found", result.Error.Code);
    }

    private static RequestReservationService CreateService(
        FakeRepository? repository = null,
        FakeUserContext? userContext = null,
        FakeAvailability? availability = null)
    {
        return new RequestReservationService(
            userContext ?? new FakeUserContext(true, "user-1"),
            repository ?? new FakeRepository(),
            availability ?? new FakeAvailability());
    }

    private sealed class FakeUserContext(bool isAuthenticated, string? userId)
        : ICurrentUserContext
    {
        public bool IsAuthenticated => isAuthenticated;

        public string? UserId => userId;
    }

    private sealed class FakeAvailability(Guid? eventId = null, Guid? seatId = null) : IAvailabilityQueryRepository
    {
        private readonly Guid eventId = eventId ?? Guid.NewGuid();
        private readonly Guid seatId = seatId ?? Guid.NewGuid();

        public Task<IReadOnlyList<Seat>> GetSeatsAsync(Guid requestedEventId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Seat> seats = requestedEventId == eventId
                ? [new Seat { Id = seatId, EventId = eventId, Label = "A-01", IsAvailable = true }]
                : [];
            return Task.FromResult(seats);
        }
    }

    private sealed class FakeRepository(ReservationRequestRecord? existing = null)
        : IReservationRequestRepository
    {
        public List<ReservationRequestRecord> Added { get; } = [];

        public Task<ReservationRequestRecord?> FindByIdempotencyKeyAsync(
            string userId,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(existing is not null &&
                existing.UserId == userId && existing.IdempotencyKey == idempotencyKey
                    ? existing
                    : null);
        }

        public Task AddAsync(
            ReservationRequestRecord request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Added.Add(request);
            return Task.CompletedTask;
        }

        public Task<(ReservationRequestRecord Request, bool Created)> AddOrGetExistingAsync(
            ReservationRequestRecord request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (existing is not null)
            {
                return Task.FromResult((existing, false));
            }

            Added.Add(request);
            return Task.FromResult((request, true));
        }
    }
}
