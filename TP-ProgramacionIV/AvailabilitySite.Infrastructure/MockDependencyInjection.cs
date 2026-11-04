using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Events;
using AvailabilitySite.Application.Reservations;
using AvailabilitySite.Infrastructure.Mock;
using Microsoft.Extensions.DependencyInjection;

namespace AvailabilitySite.Infrastructure;

// Punto único donde se elige qué implementación se usa para las interfaces de Application.
public static class MockDependencyInjection
{
    // Mocks con datos de ejemplo (lo que se usa hoy).
    public static IServiceCollection AddAvailabilityMock(this IServiceCollection services)
    {
        services.AddSingleton<MockData>();
        services.AddScoped<IAvailabilityQueryRepository, InMemoryAvailabilityQueryRepository>();
        services.AddScoped<IListEventsUseCase, MockListEventsUseCase>();
        services.AddScoped<IRequestReservationUseCase, MockRequestReservationUseCase>();
        services.AddScoped<IGetReservationRequestStatusUseCase, MockGetReservationRequestStatusUseCase>();
        services.AddScoped<IListCurrentUserReservationRequestsUseCase, MockListCurrentUserReservationRequestsUseCase>();
        return services;
    }

    // TODO(backend): crear AddAvailabilityIntegration() con el repositorio real (SQL), el broker y
    // el cliente de Booking, y los casos de uso reales en Application; llamarlo desde Program.cs
    // en lugar de AddAvailabilityMock().
}

// mock de ejemplo realizado por jonathan mariño despues hay que borrar simplemente el archivo