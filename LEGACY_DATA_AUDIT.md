# Legacy Data Audit — Un-stamped Documents (TenantId missing)

**Date:** 2026-08-07
**DB:** MongoDB Atlas (`crm` database) — production
**Status:** REPORT ONLY — no production data was modified.

## Problem
Documents created before tenant-stamping existed have no `TenantId` field. The MongoDbSet
shim's `WithTenant` filter returns documents where `TenantId == current` **OR** where the
`TenantId` field does not exist (legacy fallback). This means **every un-stamped document is
visible to every company** → cross-tenant data leakage + duplicate IDs in lists.

## Verified evidence (live)
- Tenant-1 admin lead list shows duplicate IDs (e.g. `10, 9, 8` twice).
- Tenant-2 admin (admin2) sees 36 leads, including tenant-1's legacy records.
- `GetProperties` site-visit dropdown returns duplicate `propertyId` values (legacy docs).

## Un-stamped document counts per collection
| collection            | total | stamped | un-stamped |
|-----------------------|------:|--------:|-----------:|
| leads                 |    92 |      64 |         28 |
| properties            |    54 |      38 |         16 |
| expenses              |    21 |       3 |         18 |
| revenues              |    15 |       1 |         14 |
| builders              |    24 |      13 |         11 |
| testimonials          |    14 |       6 |          8 |
| agents                |    14 |      10 |          4 |
| bookings              |    10 |       7 |          3 |
| support_tickets       |     6 |       1 |          5 |
| notifications         |   102 |      71 |         31 |
| chat_logs             |    34 |      14 |         20 |
| chat_sessions         |    12 |       3 |          9 |
| lead_histories        |    40 |      29 |         11 |
| property_histories    |    26 |      20 |          6 |
| audit_logs            |  1038 |     735 |        303 |
| role_permissions      |     8 |       4 |          4 |
| channel_partners      |     7 |       6 |          1 |
| email_templates       |     5 |       0 |          5 |
| inquiries             |     5 |       0 |          5 |
| lead_logs             |     7 |       0 |          7 |
| lead_uploads          |     1 |       0 |          1 |
| lead_notes            |     1 |       0 |          1 |
| payment_plans         |     1 |       0 |          1 |
| agent_attendances     |    40 |       0 |         40 |
| agent_payouts         |     4 |       0 |          4 |
| chat_messages         |    34 |       0 |         34 |
| maintenance_logs      |     3 |       0 |          3 |
| modules / pages / permissions / role_page_permissions / saas_* / subscription_plans / super_admins | — | — | intentionally global (not tenant-scoped) |

## Ownership inference
- **28 un-stamped leads** → all resolve via `ExecutiveId`/`CreatedBy` to **tenant-1 users**.
- **16 un-stamped properties** → all resolve via `CreatedBy` to **tenant-1 users**.
- **Expenses/revenues** → no owner field (only `ChannelPartnerId`), ownership not inferable.
- Duplicate lead IDs confirmed: `LeadId` 1–15 appear twice (one stamped + one un-stamped copy).

## Recommended fix (NOT applied — user chose report-only)
Run a one-time migration:
`db.<collection>.updateMany({ TenantId: { $exists: false } }, { $set: { TenantId: 1 } })`
on tenant-scoped collections (leads, properties, bookings, expenses, revenues, builders,
testimonials, agents, notifications, support_tickets, chat_logs, chat_sessions,
lead_histories, property_histories, audit_logs, role_permissions, channel_partners,
lead_notes, lead_logs, lead_uploads, payment_plans, email_templates, inquiries).
Collections like `saas_plans`, `saas_settings`, `modules`, `pages`, `permissions`,
`super_admins`, `subscription_plans` are **intentionally global** — do NOT stamp them.

## Confirmed healthy (no action needed)
- New writes are auto-stamped with TenantId by the shim (`StampTenant`).
- New tenants seed with explicit TenantId (verified in SuperAdminController).
- Role-scoped visibility works correctly for stamped data:
  admin=51 leads, sales1=1 (assigned), agent1/partner1=4 (org), admin2=36 (tenant 2).
- Cross-tenant isolation of NEW resources verified in earlier sessions.
