using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;

namespace PropertyManagementSystem.Services
{
    public static class ReservationHistoryYearService
    {
        public static async Task<IReadOnlyList<int>> GetAvailableYearsAsync(
            DAL context,
            string reservationType,
            string? requestorEmail,
            string? employeeEmail,
            string? requestorName)
        {
            var headers = context.ReservationHeaders
                .AsNoTracking()
                .Where(header =>
                    header.ReservationType == reservationType &&
                    header.Status != "DELETED")
                .Where(header =>
                    (!string.IsNullOrEmpty(requestorEmail) && header.RequestorEmail == requestorEmail) ||
                    (!string.IsNullOrEmpty(employeeEmail) && header.RequestorEmail == employeeEmail) ||
                    (!string.IsNullOrEmpty(requestorName) && header.Requestor == requestorName));

            var scheduledYears = await (
                    from header in headers
                    join detail in context.ReservationDetails.AsNoTracking()
                        on header.ReservationId equals detail.ReservationId
                    where detail.StartDate.HasValue
                    select detail.StartDate.Value.Year)
                .Distinct()
                .ToListAsync();

            var draftYears = await headers
                .Where(header =>
                    header.DateFiled.HasValue &&
                    !context.ReservationDetails.Any(detail =>
                        detail.ReservationId == header.ReservationId))
                .Select(header => header.DateFiled!.Value.Year)
                .Distinct()
                .ToListAsync();

            return scheduledYears
                .Concat(draftYears)
                .Distinct()
                .OrderByDescending(year => year)
                .ToList();
        }
    }
}
