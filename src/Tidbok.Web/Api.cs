using System.Globalization;
using Tidbok.Core;

namespace Tidbok.Web;

/// <summary>
/// Det publika API:t som bokningssidan och widgeten använder. Inga personuppgifter går ut här
/// utom till den som har avbokningslänken, och då bara den egna bokningen.
/// </summary>
public static class Api
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api").RequireRateLimiting(Limits.Read);

        api.MapGet("/businesses/{slug}", (string slug, ITidbokStore store) =>
        {
            var b = store.FindBusiness(slug);
            if (b is null) return NotFound("Verksamheten finns inte.");

            var services = store.Services(b.Id);
            var staff = store.StaffFor(b.Id);
            return Results.Ok(new BusinessDto(
                b.Slug, b.Name, b.Kind, b.Address, b.Phone,
                b.Settings.CancelCutoffHours, b.Settings.HorizonDays,
                services.Select(s => new ServiceDto(s.Id, s.Name, s.Description, s.DurationMinutes,
                    Format.Price(s), staff.Where(p => p.ServiceIds.Contains(s.Id)).Select(p => p.Id).ToArray())).ToArray(),
                staff.Select(p => new StaffDto(p.Id, p.Name, p.Title)).ToArray()));
        });

        api.MapGet("/businesses/{slug}/days", (string slug, int service, int? staff, string? from, int? count,
            ITidbokStore store, BookingService booking) =>
        {
            var (b, svc, error) = Resolve(store, slug, service);
            if (error is not null) return error;

            var start = ParseDate(from) ?? booking.Clock.Today;
            var days = booking.Days(b!, svc!, staff, start, count ?? 14);
            return Results.Ok(days.Select(d => new DayDto(d.Date.ToString("yyyy-MM-dd", Inv), d.FreeSlots, d.ClosedReason)));
        });

        api.MapGet("/businesses/{slug}/slots", (string slug, int service, int? staff, string date,
            ITidbokStore store, BookingService booking) =>
        {
            var (b, svc, error) = Resolve(store, slug, service);
            if (error is not null) return error;
            if (ParseDate(date) is not DateOnly day) return BadRequest("date ska vara yyyy-MM-dd.");

            var slots = booking.Slots(b!, svc!, staff, day);
            return Results.Ok(slots.Select(s => new SlotDto(s.Time.ToString("HH:mm", Inv), s.StaffIds.ToArray())));
        });

        api.MapPost("/businesses/{slug}/bookings", (string slug, BookRequestDto body, HttpContext ctx,
            ITidbokStore store, BookingService booking) =>
        {
            var b = store.FindBusiness(slug);
            if (b is null) return NotFound("Verksamheten finns inte.");

            if (!DateTime.TryParseExact(body.Start, "yyyy-MM-dd'T'HH:mm", Inv, DateTimeStyles.None, out var start))
                return Results.BadRequest(new ErrorDto("Tiden saknas eller har fel format.", new() { ["start"] = "Välj en tid." }));
            if (!body.Consent)
                return Results.BadRequest(new ErrorDto("Godkänn hanteringen av uppgifterna för att boka.",
                    new() { ["consent"] = "Kryssa i rutan för att boka." }));

            var outcome = booking.Book(b, new BookingRequest
            {
                ServiceId = body.ServiceId, StaffId = body.StaffId, Start = start,
                Name = body.Name ?? "", Phone = body.Phone ?? "", Email = body.Email, Note = body.Note
            });

            if (outcome.Errors is not null)
                return Results.BadRequest(new ErrorDto("Kontrollera uppgifterna.", outcome.Errors));
            if (outcome.Booking is null)
                return Results.Conflict(new ErrorDto(outcome.Conflict ?? "Tiden är inte ledig.", null));

            var dto = ToDto(outcome.Booking, outcome.Token!, ctx, store, booking.Clock);
            return Results.Created(dto.ManageUrl, dto);
        }).RequireRateLimiting(Limits.Book);

        api.MapGet("/bookings/{token}", (string token, HttpContext ctx, ITidbokStore store, BookingService booking) =>
        {
            var found = booking.FindByToken(token);
            return found is null ? NotFound("Bokningen hittades inte.") : Results.Ok(ToDto(found, token, ctx, store, booking.Clock));
        });

        api.MapPost("/bookings/{token}/cancel", (string token, HttpContext ctx, ITidbokStore store, BookingService booking) =>
        {
            var result = booking.CancelByCustomer(token);
            if (result.Booking is null) return NotFound("Bokningen hittades inte.");
            if (!result.Cancelled) return Results.Conflict(new ErrorDto(result.Reason!, null));
            return Results.Ok(ToDto(result.Booking, token, ctx, store, booking.Clock));
        }).RequireRateLimiting(Limits.Book);

        api.MapGet("/bookings/{token}/calendar.ics", (string token, HttpContext ctx, ITidbokStore store, BookingService booking) =>
        {
            var found = booking.FindByToken(token);
            if (found is null) return NotFound("Bokningen hittades inte.");

            var b = store.GetBusiness(found.BusinessId)!;
            var svc = store.Services(b.Id, includeInactive: true).First(s => s.Id == found.ServiceId);
            var person = store.StaffFor(b.Id, includeInactive: true).First(s => s.Id == found.StaffId);
            var ics = Ics.ForBooking(found, b, svc, person, booking.Clock.Zone, booking.Clock.UtcNow,
                $"{Origin(ctx)}/avboka/{token}");
            return Results.File(System.Text.Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8",
                $"{found.Reference}.ics");
        });
    }

    private static BookingDto ToDto(Booking bk, string token, HttpContext ctx, ITidbokStore store, LocalClock clock)
    {
        var b = store.GetBusiness(bk.BusinessId)!;
        var svc = store.Services(b.Id, includeInactive: true).First(s => s.Id == bk.ServiceId);
        var person = store.StaffFor(b.Id, includeInactive: true).First(s => s.Id == bk.StaffId);
        var blocked = BookingRules.CustomerCancelBlocked(bk, b.Settings, clock.Now);
        var origin = Origin(ctx);

        return new BookingDto(
            bk.Reference, bk.Status == BookingStatus.Booked ? "booked" : "cancelled",
            bk.Start.ToString("yyyy-MM-dd'T'HH:mm", Inv), bk.End.ToString("yyyy-MM-dd'T'HH:mm", Inv),
            Format.LongDate(bk.Start), $"{bk.Start:HH:mm}-{bk.End:HH:mm}",
            svc.Name, Format.Price(svc), person.Name, b.Name, b.Slug, b.Address, b.Phone,
            bk.Anonymized ? null : FirstName(bk.CustomerName),
            blocked is null, blocked, b.Settings.CancelCutoffHours,
            $"{origin}/avboka/{token}", $"{origin}/api/bookings/{token}/calendar.ics");
    }

    private static string FirstName(string full) => full.Split(' ', 2)[0];

    private static string Origin(HttpContext ctx) => $"{ctx.Request.Scheme}://{ctx.Request.Host}";

    private static (Business?, Service?, IResult?) Resolve(ITidbokStore store, string slug, int serviceId)
    {
        var b = store.FindBusiness(slug);
        if (b is null) return (null, null, NotFound("Verksamheten finns inte."));
        var svc = store.Services(b.Id).FirstOrDefault(s => s.Id == serviceId);
        if (svc is null) return (null, null, NotFound("Tjänsten finns inte."));
        return (b, svc, null);
    }

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d : null;

    private static IResult NotFound(string message) => Results.NotFound(new ErrorDto(message, null));
    private static IResult BadRequest(string message) => Results.BadRequest(new ErrorDto(message, null));
}

public sealed record BusinessDto(string Slug, string Name, string Kind, string Address, string Phone,
    int CancelCutoffHours, int HorizonDays, ServiceDto[] Services, StaffDto[] Staff);
public sealed record ServiceDto(int Id, string Name, string Description, int Minutes, string Price, int[] StaffIds);
public sealed record StaffDto(int Id, string Name, string Title);
public sealed record DayDto(string Date, int Free, string? Closed);
public sealed record SlotDto(string Time, int[] Staff);
public sealed record BookRequestDto(int ServiceId, int? StaffId, string? Start, string? Name, string? Phone,
    string? Email, string? Note, bool Consent);
public sealed record BookingDto(string Reference, string Status, string Start, string End, string DateText, string TimeText,
    string Service, string Price, string Staff, string Business, string BusinessSlug, string Address, string Phone,
    string? FirstName, bool CanCancel, string? CancelBlockedReason, int CancelCutoffHours, string ManageUrl, string CalendarUrl);
public sealed record ErrorDto(string Message, Dictionary<string, string>? Fields);

public static class Format
{
    private static readonly CultureInfo Sv = CultureInfo.GetCultureInfo("sv-SE");

    public static string Price(Service s) => s.PriceSek switch
    {
        null => "",
        0 => "Avgiftsfritt",
        int p => (s.PriceFrom ? "från " : "") + p.ToString("#,0", Sv).Replace(' ', ' ') + " kr"
    };

    public static string Minutes(int m) => m < 60 ? $"{m} min" : m % 60 == 0 ? $"{m / 60} tim" : $"{m / 60} tim {m % 60} min";

    public static string LongDate(DateTime d) => Capitalize(d.ToString("dddd d MMMM", Sv));
    public static string LongDate(DateOnly d) => Capitalize(d.ToString("dddd d MMMM", Sv));
    public static string ShortDate(DateOnly d) => d.ToString("ddd d MMM", Sv).Replace(".", "");

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Sv) + s[1..];
}
