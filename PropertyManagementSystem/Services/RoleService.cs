using Microsoft.AspNetCore.Identity;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Security.Claims;

namespace PropertyManagementSystem.Services
{
    public interface IRoleService
    {
        //Task<IEnumerable<string>> GetRolesAsync(ClaimsPrincipal user);
        //Task<string> GetActiveRolesAsync(ClaimsPrincipal user);
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

        public async Task<Gen_Employee> GetUserName(ClaimsPrincipal user)
        {
            var empUser = await _user.GetUserAsync(user);
            
            Gen_Employee employee = _context.Employees.Where(e => e.UserId == empUser.Id).FirstOrDefault();

            return employee;
        }
    }
}
