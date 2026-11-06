using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class EmailNotificationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Missing_or_unqualified_intended_recipient_is_rejected_before_queueing(string? address)
    {
        var service = EmailTestSupport.Service(new RecordingEmailOutbox());
        Assert.Throws<ArgumentException>(() => service.CreateMessage(EmailTestSupport.Notification() with { RecipientEmail = address! }));
    }

    [Fact]
    public void Test_email_redirects_only_to_test_inbox_and_displays_intended_recipient()
    {
        var service = EmailTestSupport.Service(new RecordingEmailOutbox());
        var message = service.CreateMessage(EmailTestSupport.Notification());
        Assert.Equal("test-inbox@example.invalid", message.To.Mailboxes.Single().Address);
        Assert.Empty(message.Cc);
        Assert.Empty(message.Bcc);
        Assert.StartsWith("[TEST] ", message.Subject);
        Assert.Contains("TEST NOTIFICATION", message.HtmlBody);
        Assert.Contains("requestor@example.invalid", message.HtmlBody);
        Assert.Contains("Your reservation is approved", message.TextBody);
        Assert.Contains("https://pms.example.invalid/pms/FunctionRoom/ViewReservationDetails?Id=sample-guid", message.HtmlBody);
    }

    [Fact]
    public void Production_email_uses_actual_recipient_without_test_prefix_or_banner()
    {
        var service = EmailTestSupport.Service(new RecordingEmailOutbox(), EmailDeliveryMode.Production);
        var message = service.CreateMessage(EmailTestSupport.Notification());
        Assert.Equal("requestor@example.invalid", message.To.Mailboxes.Single().Address);
        Assert.DoesNotContain("[TEST]", message.Subject);
        Assert.DoesNotContain("TEST NOTIFICATION", message.HtmlBody);
        Assert.NotNull(message.MessageId);
        Assert.False(string.IsNullOrWhiteSpace(message.TextBody));
    }

    [Fact]
    public void Untrusted_names_purpose_and_remarks_are_encoded()
    {
        var model = EmailTestSupport.Notification() with
        {
            RecipientName = "<script>alert('name')</script>",
            Message = "<img src=x onerror=alert('message')>",
            Details = new[] { new EmailDetail("Remarks", "<script>alert('remarks')</script>") }
        };
        var message = EmailTestSupport.Service(new RecordingEmailOutbox()).CreateMessage(model);
        Assert.DoesNotContain("<script>", message.HtmlBody);
        Assert.DoesNotContain("<img src=x", message.HtmlBody);
        Assert.Contains("&lt;script&gt;", message.HtmlBody);
        Assert.Contains("<script>", message.TextBody);
    }

    [Theory]
    [InlineData("FN", "FunctionRoom")]
    [InlineData("GH", "Accommodation")]
    [InlineData("DRM", "Dorm")]
    public void Reservation_links_preserve_virtual_application_path_and_encode_identifier(string type, string controller)
    {
        var header = EmailTestSupport.Header(type);
        header.GUID = "abc/?&value";
        var url = ReservationEmailTemplate.ReservationUrl(header, "https://pms.example.invalid/pms");
        Assert.StartsWith($"https://pms.example.invalid/pms/{controller}/ViewReservationDetails?Id=", url);
        Assert.Contains("abc%2F%3F%26value", url);
        header.GUID = null;
        Assert.Equal("https://pms.example.invalid/pms/Identity/Account/Login",
            ReservationEmailTemplate.ReservationUrl(header, "https://pms.example.invalid/pms"));
    }

    [Fact]
    public async Task Approval_request_without_room_or_facility_is_still_queued_for_the_approver()
    {
        var outbox = new RecordingEmailOutbox();
        var header = EmailTestSupport.Header();
        header.Status = ReservationStatuses.DepartmentHeadApproval;
        await EmailTestSupport.Service(outbox).SendReservationApprovalEmail(header, "head@example.invalid",
            "2026-11-09T07:00:00", "2026-11-09T16:00:00", 9, null!, null!, "Meeting");
        var message = Assert.Single(outbox.Messages);
        Assert.Contains("head@example.invalid", message.HtmlBody);
        Assert.Contains("For assignment", message.HtmlBody);
        Assert.Contains("Nov 9, 2026 at 7:00 AM", message.HtmlBody);
    }

    [Theory]
    [InlineData("SUBMIT", ReservationStatuses.Draft, ReservationStatuses.DepartmentHeadApproval, true)]
    [InlineData("SUBMIT", ReservationStatuses.Draft, ReservationStatuses.Approved, true)]
    [InlineData("RESCHEDULE_AND_RESUBMIT", ReservationStatuses.ForRescheduling, ReservationStatuses.DepartmentHeadApproval, true)]
    [InlineData("APPROVE", ReservationStatuses.DepartmentHeadApproval, ReservationStatuses.AdminApproverApproval, true)]
    [InlineData("APPROVE", ReservationStatuses.AdminApproverApproval, ReservationStatuses.Approved, true)]
    [InlineData("REJECT", ReservationStatuses.AdminApproverApproval, ReservationStatuses.Disapproved, true)]
    [InlineData("CANCEL", ReservationStatuses.Approved, ReservationStatuses.Cancelled, true)]
    [InlineData("SAVE_ROOMS", ReservationStatuses.Approved, ReservationStatuses.Approved, true)]
    [InlineData("ASSIGN", ReservationStatuses.AdminApproverApproval, ReservationStatuses.Approved, false)]
    [InlineData("EDIT", ReservationStatuses.Draft, ReservationStatuses.Draft, false)]
    [InlineData("APPROVE", ReservationStatuses.Approved, ReservationStatuses.Approved, false)]
    public void Workflow_transitions_notify_without_duplicate_assignment_emails(string action, string before, string after, bool expected)
    {
        Assert.Equal(expected, ReservationNotificationService.ShouldNotify(action, before, after));
    }

    [Fact]
    public async Task Disabled_mode_does_not_queue_or_send_anything()
    {
        var outbox = new RecordingEmailOutbox();
        await EmailTestSupport.Service(outbox, EmailDeliveryMode.Disabled).QueueNotificationAsync(EmailTestSupport.Notification());
        Assert.Empty(outbox.Messages);
    }

    [Theory]
    [InlineData(ReservationStatuses.Approved, "Approved")]
    [InlineData(ReservationStatuses.DepartmentHeadApproval, "Awaiting Department Head approval")]
    [InlineData(ReservationStatuses.AdminApproverApproval, "Awaiting Admin Approver approval")]
    [InlineData(ReservationStatuses.Disapproved, "Disapproved")]
    [InlineData(ReservationStatuses.ForRescheduling, "Rescheduling required")]
    [InlineData(ReservationStatuses.Expired, "Expired")]
    public void Template_status_is_readable_and_optional_previews_can_be_exported(string status, string expected)
    {
        var notification = EmailTestSupport.Notification();
        notification.Reservation.Status = status;
        var title = status switch
        {
            ReservationStatuses.Approved => "Your reservation is approved",
            ReservationStatuses.Disapproved => "Your reservation was not approved",
            ReservationStatuses.Expired => "Your reservation request has expired",
            ReservationStatuses.ForRescheduling => "Your reservation needs a new schedule",
            _ => "Reservation approval requested"
        };
        notification = notification with
        {
            Title = title, Subject = title + ": " + notification.Reservation.ReservationCode,
            Message = status == ReservationStatuses.Disapproved ? "Review the approver remarks before creating another request." :
                status == ReservationStatuses.ForRescheduling ? "Choose a future schedule and resubmit your request." :
                status == ReservationStatuses.Expired ? "The requested period ended before approval was completed." :
                status == ReservationStatuses.Approved ? notification.Message : "A reservation is ready for your review. Open it to review the details and available actions."
        };
        var rendered = ReservationEmailTemplate.Render(notification, EmailTestSupport.Settings());
        Assert.Contains(expected, rendered.HtmlBody);
        Assert.Contains(expected, rendered.TextBody);
        var previewDirectory = Environment.GetEnvironmentVariable("PMS_EMAIL_PREVIEW_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(previewDirectory))
        {
            Directory.CreateDirectory(previewDirectory);
            var file = status.ToLowerInvariant().Replace(' ', '-').Replace("'", "");
            File.WriteAllText(Path.Combine(previewDirectory, file + ".html"), rendered.HtmlBody);
            File.WriteAllText(Path.Combine(previewDirectory, file + ".txt"), rendered.TextBody);
        }
    }
}

public class SmtpSettingsValidatorTests
{
    [Fact]
    public void Production_rejects_test_override_localhost_and_unencrypted_smtp()
    {
        var settings = EmailTestSupport.Settings(EmailDeliveryMode.Production);
        settings.RecipientEmail = "test@example.invalid";
        settings.ApplicationBaseUrl = "https://localhost:7035";
        settings.Encryption = EmailEncryption.None;
        var result = new SmtpSettingsValidator().Validate(null, settings);
        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures!.Count());
    }

    [Fact]
    public void Test_mode_requires_an_explicit_valid_test_inbox()
    {
        var settings = EmailTestSupport.Settings();
        settings.RecipientEmail = "";
        Assert.True(new SmtpSettingsValidator().Validate(null, settings).Failed);
    }

    [Theory]
    [InlineData(EmailDeliveryMode.Test)]
    [InlineData(EmailDeliveryMode.Production)]
    public void Valid_mode_configuration_passes(EmailDeliveryMode mode)
    {
        Assert.True(new SmtpSettingsValidator().Validate(null, EmailTestSupport.Settings(mode)).Succeeded);
    }

    [Fact]
    public void Disabled_mode_can_be_packaged_without_credentials()
    {
        Assert.True(new SmtpSettingsValidator().Validate(null, new SmtpSettings { DeliveryMode = EmailDeliveryMode.Disabled }).Succeeded);
    }
}

internal static class EmailTestSupport
{
    internal static SmtpSettings Settings(EmailDeliveryMode mode = EmailDeliveryMode.Test) => new()
    {
        DeliveryMode = mode, Server = "smtp.example.invalid", SenderEmail = "pms@example.invalid",
        RecipientEmail = mode == EmailDeliveryMode.Test ? "test-inbox@example.invalid" : "",
        ApplicationBaseUrl = "https://pms.example.invalid/pms", Username = "test-user", Password = "test-placeholder"
    };
    internal static IConfiguration Configuration(EmailDeliveryMode mode = EmailDeliveryMode.Test, string path = "outbox") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SmtpSettings:DeliveryMode"] = mode.ToString(), ["SmtpSettings:Server"] = "smtp.example.invalid",
            ["SmtpSettings:SenderEmail"] = "pms@example.invalid", ["SmtpSettings:ApplicationBaseUrl"] = "https://pms.example.invalid/pms",
            ["SmtpSettings:Username"] = "test-user", ["SmtpSettings:Password"] = "test-placeholder", ["SmtpSettings:OutboxPath"] = path,
            ["SmtpSettings:RecipientEmail"] = mode == EmailDeliveryMode.Test ? "test-inbox@example.invalid" : ""
        }).Build();
    internal static EmailService Service(IEmailOutbox outbox, EmailDeliveryMode mode = EmailDeliveryMode.Test) =>
        new(Configuration(mode), NullLogger<EmailService>.Instance, outbox);
    internal static GFMS_ReservationHeader Header(string type = "FN") => new()
    {
        ReservationId = 42, GUID = "sample-guid", ReservationCode = "RSQ-" + type + "-2026-0001",
        Requestor = "Jordan Santos", RequestorEmail = "requestor@example.invalid", Status = ReservationStatuses.Approved, ReservationType = type
    };
    internal static ReservationEmailModel Notification() => new(Header(), "requestor@example.invalid", "Approved: RSQ-FN-2026-0001",
        "Your reservation is approved", "Your reservation has been approved. Review the schedule and assigned location before your booking.",
        "Jordan Santos", new[] { new EmailDetail("Start", "Nov 9, 2026 at 7:00 AM"), new("End", "Nov 9, 2026 at 4:00 PM"), new("Attendees", "9"),
            new("Facility", "ADMIN FUNCTION ROOM"), new("Room", "FUNCTION ROOM 2"), new("Purpose", "Team meeting") });
}

internal sealed class RecordingEmailOutbox : IEmailOutbox
{
    internal List<MimeMessage> Messages { get; } = new();
    internal List<DateTime> DueDates { get; } = new();
    internal List<EmailQueueContext?> Contexts { get; } = new();
    public Task QueueAsync(MimeMessage message, EmailDeliveryMode mode, string id, DateTime notBeforeUtc, EmailQueueContext? context = null)
    {
        Messages.Add(message); DueDates.Add(notBeforeUtc); Contexts.Add(context); return Task.CompletedTask;
    }
}

internal sealed class EmailTestEnvironment : IWebHostEnvironment, IDisposable
{
    public EmailTestEnvironment() { ContentRootPath = Path.Combine(Path.GetTempPath(), "PmsEmailTests_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(ContentRootPath); WebRootPath = Path.Combine(ContentRootPath, "wwwroot"); }
    public string ApplicationName { get; set; } = "EmailTests";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; }
    public string WebRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public void Dispose()
    {
        var root = Path.GetFullPath(ContentRootPath);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (root.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("PmsEmailTests_", StringComparison.Ordinal) && Directory.Exists(root)) Directory.Delete(root, true);
    }
}
