using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class TodaysBookingScheduleTests
{
    [Fact]
    public void Includes_todays_schedules_and_ongoing_stays_but_excludes_other_dates_and_invalid_ranges()
    {
        var today = new DateTime(2026, 11, 25);
        var schedules = new[]
        {
            Row(1, today.AddHours(8), today.AddHours(17)),
            Row(2, today.AddDays(-2), today.AddDays(2)),
            Row(3, today.AddDays(-1), today), // Inclusive end date, as on the dashboard.
            Row(4, today.AddDays(-1), today.AddTicks(-1)),
            Row(5, today.AddDays(1), today.AddDays(2)),
            Row(6, null, today.AddHours(10)),
            Row(7, today, null),
            Row(8, today.AddHours(10), today.AddHours(9))
        }.AsQueryable();

        Assert.Equal(new[] { 1, 2, 3 }, schedules.OnBookingDate(today).Select(row => row.ReservationDetailId));
    }

    [Fact]
    public void Approved_date_query_limits_department_heads_and_denies_other_roles()
    {
        var today = new DateTime(2026, 11, 25);
        var headers = new[]
        {
            new GFMS_ReservationHeader { ReservationId = 1, Department = 1, RequestorEmail = "other@example.invalid", Status = "APPROVED" },
            new GFMS_ReservationHeader { ReservationId = 2, Department = 2, RequestorEmail = "head@example.invalid", Status = "APPROVED" },
            new GFMS_ReservationHeader { ReservationId = 3, Department = 1, Status = "DRAFT" }
        }.AsQueryable();
        var schedules = Enumerable.Range(1, 3).Select(id => Row(id, today, today.AddHours(10))).AsQueryable();
        foreach (var role in new[] { ReservationRoles.DepartmentHead, ReservationRoles.AdminApprover,
                     ReservationRoles.SystemAdmin, ReservationRoles.Requestor, ReservationRoles.AdminManager, "Unknown" })
        {
            var visible = headers.ForTodaysBookingViewer(role, 1);
            var result = (from detail in schedules.OnBookingDate(today)
                          join header in visible on detail.ReservationId equals header.ReservationId
                          select detail.ReservationId).ToArray();
            Assert.Equal(role is ReservationRoles.SystemAdmin or ReservationRoles.AdminApprover ? new[] { 1, 2 } :
                role == ReservationRoles.DepartmentHead ? new[] { 1 } : Array.Empty<int>(), result);
        }
    }

    [Fact]
    public void Department_head_without_department_sees_no_bookings()
    {
        var headers = new[] { new GFMS_ReservationHeader { Department = null, Status = "APPROVED" } }.AsQueryable();
        Assert.Empty(headers.ForTodaysBookingViewer(ReservationRoles.DepartmentHead, null));
    }

    [Fact]
    public void Page_and_dashboard_only_allow_the_three_requested_roles()
    {
        var policy = Assert.Single(typeof(PropertyManagementSystem.Controllers.HomeController)
            .GetMethod("TodaysBookings")!.GetCustomAttributes(typeof(PropertyManagementSystem.Attributes.ActiveRoleAuthorizeAttribute), true)
            .Cast<PropertyManagementSystem.Attributes.ActiveRoleAuthorizeAttribute>());
        Assert.Equal("ActiveRolePolicy:GFMS.Department Head,GFMS.Admin Approver,GFMS.System Admin", policy.Policy);
        Assert.True(TodaysBookingScope.CanView(ReservationRoles.DepartmentHead));
        Assert.True(TodaysBookingScope.CanView(ReservationRoles.AdminApprover));
        Assert.True(TodaysBookingScope.CanView(ReservationRoles.SystemAdmin));
        Assert.False(TodaysBookingScope.CanView(ReservationRoles.Requestor));
        Assert.False(TodaysBookingScope.CanView(ReservationRoles.AdminManager));
    }

    private static GFMS_ReservationDetails Row(int id, DateTime? start, DateTime? end) => new()
    {
        ReservationId = id, ReservationDetailId = id, StartDate = start, EndDate = end
    };
}
