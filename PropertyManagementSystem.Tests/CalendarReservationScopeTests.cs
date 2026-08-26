using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public sealed class CalendarReservationScopeTests
{
    private static readonly GFMS_ReservationHeader[] Reservations =
    {
        new() { ReservationId = 1, Status = ReservationStatuses.Approved, RequestorEmail = "owner@example.test" },
        new() { ReservationId = 2, Status = ReservationStatuses.Approved, RequestorEmail = "other@example.test" },
        new() { ReservationId = 3, Status = ReservationStatuses.AdminApproverApproval, RequestorEmail = "owner@example.test" }
    };

    [Fact]
    public void Requestor_sees_only_their_own_approved_reservations()
    {
        var visible = Reservations.AsQueryable()
            .ForCalendarViewer(
                ReservationRoles.Requestor,
                "owner@example.test",
                null)
            .Select(item => item.ReservationId)
            .ToList();

        Assert.Equal(new[] { 1 }, visible);
    }

    [Theory]
    [InlineData(ReservationRoles.AdminApprover)]
    [InlineData(ReservationRoles.AdminManager)]
    public void Administrative_approvers_see_all_approved_reservations(string role)
    {
        var visible = Reservations.AsQueryable()
            .ForCalendarViewer(role, "admin@example.test", null)
            .Select(item => item.ReservationId)
            .OrderBy(id => id)
            .ToList();

        Assert.Equal(new[] { 1, 2 }, visible);
    }

    [Fact]
    public void Unknown_role_cannot_read_calendar_reservations()
    {
        Assert.Empty(Reservations.AsQueryable()
            .ForCalendarViewer("GFMS.Unknown", "owner@example.test", null));
    }
}
