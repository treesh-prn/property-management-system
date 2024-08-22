using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

public class SsrsService
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public SsrsService(IConfiguration configuration, HttpClient httpClient)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<string> GetReportUrlAsync(string reportName, int reservationId)
    {
        var baseUrl = _configuration["SsrsSettings:BaseUrl"];
        var username = _configuration["SsrsSettings:Username"];
        var password = _configuration["SsrsSettings:Password"];

        var reportUrl = $"{baseUrl}/Pages/ReportViewer.aspx?%2fReports%2f{Uri.EscapeDataString(reportName)}&rs:Command=Render&ReservationId={reservationId}";
        Console.WriteLine(reportUrl);

        try
        {
            var handler = new HttpClientHandler
            {
                Credentials = new System.Net.NetworkCredential(username, password)
            };

            using (var client = new HttpClient(handler))
            {
                var response = await client.GetAsync(reportUrl);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Failed to reach SSRS server. Status Code: {response.StatusCode}, Error: {errorContent}");
                }
            }
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"An error occurred: {e.Message}");
        }

        return reportUrl;
    }
}
