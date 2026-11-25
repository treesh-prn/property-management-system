using System.ComponentModel.DataAnnotations;

namespace PropertyManagementSystem.Models;

public sealed class GfmsUserAccessIndexViewModel
{
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public int PageSize { get; set; } = 25;
    public string? EditUserId { get; set; }
    public List<GfmsUserAccessRow> Users { get; set; } = new();
}

public sealed record GfmsUserAccessRow(string Id, string Name, string Email, string[] Roles)
{
    public string? EmployeeNumber { get; init; }
    public string? CardNumber { get; init; }
    public string? Position { get; init; }
    public string? Company { get; init; }
    public string? Department { get; init; }
}

public sealed class GfmsUserAccessEditViewModel
{
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    [Required]
    public string Id { get; set; } = string.Empty;
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> SelectedRoles { get; set; } = new();
    public List<string> AvailableRoles { get; set; } = new();
}
