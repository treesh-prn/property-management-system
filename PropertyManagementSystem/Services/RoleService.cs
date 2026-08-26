using Microsoft.AspNetCore.Identity;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Security.Claims;

namespace PropertyManagementSystem.Services
{
    public interface IRoleService
    {
        Task<IEnumerable<string>> GetRolesAsync(ClaimsPrincipal user);
        Task<string> GetActiveRoleAsync(ClaimsPrincipal user);
        Task<string> SetDefaultActiveRoleAsync(ApplicationUser user);
        Task<Gen_Employee?> GetUserName(ClaimsPrincipal user);
    }
    public class RoleService : IRoleService
    {
        private const string RequestorRole = "GFMS.Requestor";

        private readonly UserManager<ApplicationUser> _user;
        private readonly DAL _context;

        public RoleService(UserManager<ApplicationUser> user, DAL context)
        {
            _user = user;
            _context = context;
        }
        public async Task<IEnumerable<string>> GetRolesAsync(ClaimsPrincipal user)
        {
            var applicationUser = await _user.GetUserAsync(user);
            if (applicationUser == null)
            {
                return Enumerable.Empty<string>();
            }

            var userRoles = await _user.GetRolesAsync(applicationUser);
            return GetAssignedGfmsRoles(userRoles);
        }

        public async Task<string> GetActiveRoleAsync(ClaimsPrincipal user)
        {
            var applicationUser = await _user.GetUserAsync(user);

            if (applicationUser == null)
            {
                return string.Empty;
            }

            var userRoles = GetAssignedGfmsRoles(await _user.GetRolesAsync(applicationUser));
            var userClaims = await _user.GetClaimsAsync(applicationUser);
            var activeRole = userClaims.FirstOrDefault(c => c.Type == "GFMS.Active Role")?.Value;

            if (!string.IsNullOrWhiteSpace(activeRole) && userRoles.Contains(activeRole))
            {
                return activeRole;
            }

            return GetDefaultRole(userRoles);
        }

        public async Task<string> SetDefaultActiveRoleAsync(ApplicationUser user)
        {
            var userRoles = GetAssignedGfmsRoles(await _user.GetRolesAsync(user));
            var defaultRole = GetDefaultRole(userRoles);
            var userClaims = await _user.GetClaimsAsync(user);

            foreach (var claim in userClaims.Where(c => c.Type == "GFMS.Active Role"))
            {
                await _user.RemoveClaimAsync(user, claim);
            }

            if (!string.IsNullOrEmpty(defaultRole))
            {
                await _user.AddClaimAsync(user, new Claim("GFMS.Active Role", defaultRole));
            }

            return defaultRole;
        }

        private static string GetDefaultRole(IReadOnlyCollection<string> roles)
        {
            return roles.Contains(RequestorRole)
                ? RequestorRole
                : roles.FirstOrDefault() ?? string.Empty;
        }

        private static List<string> GetAssignedGfmsRoles(IEnumerable<string> assignedRoles)
        {
            return assignedRoles
                .Where(r => r.StartsWith("GFMS", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }


        public async Task<Gen_Employee?> GetUserName(ClaimsPrincipal user)
        {
            var empUser = await _user.GetUserAsync(user);

            if (empUser != null)
            {
                Gen_Employee? employee = _context.Employees.FirstOrDefault(e => e.UserId == empUser.Id);
                return employee;
            }

            return null;
        }

    }
}
