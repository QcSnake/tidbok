namespace Tidbok.Core;

/// <summary>
/// Allt Tidbok behöver av lagringen. Implementeras mot SQLite i Tidbok.Data, men kärnan vet
/// inget om SQL.
/// </summary>
public interface ITidbokStore
{
    // ---------- verksamheter och schema ----------
    IReadOnlyList<Business> Businesses();
    Business? FindBusiness(string slug);
    Business? GetBusiness(int id);
    IReadOnlyList<Service> Services(int businessId, bool includeInactive = false);
    IReadOnlyList<Staff> StaffFor(int businessId, bool includeInactive = false);
    IReadOnlyList<WorkingHours> Hours(int businessId);

    /// <summary>Upptagen tid (bokningar som inte är avbokade, och blockeringar) mellan två datum, båda inräknade.</summary>
    IReadOnlyList<BusyTime> Busy(int businessId, DateOnly from, DateOnly to);

    // ---------- bokning ----------

    /// <summary>
    /// Sparar en bokning atomärt. Lagringen låser för skrivning, läser dagens schema på nytt och
    /// frågar <paramref name="decide"/> om tiden fortfarande är ledig. Returnerar den sparade
    /// bokningen, eller null om tiden hann bli tagen.
    /// </summary>
    Booking? TryBook(int businessId, DateOnly date, string tokenHash, Func<ScheduleSnapshot, Booking?> decide);

    Booking? BookingByTokenHash(string tokenHash);
    Booking? GetBooking(int businessId, int bookingId);
    IReadOnlyList<Booking> Bookings(int businessId, DateOnly from, DateOnly to, bool includeCancelled);
    IReadOnlyList<Booking> SearchBookings(int businessId, string query, int limit);
    bool Cancel(int bookingId, CancelledBy by, DateTime at);

    // ---------- admin ----------
    IReadOnlyList<TimeBlock> Blocks(int businessId, DateOnly from, DateOnly to);
    int AddBlock(int businessId, int staffId, DateTime start, DateTime end, string reason);
    bool RemoveBlock(int businessId, int blockId);
    int SaveService(Service service);
    void SetStaffServices(int businessId, int staffId, IReadOnlyCollection<int> serviceIds);
    void SetHours(int businessId, int staffId, IReadOnlyCollection<WorkingHours> hours);
    void SaveSettings(int businessId, BookingSettings settings);

    // ---------- konton ----------
    AdminUser? FindUser(string email);
    AdminUser? FirstUserFor(int businessId);

    // ---------- gallring ----------

    /// <summary>
    /// Tar bort namn, telefon, e-post och meddelande från bokningar som slutade före
    /// <paramref name="before"/>. Själva bokningen står kvar för statistiken.
    /// </summary>
    int Anonymize(DateTime before);
}

/// <summary>En dags schema, läst inne i skrivlåset.</summary>
public sealed record ScheduleSnapshot(
    Business Business,
    IReadOnlyList<Service> Services,
    IReadOnlyList<Staff> Staff,
    IReadOnlyList<WorkingHours> Hours,
    IReadOnlyList<BusyTime> Busy);

public sealed record AdminUser(int Id, int BusinessId, string Email, string Name, string PasswordHash);
