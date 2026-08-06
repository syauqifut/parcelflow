using Microsoft.Extensions.Logging;
using ParcelFlow.Domain.Entities;
using ParcelFlow.Domain.Events;
using ParcelFlow.Domain.StateMachine;
using ParcelFlow.Events.Actions;
using ParcelFlow.Events.Rules;
using ParcelFlow.Tests.TestHelpers;
using Xunit;

namespace ParcelFlow.Tests;

public class ReturnScheduledNotificationRulesTests
{
    [Fact]
    public void Recipient_rule_applies_only_to_return_scheduled_event()
    {
        var rule = new RecipientReturnScheduledNotificationRule(null!, null!);

        Assert.True(rule.AppliesTo(CreateReturnScheduledEvent()));
        Assert.False(rule.AppliesTo(new TaskDeliveredEvent
        {
            TenantId = "t1",
            OccurredUtc = DateTime.UtcNow,
            Task = new DeliveryTask { Id = "task_1", TenantId = "t1" }
        }));
    }

    [Fact]
    public void Ops_rule_applies_only_to_return_scheduled_event()
    {
        var rule = new ReturnScheduledOpsAlertRule(null!);

        Assert.True(rule.AppliesTo(CreateReturnScheduledEvent()));
        Assert.False(rule.AppliesTo(new DeliveryAttemptFailedEvent
        {
            TenantId = "t1",
            OccurredUtc = DateTime.UtcNow,
            Task = new DeliveryTask { Id = "task_1", TenantId = "t1" },
            AttemptNumber = 3,
            Reason = "absent"
        }));
    }

    [Fact]
    public async Task Recipient_rule_sends_sms_to_parcel_recipient()
    {
        using var world = new TestWorld();
        var parcel = await world.SeedParcelAsync(reference: "NE-12345");
        var task = new DeliveryTask
        {
            Id = "task_1",
            TenantId = world.TenantId,
            ParcelId = parcel.Id,
            Status = DeliveryTaskStatus.ReturnScheduled
        };

        var logger = new CapturingLogger<SmsNotificationAction>();
        var rule = new RecipientReturnScheduledNotificationRule(world.Parcels, new SmsNotificationAction(logger));

        await rule.ExecuteAsync(CreateReturnScheduledEvent(world.TenantId, task, "recipient absent"), CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(parcel.RecipientPhone, entry.Message);
        Assert.Contains("NE-12345", entry.Message);
        Assert.Contains("being returned to the sender", entry.Message);
    }

    [Fact]
    public async Task Ops_rule_posts_return_scheduled_alert()
    {
        using var world = new TestWorld();
        var task = new DeliveryTask
        {
            Id = "task_99",
            TenantId = world.TenantId,
            ParcelId = "parcel_1",
            Status = DeliveryTaskStatus.ReturnScheduled
        };

        var logger = new CapturingLogger<OpsWebhookAction>();
        var rule = new ReturnScheduledOpsAlertRule(new OpsWebhookAction(logger));

        await rule.ExecuteAsync(CreateReturnScheduledEvent(world.TenantId, task, "address not found"), CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("task_99", entry.Message);
        Assert.Contains("scheduled for return to sender", entry.Message);
        Assert.Contains("address not found", entry.Message);
    }

    [Fact]
    public void Repeated_failure_ops_alert_fires_only_on_second_attempt()
    {
        var rule = new RepeatedFailureOpsAlertRule(null!);

        Assert.True(rule.AppliesTo(new DeliveryAttemptFailedEvent
        {
            TenantId = "t1",
            OccurredUtc = DateTime.UtcNow,
            Task = new DeliveryTask { Id = "task_1", TenantId = "t1" },
            AttemptNumber = 2,
            Reason = "absent"
        }));
        Assert.False(rule.AppliesTo(new DeliveryAttemptFailedEvent
        {
            TenantId = "t1",
            OccurredUtc = DateTime.UtcNow,
            Task = new DeliveryTask { Id = "task_1", TenantId = "t1" },
            AttemptNumber = 3,
            Reason = "absent"
        }));
    }

    private static ReturnScheduledEvent CreateReturnScheduledEvent(
        string tenantId = "test-tenant",
        DeliveryTask? task = null,
        string reason = "recipient absent")
    {
        return new ReturnScheduledEvent
        {
            TenantId = tenantId,
            OccurredUtc = DateTime.UtcNow,
            Task = task ?? new DeliveryTask { Id = "task_1", TenantId = tenantId, ParcelId = "parcel_1" },
            Reason = reason
        };
    }
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
    }
}
