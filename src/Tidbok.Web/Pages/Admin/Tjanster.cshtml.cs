using Microsoft.AspNetCore.Mvc;
using Tidbok.Core;

namespace Tidbok.Web.Pages.Admin;

public sealed class TjansterModel : AdminPageModel
{
    public IReadOnlyList<Service> Items { get; private set; } = [];
    public string? Error { get; private set; }

    public void OnGet() => Items = Store.Services(Business.Id, includeInactive: true);

    public IActionResult OnPostSave(int id, string name, string? description, int minutes, int? price, bool from, bool active)
    {
        var error = Check(name, minutes, price);
        if (error is not null)
        {
            Flash = error;
            return RedirectToPage();
        }

        Store.SaveService(new Service
        {
            Id = id, BusinessId = Business.Id, Name = BookingRules.Clean(name),
            Description = Trim(BookingRules.Clean(description), 200), DurationMinutes = minutes,
            PriceSek = price, PriceFrom = from, Active = active
        });
        Flash = id == 0 ? $"{BookingRules.Clean(name)} är tillagd. Välj vem som utför den under Personal." : "Sparat.";
        return RedirectToPage();
    }

    private static string? Check(string name, int minutes, int? price)
    {
        var n = BookingRules.Clean(name);
        if (n.Length is < 2 or > 60) return "Namnet ska vara 2 till 60 tecken.";
        if (minutes is < 5 or > 480 || minutes % 5 != 0) return "Tiden ska vara 5 till 480 minuter, i steg om 5.";
        if (price is < 0 or > 100_000) return "Priset ska vara mellan 0 och 100 000 kr.";
        return null;
    }

    private static string Trim(string s, int max) => s.Length > max ? s[..max] : s;
}
