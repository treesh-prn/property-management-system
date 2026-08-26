using System.Globalization;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public static class ReportParameterMapper
    {
        public static IReadOnlyDictionary<string, string> ForOccupancy(
            OccupancyReportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            return new Dictionary<string, string>
            {
                ["StartDate"] = ToSsrsDate(request.StartDate!.Value),
                ["EndDate"] = ToSsrsDate(request.EndDate!.Value),
                ["ReservationType"] = request.ReservationType
            };
        }

        private static string ToSsrsDate(DateTime date) =>
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
