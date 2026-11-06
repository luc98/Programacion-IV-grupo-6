using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Infrastructure.Availability;
using AvailabilitySite.Infrastructure.Booking;
using AvailabilitySite.Infrastructure.Messaging;
using AvailabilitySite.Infrastructure.Outbox;
using AvailabilitySite.Infrastructure.Persistence.MongoDb;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AvailabilitySite.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAvailabilityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        services.AddAvailabilityMongoDb(configuration, environment?.IsDevelopment() == true);

        services.AddOptions<BookingOptions>()
            .Bind(configuration.GetSection(BookingOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
                "Booking:BaseUrl debe ser una URL absoluta.")
            .ValidateOnStart();

        services.AddHttpClient<IReservationStatusClient, BookingReservationStatusClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<BookingOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
        });

        services.AddHttpClient<IOccupiedSeatsClient, BookingOccupiedSeatsClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<BookingOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
        });

        services.AddOptions<KeycloakServiceOptions>()
            .Bind(configuration.GetSection(KeycloakServiceOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.Authority, UriKind.Absolute, out _),
                "KeycloakService:Authority debe ser una URL absoluta.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ClientId),
                "KeycloakService:ClientId es obligatorio.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ClientSecret),
                "KeycloakService:ClientSecret debe configurarse mediante User Secrets o variables de entorno.")
            .ValidateOnStart();

        services.AddHttpClient("KeycloakService");
        services.AddSingleton<IAccessTokenProvider, KeycloakClientCredentialsTokenProvider>();

        services.AddOptions<ServiceBusOptions>()
            .Bind(configuration.GetSection(ServiceBusOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "ServiceBus:ConnectionString es obligatorio.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.TopicName),
                "ServiceBus:TopicName es obligatorio.")
            .ValidateOnStart();

        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
            return new Azure.Messaging.ServiceBus.ServiceBusClient(options.ConnectionString);
        });
        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
            return serviceProvider.GetRequiredService<Azure.Messaging.ServiceBus.ServiceBusClient>()
                .CreateSender(options.TopicName);
        });
        services.AddSingleton<IServiceBusMessageSender, ServiceBusMessageSender>();
        services.AddSingleton<IReservationRequestPublisher, AzureServiceBusReservationRequestPublisher>();
        services.AddHostedService<OutboxBackgroundService>();

        return services;
    }
}
