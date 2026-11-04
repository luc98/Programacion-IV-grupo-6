using AvailabilitySite.Application.Reservations;
using MongoDB.Driver;

namespace AvailabilitySite.Infrastructure.Persistence;

// Errores de persistencia sin detalles internos de MongoDB. La excepción original queda como
// InnerException para los logs, pero el mensaje es apto para mostrarse.
public class AvailabilityPersistenceException(string message, bool isTransient, Exception? innerException = null)
    : Exception(message, innerException)
{
    // true: conviene reintentar (base caída, timeout). Se corresponde con DependencyUnavailable.
    public bool IsTransient { get; } = isTransient;
}

// Se intentó guardar una solicitud con un UserId + IdempotencyKey que ya existe
// (típicamente, dos envíos simultáneos). Existing es la solicitud original.
public sealed class DuplicateReservationRequestException(ReservationRequestRecord existing)
    : AvailabilityPersistenceException("Ya existe una solicitud con esa clave de idempotencia.", isTransient: false)
{
    public ReservationRequestRecord Existing { get; } = existing;
}

internal static class MongoErrors
{
    public static bool IsDuplicateKey(Exception exception) => exception switch
    {
        MongoWriteException write => write.WriteError?.Category == ServerErrorCategory.DuplicateKey,
        MongoBulkWriteException bulk => bulk.WriteErrors.Any(e => e.Category == ServerErrorCategory.DuplicateKey),
        MongoCommandException command => command.Code == 11000,
        _ => false,
    };

    // Ejecuta una operación y traduce las excepciones de MongoDB. La cancelación se propaga tal cual.
    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AvailabilityPersistenceException)
        {
            throw;
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            throw new AvailabilityPersistenceException(
                "La base de datos de Availability no está disponible en este momento.", isTransient: true, exception);
        }
        catch (MongoException exception)
        {
            throw new AvailabilityPersistenceException(
                "No se pudo completar la operación en la base de datos de Availability.", isTransient: false, exception);
        }
    }

    public static Task ExecuteAsync(Func<Task> operation) =>
        ExecuteAsync(async () =>
        {
            await operation();
            return true;
        });

    private static bool IsTransient(Exception exception) => exception switch
    {
        TimeoutException => true,
        MongoConnectionException => true,
        MongoExecutionTimeoutException => true,
        MongoNotPrimaryException => true,
        MongoNodeIsRecoveringException => true,
        MongoException mongo when mongo.HasErrorLabel("TransientTransactionError") => true,
        MongoException mongo when mongo.HasErrorLabel("RetryableWriteError") => true,
        _ => false,
    };
}

