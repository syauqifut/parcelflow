using ParcelFlow.Domain.Events;
using ParcelFlow.Events.Actions;

namespace ParcelFlow.Events.Rules;

/// <summary>
/// When a parcel is scheduled for return to sender, alert the tenant's ops channel.
/// </summary>
public sealed class ReturnScheduledOpsAlertRule : IEventRule
{
    private readonly OpsWebhookAction _opsWebhook;

    public ReturnScheduledOpsAlertRule(OpsWebhookAction opsWebhook)
    {
        _opsWebhook = opsWebhook;
    }

    public string Name => "return-scheduled-ops-alert";

    public bool AppliesTo(IDomainEvent domainEvent) => domainEvent is ReturnScheduledEvent;

    public async Task ExecuteAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        var evt = (ReturnScheduledEvent)domainEvent;

        await _opsWebhook.SendAsync(
            evt.TenantId,
            "ops-alerts",
            $"Task {evt.Task.Id} scheduled for return to sender after 3 failed delivery attempts (reason: {evt.Reason}).",
            ct);
    }
}
