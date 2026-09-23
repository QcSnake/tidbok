using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Tidbok.Core;

/// <summary>Validering av kunduppgifter, avbokningsregler och koderna som skickas till kunden.</summary>
public static partial class BookingRules
{
    public const int NameMax = 80;
    public const int PhoneMax = 20;
    public const int EmailMax = 120;
    public const int NoteMax = 500;

    /// <summary>Tvättar och kontrollerar det kunden skrivit. Tom lista betyder godkänt.</summary>
    public static Dictionary<string, string> Validate(BookingRequest r)
    {
        var errors = new Dictionary<string, string>();

        var name = Clean(r.Name);
        if (name.Length < 2) errors["name"] = "Skriv ditt namn.";
        else if (name.Length > NameMax) errors["name"] = $"Namnet får vara högst {NameMax} tecken.";

        var phone = Clean(r.Phone);
        var digits = phone.Count(char.IsDigit);
        if (phone.Length == 0) errors["phone"] = "Skriv ett telefonnummer så att vi kan nå dig.";
        else if (phone.Length > PhoneMax || digits < 7 || !PhoneChars().IsMatch(phone))
            errors["phone"] = "Telefonnumret ser inte rätt ut. Använd siffror, till exempel 070-123 45 67.";

        var email = Clean(r.Email);
        if (email.Length > 0 && (email.Length > EmailMax || !EmailShape().IsMatch(email)))
            errors["email"] = "E-postadressen ser inte rätt ut.";

        if (Clean(r.Note).Length > NoteMax) errors["note"] = $"Meddelandet får vara högst {NoteMax} tecken.";

        return errors;
    }

    /// <summary>Tar bort styrtecken och onödiga mellanslag. Allt som sparas går igenom här.</summary>
    public static string Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Trim())
            if (!char.IsControl(c) || c == '\n') sb.Append(c);
        return Spaces().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>
    /// Om kunden själv får avboka. Null betyder ja, annars skälet. Verksamheten kan alltid avboka
    /// från adminvyn, den här regeln gäller bara länken i bekräftelsen.
    /// </summary>
    public static string? CustomerCancelBlocked(Booking booking, BookingSettings settings, DateTime now)
    {
        if (booking.Status == BookingStatus.Cancelled) return "Bokningen är redan avbokad.";
        if (booking.Start <= now) return "Tiden har redan varit.";
        if (booking.Start - now < TimeSpan.FromHours(settings.CancelCutoffHours))
            return $"Det går inte att avboka på nätet senare än {settings.CancelCutoffHours} timmar före. Ring oss i stället.";
        return null;
    }

    private const string RefAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>Kort bokningsnummer att läsa upp i telefon. Inga tecken som går att blanda ihop, som 0 och O.</summary>
    public static string NewReference()
    {
        Span<char> chars = stackalloc char[6];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = RefAlphabet[RandomNumberGenerator.GetInt32(RefAlphabet.Length)];
        return "TB-" + new string(chars);
    }

    /// <summary>
    /// Hemlig nyckel i avbokningslänken. Bara hashen sparas, så den som kommer åt databasen kan
    /// ändå inte avboka någon annans tid.
    /// </summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool LooksLikeToken(string? token) =>
        token is { Length: 32 } && TokenChars().IsMatch(token);

    [GeneratedRegex(@"^\+?[0-9 ()\-]+$")] private static partial Regex PhoneChars();
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")] private static partial Regex EmailShape();
    [GeneratedRegex(@"[ \t]+")] private static partial Regex Spaces();
    [GeneratedRegex(@"^[A-Za-z0-9_\-]+$")] private static partial Regex TokenChars();
}
