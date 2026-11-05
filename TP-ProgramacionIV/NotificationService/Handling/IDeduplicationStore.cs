namespace NotificationService.Handling;

public interface IDeduplicationStore
{
    bool TryBegin(string key);

    void Release(string key);
}
