using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Availability;
using AvailabilitySite.Application.Common;
using AvailabilitySite.Domain.Entities;
using Xunit;

namespace AvailabilitySite.Application.Tests;

public sealed class GetEventAvailabilityServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ValidEvent_ReturnsAllSeatData()
    {
        var eventId = Guid.NewGuid();
        var seats = new[]
        {
            new Seat { Id = Guid.NewGuid(), EventId = eventId, Label = "A-01", IsAvailable = true },
            new Seat { Id = Guid.NewGuid(), EventId = eventId, Label = "A-02", IsAvailable = false }
        };
        var service = CreateService(seats);

        var result = await service.ExecuteAsync(eventId);
        Assert.True(result.IsSuccess);
        var availability = result.Value!;

        Assert.Equal(eventId, availability.EventId);
        Assert.Equal(2, availability.Seats.Count);
        Assert.Equal(seats[0].Id, availability.Seats[0].SeatId);
        Assert.Equal(seats[0].Label, availability.Seats[0].Label);
        Assert.Equal(seats[0].IsAvailable, availability.Seats[0].IsAvailable);
        Assert.Equal(seats[1].Id, availability.Seats[1].SeatId);
        Assert.Equal(seats[1].Label, availability.Seats[1].Label);
        Assert.Equal(seats[1].IsAvailable, availability.Seats[1].IsAvailable);
    }

    [Fact]
    public async Task ExecuteAsync_EventWithoutSeats_ReturnsEmptyList()
    {
        var service = CreateService([]);

        var result = await service.ExecuteAsync(Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Seats);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyEventId_ReturnsValidationError()
    {
        var service = CreateService([]);

        var result = await service.ExecuteAsync(Guid.Empty);

        Assert.False(result.IsSuccess);
        Assert.Equal("event_id_required", result.Error!.Code);
        Assert.Equal(ApplicationErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryCancellation_IsPropagated()
    {
        var expected = new OperationCanceledException();
        var service = new GetEventAvailabilityService(
            new FakeAvailabilityQueryRepository(_ => throw expected),
            new FakeOccupiedSeatsClient(new HashSet<Guid>()));

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ExecuteAsync(Guid.NewGuid()));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryError_IsPropagated()
    {
        var expected = new InvalidOperationException("repository failure");
        var service = new GetEventAvailabilityService(
            new FakeAvailabilityQueryRepository(_ => throw expected),
            new FakeOccupiedSeatsClient(new HashSet<Guid>()));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(Guid.NewGuid()));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task ExecuteAsync_SeatReservedInBooking_IsNotAvailable()
    {
        var eventId = Guid.NewGuid();
        var occupied = new Seat { Id = Guid.NewGuid(), EventId = eventId, Label = "A-01", IsAvailable = true };
        var free = new Seat { Id = Guid.NewGuid(), EventId = eventId, Label = "A-02", IsAvailable = true };
        var service = CreateService([occupied, free], new HashSet<Guid> { occupied.Id });

        var result = await service.ExecuteAsync(eventId);

        Assert.False(result.Value!.Seats[0].IsAvailable);
        Assert.True(result.Value!.Seats[1].IsAvailable);
    }

    [Fact]
    public async Task ExecuteAsync_BookingUnavailable_UsesCatalog()
    {
        var eventId = Guid.NewGuid();
        var seat = new Seat { Id = Guid.NewGuid(), EventId = eventId, Label = "A-01", IsAvailable = true };
        var service = CreateService([seat], occupied: null);

        var result = await service.ExecuteAsync(eventId);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Seats[0].IsAvailable);
    }

    private static GetEventAvailabilityService CreateService(
        IReadOnlyList<Seat> seats,
        IReadOnlySet<Guid>? occupied = null)
    {
        return new GetEventAvailabilityService(
            new FakeAvailabilityQueryRepository(_ => seats),
            new FakeOccupiedSeatsClient(occupied));
    }

    private sealed class FakeOccupiedSeatsClient(IReadOnlySet<Guid>? occupied) : IOccupiedSeatsClient
    {
        public Task<IReadOnlySet<Guid>?> GetOccupiedSeatIdsAsync(
            Guid eventId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(occupied);
    }

    private sealed class FakeAvailabilityQueryRepository(
        Func<Guid, IReadOnlyList<Seat>> getSeats)
        : IAvailabilityQueryRepository
    {
        public Task<IReadOnlyList<AvailabilitySite.Application.Events.EventSummaryDto>> ListEventsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AvailabilitySite.Application.Events.EventSummaryDto>>([]);

        public Task<IReadOnlyList<Seat>> GetSeatsAsync(
            Guid eventId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(getSeats(eventId));
        }
    }
}
