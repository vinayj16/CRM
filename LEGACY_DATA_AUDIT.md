# Legacy Data Audit — Un-stamped Documents (TenantId missing)

**Date:** 2026-08-07
**DB:** MongoDB Atlas (`crm` database) — production
**Status:** ✅ **MIGRATION APPLIED** — see "Resolution" at the bottom (original audit below).

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

---

## ✅ Resolution (applied 2026-08-07)

### 1. Tenant-stamping migration — APPLIED
`scripts/stamp-legacy-data.py` stamped the inferred `TenantId` on **1,119 legacy
documents** across 30 tenant-scoped collections (leads 28→T1, properties 16→T1,
audit_logs 817 spread T1–T6 by UserId, notifications 42, agent_attendances 40,
chat 63, etc.). Ownership was inferred from owner fields (ExecutiveId/CreatedBy/
UserId/AgentId/LeadId/... → users → tenant) with fallback to tenant 1.

Backups (pre-change copies): `scripts/backups/stamp-legacy-backup-20260807-175445.json`
and `-175452.json`.

### 2. Deduplication — APPLIED
Removed **471 duplicate documents** sharing the same `(TenantId, int-key)` pair
(keep oldest), fixing the duplicate lead/property/audit IDs the audit flagged.
Backup: `scripts/backups/stamp-legacy-backup-20260807-175452.json`.

### 3. Creator auto-stamping — CODE FIX
`MongoDbSet<T>` now also stamps creator fields (`CreatedBy`, `PostedBy`,
`UploadedBy`, `CreatedByUserId`, `SenderId`) from the logged-in user whenever
unset, so every new resource is linked to the user who created it.

### 4. Mobile/API JWT tenant resolution — CODE FIX (critical)
Mobile API requests authenticate via a Bearer JWT, but only cookie auth was
registered, so `HttpContext.User` had no claims → every mobile-created resource
was stamped `TenantId=0` (orphaned + leaked). Fixed `MongoDbTenantService`,
`MongoDbSet.CurrentUserId` and `MobileApiFeaturesController.GetTenantId()/GetUserId()`
to also resolve from the Bearer JWT. Verified live: mobile campaign + company
message now stamped `TenantId=1` / `SenderId=1`.

### 4b. JWT signature validation — CODE HARDENING
`JwtHelper.ValidateToken` previously decoded tokens without checking the
signature. Since the new code now trusts those claims for tenant/creator
stamping, validation was hardened to cryptographically verify signature,
issuer, audience and lifetime (mirroring RoleAuthorizeAttribute) whenever an
`IConfiguration` is supplied. All call sites (ApiController, MobileApiController,
MobileApiFeaturesController, MongoDbTenantService, MongoDbSet) now pass config.
Verified live: a forged token (valid-looking payload, garbage signature) is
rejected and cannot misattribute resources to another tenant.

### Post-migration verification
- No unstamped/zero-`TenantId` docs remain in tenant-scoped collections (only
  intentionally-global collections remain unstamped: role_permissions, modules,
  pages, permissions, role_page_permissions, email_templates, inquiries, saas_*,
  super_admins, subscription_plans, maintenance_logs).
- No duplicate `(TenantId, LeadId)` pairs remain.
- Permissions verified working: sales1 → /Leads 200 (module granted), sales1 →
  /SuperAdmin/Tenants blocked; admin → /Leads 200; SuperAdmin unrestricted.

### Notes / known pre-existing data quirks (not changed)
- `MobileApiFeaturesController` create endpoints now reject unauthenticated or
  forged-token requests with 401 (`Unauthorized`) — no orphaned (TenantId=0)
  resources can be created via the mobile API anymore. `MobileApiController`
  already guarded every endpoint via `Authenticate()`.
- Framework-level JwtBearer `[Authorize]` on the API controllers is still
  recommended for defense-in-depth (out of scope this session).
- `email_directory` had 5 test entries mapped to TenantId=0 — now stamped to T1.

## Round 3 (2026-08-07): Full CRUD, duplicate elimination & tenant hardening

### Root cause eliminated: per-tenant auto-increment collisions
All tenants share one MongoDB collection, but `MongoDbSet.AutoAssignIntId` and
`MaxAsync` computed the max **tenant-scoped** (`WithTenant`), so every tenant
restarted IDs at 1 → collisions (six invoices all `InvoiceId=1`, leads 16–23
duplicated, users 46–52 shared by tenants 1/2/10, etc.).

**Fix:** `MongoDbSet.cs` — `AutoAssignIntId` and `MaxAsync` now compute the max
across the **whole collection** (no tenant filter), so int IDs are globally
unique. Verified live: tenant-1 create → 615, tenant-2 create → 616.

### Data migration (backup-first)
- Renumbered ~400 colliding docs across: leads, properties, agents, invoices,
  payments, notifications, builders, chat_sessions, testimonials, bank_accounts,
  email_templates, saas_settings, maintenance_logs, tenant_subscriptions,
  lead_scores, email_logs, audit_logs, lead_histories, property_histories,
  chat_logs, branding, referral_earnings, user_profiles.
- Remapped all cross-references: bookings.LeadId/PropertyId, payments.InvoiceId/
  BookingId, quotations.LeadId/PropertyId, site_visits.LeadId/PropertyId,
  followups.PropertyId, lead_histories/notes/logs/scores.LeadId, payment_plans,
  agent_attendances/attendance_logs/payouts/commission_logs/documents/leave_requests.AgentId,
  booking_amendments, channel_partner_commission_logs.BookingId,
  property_histories/flats/uploads/inventory_units.PropertyId,
  chat_logs.SessionId, properties.BuilderId, saas_payment_transactions.SubscriptionId.
- Deleted 9 E2E test-artifact users (@test.com) that collided with real launch
  accounts, plus their profiles.
- Backfilled user_profiles for all 13 users missing one (0 remain missing; 0
  orphan profiles).
- Backfilled 9 tenant-0 notifications + 3 subscription plans to correct tenants.
- **Verified 0 duplicate int-IDs remain** across all collections
  (role_page_permissions.PageId repeats are intentional FKs to `pages`).
- Backups: `scripts/backups/dedup_*.json`, `backfill_*.json`, `dups2_*.json`.

### Code hardening
1. `NotificationService.CreateNotificationAsync` accepts `int? tenantId` and
   stamps it — background services (no HTTP context) pass `tenant.TenantId`,
   so reminders are no longer created with TenantId=0.
2. `PendingApprovalReminderService` passes tenant.TenantId; startup cleanup also
   sweeps legacy tenant-0 reminder orphans (tenant-scoped filter).
3. `SubscriptionController.CreatePlan` stamps the tenant from the JWT cookie via
   a new `GetCurrentTenantId()` helper (defensive, in case shim resolution fails).
4. `MobileApiController`: added DELETE endpoints for leads, expenses, tickets,
   site visits, bookings, payments, invoices, quotations, properties,
   notifications — each verifies tenant ownership (403 on cross-tenant).
   `MobileApiFeaturesController`: DELETE for campaigns, agents, testimonials;
   fixed `MarkMessageRead` (was not persisting).
5. **Tenant-scoped GET/PUT**: added tenant checks to GetLead/UpdateLead/
   GetProperty/GetBooking/UpdateBookingStatus/GetInvoice/GetQuotation — a
   cross-tenant read/update by known ID is rejected (read filters already 404).
6. `DeleteNotification`: global (UserId==null) notifications only deletable by
   Admin/SuperAdmin.
7. **Unique indexes** created on the int-ID field of 25 collections (plus
   `user_profiles.UserId`) so future ID collisions are structurally impossible.

### Verified live (Round 3)
- Full CRUD cycles (C→R→U→D) for leads, expenses, tickets, site visits,
  campaigns, agents, testimonials: all 200; verify-gone 404.
- Tenant isolation: tenant-2 user reading tenant-1 lead by ID → 404.
- Global unique IDs across tenants: 615 / 616.
- Build: 0 errors. Server running on port 5139.

## Round 4 (2026-08-07): Retry logic, codebase cleanup & GitHub push

- **MongoDbSet retry fix**: Add/AddAsync retry loops now reset the colliding ID (ResetAutoId) between duplicate-key attempts, so concurrent creates genuinely recompute max+1 instead of retrying the same ID 5x.
- **Mobile inquiries wired end-to-end**: added GET/PUT api/mobile/inquiries (+ client.js functions + app.json with extra.apiBaseUrl). Inquiries are global SaaS data, so they are role-gated: only Admin/SuperAdmin can read/mutate (verified Sales/Agent -> 403).
- **Cleaned 4 E2E test tenants** (FlowTest x2, FlowVerify, EndToEnd) + their 11 linked records (tenant_subscriptions, settings, audit_logs, email_directory, lead_histories). Backup: scripts/backups/test_tenants_cleanup_*.json. DB now holds 6 real tenants, 30 users, 0 orphans.
- **GitHub**: committed + pushed to origin/main (ad19b54). scripts/backups/ added to .gitignore.
