using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Common;
using AvailabilitySite.Application.Reservations;
using AvailabilitySite.Domain.Entities;
using Xunit;

namespace AvailabilitySite.Application.Tests;

public sealed class RequestReservationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ValidRequest_PublishesCompatibleMessageAndReceipt()
    {
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var publisher = new FakePublisher();
        var service = CreateService(publisher: publisher);

        var result = await service.ExecuteAsync(
            new CreateReservationRequestDto(eventId, seatId, "key-1"));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotNull(publisher.Message);
        Assert.Equal(result.Value!.RequestId, publisher.Message!.SolicitudId);
        Assert.Equal(eventId, publisher.Message.EventoId);
        Assert.Equal(seatId, publisher.Message.AsientoId);
        Assert.Equal("user-1", publisher.Message.UsuarioId);
        Assert.Equal(result.Value.RequestId, publisher.Message.SolicitudId);
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
        var publisher = new FakePublisher();
        var service = CreateService(repository, publisher);

        var result = await service.ExecuteAsync(
            new CreateReservationRequestDto(existing.EventId, existing.SeatId, "key-1"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsReplay);
        Assert.Equal(existing.RequestId, result.Value.RequestId);
        Assert.Null(publisher.Message);
    }

    [Fact]
    public async Task ExecuteAsync_PublisherCancellation_IsPropagated()
    {
        var expected = new OperationCanceledException();
        var service = CreateService(publisher: new FakePublisher(() => throw expected));

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ExecuteAsync(
                new CreateReservationRequestDto(Guid.NewGuid(), Guid.NewGuid(), "key-1")));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task ExecuteAsync_PublisherError_ReturnsOutcomeUnknown()
    {
        var service = CreateService(
            publisher: new FakePublisher(() => throw new InvalidOperationException("broker failure")));

        var result = await service.ExecuteAsync(
            new CreateReservationRequestDto(Guid.NewGuid(), Guid.NewGuid(), "key-1"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationErrorType.OutcomeUnknown, result.Error!.Type);
        Assert.Equal("reservation_publication_unknown", result.Error.Code);
    }

    private static RequestReservationService CreateService(
        FakeRepository? repository = null,
        FakePublisher? publisher = null,
        FakeUserContext? userContext = null)
    {
        return new RequestReservationService(
            userContext ?? new FakeUserContext(true, "user-1"),
            repository ?? new FakeRepository(),
            publisher ?? new FakePublisher());
    }

    private sealed class FakeUserContext(bool isAuthenticated, string? userId)
        : ICurrentUserContext
    {
        public bool IsAuthenticated => isAuthenticated;

        public string? UserId => userId;
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
    }

    private sealed class FakePublisher(Action? publish = null)
        : IReservationRequestPublisher
    {
        public ReservationRequestedMessage? Message { get; private set; }

        public Task PublishAsync(
            ReservationRequestedMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            publish?.Invoke();
            Message = message;
            return Task.CompletedTask;
        }
    }
}
