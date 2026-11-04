using AvailabilitySite.Application.Availability;
using AvailabilitySite.Application.Events;
using AvailabilitySite.Application.Reservations;
using Microsoft.Extensions.DependencyInjection;

namespace AvailabilitySite.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAvailabilityApplication(
        this IServiceCollection services)
    {
        services.AddScoped<IGetEventAvailabilityUseCase, GetEventAvailabilityService>();
        services.AddScoped<IListEventsUseCase, ListEventsService>();
        services.AddScoped<IGetReservationRequestStatusUseCase, GetReservationRequestStatusService>();
        services.AddScoped<IRequestReservationUseCase, RequestReservationService>();
        services.AddScoped<IListCurrentUserReservationRequestsUseCase, ListCurrentUserReservationRequestsService>();

        return services;
    }
}
