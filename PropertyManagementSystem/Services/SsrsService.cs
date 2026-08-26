using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace PropertyManagementSystem.Services
{
    public sealed class SsrsReportException : Exception
    {
        public SsrsReportException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    public class SsrsService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SsrsService> _logger;

        public SsrsService(IConfiguration configuration, ILogger<SsrsService> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<byte[]> GetReportAsPdfAsync(
            string reportPath,
            IReadOnlyDictionary<string, string> parameters,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var configuredBaseUrl = _configuration["SsrsSettings:BaseUrl"];
                if (string.IsNullOrWhiteSpace(configuredBaseUrl))
                {
                    throw new SsrsReportException("Reporting Services is not configured.");
                }

                var ssrsBaseUrl = configuredBaseUrl.TrimEnd('/');

                var normalizedPath = reportPath.TrimStart('/').TrimEnd('/');
                var fullUrl = $"{ssrsBaseUrl}/ReportServer?/{Uri.EscapeDataString(normalizedPath)}&rs:Command=Render&rs:Format=PDF";

                if (parameters.Count > 0)
                {
                    var queryParams = parameters
                        .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
                        .Select(param => $"{Uri.EscapeDataString(param.Key)}={Uri.EscapeDataString(param.Value)}");
                    fullUrl += $"&{string.Join("&", queryParams)}";
                }

                var handler = new HttpClientHandler()
                {
                    Credentials = new NetworkCredential(
                        _configuration["SsrsSettings:Username"],
                        _configuration["SsrsSettings:Password"],
                        _configuration["SsrsSettings:Domain"]
                    ),
                    PreAuthenticate = true,
                    AllowAutoRedirect = true
                };

                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromMinutes(2);
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));

                    using var response = await client.GetAsync(
                        fullUrl,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogError(
                            "SSRS report {ReportPath} failed with status {StatusCode}. Response: {Response}",
                            reportPath,
                            (int)response.StatusCode,
                            errorContent.Length > 2000 ? errorContent[..2000] : errorContent);

                        throw new SsrsReportException(
                            "The report server could not generate this report. Verify the filters and try again.");
                    }

                    var reportBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    if (reportBytes.Length < 4 ||
                        reportBytes[0] != '%' || reportBytes[1] != 'P' ||
                        reportBytes[2] != 'D' || reportBytes[3] != 'F')
                    {
                        _logger.LogError("SSRS report {ReportPath} returned a non-PDF response.", reportPath);
                        throw new SsrsReportException("The report server returned an invalid document.");
                    }

                    return reportBytes;
                }
            }
            catch (SsrsReportException)
            {
                throw;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "SSRS report {ReportPath} timed out.", reportPath);
                throw new SsrsReportException("The report server took too long to respond. Try a shorter date range.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error generating SSRS report {ReportPath}.", reportPath);
                throw new SsrsReportException("Reporting Services is temporarily unavailable. Please try again later.", ex);
            }
        }


    }
}
