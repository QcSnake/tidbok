namespace Tidbok.Core;

/// <summary>
/// Det kunden gör: ser lediga dagar och tider, bokar och avbokar. Webbens API är ett tunt lager
/// ovanpå den här klassen.
/// </summary>
public sealed class BookingService(ITidbokStore store, LocalClock clock)
{
    public LocalClock Clock => clock;

    public IReadOnlyList<DayAvailability> Days(Business business, Service service, int? staffId,
        DateOnly from, int count)
    {
        count = Math.Clamp(count, 1, 62);
        var now = clock.Now;
        var to = from.AddDays(count - 1);

        var staff = CandidateStaff(business.Id, service, staffId);
        var hours = store.Hours(business.Id);
        var busy = store.Busy(business.Id, from, to);

        var result = new List<DayAvailability>(count);
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var closed = Availability.ClosedReason(d, business, now);
            if (closed is not null)
            {
                result.Add(new DayAvailability(d, 0, closed));
                continue;
            }

            var slots = Availability.Slots(new Availability.DayInput(d, business, service, staff, hours,
                busy.Where(b => DateOnly.FromDateTime(b.Start) == d).ToList(), now));

            var worksThatDay = hours.Any(h => h.Day == d.DayOfWeek && staff.Any(s => s.Id == h.StaffId));
            string? reason = slots.Count > 0 ? null
                : !worksThatDay ? "Stängt"
                : d == DateOnly.FromDateTime(now) ? "Inga fler tider i dag"
                : "Fullbokat";
            result.Add(new DayAvailability(d, slots.Count, reason));
        }
        return result;
    }

    public IReadOnlyList<Slot> Slots(Business business, Service service, int? staffId, DateOnly date)
    {
        var staff = CandidateStaff(business.Id, service, staffId);
        return Availability.Slots(new Availability.DayInput(date, business, service, staff,
            store.Hours(business.Id), store.Busy(business.Id, date, date), clock.Now));
    }

    public BookOutcome Book(Business business, BookingRequest request)
    {
        var errors = BookingRules.Validate(request);
        var service = store.Services(business.Id).FirstOrDefault(s => s.Id == request.ServiceId);
        if (service is null) errors["serviceId"] = "Tjänsten finns inte.";
        if (errors.Count > 0) return BookOutcome.Invalid(errors);

        var date = DateOnly.FromDateTime(request.Start);
        var closed = Availability.ClosedReason(date, business, clock.Now);
        if (closed is not null) return BookOutcome.Taken($"Dagen går inte att boka: {closed}.");

        var token = BookingRules.NewToken();
        var saved = store.TryBook(business.Id, date, BookingRules.HashToken(token), snapshot =>
        {
            // Allt nedan körs inne i skrivlåset, mot schemat som det ser ut just nu.
            var svc = snapshot.Services.FirstOrDefault(s => s.Id == service!.Id);
            if (svc is null) return null;

            var candidates = snapshot.Staff.Where(s => s.Active && s.ServiceIds.Contains(svc.Id));
            if (request.StaffId is int wanted) candidates = candidates.Where(s => s.Id == wanted);

            var slot = Availability.Slots(new Availability.DayInput(date, snapshot.Business, svc,
                    candidates.ToList(), snapshot.Hours, snapshot.Busy, clock.Now))
                .FirstOrDefault(s => s.Time == TimeOnly.FromDateTime(request.Start));
            if (slot is null) return null;

            var staffId = Availability.PickStaff(slot.StaffIds, snapshot.Staff, snapshot.Busy);
            return new Booking
            {
                BusinessId = business.Id,
                StaffId = staffId,
                ServiceId = svc.Id,
                Start = request.Start,
                End = request.Start.AddMinutes(svc.DurationMinutes),
                Status = BookingStatus.Booked,
                CustomerName = BookingRules.Clean(request.Name),
                CustomerPhone = BookingRules.Clean(request.Phone),
                CustomerEmail = NullIfEmpty(BookingRules.Clean(request.Email)),
                Note = NullIfEmpty(BookingRules.Clean(request.Note)),
                Reference = BookingRules.NewReference(),
                CreatedAt = clock.Now
            };
        });

        return saved is null
            ? BookOutcome.Taken("Tiden hann bli bokad av någon annan. Välj en annan tid.")
            : BookOutcome.Ok(saved, token);
    }

    public Booking? FindByToken(string? token) =>
        BookingRules.LooksLikeToken(token) ? store.BookingByTokenHash(BookingRules.HashToken(token!)) : null;

    public CancelOutcome CancelByCustomer(string? token)
    {
        var booking = FindByToken(token);
        if (booking is null) return new CancelOutcome(false, "Bokningen hittades inte.", null);

        var business = store.GetBusiness(booking.BusinessId)!;
        var blocked = BookingRules.CustomerCancelBlocked(booking, business.Settings, clock.Now);
        if (blocked is not null) return new CancelOutcome(false, blocked, booking);

        store.Cancel(booking.Id, CancelledBy.Customer, clock.Now);
        return new CancelOutcome(true, null, store.GetBooking(booking.BusinessId, booking.Id));
    }

    private IReadOnlyList<Staff> CandidateStaff(int businessId, Service service, int? staffId) =>
        store.StaffFor(businessId)
            .Where(s => s.ServiceIds.Contains(service.Id) && (staffId is null || s.Id == staffId))
            .ToList();

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}

public sealed record BookOutcome(Booking? Booking, string? Token, Dictionary<string, string>? Errors, string? Conflict)
{
    public static BookOutcome Ok(Booking b, string token) => new(b, token, null, null);
    public static BookOutcome Invalid(Dictionary<string, string> e) => new(null, null, e, null);
    public static BookOutcome Taken(string why) => new(null, null, null, why);
}

public sealed record CancelOutcome(bool Cancelled, string? Reason, Booking? Booking);

/// <summary>Verksamhetens klocka. Allt i Tidbok läser tiden härifrån, så testerna kan styra den.</summary>
public sealed class LocalClock(TimeProvider time, TimeZoneInfo zone)
{
    public TimeZoneInfo Zone => zone;
    public DateTime Now => TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime;
    public DateOnly Today => DateOnly.FromDateTime(Now);
    public DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public static TimeZoneInfo Stockholm() => TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
}
