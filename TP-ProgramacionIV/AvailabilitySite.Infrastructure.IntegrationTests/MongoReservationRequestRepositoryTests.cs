using System.Text.Json;
using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Common;
using AvailabilitySite.Application.Reservations;
using AvailabilitySite.Domain.Entities;
using AvailabilitySite.Infrastructure.Outbox;
using AvailabilitySite.Infrastructure.Persistence;
using AvailabilitySite.Infrastructure.Persistence.MongoDb;
using AvailabilitySite.Infrastructure.Persistence.MongoDb.Documents;
using MongoDB.Driver;
using Xunit;

namespace AvailabilitySite.Infrastructure.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class MongoReservationRequestRepositoryTests(MongoTestFixture fixture)
{
    [SkippableFact]
    public async Task AddAsync_PersistsRequestAndOutboxMessageTogether()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        var request = NewRequest("user-1", "key-1");

        await db.Requests.AddAsync(request);

        var stored = await db.Requests.FindByIdAsync(request.RequestId);
        Assert.NotNull(stored);
        Assert.Equal(request.EventId, stored.EventId);
        Assert.Equal(request.SeatId, stored.SeatId);
        Assert.Equal("user-1", stored.UserId);
        Assert.Equal("key-1", stored.IdempotencyKey);
        Assert.Equal(ReservationRequestStatus.Pending, stored.Status);
        Assert.Equal(request.ReceivedAt, stored.ReceivedAt);

        var outbox = await db.Context.OutboxMessages.Find(m => m.RequestId == request.RequestId).SingleAsync();
        Assert.Equal(OutboxMessageStatus.Pending, outbox.Status);
        Assert.Equal(MongoReservationRequestRepository.ReservationRequestedMessageType, outbox.MessageType);
        var message = JsonSerializer.Deserialize<ReservationRequestedMessage>(outbox.Payload)!;
        Assert.Equal(new ReservationRequestedMessage(request.RequestId, request.EventId, request.SeatId, "user-1"), message);
        // El JSON usa los nombres acordados con Booking.
        Assert.Contains("\"SolicitudId\"", outbox.Payload);
        Assert.Contains("\"UsuarioId\"", outbox.Payload);
    }

    [SkippableFact]
    public async Task FindByIdempotencyKeyAsync_ReturnsOnlyThatUsersRequest()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        var request = NewRequest("user-1", "key-1");
        await db.Requests.AddAsync(request);
        await db.Requests.AddAsync(NewRequest("user-2", "key-1"));

        var found = await db.Requests.FindByIdempotencyKeyAsync("user-1", "key-1");

        Assert.Equal(request.RequestId, found?.RequestId);
        Assert.Null(await db.Requests.FindByIdempotencyKeyAsync("user-1", "otra-clave"));
    }

    [SkippableFact]
    public async Task ListByUserAsync_ReturnsUsersRequestsNewestFirst()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        var older = NewRequest("user-1", "key-1", DateTimeOffset.UtcNow.AddMinutes(-5));
        var newer = NewRequest("user-1", "key-2", DateTimeOffset.UtcNow);
        await db.Requests.AddAsync(older);
        await db.Requests.AddAsync(newer);
        await db.Requests.AddAsync(NewRequest("user-2", "key-3"));

        var requests = await db.Requests.ListByUserAsync("user-1");

        Assert.Equal([newer.RequestId, older.RequestId], requests.Select(r => r.RequestId));
    }

    [SkippableFact]
    public async Task AddAsync_RepeatedIdempotencyKey_ThrowsWithOriginalRequestAndDoesNotDuplicate()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        var original = NewRequest("user-1", "key-1");
        await db.Requests.AddAsync(original);

        var duplicate = await Assert.ThrowsAsync<DuplicateReservationRequestException>(
            () => db.Requests.AddAsync(NewRequest("user-1", "key-1")));

        Assert.Equal(original.RequestId, duplicate.Existing.RequestId);
        Assert.Equal(1, await db.Context.ReservationRequests.CountDocumentsAsync(FilterDefinition<ReservationRequestDocument>.Empty));
        Assert.Equal(1, await db.Context.OutboxMessages.CountDocumentsAsync(FilterDefinition<OutboxMessageDocument>.Empty));
    }

    [SkippableFact]
    public async Task ConcurrentRequestsWithSameKey_CreateExactlyOneRequestAndOneOutboxMessage()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();

        // 10 envíos simultáneos con la misma clave: ninguno consulta antes; decide el índice único.
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            db.Requests.AddOrGetExistingAsync(NewRequest("user-1", "key-1", eventId: eventId, seatId: seatId))));

        Assert.Single(results, r => r.Created);
        Assert.Single(results.Select(r => r.Request.RequestId).Distinct());
        Assert.Equal(1, await db.Context.ReservationRequests.CountDocumentsAsync(FilterDefinition<ReservationRequestDocument>.Empty));
        Assert.Equal(1, await db.Context.OutboxMessages.CountDocumentsAsync(FilterDefinition<OutboxMessageDocument>.Empty));
    }

    [SkippableFact]
    public async Task TransactionFailure_LeavesNeitherRequestNorOutboxMessage()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        var request = NewRequest("user-1", "key-1");
        // Se fuerza que falle el segundo insert de la transacción (Outbox): ya hay un mensaje
        // del mismo tipo para ese RequestId y el índice único lo rechaza.
        await db.Context.OutboxMessages.InsertOneAsync(new OutboxMessageDocument
        {
            MessageId = Guid.NewGuid(),
            RequestId = request.RequestId,
            MessageType = MongoReservationRequestRepository.ReservationRequestedMessageType,
            Payload = "{}",
            CreatedAt = DateTime.UtcNow,
            NextAttemptAt = DateTime.UtcNow,
        });

        var error = await Assert.ThrowsAsync<AvailabilityPersistenceException>(() => db.Requests.AddAsync(request));

        Assert.IsNotType<DuplicateReservationRequestException>(error);
        Assert.DoesNotContain("E11000", error.Message);
        // El insert de la solicitud se revirtió con la transacción.
        Assert.Null(await db.Requests.FindByIdAsync(request.RequestId));
        Assert.Equal(1, await db.Context.OutboxMessages.CountDocumentsAsync(FilterDefinition<OutboxMessageDocument>.Empty));
    }

    [SkippableFact]
    public async Task AddAsync_UnreachableDatabase_ThrowsTransientErrorWithoutMongoDetails()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        var options = Microsoft.Extensions.Options.Options.Create(new MongoDbOptions { ConnectionString = "x", DatabaseName = "AvailabilityDb_Tests_Unreachable" });
        var client = new MongoClient(new MongoClientSettings
        {
            Server = new MongoServerAddress("127.0.0.1", 1),
            ServerSelectionTimeout = TimeSpan.FromMilliseconds(500),
            ConnectTimeout = TimeSpan.FromMilliseconds(500),
        });
        var repository = new MongoReservationRequestRepository(new MongoDbContext(client, options));

        var error = await Assert.ThrowsAsync<AvailabilityPersistenceException>(
            () => repository.FindByIdempotencyKeyAsync("user-1", "key-1"));

        Assert.True(error.IsTransient);
        Assert.Equal("La base de datos de Availability no está disponible en este momento.", error.Message);
    }

    [SkippableFact]
    public async Task FindByIdempotencyKeyAsync_CancelledToken_PropagatesCancellation()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => db.Requests.FindByIdempotencyKeyAsync("user-1", "key-1", cancellation.Token));
    }

    // Con el caso de uso real de Application (RequestReservationService) sobre MongoDB.

    [SkippableFact]
    public async Task RequestReservationService_RepeatedRequest_ReturnsOriginalRequestAsReplay()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        var dto = new CreateReservationRequestDto(Guid.NewGuid(), Guid.NewGuid(), "key-1");
        var service = NewService(db.Requests, dto.SeatId);

        var first = await service.ExecuteAsync(dto);
        var second = await service.ExecuteAsync(dto);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.False(first.Value!.IsReplay);
        Assert.True(second.Value!.IsReplay);
        Assert.Equal(first.Value.RequestId, second.Value.RequestId);
        Assert.Equal(1, await db.Context.ReservationRequests.CountDocumentsAsync(FilterDefinition<ReservationRequestDocument>.Empty));
    }

    [SkippableFact]
    public async Task RequestReservationService_SameKeyWithDifferentSeat_ReturnsConflict()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        var eventId = Guid.NewGuid();
        var firstRequest = new CreateReservationRequestDto(eventId, Guid.NewGuid(), "key-1");
        var service = NewService(db.Requests, firstRequest.SeatId);
        await service.ExecuteAsync(firstRequest);

        var result = await service.ExecuteAsync(new CreateReservationRequestDto(eventId, Guid.NewGuid(), "key-1"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationErrorType.Validation, result.Error!.Type);
        Assert.Equal("idempotency_key_conflict", result.Error.Code);
        Assert.Equal(1, await db.Context.ReservationRequests.CountDocumentsAsync(FilterDefinition<ReservationRequestDocument>.Empty));
    }

    private static ReservationRequestRecord NewRequest(
        string userId,
        string idempotencyKey,
        DateTimeOffset? receivedAt = null,
        Guid? eventId = null,
        Guid? seatId = null)
    {
        // Mongo guarda milisegundos: se trunca para poder comparar lo leído con lo escrito.
        var at = receivedAt ?? DateTimeOffset.UtcNow;
        at = at.AddTicks(-(at.Ticks % TimeSpan.TicksPerMillisecond));
        return new ReservationRequestRecord(
            Guid.NewGuid(), eventId ?? Guid.NewGuid(), seatId ?? Guid.NewGuid(), userId, idempotencyKey, at, ReservationRequestStatus.Pending);
    }

    private static RequestReservationService NewService(IReservationRequestRepository repository, Guid seatId) =>
        new(new TestUser("user-1"), repository, new FixedSeatAvailability(seatId));

    private sealed class TestUser(string userId) : ICurrentUserContext
    {
        public bool IsAuthenticated => true;

        public string? UserId => userId;
    }

    private sealed class FixedSeatAvailability(Guid seatId) : IAvailabilityQueryRepository
    {
        public Task<IReadOnlyList<Seat>> GetSeatsAsync(Guid eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Seat>>([new Seat { Id = seatId, EventId = eventId }]);
    }
}

