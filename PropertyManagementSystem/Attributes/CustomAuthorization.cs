using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

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
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveRoleRequirement requirement)
        {
            if (context.User.HasClaim(c => c.Type == "GFMS.Active Role"))
            {
                var activeRole = context.User.FindFirst(c => c.Type == "GFMS.Active Role").Value;

                Console.WriteLine(activeRole.ToString());

                if (requirement.AllowedRoles.Contains(activeRole))
                {
                    context.Succeed(requirement);
                }
            }

            return Task.CompletedTask;
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

