using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Security.Claims;
using PropertyManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
namespace PropertyManagementSystem.Controllers
{
    public class TokenController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;


        public TokenController(IHttpClientFactory httpClientFactory, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _signInManager = signInManager;
            _userManager = userManager;
            _configuration = configuration;
        }
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> HandleToken(string token)
        {
            // Call the authentication server to validate the token
            var isValid = await ValidateTokenWithAuthServer(token);
            if (isValid)
            {
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);

                // Create claims based on JWT
                var claims = jwtToken.Claims;
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                var subject = jwtToken.Subject;

                var user = await _userManager.FindByEmailAsync(subject);

                // Sign in the user locally
                await _signInManager.SignInAsync(user, isPersistent: false);

                return RedirectToAction("Index", "Home");
            }

            return Unauthorized();
        }

        private async Task<bool> ValidateTokenWithAuthServer(string token)
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsJsonAsync($"{_configuration["Jwt:AuthServer"]}/api/validate-token", new { Token = token });
            return response.IsSuccessStatusCode;
        }

    }
}