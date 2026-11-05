namespace PaymentAPI.BLL.Options;

// Todo configurable desde appsettings o variables de entorno
// (BancoSimulado__RetardoMs=3000), para provocar cada escenario de la demo sin
// tocar el código.
public class BancoSimuladoOptions
{
    public const string Seccion = "BancoSimulado";

    // Un cobro mayor a este monto se rechaza por fondos insuficientes. 0 = sin límite.
    public long LimitePorCobroCentavos { get; set; }

    // Usuarios a los que el banco siempre les rechaza el cobro.
    public List<string> UsuariosSinFondos { get; set; } = [];

    // Demora antes de responder. Sirve para mostrar el timeout de Polly en Booking.
    public int RetardoMs { get; set; }
}
