using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Infrastructure.Availability;
using AvailabilitySite.Infrastructure.Booking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AvailabilitySite.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAvailabilityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IAvailabilityQueryRepository, InMemoryAvailabilityQueryRepository>();

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

        return services;
    }
}
