using Microsoft.AspNetCore.Mvc;
using Tidbok.Core;

namespace Tidbok.Web.Pages.Admin;

public sealed class BokningarModel : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Query { get; set; }
    [BindProperty(SupportsGet = true, Name = "visa")] public string? Show { get; set; }

    public IReadOnlyList<Booking> Items { get; private set; } = [];
    public Dictionary<int, Service> Services { get; private set; } = [];
    public Dictionary<int, Staff> StaffById { get; private set; } = [];
    public string Heading { get; private set; } = "";

    public void OnGet()
    {
        Services = Store.Services(Business.Id, includeInactive: true).ToDictionary(s => s.Id);
        StaffById = Store.StaffFor(Business.Id, includeInactive: true).ToDictionary(s => s.Id);

        if (!string.IsNullOrWhiteSpace(Query))
        {
            Items = Store.SearchBookings(Business.Id, Query, 100);
            Heading = $"Sökträffar för \"{BookingRules.Clean(Query)}\"";
        }
        else if (Show == "avbokade")
        {
            Items = Store.Bookings(Business.Id, Clock.Today.AddDays(-14), Clock.Today.AddDays(60), includeCancelled: true)
                .Where(b => b.Status == BookingStatus.Cancelled).OrderByDescending(b => b.CancelledAt).ToList();
            Heading = "Avbokade, senaste två veckorna och framåt";
        }
        else
        {
            Items = Store.Bookings(Business.Id, Clock.Today, Clock.Today.AddDays(14), includeCancelled: false)
                .Where(b => b.End > Clock.Now).ToList();
            Heading = "Kommande två veckor";
        }
    }
}
