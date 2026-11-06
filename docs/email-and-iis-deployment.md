# Reservation email and IIS deployment

The email changes cover Function Room and Guesthouse submissions, approval progress, final and automatic approval, rejection, cancellation, guest room assignment completion, and rescheduling/expiration notices. Existing requests are not emailed retroactively.

## What recipients receive

Every notification has a consistent header, reservation code and type, readable status, summary table, and a button linking to the actual reservation. A plain-text alternative is included. Names, purpose and remarks are HTML-encoded. Email links do not grant access: users must sign in and pass the application's normal reservation permissions.

| Event | Recipient | Timing |
| --- | --- | --- |
| Submission/resubmission | Requestor | Queued immediately after the saved transition |
| Department Head review required | Assigned Department Head email | When processing eligibility begins |
| Final Admin Approver review required | Accounts assigned `GFMS.Admin Approver`, using their email or email-form username | When processing eligibility begins |
| Approval step completes | Requestor; next-stage approvers if applicable | Requestor update immediately; next-stage notices respect eligibility |
| Admin Approver submits an automatically approved request | Requestor | Approval confirmation; no Department Head notification |
| Rejection/cancellation | Requestor | After saving the transition |
| Approved Guesthouse rooms are saved | Requestor | After assignments are committed |
| Expiration/rescheduling | Requestor | After the status change is committed |

The existing cutoff remains **after** 4:00 PM, using submission time. Exactly 4:00 PM is eligible that day. Assigned Admin Approver and Admin Manager accounts retain the existing cutoff exemption. This is separate from the active-role rule allowing only Admin Approvers to book for today. The application currently uses Windows local time for its business rules; configure the IIS server for the intended business timezone before use.

## Delivery modes

| `SmtpSettings:DeliveryMode` | Behavior |
| --- | --- |
| `Test` | All messages go only to `SmtpSettings:RecipientEmail`. Subjects begin `[TEST]`, and bodies show the intended recipient. This remains the development configuration. |
| `Production` | Each message goes to its intended recipient. A leftover `RecipientEmail` override, localhost URL, non-HTTPS application URL, or unencrypted SMTP setting prevents startup. |
| `Disabled` | No new email is queued or sent. Existing queue entries remain on disk. The IIS package starts in this mode. |

Delivery mode is independent of `ASPNETCORE_ENVIRONMENT`: keep `ASPNETCORE_ENVIRONMENT=Production` on the IIS test site while using email `Test` mode. Never expose Development error pages to production users.

Example non-secret email configuration:

```json
"SmtpSettings": {
  "DeliveryMode": "Test",
  "ApplicationBaseUrl": "https://pms-test.example.com",
  "Server": "smtp.example.com",
  "Port": 587,
  "SenderName": "Property Management System",
  "SenderEmail": "pms@example.com",
  "RecipientEmail": "pms-testing@example.com",
  "Encryption": "StartTls",
  "UseAuthentication": true,
  "TimeoutSeconds": 30,
  "MaxAttempts": 5,
  "RetryDelaySeconds": 60,
  "PollIntervalSeconds": 30,
  "OutboxPath": "C:\\ProgramData\\TECO\\PMS\\EmailOutbox"
}
```

Set username/password and the database connection on the server, rather than checking them into JSON or copying development settings. Use `SslOnConnect` for an SMTP provider requiring implicit TLS (usually port 465); use `StartTls` for providers requiring STARTTLS (usually 587). `None` is limited to test mode for a controlled local relay. The SMTP provider must support the configured authentication method; password-disabled providers may require an approved app password or a separate OAuth transport implementation.

The tracked `PropertyManagementSystem/appsettings.json` contains sanitized defaults with delivery Disabled. Developer settings are loaded from the optional, ignored `appsettings.Local.json`; existing local settings have been preserved there. Rebuild/restart the application after this change so that it loads the local override. Environment variables and command-line arguments take precedence over this file. The local override is excluded from all publish output. Never copy it to the production server or commit it.

## Durable queue and monitoring

The background worker reads the private outbox every 30 seconds by default. Transient failures retry after 1, 2, 4, and 8 minutes with the default five-attempt limit; delays are capped at one hour. SMTP failures never roll back a completed reservation.

- `Pending`: queued messages and retry state, including HTML/plain-text MIME content. Dates are persisted in UTC.
- `Sent`: SMTP acceptance receipts; message bodies are removed after success. SMTP acceptance does not prove inbox placement or that a user opened the message.
- `Failed`: messages that exhausted retries, plus malformed queue entries. Review logs and fix the cause before a controlled manual requeue.
- `Held`: entries created in a different delivery mode, or an SMTP-accepted message whose receipt could not be persisted. Review before any resend; a test message is never automatically converted into a production delivery.
- `Superseded`: approval requests whose approval stage or submission has changed before sending. They are not delivered.

Queued messages retain one Message-ID across retries. File leases prevent two IIS worker processes from sending the same queue item concurrently. SMTP cannot offer exactly-once delivery: a process crash or lost network acknowledgment after server acceptance can still produce a duplicate. There is also a short gap between the reservation database commit and queue creation; enqueue failures are logged with the audit event ID. Reconcile these errors manually. For multiple application servers or stronger transaction guarantees, migrate to a SQL-backed transactional outbox before scaling beyond this single-server deployment.

New queue entries store serialized MIME bytes as Base64 to preserve the declared charset, names, and currency symbols. Existing text-format entries remain readable. Both content fields are cleared from SMTP-accepted receipts.

Monitor application logs and the `Failed`/`Held` directories. Restrict the outbox to the IIS application identity and administrators; its pending and failed files contain booking information. Never place it under `wwwroot` or a publicly served directory. Set retention/backup policies for runtime email and reservation audit records. Existing audit events are not replayed automatically.

## Prepare the IIS package

**Runtime and dependency upgrades before production:** this project still targets `net6.0`. .NET 6 support ended November 12, 2024. Plan and test an upgrade to a supported LTS version, including EF Core, Telerik, and reporting dependencies. MailKit has been updated from 4.7.0 to the patched 4.16.0 release to address [GHSA-9j88-vvj5-vhgr](https://github.com/advisories/GHSA-9j88-vvj5-vhgr). Installing a newer Hosting Bundle alone does not upgrade a framework-dependent .NET 6 application. The publish script prepares the current project; it does not perform this migration. See the [official .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

From a development machine with restored packages and the required Telerik feed access:

```powershell
dotnet restore PropertyManagementSystem.sln
.\scripts\Publish-Iis.ps1
```

The script creates a new timestamped directory under `artifacts/iis/`. It builds Release output, excludes local `appsettings*.json` and runtime `App_Data`, and supplies a sanitized configuration with email **Disabled**. It does not deploy to a server, change a database, or send email. No local SMTP/database/SSRS/JWT secrets are copied into the package. Review the package before transferring it.

If company policy blocks unsigned PowerShell scripts, use this equivalent command without changing execution policy:

```powershell
dotnet publish PropertyManagementSystem/PropertyManagementSystem.csproj -c Release --no-restore -p:SanitizePublish=true -p:UseAppHost=false -o artifacts/iis/release-001
```

Use a new output directory for each release. The project publish target supplies the sanitized settings and this guide, and rejects a destination already containing settings or runtime data.

## Configure Windows Server / IIS

1. Install IIS and the ASP.NET Core Hosting Bundle matching the **upgraded, tested runtime target**. Follow Microsoft's [IIS hosting instructions](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/). Publish output includes the generated `web.config`; deploy the full publish directory.
2. Create a dedicated application pool using **No Managed Code**, with the correct architecture. Set Maximum Worker Processes to 1 for this deployment. Use a dedicated service identity if your SQL/SMTP/asset infrastructure requires Windows credentials.
3. Create the IIS site/application and an HTTPS binding with a trusted certificate. If using a virtual application such as `/pms`, include that path in `ApplicationBaseUrl`.
4. Give the app pool identity Read/Execute permission on application files. Give it Modify permission on the external email outbox and the application's `App_Data` audit folder. Preserve runtime folders across deployments.
5. This application currently stores Data Protection keys at `C:\Keys\` and uses the application name `SharedCookieApp`. Provision and protect that directory and give the required identity access. If SSO shares cookies with another application, keep the key store/application name/cookie domain compatible; do not replace those keys during publishing.
6. Configure app-pool-scoped environment variables on the server, or use a server-managed secrets/configuration provider. Environment keys use double underscores for nested configuration, for example `SmtpSettings__Password`. Keep secret values out of the deployed repository/package. The IIS app-pool environment-variable collection is documented [here](https://learn.microsoft.com/en-us/iis/configuration/system.applicationhost/applicationpools/add/environmentvariables/); ASP.NET Core's configuration precedence is documented [here](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/).

| Server variable | Value to provide |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` | `Production` |
| `ConnectionStrings__ApplicationDbContextConnection` | Production database connection; provision the schema separately |
| `AllowedHosts` | Your site's actual host name(s) |
| `Domain` | The actual shared-cookie domain if required by your SSO setup |
| `SmtpSettings__DeliveryMode` | Start with `Test` |
| `SmtpSettings__ApplicationBaseUrl` | Actual HTTPS test or production application URL |
| `SmtpSettings__Server`, `SmtpSettings__Port` | Provider-approved SMTP host/port |
| `SmtpSettings__SenderEmail`, `SmtpSettings__SenderName` | Approved sender address/display name |
| `SmtpSettings__Username`, `SmtpSettings__Password` | Server-managed SMTP credentials |
| `SmtpSettings__RecipientEmail` | Test inbox while in Test mode; remove this override before Production |
| `SmtpSettings__Encryption` | `StartTls` or `SslOnConnect` as required by the provider |
| `SmtpSettings__OutboxPath` | Persistent private directory outside the application release folder |
| `SsrsSettings__*`, `AssetServer`, `Jwt__*` | Your reporting, asset and SSO configuration if those features are used |

7. For the email and schedule background workers, install IIS Application Initialization, enable site/application preloading, set the pool's Start Mode to **AlwaysRunning**, and prevent idle shutdown. With an idle/stopped pool, queued messages remain safe but cannot be delivered on time. The first scheduled notice after midnight is processed on the next worker poll, not guaranteed at exactly 00:00:00.
8. Set the server's business timezone appropriately, allow outbound SMTP traffic, and confirm SQL, SSRS and asset access under the actual application-pool identity. Rotate credentials that were previously committed to source control before using production accounts. Configure SPF/DKIM/DMARC with your mail administrator for the approved sender domain.

## Test and then switch to production

On the IIS test site, use a test database and email `Test` mode. Create fresh Function Room and Guesthouse requests: normal requestor, Admin Approver auto-approval, rejection with remarks, cancellation, room assignment, and resubmission. Confirm both rendered bodies and intended-recipient banners. For a non-exempt request submitted after 4 PM, verify the requestor receipt is immediate and the approver notice waits until the next day. Verify recipients still need permission when opening links. Use real Outlook/webmail/mobile clients for final email rendering and junk-folder checks; automated tests use fake SMTP and do not establish actual inbox delivery.

Before live use, finish the runtime upgrade, complete these checks, back up SQL and runtime state, and confirm the approved production sender. Set `SmtpSettings__DeliveryMode=Production`, remove `SmtpSettings__RecipientEmail`, set the production `ApplicationBaseUrl`, and recycle the application pool. Old test-mode queue entries move to `Held`; they are never rerouted to real users. New production messages use the real requestor/approver addresses without a code change.

Use the same sanitized publish script for future releases. Stage the new files, briefly stop the application pool (or use `app_offline.htm`), replace application binaries while preserving runtime state and server configuration, then restart and check logs. Review database migrations separately; this script deliberately does not run them.

No deployment or live email send is performed by preparing this documentation or by the automated test suite.
