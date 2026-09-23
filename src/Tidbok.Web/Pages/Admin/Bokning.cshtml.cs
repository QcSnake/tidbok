using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Tidbok.Core;

namespace Tidbok.Web.Pages.Admin;

public sealed class BokningModel : AdminPageModel
{
    public Booking Item { get; private set; } = default!;
    public Service? Service { get; private set; }
    public Staff? Person { get; private set; }

    public IActionResult OnGet(int id)
    {
        var b = Store.GetBooking(Business.Id, id);
        if (b is null) return NotFound();

        Item = b;
        Service = Store.Services(Business.Id, includeInactive: true).FirstOrDefault(s => s.Id == b.ServiceId);
        Person = Store.StaffFor(Business.Id, includeInactive: true).FirstOrDefault(s => s.Id == b.StaffId);
        return Page();
    }

    public IActionResult OnPostCancel(int id)
    {
        var b = Store.GetBooking(Business.Id, id);
        if (b is null) return NotFound();

        Flash = Store.Cancel(b.Id, CancelledBy.Business, Clock.Now)
            ? "Bokningen är avbokad och tiden är ledig igen. Ring kunden och berätta."
            : "Bokningen var redan avbokad.";
        return RedirectToPage(new { id });
    }

    public string Date(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
