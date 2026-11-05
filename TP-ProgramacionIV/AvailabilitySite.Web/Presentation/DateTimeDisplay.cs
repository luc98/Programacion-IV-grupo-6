using System.Globalization;

namespace AvailabilitySite.Web.Presentation;

/// <summary>
/// Las fechas se manejan en UTC (DateTimeOffset). Solo al mostrarlas se convierten a hora de Argentina,
/// para que no dependan de la zona horaria del servidor ni del navegador.
/// </summary>
public sealed class DateTimeDisplay
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-AR");
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");

    public string ZoneLabel => "hora de Argentina";

    /// <summary>"12/03/2027 21:00:05"</summary>
    public string DateTimeWithSeconds(DateTimeOffset value) => Local(value).ToString("dd/MM/yyyy HH:mm:ss", Culture);

    /// <summary>"viernes 12 de marzo de 2027, 21:00"</summary>
    public string Long(DateTimeOffset value) => Local(value).ToString("dddd d 'de' MMMM 'de' yyyy, HH:mm", Culture);

    /// <summary>"21:00:05"</summary>
    public string Time(DateTimeOffset value) => Local(value).ToString("HH:mm:ss", Culture);

    /// <summary>Valor ISO 8601 en UTC para el atributo datetime de &lt;time&gt;.</summary>
    public static string Iso(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private DateTimeOffset Local(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, _zone);
}
