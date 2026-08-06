using System.Text;
using Microsoft.AspNetCore.Mvc;
using ParcelFlow.Api.Controllers;
using ParcelFlow.Tests.TestHelpers;
using Xunit;

namespace ParcelFlow.Tests;

public class ReportsControllerTests
{
    [Fact]
    public async Task Weekly_summary_returns_csv_file_with_headers_and_driver_rows()
    {
        using var world = new TestWorld();
        var reportDay = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc);

        var parcel = await world.SeedParcelAsync();
        var driver = await world.SeedDriverAsync(name: "Alice");
        await world.SeedOpenShiftAsync(driver);
        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;
        await world.TaskService.AssignAsync(task.Id, driver.Id);
        await world.TaskService.RecordPickupAsync(task.Id);
        await world.TaskService.StartTransitAsync(task.Id);
        await world.TaskService.MarkDeliveredAsync(task.Id, null);

        var controller = new ReportsController(world.ReportService);
        var result = await controller.WeeklySummary(reportDay, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal("weekly-summary.csv", file.FileDownloadName);

        var csv = Encoding.UTF8.GetString(file.FileContents);
        var lines = csv.TrimEnd().Split(Environment.NewLine);

        Assert.Equal("Driver,Delivered Tasks,Failed Attempts,Average Hours", lines[0]);
        Assert.Equal("Alice,1,0,0.00", lines[1]);
    }
}
