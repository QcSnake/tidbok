using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Tidbok.Core;
using Tidbok.Data;
using Tidbok.Web;

var sv = CultureInfo.GetCultureInfo("sv-SE");
CultureInfo.DefaultThreadCurrentCulture = sv;
CultureInfo.DefaultThreadCurrentUICulture = sv;

var builder = WebApplication.CreateBuilder(args);

// Render talar om vilken port appen ska lyssna på.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var dataPath = builder.Configuration["DataPath"]
               ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "tidbok.db");
var demo = builder.Configuration.GetValue("Demo:Enabled", true);

var store = new SqliteStore(dataPath);
var clock = new LocalClock(TimeProvider.System, LocalClock.Stockholm());

builder.Services.AddSingleton<ITidbokStore>(store);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(clock);
builder.Services.AddSingleton<BookingService>();
builder.Services.AddSingleton(new DemoOptions(demo));
builder.Services.AddHostedService<Gallring>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LoginThrottle>();

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Admin", Policies.Admin);
    o.Conventions.AllowAnonymousToPage("/Admin/LoggaIn");
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/admin/logga-in";
        o.AccessDeniedPath = "/admin/logga-in";
        o.Cookie.Name = "tidbok.admin";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(10);
        o.SlidingExpiration = true;
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Admin, p => p.RequireClaim(Claims.Business));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string Ip(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "okänd";

    // Tio bokningar i minuten per IP räcker för en människa och stoppar ett skript som vill fylla kalendern.
    o.AddPolicy(Limits.Book, ctx => RateLimitPartition.GetFixedWindowLimiter(Ip(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(Limits.Read, ctx => RateLimitPartition.GetFixedWindowLimiter(Ip(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(Limits.Login, ctx => RateLimitPartition.GetFixedWindowLimiter(Ip(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5) }));
});

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

var app = builder.Build();

if (store.IsEmpty())
{
    Seed.Run(store, clock.Now);
    app.Logger.LogInformation("Tom databas, demodata inläst.");
}

// Render terminerar https i sin proxy och skickar vidare med X-Forwarded-*. Proxyns adress är
// inte känd i förväg, så alla källor litas på. Appen tar bara emot trafik via proxyn.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwarded.KnownNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/fel");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/fel", "?kod={0}");
app.Use(SecurityHeaders.Apply);
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
StaticPages.Map(app);
Api.Map(app);
AdminAuth.Map(app);

app.Run();

namespace Tidbok.Web
{
    public static class Policies { public const string Admin = "admin"; }
    public static class Claims { public const string Business = "tidbok:business"; }
    public static class Limits { public const string Book = "boka"; public const string Read = "las"; public const string Login = "login"; }

    public sealed record DemoOptions(bool Enabled);

    public static class UserExtensions
    {
        public static int BusinessId(this ClaimsPrincipal user) =>
            int.Parse(user.FindFirstValue(Claims.Business) ?? throw new InvalidOperationException("Inte inloggad."),
                CultureInfo.InvariantCulture);
    }
}
