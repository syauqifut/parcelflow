using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using ParcelFlow.Services.Reporting;

namespace ParcelFlow.Api.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly ReportService _reports;

    public ReportsController(ReportService reports)
    {
        _reports = reports;
    }

    /// <summary>
    /// Daily delivery summary for the tenant's ops team.
    /// Example: GET /api/reports/daily-summary?day=2026-07-01
    /// </summary>
    [HttpGet("daily-summary")]
    public async Task<IActionResult> DailySummary([FromQuery] DateTime day, CancellationToken ct)
    {
        if (day == default)
        {
            return BadRequest(new { error = "Provide a day, e.g. ?day=2026-07-01" });
        }

        var report = await _reports.GetDailySummaryAsync(day, ct);
        return Ok(report);
    }

    [HttpGet("weekly-summary")]
    public async Task<IActionResult> WeeklySummary([FromQuery] DateTime day, CancellationToken ct)
    {
        if (day == default)
        {
            day = DateTime.UtcNow.Date;
        }
        var report = await _reports.GetWeeklySummaryAsync(day, ct);
        var csv = BuildWeeklySummaryCsv(report);
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", "weekly-summary.csv");
    }

    private static string BuildWeeklySummaryCsv(WeeklySummaryReport report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Driver,Delivered Tasks,Failed Attempts,Average Hours");

        foreach (var row in report.Rows)
        {
            sb.AppendLine(
                $"{row.DriverName}," +
                $"{row.TaskDelivered}," +
                $"{row.TaskFailedAttempts}," +
                $"{row.AverageHoursFromAssignmentToDelivery.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        return sb.ToString();
    }
}
