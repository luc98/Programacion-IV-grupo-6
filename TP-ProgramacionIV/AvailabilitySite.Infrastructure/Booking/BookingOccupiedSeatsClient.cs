using System.Net.Http.Headers;
using System.Net.Http.Json;
using AvailabilitySite.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AvailabilitySite.Infrastructure.Booking;

public sealed class BookingOccupiedSeatsClient(
    HttpClient httpClient,
    IAccessTokenProvider accessTokenProvider,
    ILogger<BookingOccupiedSeatsClient> logger) : IOccupiedSeatsClient
{
    // Tope corto: esta consulta forma parte de una página pública y no puede quedar esperando a Booking.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<IReadOnlySet<Guid>?> GetOccupiedSeatIdsAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            var token = await accessTokenProvider.GetTokenAsync(timeout.Token);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/reservas/eventos/{eventId}/asientos-ocupados");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();

            var seats = await response.Content.ReadFromJsonAsync<Guid[]>(timeout.Token);
            return seats?.ToHashSet();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "No se pudo consultar a Booking los asientos ocupados del evento {EventId}: {Motivo}",
                eventId, exception.Message);
            return null;
        }
    }
}
