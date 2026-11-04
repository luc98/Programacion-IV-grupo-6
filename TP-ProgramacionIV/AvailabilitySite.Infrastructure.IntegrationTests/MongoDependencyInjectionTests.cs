using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Infrastructure.Outbox;
using AvailabilitySite.Infrastructure.Persistence.MongoDb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace AvailabilitySite.Infrastructure.IntegrationTests;

[Collection(MongoCollection.Name)]
public sealed class MongoDependencyInjectionTests(MongoTestFixture fixture)
{
    [SkippableFact]
    public async Task AddAvailabilityMongoDb_RegistersMongoRepositoriesAndCreatesIndexesOnStart()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason);
        var databaseName = $"AvailabilityDb_Tests_{Guid.NewGuid():N}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDB:ConnectionString"] = fixture.ConnectionString,
                ["MongoDB:DatabaseName"] = databaseName,
            })
            .Build();

        await using var provider = new ServiceCollection()
            .AddAvailabilityMongoDb(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<MongoAvailabilityQueryRepository>(scope.ServiceProvider.GetRequiredService<IAvailabilityQueryRepository>());
        Assert.IsType<MongoReservationRequestRepository>(scope.ServiceProvider.GetRequiredService<IReservationRequestRepository>());
        Assert.IsType<MongoOutboxRepository>(scope.ServiceProvider.GetRequiredService<IOutboxRepository>());

        foreach (var hostedService in provider.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        var context = provider.GetRequiredService<MongoDbContext>();
        Assert.Equal(databaseName, context.Database.DatabaseNamespace.DatabaseName);
        var indexes = await (await context.ReservationRequests.Indexes.ListAsync()).ToListAsync();
        Assert.Contains(indexes, index => index["name"] == MongoIndexInitializer.RequestsByIdempotencyKey);
        await context.Client.DropDatabaseAsync(databaseName);
    }

    [Fact]
    public void AddAvailabilityMongoDb_WithoutConnectionString_FailsValidation()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var provider = new ServiceCollection().AddAvailabilityMongoDb(configuration).BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MongoDbOptions>>().Value);

        Assert.Contains("MongoDB:ConnectionString", error.Message);
        Assert.Equal("AvailabilityDb", new MongoDbOptions().DatabaseName);
    }
}
