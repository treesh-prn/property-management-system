using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class ReservationProcessingPolicyTests
{
    [Fact]
    public void Request_submitted_exactly_at_4pm_is_processable_same_day()
    {
        var submittedAt = new DateTime(2026, 8, 3, 16, 0, 0);

        Assert.True(ReservationProcessingPolicy.CanProcess(
            submittedAt,
            submittedAt,
            bypassesCutoff: false));
    }

    [Fact]
    public void Requestor_submission_after_4pm_waits_until_next_day()
    {
        var submittedAt = new DateTime(2026, 8, 3, 16, 0, 1);

        Assert.False(ReservationProcessingPolicy.CanProcess(
            submittedAt,
            new DateTime(2026, 8, 3, 23, 59, 59),
            bypassesCutoff: false));

        Assert.True(ReservationProcessingPolicy.CanProcess(
            submittedAt,
            new DateTime(2026, 8, 4, 0, 0, 0),
            bypassesCutoff: false));
    }

    [Theory]
    [InlineData(ReservationRoles.AdminApprover)]
    [InlineData(ReservationRoles.AdminManager)]
    public void Admin_membership_bypasses_cutoff_even_when_requestor_role_is_also_assigned(
        string adminRole)
    {
        var roles = new[] { ReservationRoles.Requestor, adminRole };
        var submittedAt = new DateTime(2026, 8, 3, 20, 0, 0);

        Assert.True(ReservationProcessingPolicy.HasCutoffBypassRole(roles));
        Assert.True(ReservationProcessingPolicy.CanProcess(
            submittedAt,
            submittedAt,
            bypassesCutoff: true));
    }

    [Theory]
    [InlineData(ReservationRoles.Requestor)]
    [InlineData(ReservationRoles.DepartmentHead)]
    [InlineData(ReservationRoles.SystemAdmin)]
    public void Other_roles_do_not_receive_the_cutoff_bypass(string role)
    {
        Assert.False(ReservationProcessingPolicy.HasCutoffBypassRole(new[] { role }));
    }
}
