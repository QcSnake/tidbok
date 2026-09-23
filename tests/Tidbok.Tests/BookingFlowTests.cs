using Tidbok.Core;
using Tidbok.Data;
using static Tidbok.Tests.Kit;

namespace Tidbok.Tests;

/// <summary>Hela flödet mot en riktig SQLite-fil: boka, krocka, avboka, gallra.</summary>
public class BookingFlowTests
{
    // Onsdag 23 september 2026 kl 12. Salongen har öppet tisdag till lördag.
    private static readonly DateTime Now = T("2026-09-23T12:00");

    private sealed record World(SqliteStore Store, BookingService Service, Business Salong, FixedTime Time);

    private static World Make()
    {
        var store = SeededStore(Now);
        var (time, clock) = ClockAt(Now);
        return new World(store, new BookingService(store, clock), store.FindBusiness("salong-silhuett")!, time);
    }

    private static (Service Service, Slot Slot, DateOnly Day) FirstFree(World w, string serviceName, int? staffId = null)
    {
        var svc = w.Store.Services(w.Salong.Id).Single(s => s.Name == serviceName);
        var day = w.Service.Days(w.Salong, svc, staffId, D("2026-10-06"), 14).First(d => d.FreeSlots > 0).Date;
        var slot = w.Service.Slots(w.Salong, svc, staffId, day).First();
        return (svc, slot, day);
    }

    private static BookingRequest Request(Service svc, DateOnly day, Slot slot, int? staffId = null) => new()
    {
        ServiceId = svc.Id, StaffId = staffId, Start = day.ToDateTime(slot.Time),
        Name = "Test Testsson", Phone = "070-174 06 50", Email = "test@exempel.se"
    };

    [Fact]
    public void Seed_gives_two_businesses_with_staff_services_and_bookings()
    {
        var w = Make();

        Assert.Equal(2, w.Store.Businesses().Count);
        Assert.Equal(8, w.Store.Services(w.Salong.Id).Count);
        Assert.Equal(3, w.Store.StaffFor(w.Salong.Id).Count);
        Assert.NotEmpty(w.Store.Bookings(w.Salong.Id, D("2026-09-01"), D("2026-10-10"), includeCancelled: false));
        Assert.NotNull(w.Store.FindUser("DEMO@salong-silhuett.test"));
    }

    [Fact]
    public void Booking_a_free_time_saves_it_and_takes_it_off_the_list()
    {
        var w = Make();
        var (svc, slot, day) = FirstFree(w, "Herrklippning");

        var outcome = w.Service.Book(w.Salong, Request(svc, day, slot));

        Assert.NotNull(outcome.Booking);
        Assert.True(BookingRules.LooksLikeToken(outcome.Token));
        Assert.Contains(outcome.Booking!.StaffId, slot.StaffIds);
        Assert.Equal(day.ToDateTime(slot.Time).AddMinutes(30), outcome.Booking.End);

        var after = w.Service.Slots(w.Salong, svc, outcome.Booking.StaffId, day);
        Assert.DoesNotContain(after, s => s.Time == slot.Time);
    }

    [Fact]
    public void The_same_time_cannot_be_booked_twice()
    {
        var w = Make();
        var (svc, slot, day) = FirstFree(w, "Herrklippning", staffId: null);
        var staffId = slot.StaffIds[0];

        var first = w.Service.Book(w.Salong, Request(svc, day, slot, staffId));
        var second = w.Service.Book(w.Salong, Request(svc, day, slot, staffId));

        Assert.NotNull(first.Booking);
        Assert.Null(second.Booking);
        Assert.NotNull(second.Conflict);
    }

    [Fact]
    public async Task Twenty_customers_clicking_the_same_time_at_once_give_exactly_one_booking()
    {
        var w = Make();
        var (svc, slot, day) = FirstFree(w, "Balayage");
        var staffId = slot.StaffIds[0];

        var attempts = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => w.Service.Book(w.Salong, Request(svc, day, slot, staffId))))
            .ToArray();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(r => r.Booking is not null));
        Assert.Equal(19, results.Count(r => r.Conflict is not null));
    }

    [Fact]
    public void Invalid_input_is_rejected_before_anything_is_saved()
    {
        var w = Make();
        var (svc, slot, day) = FirstFree(w, "Herrklippning");
        var before = w.Store.Bookings(w.Salong.Id, day, day, includeCancelled: true).Count;

        var outcome = w.Service.Book(w.Salong, Request(svc, day, slot) with { Phone = "hej" });

        Assert.NotNull(outcome.Errors);
        Assert.True(outcome.Errors!.ContainsKey("phone"));
        Assert.Equal(before, w.Store.Bookings(w.Salong.Id, day, day, includeCancelled: true).Count);
    }

    [Fact]
    public void A_time_that_is_not_on_the_grid_is_refused()
    {
        var w = Make();
        var (svc, slot, day) = FirstFree(w, "Herrklippning");

        var outcome = w.Service.Book(w.Salong, Request(svc, day, slot) with { Start = day.ToDateTime(slot.Time).AddMinutes(7) });

        Assert.Null(outcome.Booking);
    }

    [Fact]
    public void A_service_from_another_business_cannot_be_booked()
    {
        var w = Make();
        var klinik = w.Store.FindBusiness("klinik-bjorkang")!;
        var klinikService = w.Store.Services(klinik.Id).First();
        var (_, slot, day) = FirstFree(w, "Herrklippning");

        var outcome = w.Service.Book(w.Salong, Request(klinikService, day, slot));

        Assert.NotNull(outcome.Errors);
        Assert.True(outcome.Errors!.ContainsKey("serviceId"));
    }

    [Fact]
    public void The_customer_can_cancel_with_the_link_and_the_time_opens_up_again()
    {
        var w = Make();
        var (svc, slot, day) = FirstFree(w, "Herrklippning");
        var booked = w.Service.Book(w.Salong, Request(svc, day, slot));

        var cancelled = w.Service.CancelByCustomer(booked.Token);

        Assert.True(cancelled.Cancelled);
        Assert.Equal(BookingStatus.Cancelled, cancelled.Booking!.Status);
        Assert.Equal(CancelledBy.Customer, cancelled.Booking.CancelledBy);
        Assert.Contains(w.Service.Slots(w.Salong, svc, booked.Booking!.StaffId, day), s => s.Time == slot.Time);
    }

    [Fact]
    public void Cancelling_too_late_is_refused_and_a_wrong_token_finds_nothing()
    {
        var w = Make();
        var (svc, slot, day) = FirstFree(w, "Herrklippning");
        var booked = w.Service.Book(w.Salong, Request(svc, day, slot));

        w.Time.Value = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(slot.Time).AddHours(-2), Zone);
        var late = w.Service.CancelByCustomer(booked.Token);

        Assert.False(late.Cancelled);
        Assert.Contains("24 timmar", late.Reason);
        Assert.False(w.Service.CancelByCustomer("x").Cancelled);
        Assert.Null(w.Service.FindByToken(BookingRules.NewToken()));
    }

    [Fact]
    public void Old_bookings_are_anonymized_but_kept_for_statistics()
    {
        var w = Make();
        var countBefore = w.Store.Bookings(w.Salong.Id, D("2026-08-01"), D("2026-09-22"), true).Count;

        var changed = w.Store.Anonymize(T("2026-09-16T00:00"));
        var old = w.Store.Bookings(w.Salong.Id, D("2026-08-01"), D("2026-09-15"), true);

        Assert.True(changed > 0);
        Assert.Equal(countBefore, w.Store.Bookings(w.Salong.Id, D("2026-08-01"), D("2026-09-22"), true).Count);
        Assert.All(old, b =>
        {
            Assert.True(b.Anonymized);
            Assert.Equal("Gallrad", b.CustomerName);
            Assert.Null(b.CustomerPhone);
        });
    }

    [Fact]
    public void Admin_changes_cannot_reach_another_business()
    {
        var w = Make();
        var klinik = w.Store.FindBusiness("klinik-bjorkang")!;
        var klinikPerson = w.Store.StaffFor(klinik.Id).First();

        Assert.Throws<InvalidOperationException>(() =>
            w.Store.AddBlock(w.Salong.Id, klinikPerson.Id, T("2026-10-06T10:00"), T("2026-10-06T11:00"), "x"));
        Assert.Throws<InvalidOperationException>(() =>
            w.Store.SetHours(w.Salong.Id, klinikPerson.Id, []));
        Assert.Null(w.Store.GetBooking(w.Salong.Id,
            w.Store.Bookings(klinik.Id, D("2026-09-01"), D("2026-10-10"), true).First().Id));
    }

    [Fact]
    public void Search_finds_by_name_phone_or_reference_and_treats_wildcards_as_text()
    {
        var w = Make();
        var (svc, slot, day) = FirstFree(w, "Herrklippning");
        var booked = w.Service.Book(w.Salong, Request(svc, day, slot) with { Name = "Unika Namnet" }).Booking!;

        Assert.Contains(w.Store.SearchBookings(w.Salong.Id, "unika", 10), b => b.Id == booked.Id);
        Assert.Contains(w.Store.SearchBookings(w.Salong.Id, booked.Reference, 10), b => b.Id == booked.Id);
        Assert.Empty(w.Store.SearchBookings(w.Salong.Id, "%", 10));
    }
}
