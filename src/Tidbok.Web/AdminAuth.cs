using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Tidbok.Core;

namespace Tidbok.Web;

public static class AdminAuth
{
    public static void Map(WebApplication app)
    {
        // Inloggning och utloggning sköts av sidorna under Pages/Admin, så att de får
        // antiforgery-skyddet från Razor Pages utan extra kod.
    }

    public static Task SignInAsync(HttpContext ctx, AdminUser user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(Claims.Business, user.BusinessId.ToString(CultureInfo.InvariantCulture))
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        return ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }
}
