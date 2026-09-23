// Kopierad från github.com/QcSnake/helgdagar (v1.0.0). Byts mot NuGet-paketet när det är publicerat.
namespace Helgdagar;

/// <summary>En helgdag eller afton med datum och svenskt namn.</summary>
/// <param name="Date">Datumet.</param>
/// <param name="Name">Namnet som det skrivs i almanackan, till exempel "Kristi himmelsfärdsdag".</param>
/// <param name="Kind">Om dagen är en allmän helgdag enligt lag eller en afton.</param>
public sealed record Holiday(DateOnly Date, string Name, HolidayKind Kind)
{
    /// <summary>Sant för allmänna helgdagar, det vill säga röda dagar.</summary>
    public bool IsPublicHoliday => Kind == HolidayKind.PublicHoliday;

    /// <inheritdoc />
    public override string ToString() => $"{Date:yyyy-MM-dd} {Name}";
}

/// <summary>Vilken sorts dag det är.</summary>
public enum HolidayKind
{
    /// <summary>
    /// Allmän helgdag enligt lagen (1989:253) om allmänna helgdagar. Röd dag i almanackan.
    /// </summary>
    PublicHoliday,

    /// <summary>
    /// Afton som i praktiken är ledig: påskafton, midsommarafton, julafton och nyårsafton.
    /// Midsommar-, jul- och nyårsafton jämställs med söndag i bland annat semesterlagen.
    /// </summary>
    Eve
}
