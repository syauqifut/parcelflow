using LegacyCourier.Common;
using ParcelFlow.Domain.Entities;
using ParcelFlow.Domain.StateMachine;
using ParcelFlow.Tests.TestHelpers;
using Xunit;

namespace ParcelFlow.Tests;

public class ReportServiceTests
{
    [Fact]
    public async Task Daily_summary_counts_delivered_tasks()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync();
        var driver = await world.SeedDriverAsync();
        await world.SeedOpenShiftAsync(driver);
        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;
        await world.TaskService.AssignAsync(task.Id, driver.Id);
        await world.TaskService.RecordPickupAsync(task.Id);
        await world.TaskService.StartTransitAsync(task.Id);
        await world.TaskService.MarkDeliveredAsync(task.Id, null);

        var report = await world.ReportService.GetDailySummaryAsync(world.Clock.UtcNow.Date);

        Assert.Equal(1, report.TotalDelivered);
        var row = Assert.Single(report.Rows);
        Assert.Equal(parcel.Reference, row.ParcelReference);
        Assert.Equal(DeliveryTaskStatus.Delivered.ToString(), row.Status);
    }

    [Fact]
    public async Task Daily_summary_counts_failed_attempts()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync();
        var driver = await world.SeedDriverAsync();
        await world.SeedOpenShiftAsync(driver);
        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;
        await world.TaskService.AssignAsync(task.Id, driver.Id);
        await world.TaskService.RecordPickupAsync(task.Id);
        await world.TaskService.StartTransitAsync(task.Id);
        await world.TaskService.RecordFailedAttemptAsync(task.Id, "recipient absent");

        var report = await world.ReportService.GetDailySummaryAsync(world.Clock.UtcNow.Date);

        Assert.Equal(1, report.TotalFailedAttempts);
    }

    [Fact]
    public async Task Daily_summary_is_scoped_to_requesting_tenant()
    {
        using var world = new TestWorld("tenant-a");
        var day = world.Clock.UtcNow.Date;

        var parcel = await world.SeedParcelAsync(reference: "A-001");
        var driver = await world.SeedDriverAsync(name: "Driver A");
        await world.SeedOpenShiftAsync(driver);
        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;
        await world.TaskService.AssignAsync(task.Id, driver.Id);
        await world.TaskService.RecordPickupAsync(task.Id);
        await world.TaskService.StartTransitAsync(task.Id);
        await world.TaskService.MarkDeliveredAsync(task.Id, null);

        var otherParcel = await world.SeedParcelAsync("tenant-b", "B-001", "Manila");
        var otherTask = new DeliveryTask
        {
            Id = IdGenerator.NewId("task"),
            TenantId = "tenant-b",
            ParcelId = otherParcel.Id,
            Status = DeliveryTaskStatus.Delivered,
            UpdatedUtc = day.AddHours(10)
        };
        await world.Tasks.UpsertAsync(otherTask);

        var report = await world.ReportService.GetDailySummaryAsync(day);

        Assert.Equal(1, report.TotalDelivered);
        var row = Assert.Single(report.Rows);
        Assert.Equal("A-001", row.ParcelReference);
        Assert.DoesNotContain(report.Rows, r => r.ParcelReference == "B-001");
    }

    [Fact]
    public async Task Weekly_summary_aggregates_delivered_tasks_per_driver()
    {
        using var world = new TestWorld();
        var reportDay = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc);

        var parcelA = await world.SeedParcelAsync(reference: "P-A");
        var parcelB = await world.SeedParcelAsync(reference: "P-B");
        var parcelC = await world.SeedParcelAsync(reference: "P-C");
        var alice = await world.SeedDriverAsync(name: "Alice");
        var bob = await world.SeedDriverAsync(name: "Bob");

        await DeliverTaskAsync(world, parcelA.Id, alice);
        await DeliverTaskAsync(world, parcelB.Id, alice);
        await DeliverTaskAsync(world, parcelC.Id, bob);

        var report = await world.ReportService.GetWeeklySummaryAsync(reportDay);

        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), report.FromDayUtc);
        Assert.Equal(reportDay, report.ToDayUtc);
        Assert.Equal(2, report.Rows.Count);

        var aliceRow = Assert.Single(report.Rows, r => r.DriverName == "Alice");
        Assert.Equal(2, aliceRow.TaskDelivered);
        Assert.Equal(0, aliceRow.TaskFailedAttempts);

        var bobRow = Assert.Single(report.Rows, r => r.DriverName == "Bob");
        Assert.Equal(1, bobRow.TaskDelivered);
    }

    [Fact]
    public async Task Weekly_summary_sums_failed_attempts_for_delivered_tasks()
    {
        using var world = new TestWorld();
        var reportDay = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc);
        var parcel = await world.SeedParcelAsync();
        var driver = await world.SeedDriverAsync();
        await world.SeedOpenShiftAsync(driver);

        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;
        await world.TaskService.AssignAsync(task.Id, driver.Id);
        await world.TaskService.RecordPickupAsync(task.Id);
        await world.TaskService.StartTransitAsync(task.Id);
        await world.TaskService.RecordFailedAttemptAsync(task.Id, "recipient absent");
        await world.TaskService.RetryAsync(task.Id);
        await world.TaskService.MarkDeliveredAsync(task.Id, null);

        var report = await world.ReportService.GetWeeklySummaryAsync(reportDay);

        var row = Assert.Single(report.Rows);
        Assert.Equal(1, row.TaskDelivered);
        Assert.Equal(1, row.TaskFailedAttempts);
    }

    [Fact]
    public async Task Weekly_summary_calculates_average_hours_from_assignment_to_delivery()
    {
        using var world = new TestWorld();
        var reportDay = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc);
        world.Clock.UtcNow = new DateTime(2026, 7, 5, 8, 0, 0, DateTimeKind.Utc);

        var parcel = await world.SeedParcelAsync();
        var driver = await world.SeedDriverAsync();
        await world.SeedOpenShiftAsync(driver);

        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;
        await world.TaskService.AssignAsync(task.Id, driver.Id);
        await world.TaskService.RecordPickupAsync(task.Id);
        await world.TaskService.StartTransitAsync(task.Id);

        world.Clock.UtcNow = new DateTime(2026, 7, 5, 11, 30, 0, DateTimeKind.Utc);
        await world.TaskService.MarkDeliveredAsync(task.Id, null);

        var report = await world.ReportService.GetWeeklySummaryAsync(reportDay);

        var row = Assert.Single(report.Rows);
        Assert.Equal(3, row.AverageHoursFromAssignmentToDelivery);
    }

    [Fact]
    public async Task Weekly_summary_excludes_deliveries_outside_the_seven_day_window()
    {
        using var world = new TestWorld("tenant-a");
        var reportDay = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc);
        var driver = await world.SeedDriverAsync(name: "In-window Driver");
        var inWindowParcel = await world.SeedParcelAsync(reference: "IN-WINDOW");
        await DeliverTaskAsync(world, inWindowParcel.Id, driver);

        var outOfWindowParcel = await world.SeedParcelAsync(reference: "OUT-OF-WINDOW");
        await world.Tasks.UpsertAsync(new DeliveryTask
        {
            Id = IdGenerator.NewId("task"),
            TenantId = world.TenantId,
            ParcelId = outOfWindowParcel.Id,
            DriverId = driver.Id,
            Status = DeliveryTaskStatus.Delivered,
            DeliveredUtc = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc),
            AssignedUtc = new DateTime(2026, 6, 30, 8, 0, 0, DateTimeKind.Utc),
            CreatedUtc = new DateTime(2026, 6, 30, 8, 0, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc)
        });

        var report = await world.ReportService.GetWeeklySummaryAsync(reportDay);

        var row = Assert.Single(report.Rows);
        Assert.Equal(1, row.TaskDelivered);
    }

    [Fact]
    public async Task Weekly_summary_is_scoped_to_requesting_tenant()
    {
        using var world = new TestWorld("tenant-a");
        var reportDay = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc);
        var driver = await world.SeedDriverAsync(name: "Driver A");
        var parcel = await world.SeedParcelAsync(reference: "A-001");
        await DeliverTaskAsync(world, parcel.Id, driver);

        var otherParcel = await world.SeedParcelAsync("tenant-b", "B-001", "Manila");
        var otherDriver = await world.SeedDriverAsync("tenant-b", "Driver B");
        await world.Tasks.UpsertAsync(new DeliveryTask
        {
            Id = IdGenerator.NewId("task"),
            TenantId = "tenant-b",
            ParcelId = otherParcel.Id,
            DriverId = otherDriver.Id,
            Status = DeliveryTaskStatus.Delivered,
            DeliveredUtc = new DateTime(2026, 7, 5, 10, 0, 0, DateTimeKind.Utc),
            AssignedUtc = new DateTime(2026, 7, 5, 8, 0, 0, DateTimeKind.Utc),
            CreatedUtc = new DateTime(2026, 7, 5, 8, 0, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 7, 5, 10, 0, 0, DateTimeKind.Utc)
        });

        var report = await world.ReportService.GetWeeklySummaryAsync(reportDay);

        var row = Assert.Single(report.Rows);
        Assert.Equal("Driver A", row.DriverName);
        Assert.Equal(1, row.TaskDelivered);
    }

    private static async Task DeliverTaskAsync(TestWorld world, string parcelId, Driver driver)
    {
        await world.SeedOpenShiftAsync(driver);
        var task = (await world.TaskService.CreateForParcelAsync(parcelId)).Value!;
        await world.TaskService.AssignAsync(task.Id, driver.Id);
        await world.TaskService.RecordPickupAsync(task.Id);
        await world.TaskService.StartTransitAsync(task.Id);
        await world.TaskService.MarkDeliveredAsync(task.Id, null);
    }
}
