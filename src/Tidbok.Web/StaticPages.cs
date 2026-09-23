using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Tidbok.Core;

namespace Tidbok.Web;

/// <summary>De statiska sidorna och de små hjälpsvaren runt dem.</summary>
public static partial class StaticPages
{
    public static void Map(WebApplication app)
    {
        var root = app.Environment.WebRootPath;
        IResult Html(string file) => Results.File(Path.Combine(root, file), "text/html; charset=utf-8");

        app.MapGet("/", () => Html("index.html"));
        app.MapGet("/integritet", () => Html("integritet.html"));
        app.MapGet("/health", () => Results.Text("ok"));

        app.MapGet("/boka/{slug}", (string slug, ITidbokStore store) =>
            store.FindBusiness(slug) is null ? Results.NotFound() : Html("boka.html"));

        app.MapGet("/avboka/{token}", (string token) =>
            BookingRules.LooksLikeToken(token) ? Html("avboka.html") : Results.NotFound());

        // Verksamhetens färg som en liten stilmall. Då behövs inga inline-stilar och CSP:n kan vara strikt.
        app.MapGet("/tema/{slug}.css", (string slug, ITidbokStore store, HttpContext ctx) =>
        {
            var b = store.FindBusiness(slug);
            if (b is null) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "public, max-age=300";
            return Results.Text(
                $":root{{--accent:{SafeColor(b.Accent)};--accent-deep:{SafeColor(b.AccentDeep)};}}",
                "text/css; charset=utf-8");
        });

        app.Map("/fel", (HttpContext ctx) =>
        {
            var original = ctx.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath
                           ?? ctx.Features.Get<IExceptionHandlerPathFeature>()?.Path ?? "";
            var code = int.TryParse(ctx.Request.Query["kod"], out var k) ? k : 500;
            var message = code switch
            {
                404 => "Sidan finns inte.",
                429 => "För många försök på kort tid. Vänta en minut och försök igen.",
                400 => "Förfrågan gick inte att tolka.",
                _ => "Något gick fel hos oss. Försök igen om en stund."
            };

            if (original.StartsWith("/api", StringComparison.Ordinal))
                return Results.Json(new ErrorDto(message, null), statusCode: code);

            var html = $$"""
                <!doctype html>
                <html lang="sv"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{{code}} · Tidbok</title><link rel="stylesheet" href="/css/site.css"></head>
                <body class="page page--error"><main class="error"><p class="kicker">{{code}}</p>
                <h1>{{WebUtility.HtmlEncode(message)}}</h1><p><a class="btn" href="/">Till startsidan</a></p></main></body></html>
                """;
            return Results.Content(html, "text/html; charset=utf-8", statusCode: code);
        });
    }

    private static string SafeColor(string c) => HexColor().IsMatch(c) ? c : "#2f6f5e";

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")] private static partial Regex HexColor();
}
