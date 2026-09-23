namespace Tidbok.Core;

// Alla tider i Tidbok är verksamhetens lokala tid (Europe/Stockholm). En frisör i Örebro tänker
// i "tisdag 10:00", inte i UTC, och öppettiderna är väggklockstider. Sommartidsbytet sker nattetid
// när ingen har öppet, så lokal tid rakt igenom är både enklast och rätt för den här domänen.

/// <summary>En verksamhet som tar emot bokningar, till exempel en salong eller en klinik.</summary>
public sealed record Business
{
    public int Id { get; init; }
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public string Kind { get; init; } = "";
    public string Address { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Accent { get; init; } = "#2f6f5e";
    public string AccentDeep { get; init; } = "#1f4d41";
    public BookingSettings Settings { get; init; } = new();
}

/// <summary>Reglerna för hur kunder får boka.</summary>
public sealed record BookingSettings
{
    /// <summary>Hur tätt starttiderna ligger. 15 ger 10:00, 10:15, 10:30 och så vidare.</summary>
    public int SlotMinutes { get; init; } = 15;

    /// <summary>Hur långt i förväg en bokning senast får göras.</summary>
    public int MinNoticeMinutes { get; init; } = 120;

    /// <summary>Hur långt fram i tiden det går att boka.</summary>
    public int HorizonDays { get; init; } = 30;

    /// <summary>Hur sent kunden själv får avboka.</summary>
    public int CancelCutoffHours { get; init; } = 24;

    /// <summary>Stängt på röda dagar och aftnar, utan att någon behöver lägga in det för hand.</summary>
    public bool ClosedOnHolidays { get; init; } = true;
}

public sealed record Service
{
    public int Id { get; init; }
    public int BusinessId { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public int DurationMinutes { get; init; }
    public int? PriceSek { get; init; }
    public bool PriceFrom { get; init; }
    public bool Active { get; init; } = true;
    public int Sort { get; init; }
}

public sealed record Staff
{
    public int Id { get; init; }
    public int BusinessId { get; init; }
    public required string Name { get; init; }
    public string Title { get; init; } = "";
    public bool Active { get; init; } = true;
    public int Sort { get; init; }
    public IReadOnlyList<int> ServiceIds { get; init; } = [];
}

/// <summary>Ett arbetspass en viss veckodag. Minuter från midnatt, så 600 är 10:00.</summary>
public sealed record WorkingHours(int StaffId, DayOfWeek Day, int StartMinute, int EndMinute)
{
    public TimeOnly Start => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(StartMinute));
    public TimeOnly End => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(EndMinute));
}

/// <summary>Tid som inte går att boka: en bokning, lunch, sjukdom, utbildning.</summary>
public sealed record BusyTime(int StaffId, DateTime Start, DateTime End, BusyKind Kind, string Label, int SourceId);

public enum BusyKind { Booking, Block }

public enum BookingStatus { Booked, Cancelled }

public enum CancelledBy { Customer, Business }

public sealed record Booking
{
    public int Id { get; init; }
    public int BusinessId { get; init; }
    public int StaffId { get; init; }
    public int ServiceId { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public BookingStatus Status { get; init; }
    public required string CustomerName { get; init; }
    public string? CustomerPhone { get; init; }
    public string? CustomerEmail { get; init; }
    public string? Note { get; init; }
    public required string Reference { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CancelledAt { get; init; }
    public CancelledBy? CancelledBy { get; init; }
    public bool Anonymized { get; init; }

    public int Minutes => (int)(End - Start).TotalMinutes;
}

public sealed record TimeBlock(int Id, int StaffId, DateTime Start, DateTime End, string Reason);

/// <summary>Det kunden fyller i.</summary>
public sealed record BookingRequest
{
    public int ServiceId { get; init; }
    public int? StaffId { get; init; }
    public DateTime Start { get; init; }
    public string Name { get; init; } = "";
    public string Phone { get; init; } = "";
    public string? Email { get; init; }
    public string? Note { get; init; }
}

/// <summary>En ledig starttid och vilka i personalen som kan ta den.</summary>
public sealed record Slot(TimeOnly Time, IReadOnlyList<int> StaffIds);

/// <summary>En dag i kalenderremsan: hur många lediga tider som finns, och varför det är stängt.</summary>
public sealed record DayAvailability(DateOnly Date, int FreeSlots, string? ClosedReason);
