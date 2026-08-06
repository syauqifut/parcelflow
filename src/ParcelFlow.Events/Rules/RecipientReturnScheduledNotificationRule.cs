using ParcelFlow.Domain.Entities;
using ParcelFlow.Domain.Events;
using ParcelFlow.Events.Actions;
using ParcelFlow.Storage;

namespace ParcelFlow.Events.Rules;

/// <summary>
/// When a parcel is scheduled for return after repeated delivery failures,
/// notify the recipient by SMS.
/// </summary>
public sealed class RecipientReturnScheduledNotificationRule : IEventRule
{
    private readonly ITenantScopedRepository<Parcel> _parcels;
    private readonly SmsNotificationAction _sms;

    public RecipientReturnScheduledNotificationRule(ITenantScopedRepository<Parcel> parcels, SmsNotificationAction sms)
    {
        _parcels = parcels;
        _sms = sms;
    }

    public string Name => "recipient-return-scheduled-notification";

    public bool AppliesTo(IDomainEvent domainEvent) => domainEvent is ReturnScheduledEvent;

    public async Task ExecuteAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        var evt = (ReturnScheduledEvent)domainEvent;

        var parcel = await _parcels.GetAsync(evt.TenantId, evt.Task.ParcelId, ct);
        if (parcel is null)
        {
            return;
        }

        await _sms.SendAsync(
            evt.TenantId,
            parcel.RecipientPhone,
            $"Your parcel {parcel.Reference} could not be delivered after 3 attempts and is being returned to the sender.",
            ct);
    }
}
