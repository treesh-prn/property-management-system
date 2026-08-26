using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Attributes
{
    
    public class CustomAutorization
    {
    }

    public class ActiveRoleAuthorizeAttribute : AuthorizeAttribute
    {
        public ActiveRoleAuthorizeAttribute(params string[] roles)
        {
            Policy = $"ActiveRolePolicy:{string.Join(",", roles)}";
        }
    }

    public class ActiveRoleRequirement : IAuthorizationRequirement
    {
        public IEnumerable<string> AllowedRoles { get; }

        public ActiveRoleRequirement(IEnumerable<string> allowedRoles)
        {
            AllowedRoles = allowedRoles;
        }
    }

    public class ActiveRoleHandler : AuthorizationHandler<ActiveRoleRequirement>
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public ActiveRoleHandler(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveRoleRequirement requirement)
        {
            var applicationUser = await _userManager.GetUserAsync(context.User);
            var activeRole = context.User.FindFirst("GFMS.Active Role")?.Value;

            if (applicationUser != null && !string.IsNullOrWhiteSpace(activeRole))
            {
                var assignedRoles = await _userManager.GetRolesAsync(applicationUser);

                if (assignedRoles.Contains(activeRole, StringComparer.Ordinal) &&
                    requirement.AllowedRoles.Contains(activeRole, StringComparer.Ordinal))
                {
                    context.Succeed(requirement);
                }
            }
        }
    }

    public class CustomAuthorizationPolicyProvider : DefaultAuthorizationPolicyProvider
    {
        public CustomAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options) : base(options)
        {
        }

        public override async Task<AuthorizationPolicy> GetPolicyAsync(string policyName)
        {
            if (policyName.StartsWith("ActiveRolePolicy:", StringComparison.OrdinalIgnoreCase))
            {
                var roles = policyName.Substring("ActiveRolePolicy:".Length).Split(',').Select(r => r.Trim());
                var policy = new AuthorizationPolicyBuilder();
                policy.AddRequirements(new ActiveRoleRequirement(roles));
                return await Task.FromResult(policy.Build());
            }

            return await base.GetPolicyAsync(policyName);
        }
    }


}

