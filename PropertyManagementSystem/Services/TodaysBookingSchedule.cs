using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services;

public static class TodaysBookingSchedule
{
    // Include multi-day stays and every schedule touching the selected calendar day,
    // matching the dashboard's existing inclusive end-date behavior.
    public static IQueryable<GFMS_ReservationDetails> OnBookingDate(
        this IQueryable<GFMS_ReservationDetails> schedules, DateTime date)
    {
        var day = date.Date;
        var tomorrow = day.AddDays(1);
        return schedules.Where(detail => detail.StartDate.HasValue && detail.EndDate.HasValue &&
            detail.StartDate.Value < tomorrow && detail.EndDate.Value >= day &&
            detail.EndDate.Value >= detail.StartDate.Value);
    }
}
