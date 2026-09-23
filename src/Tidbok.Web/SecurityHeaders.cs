namespace Tidbok.Web;

/// <summary>
/// Säkerhetsrubriker på varje svar. Ingen inline-JavaScript och inga inline-stilar någonstans,
/// så CSP:n kan vara strikt. Bokningssidan är den enda som får bäddas in på andra sajter,
/// eftersom det är hela poängen med widgeten. Adminvyn och avbokningssidan går aldrig att rama in.
/// </summary>
public static class SecurityHeaders
{
    private const string Base =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public static Task Apply(HttpContext ctx, Func<Task> next)
    {
        var path = ctx.Request.Path;
        var embeddable = path.StartsWithSegments("/boka");

        var h = ctx.Response.Headers;
        h["Content-Security-Policy"] = Base + (embeddable ? "; frame-ancestors *" : "; frame-ancestors 'none'");
        if (!embeddable) h["X-Frame-Options"] = "DENY";
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";

        // Svar som innehåller personuppgifter ska inte ligga kvar i cacher.
        if (path.StartsWithSegments("/admin") || path.StartsWithSegments("/api/bookings") || path.StartsWithSegments("/avboka"))
            h["Cache-Control"] = "no-store";

        return next();
    }
}
