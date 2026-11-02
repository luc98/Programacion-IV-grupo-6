using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AvailabilitySite.Application.Abstractions;
using AvailabilitySite.Application.Common;
using AvailabilitySite.Application.Reservations;

namespace AvailabilitySite.Infrastructure.Booking;

public sealed class BookingReservationStatusClient(HttpClient httpClient, IAccessTokenProvider accessTokenProvider)
    : IReservationStatusClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<Result<ReservationTrackingDto>> GetAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var token = await accessTokenProvider.GetTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return Failure(ApplicationErrorType.Unauthenticated, "booking_token_unavailable", "No hay un token disponible para consultar Booking.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/reservas/solicitudes/{requestId}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return Failure(ApplicationErrorType.DependencyUnavailable, "booking_timeout", "La consulta a Booking agotó el tiempo de espera.");
        }
        catch (HttpRequestException)
        {
            return Failure(ApplicationErrorType.DependencyUnavailable, "booking_dependency_unavailable", "No se pudo conectar con Booking.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Failure(ApplicationErrorType.NotFound, "reservation_request_not_found", "La solicitud no existe en Booking.");
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return response.StatusCode == HttpStatusCode.Unauthorized
                    ? Failure(ApplicationErrorType.Unauthenticated, "booking_unauthorized", "Booking rechazó la autenticación.")
                    : Failure(ApplicationErrorType.Forbidden, "booking_forbidden", "Booking rechazó la autorización.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return Failure(ApplicationErrorType.DependencyUnavailable, "booking_dependency_unavailable", "Booking devolvió una respuesta inesperada.");
            }

            try
            {
                var payload = await response.Content.ReadFromJsonAsync<BookingReservationStatusResponse>(JsonOptions, cancellationToken);
                return payload is null
                    ? Failure(ApplicationErrorType.DependencyUnavailable, "booking_dependency_unavailable", "Booking devolvió una respuesta vacía.")
                    : Result<ReservationTrackingDto>.Success(payload.ToApplicationDto());
            }
            catch (JsonException)
            {
                return Failure(ApplicationErrorType.DependencyUnavailable, "booking_dependency_unavailable", "Booking devolvió un JSON inválido.");
            }
        }
    }

    private static Result<ReservationTrackingDto> Failure(ApplicationErrorType type, string code, string message) =>
        Result<ReservationTrackingDto>.Failure(new ApplicationError(type, code, message));

    private sealed record BookingReservationStatusResponse(
        Guid ReservaId,
        Guid SolicitudId,
        Guid AsientoId,
        Guid EventoId,
        string Estado,
        string MotivoRechazo,
        DateTime FechaCreacionUtc,
        DateTime FechaVencimientoUtc)
    {
        public ReservationTrackingDto ToApplicationDto() => new(
            ReservaId, SolicitudId, EventoId, AsientoId, ReservationRequestStatus.Pending,
            Estado, MotivoRechazo, FechaCreacionUtc, FechaVencimientoUtc);
    }
}
