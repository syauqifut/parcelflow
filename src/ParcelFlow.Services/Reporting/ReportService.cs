using ParcelFlow.Domain.Entities;
using ParcelFlow.Domain.StateMachine;
using ParcelFlow.Services;
using ParcelFlow.Storage;

namespace ParcelFlow.Services.Reporting;

/// <summary>
/// Operational reports for tenant ops teams.
///
/// NOTE(PF-902): this logic was lifted from the retired DataWarehouse module
/// during the v2 consolidation (see docs/adr/0003-retire-datawarehouse-module.md)
/// and is due a proper clean-up. Kept behaviour identical to the DW job to
/// avoid breaking the numbers tenants are used to.
/// </summary>
public sealed class ReportService
{
    private readonly ITenantContext _tenant;
    private readonly ITenantScopedRepository<DeliveryTask> _tasks;
    private readonly ITenantScopedRepository<Parcel> _parcels;
    private readonly ITenantScopedRepository<Driver> _drivers;

    public ReportService(
        ITenantContext tenant,
        ITenantScopedRepository<DeliveryTask> tasks,
        ITenantScopedRepository<Parcel> parcels,
        ITenantScopedRepository<Driver> drivers)
    {
        _tenant = tenant;
        _tasks = tasks;
        _parcels = parcels;
        _drivers = drivers;
    }

    /// <summary>
    /// Daily delivery summary: every task that reached a terminal state or had
    /// a failed attempt on the given UTC day, with totals.
    /// </summary>
    public async Task<DailySummaryReport> GetDailySummaryAsync(DateTime dayUtc, CancellationToken ct = default)
    {
        var from = dayUtc.Date;
        var to = from.AddDays(1);

        var tenantId = _tenant.TenantId;

        var tasks = await _tasks.QueryAsync(
            tenantId,
            t => t.UpdatedUtc >= from && t.UpdatedUtc < to,
            ct);

        var parcels = (await _parcels.QueryAsync(tenantId, p => true, ct))
            .ToDictionary(p => p.Id);
        var drivers = (await _drivers.QueryAsync(tenantId, d => true, ct))
            .ToDictionary(d => d.Id);

        var rows = new List<DailySummaryRow>();
        foreach (var task in tasks)
        {
            parcels.TryGetValue(task.ParcelId, out var parcel);
            Driver? driver = null;
            if (task.DriverId is not null)
            {
                drivers.TryGetValue(task.DriverId, out driver);
            }

            rows.Add(new DailySummaryRow
            {
                TaskId = task.Id,
                ParcelReference = parcel?.Reference ?? "(unknown)",
                RecipientCity = parcel?.City ?? "(unknown)",
                DriverName = driver?.Name ?? "(unassigned)",
                Status = task.Status.ToString(),
                AttemptCount = task.AttemptCount
            });
        }

        return new DailySummaryReport
        {
            DayUtc = from,
            TotalDelivered = tasks.Count(t => t.Status == DeliveryTaskStatus.Delivered),
            TotalFailedAttempts = tasks.Sum(t => t.AttemptCount),
            TotalCancelled = tasks.Count(t => t.Status == DeliveryTaskStatus.Cancelled),
            Rows = rows
        };
    }

    /// <summary>
    /// Weekly delivery summary: 
    /// </summary>
    public async Task<WeeklySummaryReport> GetWeeklySummaryAsync(DateTime dayUtc, CancellationToken ct = default)
    {
        var to = dayUtc.Date;
        var from = to.AddDays(-7); //last 7 days

        var tenantId = _tenant.TenantId;

        var tasks = (await _tasks.QueryAsync(
                tenantId,
                t => t.DeliveredUtc.HasValue &&
                    t.DeliveredUtc.Value >= from &&
                    t.DeliveredUtc.Value < to,
                ct))
            .GroupBy(t => t.DriverId)
            .Select(g => new
            {
                DriverId = g.Key,
                TaskDelivered = g.Count(),
                TaskFailedAttempts = g.Sum(t => t.History.Count(h =>
                    h.To == DeliveryTaskStatus.AttemptFailed &&
                    h.AtUtc >= from &&
                    h.AtUtc < to)),
                AverageHoursFromAssignmentToDelivery = g
                        .Where(t => t.AssignedUtc.HasValue)
                        .Select(t => (t.DeliveredUtc!.Value - t.AssignedUtc!.Value).TotalHours)
                        .DefaultIfEmpty(0)
                        .Average()
            })
            .ToList();

        var drivers = (await _drivers.QueryAsync(tenantId, d => true, ct))
            .ToDictionary(d => d.Id);


        var rows = tasks
            .Select(task =>
            {
                drivers.TryGetValue(task.DriverId ?? "", out var driver);

                return new WeeklySummaryRow
                {
                    DriverName = driver?.Name ?? "(unassigned)",
                    TaskDelivered = task.TaskDelivered,
                    TaskFailedAttempts = task.TaskFailedAttempts,
                    AverageHoursFromAssignmentToDelivery =
                        task.AverageHoursFromAssignmentToDelivery
                };
            })
            .OrderBy(r => r.DriverName)
            .ToList();

        return new WeeklySummaryReport
        {
            FromDayUtc = from,
            ToDayUtc = to,
            Rows = rows
        };
    }
}

public sealed class DailySummaryReport
{
    public DateTime DayUtc { get; set; }
    public int TotalDelivered { get; set; }
    public int TotalFailedAttempts { get; set; }
    public int TotalCancelled { get; set; }
    public List<DailySummaryRow> Rows { get; set; } = new();
}

public sealed class DailySummaryRow
{
    public string TaskId { get; set; } = string.Empty;
    public string ParcelReference { get; set; } = string.Empty;
    public string RecipientCity { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
}

public sealed class WeeklySummaryReport
{
    public DateTime FromDayUtc { get; set; }
    public DateTime ToDayUtc { get; set; }
    public List<WeeklySummaryRow> Rows { get; set; } = new();
}

public sealed class WeeklySummaryRow
{
    public string DriverName { get; set; } = string.Empty;
    public int TaskDelivered { get; set; } = 0;
    public int TaskFailedAttempts { get; set; } = 0;
    public double AverageHoursFromAssignmentToDelivery { get; set; } = 0;
}