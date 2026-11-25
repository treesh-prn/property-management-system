using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services;

public sealed class GfmsUserAccessService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<GfmsUserAccessService> _logger;
    private readonly DAL _directory;

    public GfmsUserAccessService(ApplicationDbContext db, UserManager<ApplicationUser> users,
        ILogger<GfmsUserAccessService> logger, DAL directory)
    {
        _db = db;
        _users = users;
        _logger = logger;
        _directory = directory;
    }

    public async Task<GfmsUserAccessIndexViewModel> SearchAsync(string? search, int page)
    {
        const int pageSize = 25;
        search = search?.Trim();
        var query = _db.Users.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
        {
            var matchingEmployeeUsers = await _directory.Employees.AsNoTracking()
                .Where(employee => employee.UserId != null &&
                    ((employee.Firstname != null && employee.Firstname.Contains(search)) ||
                     (employee.Lastname != null && employee.Lastname.Contains(search)) ||
                     ((employee.Firstname + " " + employee.Lastname).Contains(search)) ||
                     (employee.EmployeeID != null && employee.EmployeeID.Contains(search)) ||
                     (employee.RFID != null && employee.RFID.Contains(search))))
                .Select(employee => employee.UserId!).Distinct().ToListAsync();
            query = query.Where(user => (user.UserName != null && user.UserName.Contains(search)) ||
                (user.Email != null && user.Email.Contains(search)) || matchingEmployeeUsers.Contains(user.Id));
        }
        var count = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var users = await query.OrderBy(user => user.UserName).ThenBy(user => user.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var ids = users.Select(user => user.Id).ToArray();
        var employees = await (from employee in _directory.Employees.AsNoTracking()
                               join position in _directory.Positions.AsNoTracking() on employee.PositionId equals (int?)position.Id into positions
                               from position in positions.DefaultIfEmpty()
                               join company in _directory.Companies.AsNoTracking() on employee.CompanyId equals (int?)company.Id into companies
                               from company in companies.DefaultIfEmpty()
                               join department in _directory.Departments.AsNoTracking() on employee.DepartmentId equals (int?)department.Id into departments
                               from department in departments.DefaultIfEmpty()
                               where employee.UserId != null && ids.Contains(employee.UserId)
                               orderby employee.ID
                               select new
                               {
                                   employee.UserId, employee.Firstname, employee.Lastname, employee.Middlename,
                                   employee.EmployeeID, employee.RFID,
                                   Position = position == null ? null : position.PositionName,
                                   Company = company == null ? null : company.Company,
                                   Department = department == null ? null : department.DepartmentName
                               }).ToListAsync();
        var supportedRoles = GfmsUserAccessPolicy.Roles.ToArray();
        var assignments = await (from assignment in _db.UserRoles.AsNoTracking()
                                 join role in _db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
                                 where ids.Contains(assignment.UserId) && supportedRoles.Contains(role.Name!)
                                 select new { assignment.UserId, role.Name }).ToListAsync();
        return new GfmsUserAccessIndexViewModel
        {
            Search = search, Page = page, TotalPages = totalPages, TotalCount = count, PageSize = pageSize,
            Users = users.Select(user =>
            {
                var employee = employees.FirstOrDefault(employee => employee.UserId == user.Id);
                var name = employee == null ? null : string.Join(" ",
                    new[] { employee.Firstname, employee.Middlename, employee.Lastname }
                        .Where(part => !string.IsNullOrWhiteSpace(part)));
                return new GfmsUserAccessRow(user.Id,
                    string.IsNullOrWhiteSpace(name) ? user.UserName ?? user.Email ?? user.Id : name,
                    user.Email ?? string.Empty,
                    assignments.Where(role => role.UserId == user.Id).Select(role => role.Name!).ToArray())
                {
                    EmployeeNumber = employee?.EmployeeID, CardNumber = employee?.RFID,
                    Position = employee?.Position, Company = employee?.Company, Department = employee?.Department
                };
            }).ToList()
        };
    }

    public async Task<GfmsUserAccessEditViewModel?> GetAsync(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return null;
        var existingRoles = await _db.Roles.Select(role => role.Name).ToListAsync();
        return new GfmsUserAccessEditViewModel
        {
            Id = user.Id, Name = user.UserName ?? user.Id, Email = user.Email ?? string.Empty,
            ConcurrencyStamp = user.ConcurrencyStamp ?? string.Empty,
            AvailableRoles = GfmsUserAccessPolicy.Roles.Where(role => existingRoles.Contains(role)).ToList(),
            SelectedRoles = (await _users.GetRolesAsync(user))
                .Where(role => GfmsUserAccessPolicy.Roles.Contains(role)).ToList()
        };
    }

    // All changes use the Identity store's context and commit together.
    public async Task<string?> UpdateAsync(GfmsUserAccessEditViewModel input, string actorId)
    {
        var selected = (input.SelectedRoles ?? new()).Distinct(StringComparer.Ordinal).ToArray();
        var validation = GfmsUserAccessPolicy.ValidateSelection(selected, input.Id == actorId);
        if (validation != null) return validation;

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = await _users.FindByIdAsync(input.Id);
        if (user == null) return "This user no longer exists.";
        if (string.IsNullOrEmpty(input.ConcurrencyStamp) || user.ConcurrencyStamp != input.ConcurrencyStamp)
            return "This account changed while you were editing. Review the current roles and try again.";
        var available = await _db.Roles.Select(role => role.Name).ToListAsync();
        if (selected.Any(role => !available.Contains(role)))
            return "One of the selected roles is unavailable. Reload the page and try again.";

        var assigned = await _users.GetRolesAsync(user);
        var remove = GfmsUserAccessPolicy.RolesToRemove(assigned, selected);
        var add = selected.Except(assigned, StringComparer.Ordinal).ToArray();
        if (remove.Length == 0 && add.Length == 0) return null;

        if (remove.Length > 0 && !(await _users.RemoveFromRolesAsync(user, remove)).Succeeded)
            return "Roles could not be removed. Reload the page and try again.";
        if (add.Length > 0 && !(await _users.AddToRolesAsync(user, add)).Succeeded)
            return "Roles could not be assigned. Reload the page and try again.";

        var activeClaims = (await _users.GetClaimsAsync(user)).Where(claim => claim.Type == "GFMS.Active Role").ToArray();
        if (activeClaims.Length != 1 || !selected.Contains(activeClaims[0].Value, StringComparer.Ordinal))
        {
            if (activeClaims.Length > 0 && !(await _users.RemoveClaimsAsync(user, activeClaims)).Succeeded)
                return "The active role could not be updated. Reload the page and try again.";
            var fallback = selected.Contains(ReservationRoles.Requestor) ? ReservationRoles.Requestor : selected.FirstOrDefault();
            if (fallback != null && !(await _users.AddClaimAsync(user,
                new System.Security.Claims.Claim("GFMS.Active Role", fallback))).Succeeded)
                return "The active role could not be updated. Reload the page and try again.";
        }
        await transaction.CommitAsync();
        _logger.LogInformation("GFMS access updated by {ActorId} for {UserId}. Added: {Added}; removed: {Removed}",
            actorId, user.Id, string.Join(", ", add), string.Join(", ", remove));
        return null;
    }
}
