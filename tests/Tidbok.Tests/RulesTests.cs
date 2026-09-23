using Tidbok.Core;
using static Tidbok.Tests.Kit;

namespace Tidbok.Tests;

public class RulesTests
{
    private static BookingRequest Valid() => new()
    {
        ServiceId = 1, Start = T("2026-10-06T10:00"), Name = "Sara Lind", Phone = "070-174 06 12"
    };

    [Fact]
    public void A_normal_request_passes()
    {
        Assert.Empty(BookingRules.Validate(Valid()));
    }

    [Theory]
    [InlineData("", "name")]
    [InlineData("S", "name")]
    public void Name_is_required(string name, string field)
    {
        Assert.True(BookingRules.Validate(Valid() with { Name = name }).ContainsKey(field));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("ring mig")]
    [InlineData("070-123 45 67 <script>")]
    public void Bad_phone_numbers_are_stopped(string phone)
    {
        Assert.True(BookingRules.Validate(Valid() with { Phone = phone }).ContainsKey("phone"));
    }

    [Theory]
    [InlineData("+46 70 174 06 12")]
    [InlineData("0701740612")]
    [InlineData("019-611 22 33")]
    public void Common_phone_formats_pass(string phone)
    {
        Assert.False(BookingRules.Validate(Valid() with { Phone = phone }).ContainsKey("phone"));
    }

    [Fact]
    public void Email_is_optional_but_checked_when_given()
    {
        Assert.False(BookingRules.Validate(Valid() with { Email = "" }).ContainsKey("email"));
        Assert.False(BookingRules.Validate(Valid() with { Email = "sara@exempel.se" }).ContainsKey("email"));
        Assert.True(BookingRules.Validate(Valid() with { Email = "sara@exempel" }).ContainsKey("email"));
    }

    [Fact]
    public void Long_notes_are_stopped()
    {
        Assert.True(BookingRules.Validate(Valid() with { Note = new string('a', 501) }).ContainsKey("note"));
    }

    [Fact]
    public void Clean_removes_control_characters_and_extra_spaces()
    {
        Assert.Equal("Sara Lind", BookingRules.Clean("  Sara\u0000   Lind \t"));
        Assert.Equal("", BookingRules.Clean(null));
    }

    [Fact]
    public void Customers_can_cancel_up_to_the_cutoff()
    {
        var settings = new BookingSettings { CancelCutoffHours = 24 };
        var booking = new Booking
        {
            CustomerName = "Sara", Reference = "TB-AAAAAA", Status = BookingStatus.Booked,
            Start = T("2026-10-06T10:00"), End = T("2026-10-06T10:30")
        };

        Assert.Null(BookingRules.CustomerCancelBlocked(booking, settings, T("2026-10-05T10:00")));
        Assert.NotNull(BookingRules.CustomerCancelBlocked(booking, settings, T("2026-10-05T10:01")));
        Assert.NotNull(BookingRules.CustomerCancelBlocked(booking, settings, T("2026-10-06T11:00")));
        Assert.NotNull(BookingRules.CustomerCancelBlocked(booking with { Status = BookingStatus.Cancelled }, settings, T("2026-09-01T10:00")));
    }

    [Fact]
    public void References_are_short_and_avoid_lookalike_characters()
    {
        for (int i = 0; i < 500; i++)
        {
            var r = BookingRules.NewReference();
            Assert.Equal(9, r.Length);
            Assert.StartsWith("TB-", r);
            Assert.DoesNotContain(r[3..], c => c is '0' or 'O' or '1' or 'I');
        }
    }

    [Fact]
    public void Tokens_are_random_and_only_the_hash_is_compared()
    {
        var a = BookingRules.NewToken();
        var b = BookingRules.NewToken();

        Assert.NotEqual(a, b);
        Assert.True(BookingRules.LooksLikeToken(a));
        Assert.False(BookingRules.LooksLikeToken("kort"));
        Assert.False(BookingRules.LooksLikeToken(a[..31] + "'"));
        Assert.Equal(BookingRules.HashToken(a), BookingRules.HashToken(a));
        Assert.NotEqual(a, BookingRules.HashToken(a));
    }

    [Fact]
    public void Passwords_verify_and_use_a_new_salt_every_time()
    {
        var h1 = Passwords.Hash("tidbok-demo");
        var h2 = Passwords.Hash("tidbok-demo");

        Assert.NotEqual(h1, h2);
        Assert.True(Passwords.Verify("tidbok-demo", h1));
        Assert.False(Passwords.Verify("tidbok-dem0", h1));
        Assert.False(Passwords.Verify("tidbok-demo", "inte-en-hash"));
    }
}
