using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public interface IReservationProcessingPolicy
    {
        DateTime GetSubmissionTimestamp();
        Task<IReadOnlyList<GFMS_ReservationHeader>> FilterEligibleAsync(
            IEnumerable<GFMS_ReservationHeader> reservations,
            DateTime asOf);
        Task<bool> IsEligibleAsync(GFMS_ReservationHeader reservation, DateTime asOf);
        Task<bool> IsCutoffExemptAsync(ClaimsPrincipal user);
    }

    public sealed class ReservationProcessingPolicy : IReservationProcessingPolicy
    {
        private static readonly TimeSpan DailyCutoff = TimeSpan.FromHours(16);
        private static readonly HashSet<string> CutoffBypassRoles = new(StringComparer.Ordinal)
        {
            ReservationRoles.AdminApprover,
            ReservationRoles.AdminManager
        };

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly DAL _context;

        public ReservationProcessingPolicy(
            UserManager<ApplicationUser> userManager,
            DAL context)
        {
            _userManager = userManager;
            _context = context;
        }

        public DateTime GetSubmissionTimestamp() => DateTime.Now;

        public async Task<bool> IsCutoffExemptAsync(ClaimsPrincipal user)
        {
            var applicationUser = await _userManager.GetUserAsync(user);
            return applicationUser != null && await HasCutoffBypassRoleAsync(applicationUser);
        }

        public async Task<IReadOnlyList<GFMS_ReservationHeader>> FilterEligibleAsync(
            IEnumerable<GFMS_ReservationHeader> reservations,
            DateTime asOf)
        {
            var result = new List<GFMS_ReservationHeader>();
            var bypassByRequestor = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var reservation in reservations)
            {
                var requestorKey = reservation.RequestorEmail?.Trim() ?? string.Empty;
                if (!bypassByRequestor.TryGetValue(requestorKey, out var bypassesCutoff))
                {
                    bypassesCutoff = await RequestorHasCutoffBypassRoleAsync(requestorKey);
                    bypassByRequestor[requestorKey] = bypassesCutoff;
                }

                var submittedAt = reservation.DateSubmitted ?? reservation.DateFiled;
                if (submittedAt.HasValue && CanProcess(submittedAt.Value, asOf, bypassesCutoff))
                {
                    result.Add(reservation);
                }
            }

            return result;
        }

        public async Task<bool> IsEligibleAsync(
            GFMS_ReservationHeader reservation,
            DateTime asOf)
        {
            var submittedAt = reservation.DateSubmitted ?? reservation.DateFiled;
            if (!submittedAt.HasValue)
            {
                return false;
            }

            var bypassesCutoff = await RequestorHasCutoffBypassRoleAsync(
                reservation.RequestorEmail?.Trim() ?? string.Empty);
            return CanProcess(submittedAt.Value, asOf, bypassesCutoff);
        }

        public static bool CanProcess(DateTime submittedAt, DateTime asOf, bool bypassesCutoff)
        {
            if (asOf < submittedAt)
            {
                return false;
            }

            if (bypassesCutoff)
            {
                return true;
            }

            var processingDate = submittedAt.TimeOfDay > DailyCutoff
                ? submittedAt.Date.AddDays(1)
                : submittedAt.Date;

            return asOf.Date >= processingDate;
        }

        public static bool HasCutoffBypassRole(IEnumerable<string> assignedRoles) =>
            assignedRoles.Any(CutoffBypassRoles.Contains);

        private async Task<bool> RequestorHasCutoffBypassRoleAsync(string requestorEmail)
        {
            if (string.IsNullOrWhiteSpace(requestorEmail))
            {
                return false;
            }

            var applicationUser = await _userManager.FindByEmailAsync(requestorEmail);
            if (applicationUser == null)
            {
                var userId = await _context.Employees
                    .AsNoTracking()
                    .Where(employee => employee.Email == requestorEmail)
                    .Select(employee => employee.UserId)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrWhiteSpace(userId))
                {
                    applicationUser = await _userManager.FindByIdAsync(userId);
                }
            }

            return applicationUser != null && await HasCutoffBypassRoleAsync(applicationUser);
        }

        private async Task<bool> HasCutoffBypassRoleAsync(ApplicationUser user)
        {
            var assignedRoles = await _userManager.GetRolesAsync(user);
            return HasCutoffBypassRole(assignedRoles);
        }
    }
}
