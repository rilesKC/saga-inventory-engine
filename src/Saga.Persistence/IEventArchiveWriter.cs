namespace Saga.Persistence;

public interface IEventArchiveWriter
{
    Task PutAsync(string key, string payload, CancellationToken cancellationToken);
}
