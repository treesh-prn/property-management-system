using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class ReservationSchedulePolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 14, 0, 0);

    [Theory]
    [InlineData(ReservationRoles.Requestor)]
    [InlineData(ReservationRoles.DepartmentHead)]
    [InlineData(ReservationRoles.AdminManager)]
    [InlineData(ReservationRoles.SystemAdmin)]
    [InlineData("")]
    public void Same_day_request_is_rejected_for_every_other_role(string role)
    {
        Assert.Equal(ReservationSchedulePolicy.SameDayMessage,
            ReservationSchedulePolicy.ValidateStart(Now.AddHours(1), Actor(role), Now));
        Assert.Equal(Now.Date.AddDays(1), ReservationSchedulePolicy.GetMinimumDate(Actor(role), Now));
    }

    [Fact]
    public void Active_admin_approver_can_book_a_future_time_today()
    {
        var actor = Actor(ReservationRoles.AdminApprover);
        Assert.Null(ReservationSchedulePolicy.ValidateStart(Now.AddMinutes(1), actor, Now));
        Assert.Equal(Now.Date, ReservationSchedulePolicy.GetMinimumDate(actor, Now));
    }

    [Fact]
    public void Assigned_admin_approver_role_does_not_bypass_the_active_role_requirement()
    {
        var actor = Actor(ReservationRoles.Requestor) with
        {
            AssignedRoles = new[] { ReservationRoles.Requestor, ReservationRoles.AdminApprover }
        };
        Assert.Equal(ReservationSchedulePolicy.SameDayMessage,
            ReservationSchedulePolicy.ValidateStart(Now.AddHours(1), actor, Now));
    }

    [Fact]
    public void Unassigned_admin_active_role_does_not_grant_same_day_booking()
    {
        var actor = Actor(ReservationRoles.AdminApprover) with { AssignedRoles = Array.Empty<string>() };
        Assert.Equal(ReservationSchedulePolicy.SameDayMessage,
            ReservationSchedulePolicy.ValidateStart(Now.AddHours(1), actor, Now));
    }

    [Theory]
    [InlineData(ReservationRoles.Requestor)]
    [InlineData(ReservationRoles.DepartmentHead)]
    [InlineData(ReservationRoles.AdminManager)]
    [InlineData(ReservationRoles.AdminApprover)]
    public void Tomorrow_midnight_and_later_dates_pass_schedule_policy(string role)
    {
        Assert.Null(ReservationSchedulePolicy.ValidateStart(Now.Date.AddDays(1), Actor(role), Now));
        Assert.Null(ReservationSchedulePolicy.ValidateStart(Now.AddDays(7), Actor(role), Now));
    }

    [Theory]
    [InlineData(ReservationRoles.Requestor)]
    [InlineData(ReservationRoles.AdminApprover)]
    public void Past_and_already_started_schedules_are_rejected_for_all_roles(string role)
    {
        Assert.NotNull(ReservationSchedulePolicy.ValidateStart(Now.AddDays(-1), Actor(role), Now));
        Assert.NotNull(ReservationSchedulePolicy.ValidateStart(Now.AddMinutes(-1), Actor(role), Now));
        Assert.NotNull(ReservationSchedulePolicy.ValidateStart(Now, Actor(role), Now));
    }

    [Fact]
    public void Same_day_rule_uses_calendar_dates_at_midnight()
    {
        var now = Now.Date.AddHours(23).AddMinutes(59);
        var requestor = Actor(ReservationRoles.Requestor);
        Assert.Equal(ReservationSchedulePolicy.SameDayMessage,
            ReservationSchedulePolicy.ValidateStart(now.AddSeconds(30), requestor, now));
        Assert.Null(ReservationSchedulePolicy.ValidateStart(now.Date.AddDays(1), requestor, now));
    }

    private static ReservationActor Actor(string role) => new(
        "user-id", "user@example.invalid", role, "Employee", "user@example.invalid",
        1, 1, new[] { role }, Array.Empty<string>());
}
