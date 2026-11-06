using System.Globalization;
using System.Net;
using System.Text;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services;

public sealed record EmailDetail(string Label, string? Value);
public sealed record ReservationEmailModel(
    GFMS_ReservationHeader Reservation, string RecipientEmail, string Subject, string Title,
    string Message, string? RecipientName = null, IReadOnlyList<EmailDetail>? Details = null,
    string ActionLabel = "View reservation");
public sealed record RenderedEmail(string Subject, string HtmlBody, string TextBody);

public static class ReservationEmailTemplate
{
    public static string ReservationUrl(GFMS_ReservationHeader reservation, string baseUrl)
    {
        var root = new Uri(baseUrl.TrimEnd('/') + "/");
        if (string.IsNullOrWhiteSpace(reservation.GUID)) return new Uri(root, "Identity/Account/Login").AbsoluteUri;
        var controller = reservation.ReservationType switch { "FN" => "FunctionRoom", "GH" => "Accommodation", "DRM" => "Dorm", _ => null };
        return new Uri(root, controller == null ? "Identity/Account/Login" :
            $"{controller}/ViewReservationDetails?Id={Uri.EscapeDataString(reservation.GUID)}").AbsoluteUri;
    }

    public static RenderedEmail Render(ReservationEmailModel model, SmtpSettings settings)
    {
        static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var reservation = model.Reservation;
        var code = string.IsNullOrWhiteSpace(reservation.ReservationCode) ? "Reservation request" : reservation.ReservationCode;
        var type = reservation.ReservationType switch { "FN" => "Function Room", "GH" => "Guesthouse", "DRM" => "Dorm & Housing", _ => "Reservation" };
        var status = reservation.Status switch
        {
            ReservationStatuses.DepartmentHeadApproval => "Awaiting Department Head approval",
            ReservationStatuses.AdminApproverApproval => "Awaiting Admin Approver approval",
            ReservationStatuses.AdminManagerApproval when reservation.ReservationType is "FN" or "GH" => "Awaiting Admin Approver approval",
            ReservationStatuses.AdminManagerApproval => "Awaiting Admin Manager approval",
            ReservationStatuses.Approved => "Approved", ReservationStatuses.Disapproved => "Disapproved",
            ReservationStatuses.ForRescheduling => "Rescheduling required", ReservationStatuses.Expired => "Expired",
            ReservationStatuses.Cancelled => "Cancelled", ReservationStatuses.Draft => "Draft",
            _ => reservation.Status ?? "Received"
        };
        var color = reservation.Status == ReservationStatuses.Approved ? "#16713d" :
            reservation.Status is ReservationStatuses.Disapproved or ReservationStatuses.Expired ? "#b42318" : "#206bc4";
        var url = ReservationUrl(reservation, settings.ApplicationBaseUrl);
        var greeting = string.IsNullOrWhiteSpace(model.RecipientName) ? "Hello," : $"Hello {model.RecipientName},";
        var details = new List<EmailDetail> { new("Reservation", code), new("Type", type), new("Status", status), new("Requestor", reservation.Requestor) };
        if (model.Details != null) details.AddRange(model.Details);
        var rows = new StringBuilder();
        var plain = new StringBuilder();
        var isTest = settings.DeliveryMode == EmailDeliveryMode.Test;
        if (isTest) plain.AppendLine($"TEST NOTIFICATION - intended recipient: {model.RecipientEmail}").AppendLine();
        plain.AppendLine(model.Title).AppendLine(code).AppendLine().AppendLine(greeting).AppendLine(model.Message).AppendLine();
        foreach (var detail in details)
        {
            var value = string.IsNullOrWhiteSpace(detail.Value) ? "Not provided" : detail.Value;
            rows.Append($"<tr><td style='width:34%;padding:12px 16px;border-bottom:1px solid #edf1f5;color:#667382;font-size:12px;font-weight:600;vertical-align:top'>{H(detail.Label)}</td><td style='padding:12px 16px;border-bottom:1px solid #edf1f5;color:#182433;font-size:14px;line-height:1.5;overflow-wrap:anywhere'>{H(value).Replace("\n", "<br/>")}</td></tr>");
            plain.AppendLine($"{detail.Label}: {value}");
        }
        plain.AppendLine().AppendLine($"{model.ActionLabel}: {url}").AppendLine().AppendLine("Property Management System - automated notification. Please do not reply.");
        var banner = isTest ? $"<tr><td style='padding:12px 28px;background:#fff4d6;color:#7a4b00;font-size:12px;line-height:1.5'><strong>TEST NOTIFICATION</strong><br/>Intended recipient: {H(model.RecipientEmail)}. This copy was redirected to the test inbox.</td></tr>" : "";
        var html = $@"<!doctype html><html lang='en'><head><meta charset='utf-8'/><meta name='viewport' content='width=device-width,initial-scale=1'/><title>{H(model.Title)}</title></head>
<body style='margin:0;padding:0;background:#f5f7fb;font-family:Arial,Helvetica,sans-serif;color:#182433'>
<div style='display:none;max-height:0;overflow:hidden;mso-hide:all'>{H(code)} - {H(model.Title)}</div>
<table role='presentation' width='100%' cellspacing='0' cellpadding='0' border='0' style='background:#f5f7fb'><tr><td align='center' style='padding:28px 12px'>
<table role='presentation' width='640' cellspacing='0' cellpadding='0' border='0' style='width:100%;max-width:640px;background:#fff;border:1px solid #e3e9f0;border-radius:8px'>
<tr><td style='padding:22px 28px;background:#19283a;color:#fff;font-size:15px;font-weight:600'>Property Management System<br/><span style='font-size:12px;font-weight:400;color:#c7d7e7'>TECO Reservations</span></td></tr>{banner}
<tr><td style='padding:28px 28px 12px'><span style='font-size:12px;color:{color};font-weight:600'>{H(status)}</span><h1 style='margin:10px 0 8px;font-size:24px;line-height:1.3;font-weight:600;color:#182433'>{H(model.Title)}</h1><p style='margin:0;color:#667382;font-size:14px'>{H(code)} - {H(type)}</p></td></tr>
<tr><td style='padding:12px 28px 20px;font-size:14px;line-height:1.65'><p style='margin:0 0 10px'>{H(greeting)}</p><p style='margin:0'>{H(model.Message).Replace("\n", "<br/>")}</p></td></tr>
<tr><td style='padding:0 28px'><table role='presentation' width='100%' cellspacing='0' cellpadding='0' border='0' style='border:1px solid #edf1f5;border-radius:6px'>{rows}</table></td></tr>
<tr><td align='left' style='padding:24px 28px'><table role='presentation' cellspacing='0' cellpadding='0' border='0'><tr><td bgcolor='#206bc4' style='border-radius:5px'><a href='{H(url)}' style='display:inline-block;padding:13px 20px;color:#fff;text-decoration:none;font-size:14px;font-weight:600'>{H(model.ActionLabel)}</a></td></tr></table><p style='margin:16px 0 0;font-size:12px;color:#667382;line-height:1.5'>If the button does not open, use this link:<br/><a href='{H(url)}' style='color:#206bc4;word-break:break-all'>{H(url)}</a></p></td></tr>
<tr><td style='padding:18px 28px;border-top:1px solid #edf1f5;color:#667382;font-size:11px;line-height:1.6'>This is an automated notification from the Property Management System. Please do not reply. Sign in to review the reservation and available actions.</td></tr>
</table></td></tr></table></body></html>";
        var subject = (isTest ? "[TEST] " : "") + model.Subject.Replace('\r', ' ').Replace('\n', ' ');
        return new RenderedEmail(subject, html, plain.ToString());
    }

    public static string FormatDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date) ||
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            ? date.ToString("MMM d, yyyy 'at' h:mm tt", CultureInfo.InvariantCulture) : value ?? "Not provided";
}
