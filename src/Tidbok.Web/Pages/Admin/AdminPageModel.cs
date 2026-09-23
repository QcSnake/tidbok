using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Tidbok.Core;

namespace Tidbok.Web.Pages.Admin;

/// <summary>
/// Gemensam grund för adminsidorna. Verksamheten hämtas alltid från inloggningen, aldrig från
/// adressen eller formuläret, så en inloggad salong kan inte se eller ändra klinikens data.
/// </summary>
public abstract class AdminPageModel : PageModel
{
    protected ITidbokStore Store => HttpContext.RequestServices.GetRequiredService<ITidbokStore>();
    public LocalClock Clock => HttpContext.RequestServices.GetRequiredService<LocalClock>();
    public DemoOptions Demo => HttpContext.RequestServices.GetRequiredService<DemoOptions>();

    public Business Business { get; private set; } = default!;

    [TempData] public string? Flash { get; set; }

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var business = Store.GetBusiness(User.BusinessId());
        if (business is null)
        {
            context.Result = new RedirectResult("/admin/logga-in");
            return;
        }

        Business = business;
        ViewData["Business"] = business;
        ViewData["Demo"] = Demo.Enabled;
        await next();
    }

    protected static int? ParseMinutes(string? hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm)) return null;
        return TimeOnly.TryParseExact(hhmm, "HH:mm", out var t) ? t.Hour * 60 + t.Minute : null;
    }

    public static string Hhmm(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
}
