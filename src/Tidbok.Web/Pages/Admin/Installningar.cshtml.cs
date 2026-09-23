using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Tidbok.Core;
using Tidbok.Data;

namespace Tidbok.Web.Pages.Admin;

public sealed class InstallningarModel : AdminPageModel
{
    public string Origin => $"{Request.Scheme}://{Request.Host}";

    public void OnGet() { }

    public IActionResult OnPostSave(int slotMinutes, int minNoticeHours, int horizonDays, int cancelCutoffHours, bool closedOnHolidays)
    {
        if (slotMinutes is not (5 or 10 or 15 or 20 or 30 or 60) ||
            minNoticeHours is < 0 or > 168 || horizonDays is < 1 or > 365 || cancelCutoffHours is < 0 or > 336)
        {
            Flash = "Något värde låg utanför det tillåtna. Inget sparades.";
            return RedirectToPage();
        }

        Store.SaveSettings(Business.Id, new BookingSettings
        {
            SlotMinutes = slotMinutes, MinNoticeMinutes = minNoticeHours * 60, HorizonDays = horizonDays,
            CancelCutoffHours = cancelCutoffHours, ClosedOnHolidays = closedOnHolidays
        });
        Flash = "Inställningarna är sparade.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetAsync()
    {
        if (!Demo.Enabled) return NotFound();
        HttpContext.RequestServices.GetRequiredService<SqliteStore>().ResetDemo(Clock.Now);

        // Id:n kan ha ändrats, så inloggningen görs om mot den nyinlästa verksamheten.
        var business = Store.FindBusiness(Business.Slug);
        var user = business is null ? null : Store.FirstUserFor(business.Id);
        if (user is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/admin/logga-in");
        }
        await AdminAuth.SignInAsync(HttpContext, user);
        Flash = "Testmiljön är återställd.";
        return Redirect("/admin");
    }
}
