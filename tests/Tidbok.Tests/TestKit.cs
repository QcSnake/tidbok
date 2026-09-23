using Tidbok.Core;
using Tidbok.Data;

namespace Tidbok.Tests;

/// <summary>En klocka som står still där testet säger.</summary>
public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Value { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Value;
}

public static class Kit
{
    public static readonly TimeZoneInfo Zone = LocalClock.Stockholm();

    /// <summary>Klocka som visar en viss lokal tid i Stockholm.</summary>
    public static (FixedTime Time, LocalClock Clock) ClockAt(DateTime local)
    {
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);
        var time = new FixedTime(new DateTimeOffset(utc, TimeSpan.Zero));
        return (time, new LocalClock(time, Zone));
    }

    public static DateTime T(string s) => Db.ParseTime(s);
    public static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd");

    public static Business Business(BookingSettings? settings = null) => new()
    {
        Id = 1, Slug = "test", Name = "Testsalongen", Settings = settings ?? new BookingSettings()
    };

    public static Service Service(int minutes, int id = 1) => new() { Id = id, BusinessId = 1, Name = "Klippning", DurationMinutes = minutes };

    public static Staff Person(int id, params int[] services) => new() { Id = id, BusinessId = 1, Name = $"Person {id}", ServiceIds = services, Sort = id };

    public static WorkingHours Shift(int staff, DayOfWeek day, string from, string to) =>
        new(staff, day, Minutes(from), Minutes(to));

    private static int Minutes(string hhmm) => int.Parse(hhmm[..2]) * 60 + int.Parse(hhmm[3..]);

    public static string[] Times(IEnumerable<Slot> slots) => slots.Select(s => s.Time.ToString("HH:mm")).ToArray();

    /// <summary>En tom databasfil i temp-mappen med demodatan inläst.</summary>
    public static SqliteStore SeededStore(DateTime now)
    {
        var path = Path.Combine(Path.GetTempPath(), "tidbok-test-" + Guid.NewGuid().ToString("N") + ".db");
        var store = new SqliteStore(path);
        Seed.Run(store, now);
        return store;
    }

    public static SqliteStore EmptyStore()
    {
        var path = Path.Combine(Path.GetTempPath(), "tidbok-test-" + Guid.NewGuid().ToString("N") + ".db");
        return new SqliteStore(path);
    }
}
