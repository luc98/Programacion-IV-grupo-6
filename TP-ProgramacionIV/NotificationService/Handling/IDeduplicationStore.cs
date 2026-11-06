namespace NotificationService.Handling;

// [JUSTIFICACIÓN - Idempotencia] Quien llama puede reintentar y el usuario no debe recibir el mismo aviso dos veces.
// Es una interfaz para poder cambiar la memoria por una base de datos.
public interface IDeduplicationStore
{
    bool TryBegin(string key);

    void Release(string key);
}
