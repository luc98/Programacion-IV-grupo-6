using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Infrastructure.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace AvailabilitySite.Infrastructure.Persistence.MongoDb;

public static class MongoDbServiceCollectionExtensions
{
    // Conexión, contexto, índices y repositorios reales de MongoDB.
    public static IServiceCollection AddAvailabilityMongoDb(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MongoDbOptions>()
            .Bind(configuration.GetSection(MongoDbOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "MongoDB:ConnectionString es obligatoria (configurarla con User Secrets o variables de entorno).")
            .Validate(options => !string.IsNullOrWhiteSpace(options.DatabaseName),
                "MongoDB:DatabaseName es obligatorio.")
            .ValidateOnStart();

        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .Validate(options => options.MaxAttempts > 0, "MongoDB:Outbox:MaxAttempts debe ser mayor a 0.")
            .ValidateOnStart();

        services.AddSingleton<IMongoClient>(serviceProvider =>
            new MongoClient(serviceProvider.GetRequiredService<IOptions<MongoDbOptions>>().Value.ConnectionString));
        services.AddSingleton<MongoDbContext>();
        services.AddSingleton<MongoIndexInitializer>();
        services.AddHostedService<MongoIndexInitializerHostedService>();
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<MongoAvailabilityQueryRepository>();
        services.AddScoped<IAvailabilityQueryRepository>(sp => sp.GetRequiredService<MongoAvailabilityQueryRepository>());
        services.AddScoped<MongoReservationRequestRepository>();
        services.AddScoped<IReservationRequestRepository>(sp => sp.GetRequiredService<MongoReservationRequestRepository>());
        services.AddScoped<IOutboxRepository, MongoOutboxRepository>();

        return services;
    }
}
