using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;

namespace PropertyManagementSystem.Services;

public sealed record ReservationCreationDetails(
    string Requestor, string RequestorEmail, int CompanyId, int DepartmentId);

public static class ReservationCreationPolicy
{
    public static bool CanChooseCostCenter(ReservationActor actor) =>
        actor.ActiveRole == ReservationRoles.AdminApprover &&
        actor.AssignedRoles.Contains(ReservationRoles.AdminApprover);

    public static string GetRequestorName(ReservationActor actor) =>
        !string.IsNullOrWhiteSpace(actor.EmployeeName) ? actor.EmployeeName : actor.AccountName;

    public static string GetRequestorEmail(ReservationActor actor) =>
        !string.IsNullOrWhiteSpace(actor.EmployeeEmail) ? actor.EmployeeEmail :
        CanChooseCostCenter(actor) ? actor.Email : string.Empty;

    public static ReservationCreationDetails? Resolve(
        ReservationActor actor, int? requestedCompanyId, int? requestedDepartmentId)
    {
        var canChoose = CanChooseCostCenter(actor);
        var companyId = canChoose ? requestedCompanyId : actor.CompanyId;
        var departmentId = canChoose ? requestedDepartmentId : actor.DepartmentId;
        var name = GetRequestorName(actor);
        var email = GetRequestorEmail(actor);
        if (companyId is null or <= 0 || departmentId is null or <= 0 ||
            string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
            return null;

        return new(name, email, companyId.Value, departmentId.Value);
    }

    public static Task<bool> IsValidCostCenterAsync(DAL context, ReservationCreationDetails details) =>
        context.Departments.AnyAsync(department =>
            department.Id == details.DepartmentId && department.CompanyId == details.CompanyId &&
            context.Companies.Any(company => company.Id == details.CompanyId));
}
