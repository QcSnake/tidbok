using System.Text;

namespace Tidbok.Core;

/// <summary>
/// Kalenderfil enligt RFC 5545, så att kunden kan lägga in tiden i telefonens kalender.
/// Tiderna skrivs i UTC med Z på slutet. Då behövs ingen tidszonsdefinition i filen, och
/// Outlook, Google och Apple visar ändå rätt lokal tid.
/// </summary>
public static class Ics
{
    public static string ForBooking(Booking booking, Business business, Service service, Staff staff,
        TimeZoneInfo zone, DateTime nowUtc, string? manageUrl)
    {
        var sb = new StringBuilder();
        void Line(string s) => Fold(sb, s);

        var description = $"Bokningsnummer {booking.Reference}. Du träffar {staff.Name}.";
        if (manageUrl is not null) description += $" Avboka eller se bokningen: {manageUrl}";

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Tidbok//Bokning//SV");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        Line("BEGIN:VEVENT");
        Line($"UID:{booking.Reference}@tidbok");
        Line($"DTSTAMP:{Utc(nowUtc)}");
        Line($"DTSTART:{Utc(ToUtc(booking.Start, zone))}");
        Line($"DTEND:{Utc(ToUtc(booking.End, zone))}");
        Line($"SUMMARY:{Escape($"{service.Name}, {business.Name}")}");
        if (business.Address.Length > 0) Line($"LOCATION:{Escape(business.Address)}");
        Line($"DESCRIPTION:{Escape(description)}");
        if (booking.Status == BookingStatus.Cancelled) Line("STATUS:CANCELLED");
        Line("BEGIN:VALARM");
        Line("TRIGGER:-PT2H");
        Line("ACTION:DISPLAY");
        Line($"DESCRIPTION:{Escape(service.Name)}");
        Line("END:VALARM");
        Line("END:VEVENT");
        Line("END:VCALENDAR");
        return sb.ToString();
    }

    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);

    private static string Utc(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'");

    /// <summary>Kommatecken, semikolon och bakstreck har egen betydelse i formatet och måste escapas.</summary>
    public static string Escape(string s) => s
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n");

    /// <summary>
    /// Rader över 75 byte ska delas med radbrytning plus mellanslag. Räknas i UTF-8-byte, inte
    /// tecken, och delar aldrig mitt i ett å, ä eller ö.
    /// </summary>
    private static void Fold(StringBuilder sb, string line)
    {
        int bytes = 0;
        bool first = true;
        foreach (var rune in line.EnumerateRunes())
        {
            int len = rune.Utf8SequenceLength;
            int limit = first ? 75 : 74;
            if (bytes + len > limit)
            {
                sb.Append("\r\n ");
                bytes = 0;
                first = false;
            }
            sb.Append(rune.ToString());
            bytes += len;
        }
        sb.Append("\r\n");
    }
}
