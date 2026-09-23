using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Tidbok.Core;
using Tidbok.Data;

namespace Tidbok.Web.Pages.Admin;

public sealed class LoggaInModel(ITidbokStore store, DemoOptions demo, LoginThrottle throttle) : PageModel
{
    public DemoOptions Demo => demo;
    public IReadOnlyList<Business> Businesses { get; private set; } = [];

    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "okänd";

    public void OnGet() => Businesses = store.Businesses();

    public async Task<IActionResult> OnPostAsync()
    {
        Businesses = store.Businesses();

        if (throttle.IsBlocked(Ip))
        {
            Error = "För många felaktiga försök. Vänta en kvart och försök igen.";
            return Page();
        }

        var user = store.FindUser(Email);
        // Hashen räknas även när e-posten inte finns, så att svarstiden inte avslöjar vilka konton som finns.
        var ok = Passwords.Verify(Password, user?.PasswordHash ?? Dummy);
        if (user is null || !ok)
        {
            throttle.Fail(Ip);
            Error = "Fel e-post eller lösenord.";
            return Page();
        }

        throttle.Succeed(Ip);
        await AdminAuth.SignInAsync(HttpContext, user);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/admin");
    }

    /// <summary>Knapparna "Logga in som ..." i testmiljön. Finns bara när demoläget är på.</summary>
    public async Task<IActionResult> OnPostDemoAsync(string slug)
    {
        if (!demo.Enabled) return NotFound();
        var business = store.FindBusiness(slug);
        var user = business is null ? null : store.FirstUserFor(business.Id);
        if (user is null) return NotFound();

        await AdminAuth.SignInAsync(HttpContext, user);
        return LocalRedirect("/admin");
    }

    private static readonly string Dummy = Passwords.Hash(Guid.NewGuid().ToString());
}

public sealed class LoggaUtModel : PageModel
{
    public IActionResult OnGet() => Redirect("/admin");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/admin/logga-in");
    }
}
