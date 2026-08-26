using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Areas.Identity.Pages.Account.Manage;

public class IndexModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    [TempData] public string StatusMessage { get; set; } = string.Empty;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, EmailAddress, Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;
        [Display(Name = "Phone number"), Phone]
        public string? PhoneNumber { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();
        Input = new InputModel { Email = user.Email ?? string.Empty, PhoneNumber = user.PhoneNumber };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();
        if (user.Email != Input.Email)
        {
            var result = await _userManager.SetEmailAsync(user, Input.Email);
            if (!result.Succeeded) { foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description); return Page(); }
            await _userManager.SetUserNameAsync(user, Input.Email);
        }
        await _userManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
        StatusMessage = "Your profile has been updated.";
        return RedirectToPage();
    }
}
