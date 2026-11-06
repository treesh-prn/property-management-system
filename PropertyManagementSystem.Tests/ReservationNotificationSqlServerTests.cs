using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class ReservationNotificationSqlServerTests
{
    [Theory]
    [InlineData("FN", false)]
    [InlineData("GH", false)]
    [InlineData("FN", true)]
    [InlineData("GH", true)]
    public async Task Submission_sends_receipt_to_requestor_and_routes_review_to_assigned_head_with_cutoff(string type, bool eligible)
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithDatabase(async context =>
        {
            var header = await Seed(context, type, ReservationStatuses.DepartmentHeadApproval);
            var outbox = new RecordingEmailOutbox();
            using var users = new NotificationUsers();
            var service = Service(context, outbox, users, eligible);
            var before = DateTime.UtcNow;
            await service.QueueTransitionAsync(header, "SUBMIT", ReservationStatuses.Draft, "event-submission");
            Assert.Equal(new[] { "requestor@example.invalid", "head@example.invalid" }, outbox.Messages.Select(message => message.To.Mailboxes.Single().Address));
            Assert.Null(outbox.Contexts[0]);
            Assert.Equal(ReservationStatuses.DepartmentHeadApproval, outbox.Contexts[1]!.RequiredStatus);
            Assert.Contains("For assignment", outbox.Messages[1].HtmlBody);
            if (eligible) Assert.InRange(outbox.DueDates[1], before, DateTime.UtcNow);
            else Assert.Equal(header.DateSubmitted!.Value.Date.AddDays(1).ToUniversalTime(), outbox.DueDates[1]);
            Assert.Empty(users.RequestedRoles);
        });
    }

    [Theory]
    [InlineData("FN")]
    [InlineData("GH")]
    public async Task Next_stage_goes_to_admin_approver_accounts_including_email_form_username_fallback(string type)
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithDatabase(async context =>
        {
            var header = await Seed(context, type, ReservationStatuses.AdminApproverApproval);
            header.ApproverEmail = null;
            var outbox = new RecordingEmailOutbox();
            using var users = new NotificationUsers();
            await Service(context, outbox, users, true).QueueTransitionAsync(header, "APPROVE", ReservationStatuses.DepartmentHeadApproval, "event-review");
            Assert.Equal(new[] { "requestor@example.invalid", "admin1@example.invalid", "admin2@example.invalid" },
                outbox.Messages.Select(message => message.To.Mailboxes.Single().Address));
            Assert.Equal(new[] { ReservationRoles.AdminApprover }, users.RequestedRoles);
            Assert.DoesNotContain(outbox.Messages, message => message.To.Mailboxes.Any(address => address.Address == "head@example.invalid"));
        });
    }

    [Theory]
    [InlineData("FN")]
    [InlineData("GH")]
    public async Task Auto_approval_notifies_requestor_without_routing_to_department_head(string type)
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithDatabase(async context =>
        {
            var header = await Seed(context, type, ReservationStatuses.Approved);
            header.ApproverEmail = null;
            var outbox = new RecordingEmailOutbox();
            using var users = new NotificationUsers();
            await Service(context, outbox, users, true).QueueTransitionAsync(header, "SUBMIT", ReservationStatuses.Draft, "event-auto-approved");
            Assert.Equal("requestor@example.invalid", Assert.Single(outbox.Messages).To.Mailboxes.Single().Address);
            Assert.Contains("Your reservation is approved", outbox.Messages[0].HtmlBody);
            Assert.Empty(users.RequestedRoles);
        });
    }

    private static ReservationNotificationService Service(DAL context, RecordingEmailOutbox outbox, UserManager<ApplicationUser> users, bool eligible) =>
        new(context, users, EmailTestSupport.Service(outbox, EmailDeliveryMode.Production), new FixedProcessingPolicy(eligible), NullLogger<ReservationNotificationService>.Instance);

    private static async Task<GFMS_ReservationHeader> Seed(DAL context, string type, string status)
    {
        var header = EmailTestSupport.Header(type);
        header.ReservationId = 0;
        header.Status = status;
        header.ApproverEmail = "head@example.invalid";
        header.Approver = "Department Head";
        header.DateSubmitted = DateTime.Today.AddHours(17);
        context.ReservationHeaders.Add(header);
        context.ReservationDetails.Add(new GFMS_ReservationDetails
        {
            ReservationHeader = header, StartDate = DateTime.Today.AddDays(3).AddHours(7),
            EndDate = DateTime.Today.AddDays(3).AddHours(16), NumberOfGuest = 9, Purpose = "Team meeting"
        });
        await context.SaveChangesAsync();
        return header;
    }

    private static async Task WithDatabase(Func<DAL, Task> check)
    {
        var name = "PmsEmailRouting_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<DAL>().UseSqlServer(
            $"Server=(localdb)\\MSSQLLocalDB;Database={name};Integrated Security=true;TrustServerCertificate=true").Options;
        await using var context = new DAL(options);
        try { await context.Database.EnsureCreatedAsync(); await check(context); }
        finally { await context.Database.EnsureDeletedAsync(); }
    }

    private sealed class FixedProcessingPolicy : IReservationProcessingPolicy
    {
        private readonly bool eligible;
        public FixedProcessingPolicy(bool eligible) { this.eligible = eligible; }
        public DateTime GetSubmissionTimestamp() => DateTime.Now;
        public Task<bool> IsEligibleAsync(GFMS_ReservationHeader reservation, DateTime asOf) => Task.FromResult(eligible);
        public Task<bool> IsCutoffExemptAsync(ClaimsPrincipal user) => Task.FromResult(eligible);
        public Task<IReadOnlyList<GFMS_ReservationHeader>> FilterEligibleAsync(IEnumerable<GFMS_ReservationHeader> reservations, DateTime asOf) =>
            Task.FromResult<IReadOnlyList<GFMS_ReservationHeader>>(eligible ? reservations.ToList() : Array.Empty<GFMS_ReservationHeader>());
    }

    private sealed class NotificationUsers : UserManager<ApplicationUser>
    {
        internal List<string> RequestedRoles { get; } = new();
        public NotificationUsers() : base(new EmptyUserStore(), null!, null!, null!, null!, null!, null!, null!, NullLogger<UserManager<ApplicationUser>>.Instance) { }
        public override Task<IList<ApplicationUser>> GetUsersInRoleAsync(string roleName)
        {
            RequestedRoles.Add(roleName);
            return Task.FromResult<IList<ApplicationUser>>(new[]
            {
                new ApplicationUser { Email = "admin1@example.invalid" },
                new ApplicationUser { UserName = "admin2@example.invalid" },
                new ApplicationUser { UserName = "not-an-email" }
            });
        }
    }
    private sealed class EmptyUserStore : IUserStore<ApplicationUser>
    {
        public void Dispose() { }
        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken token) => Task.FromResult(user.Id);
        public Task<string> GetUserNameAsync(ApplicationUser user, CancellationToken token) => Task.FromResult(user.UserName ?? string.Empty);
        public Task SetUserNameAsync(ApplicationUser user, string? name, CancellationToken token) { user.UserName = name; return Task.CompletedTask; }
        public Task<string> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken token) => Task.FromResult(user.NormalizedUserName ?? string.Empty);
        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? name, CancellationToken token) { user.NormalizedUserName = name; return Task.CompletedTask; }
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken token) => throw new NotSupportedException();
        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken token) => throw new NotSupportedException();
        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken token) => throw new NotSupportedException();
        public Task<ApplicationUser> FindByIdAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public Task<ApplicationUser> FindByNameAsync(string name, CancellationToken token) => throw new NotSupportedException();
    }
}
