// Kopierad från github.com/QcSnake/helgdagar (v1.0.0). Byts mot NuGet-paketet när det är publicerat.
using System.Collections.Concurrent;

namespace Helgdagar;

/// <summary>
/// Svenska helgdagar, aftnar och arbetsdagar.
///
/// Allt räknas fram ur reglerna, inget ligger i tabeller. Påsken räknas med den gregorianska
/// påskformeln och resten av den rörliga kalendern hänger på den eller på en veckodag inom
/// ett fast datumspann (midsommar, alla helgons dag).
///
/// Reglerna gäller från och med 2005, året då nationaldagen blev helgdag och annandag pingst
/// slutade vara det. Äldre år ger ett fel i stället för ett svar som ser rätt ut men inte är det.
/// </summary>
public static class SwedishHolidays
{
    /// <summary>Första året reglerna i biblioteket stämmer för.</summary>
    public const int MinYear = 2005;

    /// <summary>Sista året som går att räkna på, begränsat av <see cref="DateOnly"/>.</summary>
    public const int MaxYear = 9998;

    private static readonly ConcurrentDictionary<int, YearTable> Cache = new();

    /// <summary>Påskdagen för året.</summary>
    /// <remarks>
    /// Anonym gregoriansk algoritm (Meeus, Jones, Butcher). Ger påskdagen som första söndagen
    /// efter den kyrkliga fullmånen på eller efter 21 mars.
    /// </remarks>
    public static DateOnly EasterSunday(int year)
    {
        EnsureYear(year);
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }

    /// <summary>Alla helgdagar och aftnar för året, sorterade på datum.</summary>
    public static IReadOnlyList<Holiday> For(int year) => Table(year).All;

    /// <summary>Alla helgdagar och aftnar mellan två datum, båda inräknade.</summary>
    public static IEnumerable<Holiday> Between(DateOnly from, DateOnly to)
    {
        if (to < from) (from, to) = (to, from);
        for (int y = from.Year; y <= to.Year; y++)
            foreach (var h in For(y))
                if (h.Date >= from && h.Date <= to)
                    yield return h;
    }

    /// <summary>Helgdagen eller aftonen på datumet, eller null om det är en vanlig dag.</summary>
    public static Holiday? Get(DateOnly date) =>
        Table(date.Year).ByDate.TryGetValue(date, out var h) ? h : null;

    /// <summary>Sant om datumet är en allmän helgdag enligt lag. Söndagar räknas inte här.</summary>
    public static bool IsPublicHoliday(DateOnly date) => Get(date)?.Kind == HolidayKind.PublicHoliday;

    /// <summary>Sant om datumet är påsk-, midsommar-, jul- eller nyårsafton.</summary>
    public static bool IsEve(DateOnly date) => Get(date)?.Kind == HolidayKind.Eve;

    /// <summary>Röd dag i almanackan: söndag eller allmän helgdag.</summary>
    public static bool IsRedDay(DateOnly date) =>
        date.DayOfWeek == DayOfWeek.Sunday || IsPublicHoliday(date);

    /// <summary>
    /// Sant om datumet är en vanlig arbetsdag: måndag till fredag och varken helgdag eller afton.
    /// </summary>
    /// <param name="date">Datumet.</param>
    /// <param name="evesAreDaysOff">
    /// Om aftnarna räknas som lediga. Standard är ja, eftersom midsommar-, jul- och nyårsafton
    /// jämställs med söndag i semesterlagen och de flesta verksamheter håller stängt.
    /// </param>
    public static bool IsBusinessDay(DateOnly date, bool evesAreDaysOff = true)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        var h = Get(date);
        if (h is null) return true;
        return h.Kind == HolidayKind.Eve && !evesAreDaysOff;
    }

    /// <summary>
    /// Flyttar datumet ett antal arbetsdagar framåt, eller bakåt om antalet är negativt.
    /// Noll arbetsdagar ger samma datum tillbaka, även om det inte är en arbetsdag.
    /// </summary>
    public static DateOnly AddBusinessDays(DateOnly date, int businessDays, bool evesAreDaysOff = true)
    {
        int step = Math.Sign(businessDays);
        int left = Math.Abs(businessDays);
        var d = date;
        while (left > 0)
        {
            d = d.AddDays(step);
            if (IsBusinessDay(d, evesAreDaysOff)) left--;
        }
        return d;
    }

    /// <summary>
    /// Antal arbetsdagar från och med <paramref name="from"/> till men inte med <paramref name="to"/>.
    /// Negativt om <paramref name="to"/> ligger före <paramref name="from"/>.
    /// </summary>
    public static int BusinessDaysBetween(DateOnly from, DateOnly to, bool evesAreDaysOff = true)
    {
        if (to < from) return -BusinessDaysBetween(to, from, evesAreDaysOff);
        int count = 0;
        for (var d = from; d < to; d = d.AddDays(1))
            if (IsBusinessDay(d, evesAreDaysOff)) count++;
        return count;
    }

    /// <summary>Nästa arbetsdag efter datumet.</summary>
    public static DateOnly NextBusinessDay(DateOnly date, bool evesAreDaysOff = true) =>
        AddBusinessDays(date, 1, evesAreDaysOff);

    /// <summary>
    /// Klämdagar under året: en ensam arbetsdag med lediga dagar på båda sidor, där minst en av
    /// grannarna är en helgdag eller afton. Typexemplet är fredagen efter Kristi himmelsfärdsdag.
    /// </summary>
    public static IReadOnlyList<DateOnly> BridgeDays(int year, bool evesAreDaysOff = true)
    {
        EnsureYear(year);
        var result = new List<DateOnly>();
        for (var d = new DateOnly(year, 1, 1); d.Year == year; d = d.AddDays(1))
        {
            if (!IsBusinessDay(d, evesAreDaysOff)) continue;
            var before = d.AddDays(-1);
            var after = d.AddDays(1);
            if (IsBusinessDay(before, evesAreDaysOff) || IsBusinessDay(after, evesAreDaysOff)) continue;
            if (Get(before) is not null || Get(after) is not null) result.Add(d);
        }
        return result;
    }

    // ---------------------------------------------------------------------------------------

    private static YearTable Table(int year)
    {
        EnsureYear(year);
        return Cache.GetOrAdd(year, Build);
    }

    private static YearTable Build(int year)
    {
        var easter = EasterSunday(year);
        var midsummerEve = FirstWeekdayFrom(new DateOnly(year, 6, 19), DayOfWeek.Friday);
        var allSaints = FirstWeekdayFrom(new DateOnly(year, 10, 31), DayOfWeek.Saturday);

        var list = new List<Holiday>
        {
            Public(new DateOnly(year, 1, 1), "Nyårsdagen"),
            Public(new DateOnly(year, 1, 6), "Trettondedag jul"),
            Public(easter.AddDays(-2), "Långfredagen"),
            Eve(easter.AddDays(-1), "Påskafton"),
            Public(easter, "Påskdagen"),
            Public(easter.AddDays(1), "Annandag påsk"),
            Public(new DateOnly(year, 5, 1), "Första maj"),
            Public(easter.AddDays(39), "Kristi himmelsfärdsdag"),
            Public(easter.AddDays(49), "Pingstdagen"),
            Public(new DateOnly(year, 6, 6), "Sveriges nationaldag"),
            Eve(midsummerEve, "Midsommarafton"),
            Public(midsummerEve.AddDays(1), "Midsommardagen"),
            Public(allSaints, "Alla helgons dag"),
            Eve(new DateOnly(year, 12, 24), "Julafton"),
            Public(new DateOnly(year, 12, 25), "Juldagen"),
            Public(new DateOnly(year, 12, 26), "Annandag jul"),
            Eve(new DateOnly(year, 12, 31), "Nyårsafton"),
        };

        // Två helgdagar kan falla på samma datum. Kristi himmelsfärdsdag hamnar på första maj
        // när påsken infaller 23 mars (senast 2008), och pingstdagen på nationaldagen när påsken
        // infaller 18 april (nästa gång 2049). Listan behåller båda, uppslaget per datum ger den
        // som står först i listan ovan. OrderBy är stabil, så ordningen följer med i sorteringen.
        var sorted = list.OrderBy(h => h.Date).ToList();
        var byDate = new Dictionary<DateOnly, Holiday>();
        foreach (var h in sorted) byDate.TryAdd(h.Date, h);

        return new YearTable(sorted.AsReadOnly(), byDate);
    }

    private static DateOnly FirstWeekdayFrom(DateOnly start, DayOfWeek day)
    {
        int diff = ((int)day - (int)start.DayOfWeek + 7) % 7;
        return start.AddDays(diff);
    }

    private static Holiday Public(DateOnly d, string name) => new(d, name, HolidayKind.PublicHoliday);
    private static Holiday Eve(DateOnly d, string name) => new(d, name, HolidayKind.Eve);

    private static void EnsureYear(int year)
    {
        if (year < MinYear || year > MaxYear)
            throw new ArgumentOutOfRangeException(nameof(year), year,
                $"Året måste ligga mellan {MinYear} och {MaxYear}. Före 2005 gällde andra regler.");
    }

    private sealed record YearTable(IReadOnlyList<Holiday> All, Dictionary<DateOnly, Holiday> ByDate);
}
