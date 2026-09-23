using Tidbok.Core;

namespace Tidbok.Data;

/// <summary>
/// Demodata: två påhittade verksamheter med samma tjänster, priser, personal och öppettider som
/// demosajterna på abdimalik-portfolio.onrender.com, plus bokningar tre veckor bakåt och två
/// framåt så att kalendern och statistiken inte är tomma. Kundnamnen är påhittade och
/// telefonnumren ligger i PTS serie för fiktiva nummer (070-174 06 05 till 070-174 06 99).
/// </summary>
public static class Seed
{
    public const string DemoPassword = "tidbok-demo";

    public static void Run(SqliteStore store, DateTime now)
    {
        using var c = store.Open();
        c.InTransaction(() =>
        {
            Salong(c, now);
            Klinik(c, now);
            return 0;
        });
    }

    private static void Salong(SqliteConnection c, DateTime now)
    {
        var b = (int)c.Insert(
            "INSERT INTO business (slug, name, kind, address, phone, accent, accent_deep, slot_minutes, " +
            "min_notice_minutes, horizon_days, cancel_cutoff_hours) VALUES (?, ?, ?, ?, ?, ?, ?, 15, 120, 30, 24)",
            "salong-silhuett", "Salong Silhuett", "Frisörsalong", "Drottninggatan 22, 702 21 Örebro",
            "019-611 22 33", "#9c5d54", "#7a4038");

        var dam = Svc(c, b, "Damklippning", "Konsultation, tvätt, klippning och föning.", 60, 595, false);
        var herr = Svc(c, b, "Herrklippning", "Klippning med maskin eller sax, tvätt ingår.", 30, 395, false);
        var barn = Svc(c, b, "Barnklippning", "Till och med 12 år.", 30, 295, false);
        var farg = Svc(c, b, "Helfärgning", "Pris beroende på hårlängd.", 120, 895, true);
        var slingor = Svc(c, b, "Slingor, halva huvudet", "Folieslingor i toppen och sidorna.", 120, 995, true);
        var balayage = Svc(c, b, "Balayage", "Frihandsmålad färg för mjuka övergångar.", 180, 1595, true);
        var fest = Svc(c, b, "Uppsättning, fest", "Ta gärna med en bild på vad du tänkt dig.", 60, 695, false);
        var foning = Svc(c, b, "Föning", "Tvätt och föning.", 30, 345, false);

        var elin = Person(c, b, "Elin", "Färg och balayage", 1, dam, farg, slingor, balayage, foning);
        var samir = Person(c, b, "Samir", "Herrklippning", 2, herr, barn, dam, foning);
        var mikaela = Person(c, b, "Mikaela", "Styling och brud", 3, fest, foning, dam, farg);

        // Tisdag till fredag 10-18 med lunch, lördag 10-14.
        int[] weekdays = [2, 3, 4, 5];
        foreach (var d in weekdays) { Shift(c, elin, d, "10:00", "13:00"); Shift(c, elin, d, "13:30", "18:00"); }
        foreach (var d in weekdays) { Shift(c, samir, d, "10:00", "13:00"); Shift(c, samir, d, "13:30", "18:00"); }
        Shift(c, samir, 6, "10:00", "14:00");
        foreach (var d in new[] { 3, 4, 5 }) { Shift(c, mikaela, d, "10:00", "13:00"); Shift(c, mikaela, d, "13:30", "18:00"); }
        Shift(c, mikaela, 6, "10:00", "14:00");

        User(c, b, "demo@salong-silhuett.test", "Salong Silhuett");
        Bookings(c, b, now, fill: 0.55, seed: 11);
        Block(c, mikaela, NextWeekday(now, DayOfWeek.Thursday).AddHours(14), 4 * 60, "Utbildning, färgkurs");
    }

    private static void Klinik(SqliteConnection c, DateTime now)
    {
        var b = (int)c.Insert(
            "INSERT INTO business (slug, name, kind, address, phone, accent, accent_deep, slot_minutes, " +
            "min_notice_minutes, horizon_days, cancel_cutoff_hours) VALUES (?, ?, ?, ?, ?, ?, ?, 15, 240, 45, 24)",
            "klinik-bjorkang", "Klinik Björkäng", "Tandvårdsklinik", "Rudbecksgatan 14, 703 61 Örebro",
            "019-33 01 20", "#2c6e8f", "#1d4f68");

        var us = Svc(c, b, "Basundersökning", "Undersökning med diagnostik och röntgen vid behov.", 45, 995, false);
        var akut = Svc(c, b, "Akut undersökning", "Tandvärk eller skada. Ring om det inte finns någon tid som passar.", 30, 750, false);
        var tandsten = Svc(c, b, "Tandstensborttagning", "Hos tandhygienist, med råd om munhygien hemma.", 45, 895, false);
        var lagning = Svc(c, b, "Lagning, mindre", "Du får ett fast pris innan vi börjar.", 45, 1350, false);
        var blek = Svc(c, b, "Tandblekning", "Båda käkarna, på kliniken.", 90, 3200, false);
        var barn = Svc(c, b, "Barn och unga", "Undersökning. Avgiftsfritt till och med 23 år.", 30, 0, false);

        var anna = Person(c, b, "Anna Nilsson", "Tandläkare, klinikchef", 1, us, akut, lagning, blek, barn);
        var rasmus = Person(c, b, "Rasmus Karlsson", "Tandläkare", 2, us, akut, lagning, barn);
        var ida = Person(c, b, "Ida Holm", "Tandhygienist", 3, tandsten, blek, barn);

        foreach (var p in new[] { anna, rasmus, ida })
        {
            foreach (var d in new[] { 1, 2, 3, 4 }) { Shift(c, p, d, "08:00", "12:00"); Shift(c, p, d, "13:00", "17:00"); }
            Shift(c, p, 5, "08:00", "12:00");
            Shift(c, p, 5, "12:30", "14:00");
        }

        User(c, b, "demo@klinik-bjorkang.test", "Klinik Björkäng");
        Bookings(c, b, now, fill: 0.5, seed: 23);
        Block(c, rasmus, NextWeekday(now, DayOfWeek.Monday).AddHours(8), 9 * 60, "Ledig");
    }

    // ---------------------------------------------------------------------------------------

    private static int Svc(SqliteConnection c, int b, string name, string desc, int minutes, int? price, bool from)
    {
        var sort = (int)c.Scalar("SELECT COUNT(*) FROM service WHERE business_id = ?", b);
        return (int)c.Insert(
            "INSERT INTO service (business_id, name, description, duration_minutes, price_sek, price_from, sort) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?)", b, name, desc, minutes, price, from, sort);
    }

    private static int Person(SqliteConnection c, int b, string name, string title, int sort, params int[] services)
    {
        var id = (int)c.Insert("INSERT INTO staff (business_id, name, title, sort) VALUES (?, ?, ?, ?)", b, name, title, sort);
        foreach (var s in services) c.Execute("INSERT INTO staff_service (staff_id, service_id) VALUES (?, ?)", id, s);
        return id;
    }

    private static void Shift(SqliteConnection c, int staff, int weekday, string from, string to) =>
        c.Execute("INSERT INTO working_hours (staff_id, weekday, start_minute, end_minute) VALUES (?, ?, ?, ?)",
            staff, weekday, Minutes(from), Minutes(to));

    private static int Minutes(string hhmm) => int.Parse(hhmm[..2]) * 60 + int.Parse(hhmm[3..]);

    private static void User(SqliteConnection c, int b, string email, string name) =>
        c.Execute("INSERT INTO app_user (business_id, email, name, password_hash) VALUES (?, ?, ?, ?)",
            b, email, name, Passwords.Hash(DemoPassword));

    private static void Block(SqliteConnection c, int staff, DateTime start, int minutes, string reason)
    {
        var end = start.AddMinutes(minutes);
        c.Execute("INSERT INTO time_block (staff_id, start_time, end_time, reason) VALUES (?, ?, ?, ?)",
            staff, start, end, reason);
        // Exempelbokningarna lades innan blockeringen, så de som krockar tas bort.
        c.Execute("DELETE FROM booking WHERE staff_id = ? AND start_time < ? AND end_time > ?", staff, end, start);
    }

    private static DateTime NextWeekday(DateTime now, DayOfWeek day)
    {
        var d = now.Date.AddDays(1);
        while (d.DayOfWeek != day) d = d.AddDays(1);
        return d;
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static readonly string[] FirstNames =
    [
        "Sara", "Johan", "Emma", "Ali", "Maja", "Erik", "Fatima", "Oskar", "Linnea", "Ahmed", "Ida", "Karl",
        "Nora", "Hassan", "Wilma", "Anton", "Leila", "Hugo", "Elsa", "Mohammed", "Ebba", "Viktor", "Amira", "Lucas"
    ];

    private static readonly string[] Initials = ["A", "B", "E", "H", "J", "K", "L", "M", "N", "P", "S", "W"];

    /// <summary>
    /// Fyller schemat slumpmässigt men förutsägbart: samma dag ger samma bokningar varje gång
    /// appen startar. Går igenom varje pass och lägger bokningar med en viss sannolikhet.
    /// </summary>
    private static void Bookings(SqliteConnection c, int businessId, DateTime now, double fill, int seed)
    {
        var services = c.Query("SELECT id, duration_minutes FROM service WHERE business_id = ?",
            r => (Id: r.Int(0), Minutes: r.Int(1)), businessId).ToDictionary(x => x.Id, x => x.Minutes);
        var links = c.Query(
                "SELECT ss.staff_id, ss.service_id FROM staff_service ss JOIN staff s ON s.id = ss.staff_id WHERE s.business_id = ?",
                r => (Staff: r.Int(0), Service: r.Int(1)), businessId)
            .ToLookup(x => x.Staff, x => x.Service);
        var shifts = c.Query(
            "SELECT h.staff_id, h.weekday, h.start_minute, h.end_minute FROM working_hours h " +
            "JOIN staff s ON s.id = h.staff_id WHERE s.business_id = ?",
            r => new WorkingHours(r.Int(0), (DayOfWeek)r.Int(1), r.Int(2), r.Int(3)), businessId);

        int n = 0;
        for (var day = DateOnly.FromDateTime(now).AddDays(-21); day <= DateOnly.FromDateTime(now).AddDays(14); day = day.AddDays(1))
        {
            if (Helgdagar.SwedishHolidays.Get(day) is not null) continue;
            var rng = new Random(seed * 100_000 + day.DayNumber);

            foreach (var shift in shifts.Where(s => s.Day == day.DayOfWeek))
            {
                var t = day.ToDateTime(TimeOnly.MinValue).AddMinutes(shift.StartMinute);
                var end = day.ToDateTime(TimeOnly.MinValue).AddMinutes(shift.EndMinute);

                // Närmaste dagarna är mer fullbokade än de som ligger längre fram.
                var daysAhead = day.DayNumber - DateOnly.FromDateTime(now).DayNumber;
                var p = daysAhead <= 0 ? fill + 0.2 : Math.Max(0.15, fill - daysAhead * 0.03);

                while (t < end)
                {
                    var options = links[shift.StaffId].Where(s => t.AddMinutes(services[s]) <= end).ToList();
                    if (options.Count == 0) break;

                    if (rng.NextDouble() < p)
                    {
                        var svc = options[rng.Next(options.Count)];
                        var bookingEnd = t.AddMinutes(services[svc]);
                        var name = $"{FirstNames[rng.Next(FirstNames.Length)]} {Initials[rng.Next(Initials.Length)]}.";
                        var phone = $"070-174 06 {rng.Next(5, 100):00}";
                        var cancelled = rng.NextDouble() < 0.08;
                        var created = t.AddDays(-rng.Next(1, 20)).AddHours(-rng.Next(0, 8));
                        if (created > now) created = now.AddHours(-rng.Next(1, 48));

                        c.Execute(
                            "INSERT INTO booking (business_id, staff_id, service_id, start_time, end_time, status, customer_name, " +
                            "customer_phone, reference, token_hash, created_at, cancelled_at, cancelled_by) " +
                            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                            businessId, shift.StaffId, svc, t, bookingEnd, cancelled ? "Cancelled" : "Booked", name, phone,
                            $"TB-D{businessId}{++n:0000}", BookingRules.HashToken(BookingRules.NewToken()), created,
                            cancelled ? Min(created.AddDays(1), now) : null, cancelled ? "Customer" : null);

                        t = bookingEnd;
                    }
                    else
                    {
                        t = t.AddMinutes(30);
                    }
                }
            }
        }
    }
}
