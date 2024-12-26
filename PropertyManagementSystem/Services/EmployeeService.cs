using Microsoft.AspNetCore.Mvc;
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

           var dept = await(from d in _context.Departments
                            where d.Id == departmentId
                            select new
                            {
                                d.DepartmentHead,
                            }).FirstOrDefaultAsync();

            var manager = await (from e in _context.Employees
                                 where e.GUID == dept.DepartmentHead
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
        public async Task<string> GetPositionNameById(int positionId)
        {
            var position = await _context.Positions.FirstOrDefaultAsync(p => p.Id == positionId);
            return position?.PositionName;
        }
    }
}
