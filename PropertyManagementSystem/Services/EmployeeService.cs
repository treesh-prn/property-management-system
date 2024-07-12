using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;

namespace PropertyManagementSystem.Services
{
    public class EmployeeService
    {
        private readonly DAL _context;

        public EmployeeService(DAL context)
        {
            _context = context;
        }

        public async Task<(string Email, string FullName)> GetManagerInfo(string userId)
        {
            var currentUser = await _context.Employees.FirstOrDefaultAsync(e => e.UserId == userId);
            var departmentId = currentUser?.DepartmentId;

            if(departmentId == null)
            {
                return (null, null);
            }

            var managerPosition = await _context.Positions.FirstOrDefaultAsync(p => p.PositionName == "Manager");

            if (managerPosition == null)
            {
                return (null, null);
            }

            var manager = await (from e in _context.Employees
                                 where e.DepartmentId == departmentId && e.PositionId == managerPosition.Id
                                 select new
                                 {
                                     e.Email,
                                     FullName = e.Firstname + " " + e.Lastname
                                 }).FirstOrDefaultAsync();

            if (manager == null)
            {
                return (null, null);
            }

            return (manager.Email, manager.FullName);
        }
    }
}
