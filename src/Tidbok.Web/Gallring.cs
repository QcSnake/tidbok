using Tidbok.Core;

namespace Tidbok.Web;

/// <summary>
/// Gallrar kunduppgifter 30 dagar efter besöket. Namn, telefon, e-post och meddelande tas bort,
/// själva bokningen står kvar så att statistiken fortfarande stämmer. Körs vid start och sedan
/// var sjätte timme.
/// </summary>
public sealed class Gallring(ITidbokStore store, LocalClock clock, ILogger<Gallring> log) : BackgroundService
{
    public const int KeepDays = 30;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                var n = store.Anonymize(clock.Now.Date.AddDays(-KeepDays));
                if (n > 0) log.LogInformation("Gallrade kunduppgifter i {Antal} bokningar.", n);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Gallringen misslyckades.");
            }
        }
        while (await timer.WaitForNextTickAsync(stop));
    }
}
