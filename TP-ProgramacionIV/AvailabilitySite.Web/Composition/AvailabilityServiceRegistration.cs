using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Availability;
using AvailabilitySite.Infrastructure;
using AvailabilitySite.Web.Presentation;
using AvailabilitySite.Web.Security;

namespace AvailabilitySite.Web.Composition;

// Composition root: el único lugar que decide qué implementación se usa para cada interfaz.
// Para pasar al backend real se cambian estas líneas; controllers y vistas no se tocan.
public static class AvailabilityServiceRegistration
{
    public static WebApplicationBuilder AddAvailability(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<DateTimeDisplay>();
        builder.Services.AddScoped<EventLookup>();

        // Caso de uso ya implementado en Application (usa IAvailabilityQueryRepository).
        builder.Services.AddScoped<IGetEventAvailabilityUseCase, GetEventAvailabilityService>();

        // TODO(backend): reemplazar por el usuario de la sesión real (login con el servidor de autorización).
        builder.Services.AddScoped<ICurrentUserContext, ExampleCurrentUserContext>();

        // TODO(backend): reemplazar por AddAvailabilityIntegration() con SQL + broker + Booking.
        builder.Services.AddAvailabilityMock();

        return builder;
    }
}
