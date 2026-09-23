using System.Globalization;
using Tidbok.Core;

namespace Tidbok.Data;

public static class Db
{
    public const string TimeFormat = "yyyy-MM-dd'T'HH:mm";
    public static string FormatTime(DateTime t) => t.ToString(TimeFormat, CultureInfo.InvariantCulture);
    public static string FormatDate(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static DateTime ParseTime(string s) =>
        DateTime.ParseExact(s, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None);
}

/// <summary>Tidboks lagring i en SQLite-fil. En ny anslutning per arbetsmoment, WAL-läge för samtidiga läsare.</summary>
public sealed class SqliteStore : ITidbokStore
{
    private readonly string _path;

    public SqliteStore(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var stream = typeof(SqliteStore).Assembly.GetManifestResourceStream("Tidbok.Data.Schema.sql")
            ?? throw new InvalidOperationException("Schema.sql saknas i assemblyn.");
        using var reader = new StreamReader(stream);
        using var c = Open();
        c.ExecuteScript(reader.ReadToEnd());
    }

    public SqliteConnection Open() => new(_path);

    public bool IsEmpty()
    {
        using var c = Open();
        return c.Scalar("SELECT COUNT(*) FROM business") == 0;
    }

    // ---------- verksamheter och schema ----------

    private const string BusinessCols =
        "id, slug, name, kind, address, phone, accent, accent_deep, slot_minutes, min_notice_minutes, " +
        "horizon_days, cancel_cutoff_hours, closed_on_holidays";

    private static Business MapBusiness(Row r) => new()
    {
        Id = r.Int(0), Slug = r.Text(1), Name = r.Text(2), Kind = r.Text(3), Address = r.Text(4),
        Phone = r.Text(5), Accent = r.Text(6), AccentDeep = r.Text(7),
        Settings = new BookingSettings
        {
            SlotMinutes = r.Int(8), MinNoticeMinutes = r.Int(9), HorizonDays = r.Int(10),
            CancelCutoffHours = r.Int(11), ClosedOnHolidays = r.Bool(12)
        }
    };

    public IReadOnlyList<Business> Businesses()
    {
        using var c = Open();
        return c.Query($"SELECT {BusinessCols} FROM business ORDER BY id", MapBusiness);
    }

    public Business? FindBusiness(string slug)
    {
        using var c = Open();
        return c.Single($"SELECT {BusinessCols} FROM business WHERE slug = ?", MapBusiness, slug);
    }

    public Business? GetBusiness(int id)
    {
        using var c = Open();
        return GetBusiness(c, id);
    }

    private static Business? GetBusiness(SqliteConnection c, int id) =>
        c.Single($"SELECT {BusinessCols} FROM business WHERE id = ?", MapBusiness, id);

    public IReadOnlyList<Service> Services(int businessId, bool includeInactive = false)
    {
        using var c = Open();
        return Services(c, businessId, includeInactive);
    }

    private static List<Service> Services(SqliteConnection c, int businessId, bool includeInactive) =>
        c.Query(
            "SELECT id, business_id, name, description, duration_minutes, price_sek, price_from, active, sort " +
            "FROM service WHERE business_id = ? AND (active = 1 OR ? = 1) ORDER BY sort, id",
            r => new Service
            {
                Id = r.Int(0), BusinessId = r.Int(1), Name = r.Text(2), Description = r.Text(3),
                DurationMinutes = r.Int(4), PriceSek = r.IntOrNull(5), PriceFrom = r.Bool(6),
                Active = r.Bool(7), Sort = r.Int(8)
            },
            businessId, includeInactive);

    public IReadOnlyList<Staff> StaffFor(int businessId, bool includeInactive = false)
    {
        using var c = Open();
        return StaffFor(c, businessId, includeInactive);
    }

    private static List<Staff> StaffFor(SqliteConnection c, int businessId, bool includeInactive)
    {
        var links = c.Query(
                "SELECT ss.staff_id, ss.service_id FROM staff_service ss JOIN staff s ON s.id = ss.staff_id " +
                "WHERE s.business_id = ?",
                r => (Staff: r.Int(0), Service: r.Int(1)), businessId)
            .ToLookup(x => x.Staff, x => x.Service);

        return c.Query(
            "SELECT id, business_id, name, title, active, sort FROM staff " +
            "WHERE business_id = ? AND (active = 1 OR ? = 1) ORDER BY sort, id",
            r => new Staff
            {
                Id = r.Int(0), BusinessId = r.Int(1), Name = r.Text(2), Title = r.Text(3),
                Active = r.Bool(4), Sort = r.Int(5), ServiceIds = links[r.Int(0)].ToList()
            },
            businessId, includeInactive);
    }

    public IReadOnlyList<WorkingHours> Hours(int businessId)
    {
        using var c = Open();
        return Hours(c, businessId);
    }

    private static List<WorkingHours> Hours(SqliteConnection c, int businessId) =>
        c.Query(
            "SELECT h.staff_id, h.weekday, h.start_minute, h.end_minute FROM working_hours h " +
            "JOIN staff s ON s.id = h.staff_id WHERE s.business_id = ? ORDER BY h.staff_id, h.weekday, h.start_minute",
            r => new WorkingHours(r.Int(0), (DayOfWeek)r.Int(1), r.Int(2), r.Int(3)),
            businessId);

    public IReadOnlyList<BusyTime> Busy(int businessId, DateOnly from, DateOnly to)
    {
        using var c = Open();
        return Busy(c, businessId, from, to);
    }

    private static List<BusyTime> Busy(SqliteConnection c, int businessId, DateOnly from, DateOnly to)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var list = c.Query(
            "SELECT b.staff_id, b.start_time, b.end_time, sv.name, b.id FROM booking b " +
            "JOIN service sv ON sv.id = b.service_id " +
            "WHERE b.business_id = ? AND b.status = 'Booked' AND b.start_time < ? AND b.end_time > ?",
            r => new BusyTime(r.Int(0), r.Time(1), r.Time(2), BusyKind.Booking, r.Text(3), r.Int(4)),
            businessId, end, start);

        list.AddRange(c.Query(
            "SELECT t.staff_id, t.start_time, t.end_time, t.reason, t.id FROM time_block t " +
            "JOIN staff s ON s.id = t.staff_id " +
            "WHERE s.business_id = ? AND t.start_time < ? AND t.end_time > ?",
            r => new BusyTime(r.Int(0), r.Time(1), r.Time(2), BusyKind.Block, r.Text(3), r.Int(4)),
            businessId, end, start));

        return list;
    }

    // ---------- bokning ----------

    public Booking? TryBook(int businessId, DateOnly date, string tokenHash, Func<ScheduleSnapshot, Booking?> decide)
    {
        using var c = Open();
        return c.InTransaction(() =>
        {
            var business = GetBusiness(c, businessId) ?? throw new InvalidOperationException("Verksamheten finns inte.");
            var snapshot = new ScheduleSnapshot(
                business,
                Services(c, businessId, includeInactive: false),
                StaffFor(c, businessId, includeInactive: false),
                Hours(c, businessId),
                Busy(c, businessId, date, date));

            var booking = decide(snapshot);
            if (booking is null) return null;

            var id = c.Insert(
                "INSERT INTO booking (business_id, staff_id, service_id, start_time, end_time, status, " +
                "customer_name, customer_phone, customer_email, note, reference, token_hash, created_at) " +
                "VALUES (?, ?, ?, ?, ?, 'Booked', ?, ?, ?, ?, ?, ?, ?)",
                businessId, booking.StaffId, booking.ServiceId, booking.Start, booking.End,
                booking.CustomerName, booking.CustomerPhone, booking.CustomerEmail, booking.Note,
                booking.Reference, tokenHash, booking.CreatedAt);

            return booking with { Id = (int)id };
        });
    }

    private const string BookingCols =
        "id, business_id, staff_id, service_id, start_time, end_time, status, customer_name, customer_phone, " +
        "customer_email, note, reference, created_at, cancelled_at, cancelled_by, anonymized";

    private static Booking MapBooking(Row r) => new()
    {
        Id = r.Int(0), BusinessId = r.Int(1), StaffId = r.Int(2), ServiceId = r.Int(3),
        Start = r.Time(4), End = r.Time(5), Status = Enum.Parse<BookingStatus>(r.Text(6)),
        CustomerName = r.Text(7), CustomerPhone = r.TextOrNull(8), CustomerEmail = r.TextOrNull(9),
        Note = r.TextOrNull(10), Reference = r.Text(11), CreatedAt = r.Time(12),
        CancelledAt = r.TimeOrNull(13),
        CancelledBy = r.IsNull(14) ? null : Enum.Parse<CancelledBy>(r.Text(14)),
        Anonymized = r.Bool(15)
    };

    public Booking? BookingByTokenHash(string tokenHash)
    {
        using var c = Open();
        return c.Single($"SELECT {BookingCols} FROM booking WHERE token_hash = ?", MapBooking, tokenHash);
    }

    public Booking? GetBooking(int businessId, int bookingId)
    {
        using var c = Open();
        return c.Single($"SELECT {BookingCols} FROM booking WHERE business_id = ? AND id = ?", MapBooking,
            businessId, bookingId);
    }

    public IReadOnlyList<Booking> Bookings(int businessId, DateOnly from, DateOnly to, bool includeCancelled)
    {
        using var c = Open();
        return c.Query(
            $"SELECT {BookingCols} FROM booking WHERE business_id = ? AND start_time >= ? AND start_time < ? " +
            "AND (status = 'Booked' OR ? = 1) ORDER BY start_time, staff_id",
            MapBooking, businessId, from.ToDateTime(TimeOnly.MinValue),
            to.AddDays(1).ToDateTime(TimeOnly.MinValue), includeCancelled);
    }

    public IReadOnlyList<Booking> SearchBookings(int businessId, string query, int limit)
    {
        var q = BookingRules.Clean(query);
        if (q.Length == 0) return [];
        var like = "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        using var c = Open();
        return c.Query(
            $"SELECT {BookingCols} FROM booking WHERE business_id = ? AND (" +
            "customer_name LIKE ? ESCAPE '\\' OR customer_phone LIKE ? ESCAPE '\\' OR " +
            "customer_email LIKE ? ESCAPE '\\' OR reference LIKE ? ESCAPE '\\') " +
            "ORDER BY start_time DESC LIMIT ?",
            MapBooking, businessId, like, like, like, like, Math.Clamp(limit, 1, 200));
    }

    public bool Cancel(int bookingId, CancelledBy by, DateTime at)
    {
        using var c = Open();
        return c.Execute(
            "UPDATE booking SET status = 'Cancelled', cancelled_at = ?, cancelled_by = ? " +
            "WHERE id = ? AND status = 'Booked'", at, by, bookingId) == 1;
    }

    // ---------- admin ----------

    public IReadOnlyList<TimeBlock> Blocks(int businessId, DateOnly from, DateOnly to)
    {
        using var c = Open();
        return c.Query(
            "SELECT t.id, t.staff_id, t.start_time, t.end_time, t.reason FROM time_block t " +
            "JOIN staff s ON s.id = t.staff_id WHERE s.business_id = ? AND t.start_time < ? AND t.end_time > ? " +
            "ORDER BY t.start_time",
            r => new TimeBlock(r.Int(0), r.Int(1), r.Time(2), r.Time(3), r.Text(4)),
            businessId, to.AddDays(1).ToDateTime(TimeOnly.MinValue), from.ToDateTime(TimeOnly.MinValue));
    }

    public int AddBlock(int businessId, int staffId, DateTime start, DateTime end, string reason)
    {
        using var c = Open();
        if (c.Scalar("SELECT COUNT(*) FROM staff WHERE id = ? AND business_id = ?", staffId, businessId) == 0)
            throw new InvalidOperationException("Personen hör inte till verksamheten.");
        return (int)c.Insert("INSERT INTO time_block (staff_id, start_time, end_time, reason) VALUES (?, ?, ?, ?)",
            staffId, start, end, reason);
    }

    public bool RemoveBlock(int businessId, int blockId)
    {
        using var c = Open();
        return c.Execute(
            "DELETE FROM time_block WHERE id = ? AND staff_id IN (SELECT id FROM staff WHERE business_id = ?)",
            blockId, businessId) == 1;
    }

    public int SaveService(Service s)
    {
        using var c = Open();
        if (s.Id == 0)
        {
            var sort = (int)c.Scalar("SELECT COALESCE(MAX(sort), 0) + 1 FROM service WHERE business_id = ?", s.BusinessId);
            return (int)c.Insert(
                "INSERT INTO service (business_id, name, description, duration_minutes, price_sek, price_from, active, sort) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                s.BusinessId, s.Name, s.Description, s.DurationMinutes, s.PriceSek, s.PriceFrom, s.Active, sort);
        }

        var changed = c.Execute(
            "UPDATE service SET name = ?, description = ?, duration_minutes = ?, price_sek = ?, price_from = ?, active = ? " +
            "WHERE id = ? AND business_id = ?",
            s.Name, s.Description, s.DurationMinutes, s.PriceSek, s.PriceFrom, s.Active, s.Id, s.BusinessId);
        if (changed != 1) throw new InvalidOperationException("Tjänsten hittades inte.");
        return s.Id;
    }

    public void SetStaffServices(int businessId, int staffId, IReadOnlyCollection<int> serviceIds)
    {
        using var c = Open();
        c.InTransaction(() =>
        {
            if (c.Scalar("SELECT COUNT(*) FROM staff WHERE id = ? AND business_id = ?", staffId, businessId) == 0)
                throw new InvalidOperationException("Personen hör inte till verksamheten.");
            c.Execute("DELETE FROM staff_service WHERE staff_id = ?", staffId);
            foreach (var id in serviceIds.Distinct())
                c.Execute(
                    "INSERT INTO staff_service (staff_id, service_id) SELECT ?, id FROM service WHERE id = ? AND business_id = ?",
                    staffId, id, businessId);
            return 0;
        });
    }

    public void SetHours(int businessId, int staffId, IReadOnlyCollection<WorkingHours> hours)
    {
        using var c = Open();
        c.InTransaction(() =>
        {
            if (c.Scalar("SELECT COUNT(*) FROM staff WHERE id = ? AND business_id = ?", staffId, businessId) == 0)
                throw new InvalidOperationException("Personen hör inte till verksamheten.");
            c.Execute("DELETE FROM working_hours WHERE staff_id = ?", staffId);
            foreach (var h in hours)
                c.Execute("INSERT INTO working_hours (staff_id, weekday, start_minute, end_minute) VALUES (?, ?, ?, ?)",
                    staffId, (int)h.Day, h.StartMinute, h.EndMinute);
            return 0;
        });
    }

    public void SaveSettings(int businessId, BookingSettings s)
    {
        using var c = Open();
        c.Execute(
            "UPDATE business SET slot_minutes = ?, min_notice_minutes = ?, horizon_days = ?, " +
            "cancel_cutoff_hours = ?, closed_on_holidays = ? WHERE id = ?",
            s.SlotMinutes, s.MinNoticeMinutes, s.HorizonDays, s.CancelCutoffHours, s.ClosedOnHolidays, businessId);
    }

    // ---------- konton ----------

    public AdminUser? FindUser(string email)
    {
        using var c = Open();
        return c.Single("SELECT id, business_id, email, name, password_hash FROM app_user WHERE email = ?",
            r => new AdminUser(r.Int(0), r.Int(1), r.Text(2), r.Text(3), r.Text(4)), BookingRules.Clean(email));
    }

    public AdminUser? FirstUserFor(int businessId)
    {
        using var c = Open();
        return c.Single("SELECT id, business_id, email, name, password_hash FROM app_user WHERE business_id = ? ORDER BY id LIMIT 1",
            r => new AdminUser(r.Int(0), r.Int(1), r.Text(2), r.Text(3), r.Text(4)), businessId);
    }

    /// <summary>Tömmer allt och läser in demodatan igen. Bara för testmiljön.</summary>
    public void ResetDemo(DateTime now)
    {
        using (var c = Open())
        {
            c.InTransaction(() =>
            {
                foreach (var table in new[] { "booking", "time_block", "working_hours", "staff_service", "app_user", "service", "staff", "business" })
                    c.Execute($"DELETE FROM {table}");
                return 0;
            });
        }
        Seed.Run(this, now);
    }

    // ---------- gallring ----------

    public int Anonymize(DateTime before)
    {
        using var c = Open();
        return c.Execute(
            "UPDATE booking SET customer_name = 'Gallrad', customer_phone = NULL, customer_email = NULL, " +
            "note = NULL, anonymized = 1 WHERE anonymized = 0 AND end_time < ?", before);
    }
}
