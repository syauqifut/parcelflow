using ParcelFlow.Domain.Events;
using ParcelFlow.Domain.StateMachine;
using ParcelFlow.Tests.TestHelpers;
using Xunit;

namespace ParcelFlow.Tests;

public class DeliveryTaskServiceTests
{
    [Fact]
    public async Task Creates_task_for_parcel()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync();

        var result = await world.TaskService.CreateForParcelAsync(parcel.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryTaskStatus.Created, result.Value!.Status);
        Assert.Equal(world.TenantId, result.Value.TenantId);
    }

    [Fact]
    public async Task Rejects_second_active_task_for_same_parcel()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync();
        await world.TaskService.CreateForParcelAsync(parcel.Id);

        var second = await world.TaskService.CreateForParcelAsync(parcel.Id);

        Assert.False(second.IsSuccess);
    }

    [Fact]
    public async Task Full_happy_path_reaches_delivered_and_emits_event()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync();
        var driver = await world.SeedDriverAsync();
        await world.SeedOpenShiftAsync(driver);
        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;

        Assert.True((await world.TaskService.AssignAsync(task.Id, driver.Id)).IsSuccess);
        Assert.True((await world.TaskService.RecordPickupAsync(task.Id)).IsSuccess);
        Assert.True((await world.TaskService.StartTransitAsync(task.Id)).IsSuccess);
        var delivered = await world.TaskService.MarkDeliveredAsync(task.Id, "left at reception");

        Assert.True(delivered.IsSuccess);
        Assert.Equal(DeliveryTaskStatus.Delivered, delivered.Value!.Status);
        Assert.NotNull(delivered.Value.DeliveredUtc);
        Assert.Contains(world.Events.Dispatched, e => e is TaskDeliveredEvent);
    }

    [Fact]
    public async Task Failed_attempt_increments_count_and_emits_event()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync();
        var driver = await world.SeedDriverAsync();
        await world.SeedOpenShiftAsync(driver);
        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;
        await world.TaskService.AssignAsync(task.Id, driver.Id);
        await world.TaskService.RecordPickupAsync(task.Id);
        await world.TaskService.StartTransitAsync(task.Id);

        var failed = await world.TaskService.RecordFailedAttemptAsync(task.Id, "recipient absent");

        Assert.True(failed.IsSuccess);
        Assert.Equal(1, failed.Value!.AttemptCount);
        var evt = Assert.IsType<DeliveryAttemptFailedEvent>(
            Assert.Single(world.Events.Dispatched, e => e is DeliveryAttemptFailedEvent));
        Assert.Equal(1, evt.AttemptNumber);
    }

    [Fact]
    public async Task Retry_puts_failed_task_back_in_transit()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync();
        var driver = await world.SeedDriverAsync();
        await world.SeedOpenShiftAsync(driver);
        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;
        await world.TaskService.AssignAsync(task.Id, driver.Id);
        await world.TaskService.RecordPickupAsync(task.Id);
        await world.TaskService.StartTransitAsync(task.Id);
        await world.TaskService.RecordFailedAttemptAsync(task.Id, "address not found");

        var retried = await world.TaskService.RetryAsync(task.Id);

        Assert.True(retried.IsSuccess);
        Assert.Equal(DeliveryTaskStatus.InTransit, retried.Value!.Status);
        Assert.Equal(1, retried.Value.AttemptCount);
    }

    [Fact]
    public async Task Cannot_deliver_a_task_that_was_never_picked_up()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync();
        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;

        var result = await world.TaskService.MarkDeliveredAsync(task.Id, null);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Cannot_assign_a_driver_with_no_open_shift()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync();
        var driver = await world.SeedDriverAsync();
        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;

        var result = await world.TaskService.AssignAsync(task.Id, driver.Id);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Task_from_another_tenant_is_invisible()
    {
        using var world = new TestWorld();
        var foreignParcel = await world.SeedParcelAsync(tenantId: "other-tenant");

        var result = await world.TaskService.CreateForParcelAsync(foreignParcel.Id);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Third_failed_attempt_schedules_return()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync();
        var driver = await world.SeedDriverAsync();
        await world.SeedOpenShiftAsync(driver);
        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;
        await world.TaskService.AssignAsync(task.Id, driver.Id);
        await world.TaskService.RecordPickupAsync(task.Id);
        await world.TaskService.StartTransitAsync(task.Id);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await world.TaskService.RecordFailedAttemptAsync(task.Id, $"failure {attempt}");
            await world.TaskService.RetryAsync(task.Id);
        }

        var thirdFailure = await world.TaskService.RecordFailedAttemptAsync(task.Id, "failure 3");

        Assert.True(thirdFailure.IsSuccess);
        Assert.Equal(DeliveryTaskStatus.ReturnScheduled, thirdFailure.Value!.Status);
        Assert.Equal(3, thirdFailure.Value.AttemptCount);
        var history = thirdFailure.Value.History;
        Assert.Equal(DeliveryTaskStatus.InTransit, history[^2].From);
        Assert.Equal(DeliveryTaskStatus.AttemptFailed, history[^2].To);
        Assert.Equal(DeliveryTaskStatus.AttemptFailed, history[^1].From);
        Assert.Equal(DeliveryTaskStatus.ReturnScheduled, history[^1].To);
    }

    [Fact]
    public async Task Retry_rejected_after_max_delivery_attempts()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync();
        var driver = await world.SeedDriverAsync();
        await world.SeedOpenShiftAsync(driver);
        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;
        await world.TaskService.AssignAsync(task.Id, driver.Id);
        await world.TaskService.RecordPickupAsync(task.Id);
        await world.TaskService.StartTransitAsync(task.Id);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await world.TaskService.RecordFailedAttemptAsync(task.Id, $"failure {attempt}");
            await world.TaskService.RetryAsync(task.Id);
        }

        await world.TaskService.RecordFailedAttemptAsync(task.Id, "failure 3");
        var retry = await world.TaskService.RetryAsync(task.Id);

        Assert.False(retry.IsSuccess);
    }

    [Fact]
    public async Task Complete_return_moves_task_to_returned_terminal_state()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync();
        var driver = await world.SeedDriverAsync();
        await world.SeedOpenShiftAsync(driver);
        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;
        await world.TaskService.AssignAsync(task.Id, driver.Id);
        await world.TaskService.RecordPickupAsync(task.Id);
        await world.TaskService.StartTransitAsync(task.Id);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await world.TaskService.RecordFailedAttemptAsync(task.Id, $"failure {attempt}");
            await world.TaskService.RetryAsync(task.Id);
        }

        await world.TaskService.RecordFailedAttemptAsync(task.Id, "failure 3");
        var completed = await world.TaskService.CompleteReturnAsync(task.Id);

        Assert.True(completed.IsSuccess);
        Assert.Equal(DeliveryTaskStatus.Returned, completed.Value!.Status);
        Assert.NotNull(completed.Value.ReturnedUtc);
    }

    [Fact]
    public async Task Cannot_complete_return_from_non_scheduled_status()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync();
        var driver = await world.SeedDriverAsync();
        await world.SeedOpenShiftAsync(driver);
        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;
        await world.TaskService.AssignAsync(task.Id, driver.Id);
        await world.TaskService.RecordPickupAsync(task.Id);
        await world.TaskService.StartTransitAsync(task.Id);

        var result = await world.TaskService.CompleteReturnAsync(task.Id);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task New_task_allowed_after_previous_task_returned()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync();
        var driver = await world.SeedDriverAsync();
        await world.SeedOpenShiftAsync(driver);
        var task = (await world.TaskService.CreateForParcelAsync(parcel.Id)).Value!;
        await world.TaskService.AssignAsync(task.Id, driver.Id);
        await world.TaskService.RecordPickupAsync(task.Id);
        await world.TaskService.StartTransitAsync(task.Id);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await world.TaskService.RecordFailedAttemptAsync(task.Id, $"failure {attempt}");
            await world.TaskService.RetryAsync(task.Id);
        }

        await world.TaskService.RecordFailedAttemptAsync(task.Id, "failure 3");
        await world.TaskService.CompleteReturnAsync(task.Id);

        var secondTask = await world.TaskService.CreateForParcelAsync(parcel.Id);

        Assert.True(secondTask.IsSuccess);
        Assert.Equal(DeliveryTaskStatus.Created, secondTask.Value!.Status);
    }
}
