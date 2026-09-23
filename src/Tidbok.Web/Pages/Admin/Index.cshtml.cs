using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Tidbok.Core;

namespace Tidbok.Web.Pages.Admin;

/// <summary>Dagsvyn: en kolumn per person, bokningar och blockeringar på en tidslinje, veckans siffror överst.</summary>
public sealed class IndexModel : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "datum")] public string? DateText { get; set; }

    public DateOnly Date { get; private set; }
    public int DayStart { get; private set; }
    public int DayEnd { get; private set; }
    public IReadOnlyList<Column> Columns { get; private set; } = [];
    public IReadOnlyList<Booking> Bookings { get; private set; } = [];
    public Dictionary<int, Service> Services { get; private set; } = [];
    public Dictionary<int, Staff> StaffById { get; private set; } = [];
    public WeekStats Week { get; private set; } = default!;
    public string? HolidayName { get; private set; }
    public int CancelledToday { get; private set; }

    public sealed record Column(Staff Person, IReadOnlyList<WorkingHours> Shifts, IReadOnlyList<Item> Items);
    public sealed record Item(string Kind, int Id, int Start, int End, string Title, string Sub, string? Href);
    public sealed record WeekStats(DateOnly Monday, int Bookings, int BookedMinutes, int OpenMinutes, int Cancelled, int NewLast7);

    public void OnGet()
    {
        Date = DateOnly.TryParseExact(DateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : Clock.Today;

        var staff = Store.StaffFor(Business.Id);
        var hours = Store.Hours(Business.Id);
        Services = Store.Services(Business.Id, includeInactive: true).ToDictionary(s => s.Id);
        StaffById = Store.StaffFor(Business.Id, includeInactive: true).ToDictionary(s => s.Id);

        var all = Store.Bookings(Business.Id, Date, Date, includeCancelled: true);
        Bookings = all.Where(b => b.Status == BookingStatus.Booked).ToList();
        CancelledToday = all.Count - Bookings.Count;
        var blocks = Store.Blocks(Business.Id, Date, Date);
        HolidayName = Helgdagar.SwedishHolidays.Get(Date)?.Name;

        int M(DateTime t) => (int)(t - Date.ToDateTime(TimeOnly.MinValue)).TotalMinutes;

        var dayShifts = hours.Where(h => h.Day == Date.DayOfWeek).ToList();
        var starts = dayShifts.Select(h => h.StartMinute).Concat(Bookings.Select(b => M(b.Start))).ToList();
        var ends = dayShifts.Select(h => h.EndMinute).Concat(Bookings.Select(b => M(b.End))).ToList();
        DayStart = starts.Count > 0 ? starts.Min() / 60 * 60 : 8 * 60;
        DayEnd = ends.Count > 0 ? (ends.Max() + 59) / 60 * 60 : 18 * 60;
        if (DayEnd - DayStart < 4 * 60) DayEnd = DayStart + 4 * 60;

        Columns = staff.Select(p => new Column(
            p,
            dayShifts.Where(h => h.StaffId == p.Id).ToList(),
            Bookings.Where(b => b.StaffId == p.Id)
                .Select(b => new Item("booking", b.Id, M(b.Start), M(b.End),
                    Services.TryGetValue(b.ServiceId, out var s) ? s.Name : "Tjänst",
                    $"{b.Start:HH:mm} {b.CustomerName}", $"/admin/bokning/{b.Id}"))
                .Concat(blocks.Where(x => x.StaffId == p.Id)
                    .Select(x => new Item("block", x.Id, Math.Max(0, M(x.Start)), Math.Min(24 * 60, M(x.End)),
                        x.Reason.Length > 0 ? x.Reason : "Blockerad", $"{x.Start:HH:mm}-{x.End:HH:mm}", null)))
                .OrderBy(i => i.Start)
                .ToList()))
            .ToList();

        Week = Stats(hours);
    }

    private WeekStats Stats(IReadOnlyList<WorkingHours> hours)
    {
        var monday = Date.AddDays(-(((int)Date.DayOfWeek + 6) % 7));
        var sunday = monday.AddDays(6);
        var week = Store.Bookings(Business.Id, monday, sunday, includeCancelled: true);
        var booked = week.Where(b => b.Status == BookingStatus.Booked).ToList();

        int open = 0;
        for (var d = monday; d <= sunday; d = d.AddDays(1))
        {
            if (Business.Settings.ClosedOnHolidays && Helgdagar.SwedishHolidays.Get(d) is not null) continue;
            open += hours.Where(h => h.Day == d.DayOfWeek).Sum(h => h.EndMinute - h.StartMinute);
        }
        open -= Store.Blocks(Business.Id, monday, sunday).Sum(b => (int)(b.End - b.Start).TotalMinutes);

        var since = Clock.Now.AddDays(-7);
        var recent = Store.Bookings(Business.Id, Clock.Today.AddDays(-30), Clock.Today.AddDays(90), includeCancelled: true)
            .Count(b => b.CreatedAt >= since);

        return new WeekStats(monday, booked.Count, booked.Sum(b => b.Minutes), Math.Max(open, 0),
            week.Count - booked.Count, recent);
    }

    public IActionResult OnPostBlock(int staffId, string date, string from, string to, string? reason)
    {
        var d = DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var x) ? x : Clock.Today;
        var start = ParseMinutes(from);
        var end = ParseMinutes(to);

        if (start is null || end is null || end <= start)
            Flash = "Blockeringen sparades inte: sluttiden måste vara efter starttiden.";
        else
        {
            var text = BookingRules.Clean(reason);
            Store.AddBlock(Business.Id, staffId, d.ToDateTime(TimeOnly.MinValue).AddMinutes(start.Value),
                d.ToDateTime(TimeOnly.MinValue).AddMinutes(end.Value), text.Length > 60 ? text[..60] : text);
            Flash = $"Tiden {from}-{to} är blockerad. Befintliga bokningar ligger kvar.";
        }
        return RedirectToPage(new { datum = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
    }

    public IActionResult OnPostRemoveBlock(int id, string date)
    {
        Flash = Store.RemoveBlock(Business.Id, id) ? "Blockeringen är borttagen." : "Blockeringen fanns inte.";
        return RedirectToPage(new { datum = date });
    }

    public static string Percent(int part, int whole) =>
        whole <= 0 ? "0" : Math.Round(100.0 * part / whole).ToString(CultureInfo.InvariantCulture);

    public string Pos(int minute) =>
        (100.0 * (minute - DayStart) / (DayEnd - DayStart)).ToString("0.###", CultureInfo.InvariantCulture);
}
