using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class ReservationEditValidationTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 10, 0, 0);

    [Theory]
    [InlineData(-60, 60)]
    [InlineData(0, 60)]
    [InlineData(120, 60)]
    [InlineData(60, 60)]
    public void Invalid_schedule_is_rejected_for_every_edit(int startMinutes, int endMinutes)
    {
        Assert.NotNull(ReservationEditValidation.Validate(Now.AddMinutes(startMinutes), Now.AddMinutes(endMinutes), "Meeting", Now));
    }

    [Fact]
    public void Missing_dates_are_rejected_without_throwing()
    {
        Assert.NotNull(ReservationEditValidation.Validate(default, default, "Meeting", Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("This purpose is too long and exceeds fifty characters in length.")]
    public void Invalid_purpose_is_rejected(string? purpose)
    {
        Assert.NotNull(ReservationEditValidation.Validate(Now.AddHours(1), Now.AddHours(2), purpose, Now));
    }

    [Fact]
    public void Future_schedule_today_is_allowed()
    {
        Assert.Null(ReservationEditValidation.Validate(Now.AddMinutes(1), Now.AddHours(1), "Meeting", Now));
    }
}
