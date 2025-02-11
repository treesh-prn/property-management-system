// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Areas.Identity.Pages.Account
{
    public class LogoutModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<LogoutModel> _logger;
        private readonly IConfiguration _configuration;

        public LogoutModel(SignInManager<ApplicationUser> signInManager, ILogger<LogoutModel> logger, IConfiguration configuration)
        {
            _signInManager = signInManager;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<IActionResult> OnPost(string returnUrl = null)
        {
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");
            string authServerUrl = _configuration["Jwt:AuthServer"];
            if (string.IsNullOrEmpty(authServerUrl))
            {
                if (returnUrl != null)
                {
                    return LocalRedirect(returnUrl);
                }
                return RedirectToPage();
            }
            return Redirect($"{_configuration["Jwt:AuthServer"]}/Identity/Account/Login?returnUrl={_configuration["Jwt:AppUrl"]}/Token/HandleToken");
        }
    }
}
