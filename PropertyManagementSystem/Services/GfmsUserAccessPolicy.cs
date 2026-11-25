namespace PropertyManagementSystem.Services;

public static class GfmsUserAccessPolicy
{
    public static IReadOnlyList<string> Roles { get; } = Array.AsReadOnly(new[]
    {
        ReservationRoles.Requestor,
        ReservationRoles.DepartmentHead,
        ReservationRoles.AdminApprover,
        ReservationRoles.AdminManager,
        ReservationRoles.SystemAdmin
    });

    public static string? ValidateSelection(IEnumerable<string> selectedRoles, bool editingSelf)
    {
        var selected = selectedRoles.ToArray();
        if (selected.Any(role => !Roles.Contains(role, StringComparer.Ordinal)))
            return "Select only the supported GFMS roles.";
        if (editingSelf && !selected.Contains(ReservationRoles.SystemAdmin, StringComparer.Ordinal))
            return "Keep your System Admin role to retain access to user administration.";
        return null;
    }

    public static string[] RolesToRemove(IEnumerable<string> assigned, IEnumerable<string> selected) =>
        assigned.Where(role => Roles.Contains(role, StringComparer.Ordinal))
            .Except(selected, StringComparer.Ordinal).ToArray();
}
