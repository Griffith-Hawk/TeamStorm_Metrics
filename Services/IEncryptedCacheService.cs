namespace TeamStorm.Metrics.Services;

public interface IEncryptedCacheService
{
    Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory, CancellationToken cancellationToken);
}
