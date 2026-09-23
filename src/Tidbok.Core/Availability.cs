using Helgdagar;

namespace Tidbok.Core;

/// <summary>
/// Räknar fram lediga tider. Rena funktioner utan databas och utan klocka: allt kommer in som
/// argument, så varje regel går att testa med exakta tider.
/// </summary>
public static class Availability
{
    /// <summary>Allt som behövs för att räkna fram en dags lediga tider.</summary>
    public sealed record DayInput(
        DateOnly Date,
        Business Business,
        Service Service,
        IReadOnlyList<Staff> Staff,
        IReadOnlyList<WorkingHours> Hours,
        IReadOnlyList<BusyTime> Busy,
        DateTime Now);

    /// <summary>
    /// Varför en dag inte går att boka alls, eller null om den går. Används både för att grå ut
    /// dagar i kalendern och som första kontroll när en bokning kommer in.
    /// </summary>
    public static string? ClosedReason(DateOnly date, Business business, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (date < today) return "Passerat";
        if (date > today.AddDays(business.Settings.HorizonDays)) return "Går inte att boka än";

        if (business.Settings.ClosedOnHolidays && date.Year >= SwedishHolidays.MinYear)
        {
            var holiday = SwedishHolidays.Get(date);
            if (holiday is not null) return holiday.Name;
        }
        return null;
    }

    /// <summary>Lediga starttider för tjänsten, med vilka i personalen som kan ta varje tid.</summary>
    public static IReadOnlyList<Slot> Slots(DayInput input)
    {
        if (ClosedReason(input.Date, input.Business, input.Now) is not null) return [];

        var duration = TimeSpan.FromMinutes(input.Service.DurationMinutes);
        var step = TimeSpan.FromMinutes(Math.Max(5, input.Business.Settings.SlotMinutes));
        var earliest = input.Now.AddMinutes(input.Business.Settings.MinNoticeMinutes);
        var dayStart = input.Date.ToDateTime(TimeOnly.MinValue);

        var byTime = new SortedDictionary<TimeOnly, List<int>>();

        foreach (var person in input.Staff)
        {
            if (!person.Active || !person.ServiceIds.Contains(input.Service.Id)) continue;

            var busy = input.Busy.Where(b => b.StaffId == person.Id).ToList();
            var shifts = input.Hours.Where(h => h.StaffId == person.Id && h.Day == input.Date.DayOfWeek);

            foreach (var shift in shifts)
            {
                var shiftEnd = dayStart.AddMinutes(shift.EndMinute);
                for (var start = dayStart.AddMinutes(shift.StartMinute); start + duration <= shiftEnd; start += step)
                {
                    if (start < earliest) continue;
                    var end = start + duration;
                    if (busy.Any(b => Overlaps(start, end, b.Start, b.End))) continue;

                    var key = TimeOnly.FromDateTime(start);
                    if (!byTime.TryGetValue(key, out var list)) byTime[key] = list = [];
                    list.Add(person.Id);
                }
            }
        }

        return byTime.Select(kv => new Slot(kv.Key, kv.Value)).ToList();
    }

    /// <summary>
    /// Kontrollerar att en viss person kan ta en viss tid: inom ett arbetspass och utan krock.
    /// Körs igen inne i databastransaktionen när bokningen sparas, så att två kunder som klickar
    /// på samma tid samtidigt inte båda får den.
    /// </summary>
    public static bool IsFree(int staffId, DateTime start, DateTime end,
        IReadOnlyList<WorkingHours> hours, IReadOnlyList<BusyTime> busy)
    {
        var dayStart = start.Date;
        var inShift = hours.Any(h =>
            h.StaffId == staffId &&
            h.Day == start.DayOfWeek &&
            start >= dayStart.AddMinutes(h.StartMinute) &&
            end <= dayStart.AddMinutes(h.EndMinute));

        return inShift && !busy.Any(b => b.StaffId == staffId && Overlaps(start, end, b.Start, b.End));
    }

    /// <summary>
    /// Väljer vem som får bokningen när kunden valt "Vem som helst": den som har minst bokat den
    /// dagen, så att arbetet sprids jämnt. Vid lika vinner den som står först i personallistan.
    /// </summary>
    public static int PickStaff(IReadOnlyList<int> candidates, IReadOnlyList<Staff> staffOrder,
        IReadOnlyList<BusyTime> busy)
    {
        if (candidates.Count == 0) throw new ArgumentException("Ingen kandidat.", nameof(candidates));

        int Booked(int id) => busy
            .Where(b => b.StaffId == id && b.Kind == BusyKind.Booking)
            .Sum(b => (int)(b.End - b.Start).TotalMinutes);

        int Order(int id)
        {
            for (int i = 0; i < staffOrder.Count; i++) if (staffOrder[i].Id == id) return i;
            return int.MaxValue;
        }

        return candidates.OrderBy(Booked).ThenBy(Order).First();
    }

    /// <summary>Halvöppna intervall: en bokning som slutar 10:30 krockar inte med en som börjar 10:30.</summary>
    public static bool Overlaps(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd) =>
        aStart < bEnd && bStart < aEnd;
}
