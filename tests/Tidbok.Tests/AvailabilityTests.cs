using Tidbok.Core;
using static Tidbok.Tests.Kit;

namespace Tidbok.Tests;

public class AvailabilityTests
{
    // Tisdag 6 oktober 2026, sett från onsdag 23 september.
    private static readonly DateOnly Day = D("2026-10-06");
    private static readonly DateTime Now = T("2026-09-23T12:00");

    private static IReadOnlyList<Slot> Slots(Service svc, IReadOnlyList<Staff> staff, IReadOnlyList<WorkingHours> hours,
        IReadOnlyList<BusyTime>? busy = null, DateTime? now = null, DateOnly? day = null, BookingSettings? settings = null) =>
        Availability.Slots(new Availability.DayInput(day ?? Day, Business(settings), svc, staff, hours, busy ?? [], now ?? Now));

    [Fact]
    public void Slots_step_through_the_shift_and_stop_when_the_service_no_longer_fits()
    {
        var slots = Slots(Service(60), [Person(1, 1)], [Shift(1, DayOfWeek.Tuesday, "10:00", "12:00")]);

        Assert.Equal(new[] { "10:00", "10:15", "10:30", "10:45", "11:00" }, Times(slots));
    }

    [Fact]
    public void A_booking_blocks_every_start_that_would_overlap_it()
    {
        var busy = new[] { new BusyTime(1, T("2026-10-06T10:30"), T("2026-10-06T11:00"), BusyKind.Booking, "", 1) };

        var slots = Slots(Service(30), [Person(1, 1)], [Shift(1, DayOfWeek.Tuesday, "10:00", "12:00")], busy);

        // 10:00-10:30 slutar precis när bokningen börjar och går bra. 10:15 och 10:45 krockar.
        Assert.Equal(new[] { "10:00", "11:00", "11:15", "11:30" }, Times(slots));
    }

    [Fact]
    public void A_service_does_not_run_over_lunch()
    {
        var hours = new[]
        {
            Shift(1, DayOfWeek.Tuesday, "10:00", "12:00"),
            Shift(1, DayOfWeek.Tuesday, "12:30", "14:00")
        };

        var slots = Slots(Service(90), [Person(1, 1)], hours);

        Assert.Equal(new[] { "10:00", "10:15", "10:30", "12:30" }, Times(slots));
    }

    [Fact]
    public void Nothing_can_be_booked_closer_than_the_minimum_notice()
    {
        var now = T("2026-10-06T09:40");
        var slots = Slots(Service(30), [Person(1, 1)], [Shift(1, DayOfWeek.Tuesday, "10:00", "13:00")], now: now,
            settings: new BookingSettings { MinNoticeMinutes = 120 });

        Assert.Equal("11:45", Times(slots).First());
    }

    [Fact]
    public void Red_days_are_closed_without_anyone_entering_them()
    {
        var christmas = D("2026-12-25");
        Assert.Equal("Juldagen", Availability.ClosedReason(christmas, Business(new BookingSettings { HorizonDays = 120 }), Now));

        var slots = Slots(Service(30), [Person(1, 1)], [Shift(1, DayOfWeek.Friday, "10:00", "13:00")],
            day: christmas, settings: new BookingSettings { HorizonDays = 120 });
        Assert.Empty(slots);
    }

    [Fact]
    public void Holidays_can_be_opened_by_the_business()
    {
        var settings = new BookingSettings { ClosedOnHolidays = false, HorizonDays = 120 };
        var slots = Slots(Service(30), [Person(1, 1)], [Shift(1, DayOfWeek.Friday, "10:00", "11:00")],
            day: D("2026-12-25"), settings: settings);

        Assert.Equal(new[] { "10:00", "10:15", "10:30" }, Times(slots));
    }

    [Fact]
    public void Days_past_the_horizon_or_in_the_past_are_closed()
    {
        var b = Business(new BookingSettings { HorizonDays = 10 });
        Assert.Equal("Passerat", Availability.ClosedReason(D("2026-09-22"), b, Now));
        Assert.Null(Availability.ClosedReason(D("2026-10-02"), b, Now));
        Assert.Equal("Går inte att boka än", Availability.ClosedReason(D("2026-10-04"), b, Now));
    }

    [Fact]
    public void Staff_who_do_not_offer_the_service_are_left_out()
    {
        var slots = Slots(Service(30, id: 7), [Person(1, 1), Person(2, 7)],
            [Shift(1, DayOfWeek.Tuesday, "10:00", "11:00"), Shift(2, DayOfWeek.Tuesday, "14:00", "15:00")]);

        Assert.Equal(new[] { "14:00", "14:15", "14:30" }, Times(slots));
        Assert.All(slots, s => Assert.Equal(new[] { 2 }, s.StaffIds));
    }

    [Fact]
    public void Any_staff_merges_everyones_free_times()
    {
        var slots = Slots(Service(30), [Person(1, 1), Person(2, 1)],
            [Shift(1, DayOfWeek.Tuesday, "10:00", "11:00"), Shift(2, DayOfWeek.Tuesday, "10:30", "11:30")]);

        Assert.Equal(new[] { "10:00", "10:15", "10:30", "10:45", "11:00" }, Times(slots));
        Assert.Equal(new[] { 1, 2 }, slots.Single(s => s.Time == new TimeOnly(10, 30)).StaffIds);
    }

    [Fact]
    public void Any_staff_goes_to_the_person_with_least_booked_that_day()
    {
        var staff = new[] { Person(1, 1), Person(2, 1) };
        var busy = new[] { new BusyTime(1, T("2026-10-06T13:00"), T("2026-10-06T14:00"), BusyKind.Booking, "", 1) };

        Assert.Equal(2, Availability.PickStaff([1, 2], staff, busy));
        Assert.Equal(1, Availability.PickStaff([1, 2], staff, []));
    }

    [Fact]
    public void Blocks_do_not_count_as_booked_time_when_spreading_work()
    {
        var staff = new[] { Person(1, 1), Person(2, 1) };
        var busy = new[] { new BusyTime(1, T("2026-10-06T08:00"), T("2026-10-06T12:00"), BusyKind.Block, "Utbildning", 1) };

        Assert.Equal(1, Availability.PickStaff([1, 2], staff, busy));
    }

    [Fact]
    public void IsFree_checks_both_the_shift_and_collisions()
    {
        var hours = new[] { Shift(1, DayOfWeek.Tuesday, "10:00", "12:00") };
        var busy = new[] { new BusyTime(1, T("2026-10-06T11:00"), T("2026-10-06T11:30"), BusyKind.Block, "", 1) };

        Assert.True(Availability.IsFree(1, T("2026-10-06T10:00"), T("2026-10-06T11:00"), hours, busy));
        Assert.False(Availability.IsFree(1, T("2026-10-06T10:30"), T("2026-10-06T11:30"), hours, busy));
        Assert.False(Availability.IsFree(1, T("2026-10-06T11:30"), T("2026-10-06T12:30"), hours, busy));
        Assert.False(Availability.IsFree(2, T("2026-10-06T10:00"), T("2026-10-06T10:30"), hours, busy));
    }

    [Theory]
    [InlineData("10:00", "10:30", "10:30", "11:00", false)]
    [InlineData("10:00", "10:31", "10:30", "11:00", true)]
    [InlineData("10:00", "12:00", "10:30", "11:00", true)]
    [InlineData("11:00", "11:30", "10:30", "11:00", false)]
    public void Overlap_is_half_open(string a1, string a2, string b1, string b2, bool expected)
    {
        DateTime At(string hhmm) => T("2026-10-06T" + hhmm);
        Assert.Equal(expected, Availability.Overlaps(At(a1), At(a2), At(b1), At(b2)));
    }
}
