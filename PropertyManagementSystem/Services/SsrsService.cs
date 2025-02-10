using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Net.Http.Headers;

namespace PropertyManagementSystem.Services
{
    public class SsrsService
    {
        private readonly IConfiguration _configuration;

        public SsrsService(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public async Task<byte[]> GetReportAsPdfAsync(string reportPath, Dictionary<string, string> parameters)
        {
            try
            {
                // Base URL for SSRS
                var ssrsBaseUrl = _configuration["SsrsSettings:BaseUrl"].TrimEnd('/');

                // Ensure the path starts with a single slash and does not contain extra slashes
                var normalizedPath = reportPath.TrimStart('/').TrimEnd('/');
                var fullUrl = $"{ssrsBaseUrl}/ReportServer?/{Uri.EscapeDataString(normalizedPath)}&rs:Command=Render&rs:Format=PDF";

                // Append query parameters
                if (parameters != null && parameters.Count > 0)
                {
                    var queryParams = parameters
                        .Select(param => $"{Uri.EscapeDataString(param.Key)}={Uri.EscapeDataString(param.Value)}");
                    fullUrl += $"&{string.Join("&", queryParams)}";
                }

                // Use HttpClientHandler to support NTLM authentication
                var handler = new HttpClientHandler()
                {
                    Credentials = new NetworkCredential(
                        _configuration["SsrsSettings:Username"], // SSRS Username
                        _configuration["SsrsSettings:Password"], // SSRS Password
                        _configuration["SsrsSettings:Domain"]    // Domain, if applicable
                    ),
                    PreAuthenticate = true,
                    AllowAutoRedirect = true
                };

                // Create HttpClient with the handler
                using (var client = new HttpClient(handler))
                {
                    // Set headers to expect PDF
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));

                    // Make the GET request to the SSRS server
                    var response = await client.GetAsync(fullUrl);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        throw new Exception($"Error fetching report. Status Code: {response.StatusCode}, Response: {errorContent}");
                    }

                    // Return the PDF content as a byte array
                    return await response.Content.ReadAsByteArrayAsync();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error generating report: {ex.Message}", ex);
            }
        }


    }
}
