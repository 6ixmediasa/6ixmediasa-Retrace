namespace Retrace.Core;

public interface IEventRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<long> AddAsync(RetraceEvent item, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RetraceEvent>> GetRecentAsync(int limit = 500, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RetraceEvent>> GetSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RetraceEvent>> SearchAsync(string query, int limit = 200, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(long id, string status, CancellationToken cancellationToken = default);
    Task DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
