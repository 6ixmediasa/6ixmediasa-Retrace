using System.Text.RegularExpressions;

namespace Retrace.Core;

public sealed class CommandParser
{
    public async Task<IReadOnlyList<RetraceEvent>> ResolveRecoveryCommandAsync(string command, IEventRepository repository, CancellationToken token = default)
    {
        var text = (command ?? string.Empty).Trim().ToLowerInvariant();
        var since = DateTime.UtcNow.AddMinutes(-10);
        var match = Regex.Match(text, @"(?:last\s+)?(\d+)\s*(minute|minutes|min|hour|hours|hr|hrs)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var n))
            since = match.Groups[2].Value.StartsWith("h") ? DateTime.UtcNow.AddHours(-n) : DateTime.UtcNow.AddMinutes(-n);
        else if (text.Contains("today")) since = DateTime.Today.ToUniversalTime();
        else if (text.Contains("hour")) since = DateTime.UtcNow.AddHours(-1);
        else if (text.Contains("just") || text.Contains("recent")) since = DateTime.UtcNow.AddMinutes(-5);

        var events = await repository.GetSinceAsync(since, token);
        if (text.Contains("deleted") || text.Contains("delete")) events = events.Where(x => x.EventType == RetraceEventType.Deleted).ToList();
        else if (text.Contains("rename")) events = events.Where(x => x.EventType == RetraceEventType.Renamed).ToList();
        else if (text.Contains("modified") || text.Contains("changed") || text.Contains("edit")) events = events.Where(x => x.EventType == RetraceEventType.Modified).ToList();
        return events.Where(x => x.RecoveryAvailable && x.Status == "Active").ToList();
    }
}
