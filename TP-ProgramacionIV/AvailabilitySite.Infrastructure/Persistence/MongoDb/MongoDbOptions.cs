namespace AvailabilitySite.Infrastructure.Persistence.MongoDb;

// Se configura con MongoDB:ConnectionString y MongoDB:DatabaseName.
// La cadena de conexión va en User Secrets o variables de entorno, nunca en el repositorio.
public sealed class MongoDbOptions
{
    public const string SectionName = "MongoDB";

    public string ConnectionString { get; set; } = string.Empty;

    public string DatabaseName { get; set; } = "AvailabilityDb";
}

// Parámetros del Outbox (sección MongoDB:Outbox). Los usa el publisher al reclamar y reintentar mensajes.
public sealed class OutboxOptions
{
    public const string SectionName = "MongoDB:Outbox";

    // Intentos fallidos a partir de los cuales el mensaje queda en Failed y deja de reintentarse.
    public int MaxAttempts { get; set; } = 5;

    // Tiempo durante el cual un mensaje reclamado queda reservado para un proceso.
    // Si el proceso se cae, al vencer otro proceso puede volver a reclamarlo.
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    // Espera antes del primer reintento; se duplica en cada fallo (hasta MaxRetryDelay).
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
}
