using System.IdentityModel.Tokens.Jwt;
using System.Text;
using PropertyManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace PropertyManagementSystem.Middlewares
{
    public class CookieTokenValidator
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CookieTokenValidator> _logger;

        public CookieTokenValidator(RequestDelegate next, ILogger<CookieTokenValidator> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var token = context.Request.Cookies["access_token"]; //Get cookie that contains access token from TIPCO360 app
            bool isAuthenticated = context.User.Identity.IsAuthenticated;
            var userManager = context.RequestServices.GetRequiredService<
                UserManager<ApplicationUser>
            >();
            var signInManager = context.RequestServices.GetRequiredService<
                SignInManager<ApplicationUser>
            >();

            if (string.IsNullOrEmpty(token))
            {
                if(isAuthenticated) signInManager.SignOutAsync();

                context.Response.Redirect("https://localhost:5000/Identity/Account/Login");
                return;
            }

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var validationParams = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(
                            "ilovetecoilovetecoilovetecoilovetecoilovetecoiloveteco"
                        )
                    ),
                    ValidIssuer = "https://localhost:5000",
                    ValidAudience = "tipco360"
                };

                var principal = tokenHandler.ValidateToken(
                    token,
                    validationParams,
                    out SecurityToken validatedToken
                );

                var validToken = validatedToken as JwtSecurityToken;

                var email = validToken?.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

                if (!string.IsNullOrEmpty(email) && !isAuthenticated)
                {
                    var user = await userManager.FindByEmailAsync(email);
                    if (user != null)
                    {
                        await signInManager.SignInAsync(user, isPersistent: false);
                    }
                }
            }
            catch (SecurityTokenException)
            {
                return;
            }
            await _next(context);
        }
    }
}
