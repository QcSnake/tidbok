using System.Text;
using Tidbok.Core;
using static Tidbok.Tests.Kit;

namespace Tidbok.Tests;

public class IcsTests
{
    private static string Make(string start, string end, string address = "Drottninggatan 22, 702 21 Örebro")
    {
        var booking = new Booking
        {
            Reference = "TB-ABC234", CustomerName = "Sara", Start = T(start), End = T(end), Status = BookingStatus.Booked
        };
        var business = Business() with { Name = "Salong Silhuett", Address = address };
        return Ics.ForBooking(booking, business, Service(60) with { Name = "Damklippning" }, Person(1, 1),
            Zone, new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc), "https://tidbok.example/avboka/abc");
    }

    [Fact]
    public void Summer_time_is_two_hours_ahead_of_utc()
    {
        var ics = Make("2026-06-10T10:00", "2026-06-10T11:00");
        Assert.Contains("DTSTART:20260610T080000Z", ics);
        Assert.Contains("DTEND:20260610T090000Z", ics);
    }

    [Fact]
    public void Winter_time_is_one_hour_ahead_of_utc()
    {
        var ics = Make("2026-12-10T10:00", "2026-12-10T11:00");
        Assert.Contains("DTSTART:20261210T090000Z", ics);
    }

    [Fact]
    public void Commas_and_semicolons_are_escaped()
    {
        var ics = Make("2026-10-06T10:00", "2026-10-06T11:00", address: "Gata 1; plan 2, Örebro");
        Assert.Contains(@"LOCATION:Gata 1\; plan 2\, Örebro", ics);
        Assert.Contains(@"SUMMARY:Damklippning\, Salong Silhuett", ics);
    }

    [Fact]
    public void Lines_are_folded_at_75_bytes_and_use_crlf()
    {
        var ics = Make("2026-10-06T10:00", "2026-10-06T11:00");
        var lines = ics.Split("\r\n");

        Assert.All(lines, l => Assert.True(Encoding.UTF8.GetByteCount(l) <= 75, l));
        Assert.Contains(lines, l => l.StartsWith(' '));
        Assert.DoesNotContain("\n", ics.Replace("\r\n", ""));
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
    }
}
