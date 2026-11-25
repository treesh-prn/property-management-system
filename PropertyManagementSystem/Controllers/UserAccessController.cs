using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Attributes;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;

namespace PropertyManagementSystem.Controllers;

[Authorize]
[ActiveRoleAuthorize(ReservationRoles.SystemAdmin)]
public sealed class UserAccessController : Controller
{
    private readonly GfmsUserAccessService _access;
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly ILogger<UserAccessController> _logger;

    public UserAccessController(GfmsUserAccessService access, UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn, ILogger<UserAccessController> logger)
    {
        _access = access;
        _users = users;
        _signIn = signIn;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, string? editUser = null)
    {
        var model = await _access.SearchAsync(search, page);
        model.EditUserId = editUser;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id, bool offcanvas = false, string? search = null, int page = 1)
    {
        if (!offcanvas) return RedirectToAction(nameof(Index), new { search, page, editUser = id });
        var model = await _access.GetAsync(id);
        if (model == null) return NotFound();
        model.Search = search;
        model.Page = page;
        return PartialView("_Edit", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(GfmsUserAccessEditViewModel model)
    {
        if (!ModelState.IsValid) return BadRequest("Invalid user access request. Reload the page and try again.");
        var actorId = _users.GetUserId(User);
        if (actorId == null) return Forbid();
        string? error;
        try
        {
            error = await _access.UpdateAsync(model, actorId);
        }
        catch (Exception exception) when (exception is DbUpdateException or Microsoft.Data.SqlClient.SqlException)
        {
            _logger.LogError(exception, "Failed to update GFMS roles for {UserId}", model.Id);
            error = "Access could not be saved. Reload the page and try again.";
        }
        if (error != null)
        {
            TempData["AccessError"] = error;
            return RedirectToAction(nameof(Index), new { search = model.Search, page = model.Page, editUser = model.Id });
        }
        if (model.Id == actorId)
        {
            var user = await _users.GetUserAsync(User);
            if (user != null) await _signIn.RefreshSignInAsync(user);
        }
        TempData["ToastTitle"] = "GFMS access saved";
        TempData["ToastMessage"] = "Ask the user to sign out and sign in again, then select their active role.";
        TempData["ToastType"] = "success";
        return RedirectToAction(nameof(Index), new { search = model.Search, page = model.Page });
    }
}
