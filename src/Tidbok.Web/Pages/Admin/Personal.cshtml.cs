using Microsoft.AspNetCore.Mvc;
using Tidbok.Core;

namespace Tidbok.Web.Pages.Admin;

public sealed class PersonalModel : AdminPageModel
{
    public static readonly DayOfWeek[] Week =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    public static readonly string[] WeekNames = ["Måndag", "Tisdag", "Onsdag", "Torsdag", "Fredag", "Lördag", "Söndag"];

    public IReadOnlyList<Staff> People { get; private set; } = [];
    public IReadOnlyList<Service> Services { get; private set; } = [];
    public IReadOnlyList<WorkingHours> Hours { get; private set; } = [];

    public void OnGet()
    {
        People = Store.StaffFor(Business.Id);
        Services = Store.Services(Business.Id, includeInactive: true);
        Hours = Store.Hours(Business.Id);
    }

    public IReadOnlyList<WorkingHours> ShiftsFor(int staffId, DayOfWeek day) =>
        Hours.Where(h => h.StaffId == staffId && h.Day == day).OrderBy(h => h.StartMinute).ToList();

    public IActionResult OnPostServices(int staffId, int[]? serviceIds)
    {
        Store.SetStaffServices(Business.Id, staffId, serviceIds ?? []);
        Flash = "Tjänsterna är sparade.";
        return RedirectToPage();
    }

    /// <summary>Två pass per dag räcker för lunch. Tomma fält betyder ledig.</summary>
    public IActionResult OnPostHours(int staffId, string?[] from1, string?[] to1, string?[] from2, string?[] to2)
    {
        var shifts = new List<WorkingHours>();
        for (int i = 0; i < Week.Length; i++)
        {
            foreach (var (f, t) in new[] { (At(from1, i), At(to1, i)), (At(from2, i), At(to2, i)) })
            {
                if (f is null && t is null) continue;
                if (f is null || t is null || t <= f)
                {
                    Flash = $"{WeekNames[i]}: ett pass måste ha både start och slut, och slutet efter starten. Inget sparades.";
                    return RedirectToPage();
                }
                shifts.Add(new WorkingHours(staffId, Week[i], f.Value, t.Value));
            }

            var day = shifts.Where(s => s.Day == Week[i]).OrderBy(s => s.StartMinute).ToList();
            if (day.Count == 2 && day[1].StartMinute < day[0].EndMinute)
            {
                Flash = $"{WeekNames[i]}: passen överlappar varandra. Inget sparades.";
                return RedirectToPage();
            }
        }

        Store.SetHours(Business.Id, staffId, shifts);
        Flash = "Arbetstiderna är sparade. Bokningssidan visar de nya tiderna direkt.";
        return RedirectToPage();
    }

    private static int? At(string?[]? values, int i) => values is not null && i < values.Length ? ParseMinutes(values[i]) : null;
}
