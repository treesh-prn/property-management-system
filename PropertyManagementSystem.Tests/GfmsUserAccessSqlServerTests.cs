using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public sealed class GfmsUserAccessSqlServerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saves_only_gfms_roles_and_rolls_back_if_identity_fails(bool failAssignment)
    {
        if (!OperatingSystem.IsWindows()) return;
        var database = $"PmsGfmsAccess_{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(
            $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true"));
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        try
        {
            await db.Database.EnsureCreatedAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in GfmsUserAccessPolicy.Roles.Append("OtherApp.Admin"))
                Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
            var user = new ApplicationUser { UserName = "access.test", Email = "access@example.invalid" };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            Assert.True((await users.AddToRolesAsync(user, new[] { ReservationRoles.Requestor, "OtherApp.Admin" })).Succeeded);
            Assert.True((await users.AddClaimAsync(user,
                new System.Security.Claims.Claim("GFMS.Active Role", ReservationRoles.Requestor))).Succeeded);
            Assert.True((await users.AddClaimAsync(user,
                new System.Security.Claims.Claim("OtherApp.Preference", "keep"))).Succeeded);

            var manager = failAssignment ? new FailingAssignmentManager(scope.ServiceProvider) : users;
            await using var directory = new DAL(new DbContextOptionsBuilder<DAL>()
                .UseSqlServer(db.Database.GetConnectionString()!).Options);
            var service = new GfmsUserAccessService(db, manager, NullLogger<GfmsUserAccessService>.Instance, directory);
            if (!failAssignment)
            {
                await directory.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
                var company = new Gen_Company { Company = "Example Company", CompanyCode = "EX" };
                var position = new Gen_Position { PositionName = "Quality Controller", PositionCode = "QC", IsActive = true };
                directory.AddRange(company, position);
                await directory.SaveChangesAsync();
                var department = new Gen_Department { DepartmentName = "Production", CompanyId = company.Id };
                directory.Add(department);
                await directory.SaveChangesAsync();
                directory.Add(new Gen_Employee
                {
                    UserId = user.Id, GUID = Guid.NewGuid(), Firstname = "Jamie", Lastname = "Example",
                    EmployeeID = "EMP-123", RFID = "CARD-456", CompanyId = company.Id,
                    DepartmentId = department.Id, PositionId = position.Id
                });
                await directory.SaveChangesAsync();
                foreach (var search in new[] { "Jamie Example", "EMP-123", "CARD-456", "access@example.invalid" })
                {
                    var row = Assert.Single((await service.SearchAsync(search, 1)).Users);
                    Assert.Equal("Jamie Example", row.Name);
                    Assert.Equal("Quality Controller", row.Position);
                    Assert.Equal("Example Company", row.Company);
                    Assert.Equal("Production", row.Department);
                    Assert.Equal(new[] { ReservationRoles.Requestor }, row.Roles);
                }
            }
            var form = (await service.GetAsync(user.Id))!;
            var oldStamp = form.ConcurrencyStamp;
            form.SelectedRoles = new() { ReservationRoles.AdminApprover };
            var error = await service.UpdateAsync(form, "another-admin");
            db.ChangeTracker.Clear();
            user = (await users.FindByIdAsync(user.Id))!;
            var assigned = await users.GetRolesAsync(user);
            Assert.Contains("OtherApp.Admin", assigned);
            Assert.Equal(!failAssignment, error == null);
            Assert.Contains(failAssignment ? ReservationRoles.Requestor : ReservationRoles.AdminApprover, assigned);
            Assert.DoesNotContain(failAssignment ? ReservationRoles.AdminApprover : ReservationRoles.Requestor, assigned);
            var claims = await users.GetClaimsAsync(user);
            Assert.Contains(claims, claim => claim.Type == "OtherApp.Preference" && claim.Value == "keep");
            Assert.Equal(failAssignment ? ReservationRoles.Requestor : ReservationRoles.AdminApprover,
                Assert.Single(claims.Where(claim => claim.Type == "GFMS.Active Role")).Value);

            if (!failAssignment)
            {
                // A stale browser form cannot overwrite a newer role assignment.
                form.ConcurrencyStamp = oldStamp;
                form.SelectedRoles.Clear();
                Assert.NotNull(await service.UpdateAsync(form, "another-admin"));
                db.ChangeTracker.Clear();
                form = (await service.GetAsync(user.Id))!;
                form.SelectedRoles.Clear();
                Assert.Null(await service.UpdateAsync(form, "another-admin"));
                db.ChangeTracker.Clear();
                user = (await users.FindByIdAsync(user.Id))!;
                Assert.Equal(new[] { "OtherApp.Admin" }, await users.GetRolesAsync(user));
                Assert.DoesNotContain(await users.GetClaimsAsync(user), claim => claim.Type == "GFMS.Active Role");
            }
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class FailingAssignmentManager : UserManager<ApplicationUser>
    {
        public FailingAssignmentManager(IServiceProvider services) : base(
            services.GetRequiredService<IUserStore<ApplicationUser>>(), Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(), Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), services, NullLogger<UserManager<ApplicationUser>>.Instance) { }

        public override Task<IdentityResult> AddToRolesAsync(ApplicationUser user, IEnumerable<string> roles) =>
            Task.FromResult(IdentityResult.Failed(new IdentityError { Description = "Simulated assignment failure" }));
    }
}
