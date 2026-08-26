using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class ReservationScheduleStatusServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 11, 10, 0, 0);

    [Fact]
    public void Future_schedule_remains_pending()
    {
        var transition = ReservationScheduleStatusService.Evaluate(
            "FN",
            new DateTime?[] { Now.AddHours(1) },
            new DateTime?[] { Now.AddHours(3) },
            Now);

        Assert.Null(transition);
    }

    [Theory]
    [InlineData("FN")]
    [InlineData("GH")]
    public void Started_reservations_are_returned_for_rescheduling(string reservationType)
    {
        var transition = ReservationScheduleStatusService.Evaluate(
            reservationType,
            new DateTime?[] { Now.AddMinutes(-1) },
            new DateTime?[] { Now.AddHours(2) },
            Now);

        Assert.Equal(ReservationStatuses.ForRescheduling, transition?.NewStatus);
    }

    [Fact]
    public void Fully_elapsed_reservations_expire()
    {
        var transition = ReservationScheduleStatusService.Evaluate(
            "GH",
            new DateTime?[] { Now.AddHours(-3) },
            new DateTime?[] { Now.AddMinutes(-1) },
            Now);

        Assert.Equal(ReservationStatuses.Expired, transition?.NewStatus);
    }

    [Fact]
    public void Started_dorm_assignment_expires_instead_of_requesting_an_uneditable_reschedule()
    {
        var transition = ReservationScheduleStatusService.Evaluate(
            "DRM",
            new DateTime?[] { Now.AddMinutes(-1) },
            new DateTime?[] { Now.AddDays(30) },
            Now);

        Assert.Equal(ReservationStatuses.Expired, transition?.NewStatus);
    }

    [Fact]
    public void Request_without_schedule_is_not_changed()
    {
        var transition = ReservationScheduleStatusService.Evaluate(
            "FN",
            Array.Empty<DateTime?>(),
            Array.Empty<DateTime?>(),
            Now);

        Assert.Null(transition);
    }

    [Theory]
    [InlineData(ReservationStatuses.Expired, "Schedule expired.")]
    [InlineData(ReservationStatuses.ForRescheduling, "Reschedule required.")]
    public void Stored_system_remark_fits_legacy_database_column(
        string status,
        string expected)
    {
        var remark = ReservationScheduleStatusService.GetStoredRemark(status);

        Assert.Equal(expected, remark);
        Assert.True(remark.Length <= 50);
    }
}
