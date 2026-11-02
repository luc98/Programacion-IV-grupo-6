using System.Collections.Concurrent;
using AvailabilitySite.Application.Events;
using AvailabilitySite.Domain.Entities;

namespace AvailabilitySite.Infrastructure.Mock;

// DATOS DE EJEMPLO EN MEMORIA. Reemplazan a la base de datos, al broker y a Booking
// solo para poder usar las pantallas. Se registra como singleton: sobrevive entre requests
// y se pierde al reiniciar la app.
public sealed class MockData
{
    // Usuario fijo de ejemplo (todavía no hay login).
    public const string ExampleUserId = "usuario-ejemplo";

    // Tiempos con los que avanza una solicitud nueva: pendiente → Held → Confirmed.
    public static readonly TimeSpan PendingFor = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan PaymentAfter = TimeSpan.FromSeconds(5);

    // Valores de BookingStatus / RejectionReason que usa el mock. Son strings en el contrato:
    // hay que acordar los valores reales con Booking.
    public const string Held = "Held";
    public const string Confirmed = "Confirmed";
    public const string Rejected = "Rejected";
    public const string Expired = "Expired";

    private static readonly TimeSpan Argentina = TimeSpan.FromHours(-3);

    public MockData()
    {
        AddEvent(1, "Recital Aurora — Gira de otoño", "Teatro Central",
            new DateTimeOffset(2027, 3, 12, 21, 0, 0, Argentina), rows: "ABCDE", seatsPerRow: 8,
            occupied: ["A-01", "A-02", "C-04", "B-05", "D-02"]);
        AddEvent(2, "Conferencia DevSur 2027", "Centro de Convenciones, Sala 2",
            new DateTimeOffset(2027, 4, 20, 9, 30, 0, Argentina), rows: "ABC", seatsPerRow: 6,
            occupied: ["A-01", "B-03"]);
        AddEvent(3, "Función privada (asientos sin publicar)", "Sala Norte",
            new DateTimeOffset(2027, 5, 2, 20, 0, 0, Argentina), rows: "", seatsPerRow: 0,
            occupied: []);
    }

    public List<EventSummaryDto> Events { get; } = [];

    // Asientos de cada evento. IsAvailable refleja solo lo ocupado "por otras personas";
    // las solicitudes enviadas en esta ejecución se suman en InMemoryAvailabilityQueryRepository.
    public Dictionary<Guid, List<Seat>> Seats { get; } = [];

    // Solicitudes enviadas desde la pantalla durante esta ejecución.
    public ConcurrentDictionary<Guid, MockReservationRequest> Submitted { get; } = new();

    public EventSummaryDto? FindEvent(Guid eventId) => Events.FirstOrDefault(e => e.EventId == eventId);

    public Seat? FindSeat(Guid eventId, Guid seatId) =>
        Seats.TryGetValue(eventId, out var seats) ? seats.FirstOrDefault(s => s.Id == seatId) : null;

    // Ids fijos para que los links no cambien entre ejecuciones.
    public static Guid EventIdOf(int n) => new($"{n:D8}-e7e7-0000-0000-000000000000");

    // "A-01" del evento 1 → 00000001-5ea7-0000-0000-000001000001
    public static Guid SeatIdOf(int eventN, string label) =>
        new($"{eventN:D8}-5ea7-0000-0000-{label[0] - 'A' + 1:D6}{int.Parse(label[2..]):D6}");

    // Una solicitud de ejemplo por cada estado, para poder ver todas las pantallas de seguimiento.
    // Son del evento 1 y del usuario de ejemplo.
    public IReadOnlyList<MockReservationRequest> Examples(DateTimeOffset now) =>
    [
        Example(1, "E-01", now.AddSeconds(-1), bookingStatus: null),
        Example(2, "B-05", now.AddMinutes(-1), Held),
        Example(3, "C-04", now.AddHours(-2), Confirmed),
        Example(4, "A-01", now.AddHours(-3), Rejected, "SeatOccupied"),
        Example(5, "E-06", now.AddHours(-4), Rejected, "PaymentFailed"),
        Example(6, "E-07", now.AddHours(-5), Rejected, "PaymentUnavailable"),
        Example(7, "E-08", now.AddDays(-1), Expired),
    ];

    private static MockReservationRequest Example(int n, string seat, DateTimeOffset createdAt, string? bookingStatus, string? rejectionReason = null) =>
        new(new Guid($"00000000-0000-0000-0000-{n:D12}"), ExampleUserId, EventIdOf(1), SeatIdOf(1, seat), $"ejemplo-{n}", createdAt)
        {
            FixedBookingStatus = bookingStatus,
            FixedRejectionReason = rejectionReason,
            IsExample = true,
        };

    private void AddEvent(int n, string name, string venue, DateTimeOffset startsAt, string rows, int seatsPerRow, string[] occupied)
    {
        var eventId = EventIdOf(n);
        Events.Add(new EventSummaryDto(eventId, name, venue, startsAt));
        Seats[eventId] = rows
            .SelectMany(row => Enumerable.Range(1, seatsPerRow).Select(number => $"{row}-{number:D2}"))
            .Select(label => new Seat
            {
                Id = SeatIdOf(n, label),
                EventId = eventId,
                Label = label,
                IsAvailable = !occupied.Contains(label),
            })
            .ToList();
    }
}

// Solicitud guardada por el mock. No es parte del contrato: es el "registro" interno que
// en el backend real viviría en la base de datos de Availability.
public sealed record MockReservationRequest(
    Guid RequestId,
    string UserId,
    Guid EventId,
    Guid SeatId,
    string IdempotencyKey,
    DateTimeOffset CreatedAt)
{
    public bool IsExample { get; init; }
    public string? FixedBookingStatus { get; init; }
    public string? FixedRejectionReason { get; init; }

    // Estado de Booking en este momento: los ejemplos son fijos; las nuevas avanzan solas
    // (sin estado = pendiente → Held → Confirmed).
    public string? BookingStatusAt(DateTimeOffset now)
    {
        if (IsExample)
            return FixedBookingStatus;

        var heldAt = CreatedAt + MockData.PendingFor;
        if (now < heldAt)
            return null;
        return now < heldAt + MockData.PaymentAfter ? MockData.Held : MockData.Confirmed;
    }

    public string? RejectionReasonAt(DateTimeOffset now) => IsExample ? FixedRejectionReason : null;
}
