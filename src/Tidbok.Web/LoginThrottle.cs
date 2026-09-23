using System.Collections.Concurrent;

namespace Tidbok.Web;

/// <summary>
/// Bromsar gissning av lösenord. Räknar bara misslyckade försök, per IP-adress, i ett
/// rullande fönster på 15 minuter. Efter åtta fel stängs inloggningen för den adressen tills
/// fönstret gått ut. Den som skriver rätt påverkas aldrig.
/// </summary>
public sealed class LoginThrottle(TimeProvider time)
{
    private const int MaxFailures = 8;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _failures = new();

    public bool IsBlocked(string ip)
    {
        if (!_failures.TryGetValue(ip, out var q)) return false;
        lock (q)
        {
            Trim(q);
            return q.Count >= MaxFailures;
        }
    }

    public void Fail(string ip)
    {
        var q = _failures.GetOrAdd(ip, _ => new Queue<DateTimeOffset>());
        lock (q)
        {
            Trim(q);
            q.Enqueue(time.GetUtcNow());
        }
    }

    public void Succeed(string ip) => _failures.TryRemove(ip, out _);

    private void Trim(Queue<DateTimeOffset> q)
    {
        var limit = time.GetUtcNow() - Window;
        while (q.Count > 0 && q.Peek() < limit) q.Dequeue();
    }
}
