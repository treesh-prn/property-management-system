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
        Task<Gen_Employee> GetUserName(ClaimsPrincipal user);
    }
    public class RoleService : IRoleService
    {
        private readonly UserManager<IdentityUser> _user;
        private readonly DAL _context;

        public RoleService(UserManager<IdentityUser> user, DAL context)
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
            return userRoles.Where(r => r.StartsWith("GFMS"));
        }

        public async Task<string> GetActiveRoleAsync(ClaimsPrincipal user)
        {
            var applicationUser = await _user.GetUserAsync(user);

        
            if (applicationUser == null)
            {
                return null; 
            }

            var userClaims = await _user.GetClaimsAsync(applicationUser);
            var activeRole = userClaims.FirstOrDefault(c => c.Type == "GFMS.Active Role")?.Value;

            return activeRole;
        }


        public async Task<Gen_Employee> GetUserName(ClaimsPrincipal user)
        {
            var empUser = await _user.GetUserAsync(user);
            Console.WriteLine("User Role:" + user.IsInRole);

            if (empUser != null)
            {
                Gen_Employee employee = _context.Employees.FirstOrDefault(e => e.UserId == empUser.Id);
                return employee;
            }

            return null;
        }

    }
}
