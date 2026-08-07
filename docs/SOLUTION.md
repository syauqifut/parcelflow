# Solution

## Overview

Fixed cross-tenant leak in daily report (A), added return-to-sender lifecycle (B), added weekly driver CSV report (C).

---

## Part A — Cross-tenant report bug

### Review
- Ticket PF-1287: `nusantara-express` saw other carriers' data in daily summary (1 Jul 2026).

### Reproduce
- `GET /api/reports/daily-summary?day=2026-07-01` with `X-Tenant-Id: nusantara-express` — foreign cities/drivers in response.
- Check in Studio 3T to validate the current result.

### Investigation / analysis
- Middleware and DB are fine (`TenantId` on documents is correct).
- `ReportService` still calls `QueryAllTenantsAsync`.

### Root cause
- Report queries all tenants, returns combined data to whoever calls the API.

### Decision
- Scope queries to `ITenantContext.TenantId` via `QueryAsync`. No middleware changes.

### Implementation
- Inject `ITenantContext` into `ReportService`.
- Replace `QueryAllTenantsAsync` with tenant-scoped queries in daily (and weekly) reports.

### Validation
- Verified the daily report only returns parcels for the current tenant.

---

## Part B — Return to sender

### Review
- Spec: 3rd failed attempt → schedule return, SMS + ops alert, hub closes return.

### Investigation / analysis
- Check the existing flow: retry via `AttemptFailed → InTransit`; events already drive notifications.

### Decision
- New states: `ReturnScheduled` and `Returned`, since ParcelFlow only models the last-mile delivery process.
- 3rd failure auto-transitions and trigger `ReturnScheduledEvent`; retries blocked after 3 attempts.
- Hub: `POST /api/tasks/{id}/return-completed`.
- Return cannot be cancelled.

### Implementation
- Extend state machine, `RecordFailedAttemptAsync`, `CompleteReturnAsync`.
- Add SMS + ops webhook rules on `ReturnScheduledEvent`.
- Update `ARCHITECTURE.md`.

### Validation
- Verified notification rules are triggered.
- Verified valid and invalid state transitions.

---

## Part C — Weekly driver summary

### Review
- Daily report already available per tenant via API.

### Investigation / analysis
- Found that the daily report was already exposed through the Reports API for each tenant.
- Need: per driver, last 7 days — delivered, failed attempts, avg assignment-to-delivery hours.
- Choose CSV or XLSX export.
- Reviewed the reporting query and validated the required data against MongoDB.

### Decision
- Reused the existing reporting architecture.
- Choose CSV over XLSX (simpler, no extra lib).
- `GET /api/reports/weekly-summary` → CSV download.

### Implementation
- Added `GetWeeklySummaryAsync` to `ReportService`.
- Added the weekly summary endpoint in `ReportsController`.

### Validation
- Verified the generated report against MongoDB.
- Verified aggregation per driver.

---

## Trade-offs

- Reused the existing reporting architecture instead of introducing a new reporting component.
- Chose CSV over XLSX to keep the implementation simple and dependency-free.
- The return workflow ends at `Returned`; ParcelFlow models only the last-mile delivery process.
- Weekly reporting is exposed as an API endpoint. Scheduling and email delivery are intentionally left outside the assignment scope.
