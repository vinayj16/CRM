#!/usr/bin/env python3
"""
One-time production data migration for the CRM MongoDB.

Stamps the correct TenantId onto legacy documents that were created before
tenant-stamping existed (no TenantId field, or TenantId == 0). These documents
are currently visible to EVERY tenant because the MongoDbSet shim's WithTenant
filter falls back to "no TenantId field" documents (legacy / shared fallback),
causing cross-tenant data leakage + duplicate IDs in lists.

What this script does
---------------------
1. Builds tenant-resolution maps from existing data (users, channel_partners,
   agents, leads, properties, bookings, chat_sessions).
2. For every tenant-scoped collection, finds documents with missing/null/0
   TenantId and stamps the inferred tenant:
     - owner fields (ExecutiveId/CreatedBy/PostedBy/UserId/SenderId/AgentId/
       LeadId/PropertyId/BookingId/PartnerId/ChannelPartnerId) are resolved
       through the maps above
     - fallback = --fallback-tenant (default 1 = the original "Default CRM"
       tenant that all legacy data belongs to)
3. Writes a JSON backup of every modified document (--backup-file) BEFORE
   making changes, so the migration is fully reversible.
4. Verifies afterwards that no tenant-scoped collection has unstamped docs.

NOT touched (intentionally global by design - models have no TenantId property
or are shared defaults): modules, pages, permissions, role_page_permissions,
role_permissions, email_templates, inquiries, saas_*, super_admins,
subscription_plans, maintenance_logs, tenants, users, settings, branding.

Usage:
  python stamp-legacy-data.py --connection-string "mongodb://user:pass@host:27017/dbname"
  python stamp-legacy-data.py --connection-string "..." --dry-run     # preview only
  python stamp-legacy-data.py --connection-string "..." --apply       # write changes
  python stamp-legacy-data.py --connection-string "..." --collections leads,properties
  python stamp-legacy-data.py --connection-string "..." --fallback-tenant 1

Environment variable MONGO_CONNECTION_STRING also works.
"""
import argparse
import datetime
import json
import os
import sys
from collections import defaultdict, Counter

try:
    from pymongo import MongoClient
    from pymongo.errors import PyMongoError
except ImportError:
    print("pymongo is required:  pip install pymongo")
    sys.exit(1)


# ---------------------------------------------------------------------------
# Tenant-scoped collections that must be stamped (model has a TenantId property)
#   key   = collection name
#   value = list of inference steps, tried in order until one yields a tenant
# Each step: (field, resolver_key)
#   resolver_key in: users, channel_partners, agents, leads, properties,
#                    bookings, chat_sessions  (maps built from live data)
# Special sentinel '__fallback__' = use --fallback-tenant.
# ---------------------------------------------------------------------------
TENANT_SCOPED = {
    "leads":            [("ExecutiveId", "users"), ("CreatedBy", "users"), "__fallback__"],
    "properties":       [("CreatedBy", "users"), ("PostedBy", "users"), ("AssignedTo", "users"), "__fallback__"],
    "bookings":         [("CreatedBy", "users"), ("LeadId", "leads"), ("UploadedBy", "users"), "__fallback__"],
    "builders":         [("CreatedBy", "users"), "__fallback__"],
    "support_tickets":  [("CreatedByUserId", "users"), ("CreatedBy", "users"), ("UserId", "users"), ("ChannelPartnerId", "channel_partners"), "__fallback__"],
    "notifications":    [("UserId", "users"), "__fallback__"],
    "audit_logs":       [("UserId", "users"), "__fallback__"],
    "lead_histories":   [("LeadId", "leads"), ("ExecutiveId", "users"), "__fallback__"],
    "lead_logs":        [("LeadId", "leads"), ("ExecutiveId", "users"), "__fallback__"],
    "lead_notes":       [("LeadId", "leads"), ("ExecutiveId", "users"), "__fallback__"],
    "lead_uploads":     [("LeadId", "leads"), ("UploadedBy", "users"), "__fallback__"],
    "property_histories":[("PropertyId", "properties"), "__fallback__"],
    "agent_attendances":[("AgentId", "agents"), ("ApprovedBy", "users"), "__fallback__"],
    "agent_payouts":    [("AgentId", "agents"), "__fallback__"],
    "agents":           [("ChannelPartnerId", "channel_partners"), ("ApprovedBy", "users"), "__fallback__"],
    "channel_partners": [("UserId", "users"), ("ApprovedBy", "users"), "__fallback__"],
    "chat_logs":        [("SessionId", "chat_sessions"), ("UserId", "users"), "__fallback__"],
    "chat_sessions":    [("UserId", "users"), ("AssignedAgentId", "users"), "__fallback__"],
    "chat_messages":    [("SessionId", "chat_sessions"), ("UserId", "users"), "__fallback__"],
    "company_messages": [("SenderId", "users"), ("RecipientId", "users"), "__fallback__"],
    "email_directory":  [("Email", "__email__"), "__fallback__"],
    "expenses":         [("ChannelPartnerId", "channel_partners"), ("CreatedBy", "users"), "__fallback__"],
    "revenues":         [("ChannelPartnerId", "channel_partners"), ("CreatedBy", "users"), "__fallback__"],
    "partner_commissions":[("PartnerId", "channel_partners"), ("ChannelPartnerId", "channel_partners"), ("LeadId", "leads"), "__fallback__"],
    "payment_plans":    [("BookingId", "bookings"), "__fallback__"],
    "tenant_subscriptions":[("TenantId", "tenant_self"), "__fallback__"],
    "testimonials":     ["__fallback__"],
    "webhook_leads":    ["__fallback__"],
    "whatsapp_logs":    ["__fallback__"],
}

# collection -> int key field used for dedupe (keeps OLDEST per (TenantId, Key))
DEDUPE_KEY_MAP = {
    "leads": "LeadId",
    "agents": "AgentId",
    "bookings": "BookingId",
    "expenses": "ExpenseId",
    "invoices": "InvoiceId",
    "payments": "PaymentId",
    "notifications": "NotificationId",
    "properties": "PropertyId",
    "audit_logs": "AuditId",
    "quotations": "QuotationId",
    "followups": "FollowUpId",
    "revenues": "RevenueId",
    "support_tickets": "TicketId",
    "builders": "BuilderId",
    "testimonials": "TestimonialId",
    "channel_partners": "ChannelPartnerId",
    "agents_alt": "AgentId",
}

# Collections that are intentionally global / shared defaults - NEVER stamp.
GLOBAL_COLLECTIONS = {
    "modules", "pages", "permissions", "role_page_permissions", "role_permissions",
    "email_templates", "email_settings", "email_logs", "inquiries", "inquiry_forms",
    "inquiry_view_models", "saas_plans", "saas_settings", "saas_brandings",
    "saas_payment_configs", "saas_payment_transactions", "super_admins",
    "subscription_plans", "subscription_addons", "maintenance_logs",
    "tenants", "users", "settings", "branding", "error_view_models",
}


def parse_cs(cs):
    import re
    m = re.search(r"/([^/]+)$", cs.split("?")[0])
    dbname = m.group(1) if m else "crm"
    return dbname


def load_connection(args):
    cs = args.connection_string or os.environ.get("MONGO_CONNECTION_STRING")
    if not cs:
        # fall back to appsettings.json when run from the repo root
        try:
            with open("appsettings.json", encoding="utf-8") as f:
                cs = json.load(f)["MongoDb"]["ConnectionString"]
        except Exception:
            pass
    if not cs:
        print("Provide --connection-string or set MONGO_CONNECTION_STRING (or run from repo root with appsettings.json).")
        sys.exit(1)
    return cs


def build_maps(db, fallback_tenant):
    """Build owner->tenant resolution maps from live data."""
    users = {}
    for d in db.users.find({}, {"UserId": 1, "Email": 1, "TenantId": 1}):
        uid = d.get("UserId")
        if uid is not None:
            users[uid] = d.get("TenantId") or fallback_tenant
    users_by_email = {}
    for d in db.users.find({}, {"Email": 1, "TenantId": 1}):
        em = (d.get("Email") or "").strip().lower()
        if em:
            users_by_email[em] = d.get("TenantId") or fallback_tenant

    # channel partners: PartnerId / ChannelPartnerId / UserId -> tenant
    chp = {}
    for d in db.channel_partners.find({}, {"PartnerId": 1, "ChannelPartnerId": 1, "UserId": 1, "TenantId": 1}):
        t = d.get("TenantId") or (users.get(d.get("UserId")) if d.get("UserId") is not None else None) or fallback_tenant
        for f in ("PartnerId", "ChannelPartnerId"):
            v = d.get(f)
            if v is not None:
                chp[v] = t
        uid = d.get("UserId")
        if uid is not None:
            chp[("uid", uid)] = t

    # agents: AgentId -> tenant
    agents = {}
    for d in db.agents.find({}, {"AgentId": 1, "ChannelPartnerId": 1, "TenantId": 1}):
        t = d.get("TenantId") or chp.get(d.get("ChannelPartnerId")) or fallback_tenant
        aid = d.get("AgentId")
        if aid is not None:
            agents[aid] = t

    # leads: LeadId -> tenant
    leads = {}
    for d in db.leads.find({}, {"LeadId": 1, "ExecutiveId": 1, "CreatedBy": 1, "TenantId": 1}):
        t = d.get("TenantId") or users.get(d.get("ExecutiveId")) or users.get(d.get("CreatedBy")) or fallback_tenant
        lid = d.get("LeadId")
        if lid is not None:
            leads[lid] = t

    # properties: PropertyId -> tenant
    props = {}
    for d in db.properties.find({}, {"PropertyId": 1, "CreatedBy": 1, "PostedBy": 1, "TenantId": 1}):
        t = d.get("TenantId") or users.get(d.get("CreatedBy")) or users.get(d.get("PostedBy")) or fallback_tenant
        pid = d.get("PropertyId")
        if pid is not None:
            props[pid] = t

    # bookings: BookingId -> tenant
    bookings = {}
    for d in db.bookings.find({}, {"BookingId": 1, "CreatedBy": 1, "LeadId": 1, "TenantId": 1}):
        t = d.get("TenantId") or users.get(d.get("CreatedBy")) or leads.get(d.get("LeadId")) or fallback_tenant
        bid = d.get("BookingId")
        if bid is not None:
            bookings[bid] = t

    # chat_sessions: SessionId -> tenant
    chat_sessions = {}
    for d in db.chat_sessions.find({}, {"SessionId": 1, "SessionGuid": 1, "UserId": 1, "TenantId": 1}):
        t = d.get("TenantId") or users.get(d.get("UserId")) or fallback_tenant
        for f in ("SessionId", "SessionGuid"):
            v = d.get(f)
            if v is not None:
                chat_sessions[v] = t

    return {
        "users": users,
        "users_by_email": users_by_email,
        "channel_partners": chp,
        "agents": agents,
        "leads": leads,
        "properties": props,
        "bookings": bookings,
        "chat_sessions": chat_sessions,
        "fallback": fallback_tenant,
    }


def resolve_tenant(doc, steps, maps):
    """Return the inferred TenantId for a document, or None if not resolvable."""
    users = maps["users"]
    chp = maps["channel_partners"]
    for step in steps:
        if step == "__fallback__":
            return maps["fallback"]
        field, resolver = step
        val = doc.get(field)
        if val is None:
            continue
        if resolver == "users":
            if val in users:
                return users[val]
        elif resolver == "__email__":
            key = str(val).strip().lower()
            if key in maps["users_by_email"]:
                return maps["users_by_email"][key]
        elif resolver == "channel_partners":
            if val in chp:
                return chp[val]
            if ("uid", val) in chp:
                return chp[("uid", val)]
        elif resolver in maps and val in maps[resolver]:
            return maps[resolver][val]
    return None


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--connection-string", default=None, help="MongoDB connection string (or MONGO_CONNECTION_STRING env)")
    ap.add_argument("--dry-run", action="store_true", help="Preview only - make no changes")
    ap.add_argument("--apply", action="store_true", help="Write changes (default is dry-run)")
    ap.add_argument("--collections", default="", help="Comma-separated subset (default: all tenant-scoped)")
    ap.add_argument("--fallback-tenant", type=int, default=1, help="Tenant used when ownership cannot be inferred (default 1)")
    ap.add_argument("--backup-file", default="", help="JSON backup path for modified docs (default: scripts/backups/stamp-legacy-backup-<ts>.json)")
    ap.add_argument("--dedupe", action="store_true", help="After stamping, remove duplicate docs sharing (TenantId, int-key), keeping the OLDEST by _id")
    args = ap.parse_args()

    write = args.apply and not args.dry_run
    if args.dry_run and args.apply:
        print("--dry-run and --apply both set; --dry-run wins (no writes).")
        write = False

    cs = load_connection(args)
    dbname = parse_cs(cs)
    client = MongoClient(cs, serverSelectionTimeoutMS=10000)
    db = client[dbname]
    print(f"Connected to database: {dbname}  (mode={'DRY-RUN - no writes' if not write else 'APPLY - writes enabled'})")

    maps = build_maps(db, args.fallback_tenant)

    wanted = set(c.lower() for c in args.collections.split(",")) if args.collections else None

    # Gather affected docs per collection (missing, null, or zero TenantId)
    affected = {}
    for coll, steps in TENANT_SCOPED.items():
        if wanted and coll.lower() not in wanted:
            continue
        if coll not in db.list_collection_names():
            continue
        q = {"$or": [{"TenantId": {"$exists": False}}, {"TenantId": None}, {"TenantId": 0}]}
        docs = list(db[coll].find(q))
        if not docs:
            continue
        affected[coll] = (steps, docs)

    total = 0
    changes = []
    for coll, (steps, docs) in sorted(affected.items()):
        per_tenant = defaultdict(int)
        per_tenant_unresolved = 0
        for d in docs:
            t = resolve_tenant(d, steps, maps)
            if t is None:
                t = args.fallback_tenant
                per_tenant_unresolved += 1
            per_tenant[t] += 1
            changes.append({"collection": coll, "_id": str(d.get("_id")), "tenantId": t})
            total += 1
        dist = ", ".join(f"T{t}={n}" for t, n in sorted(per_tenant.items()))
        print(f"  {coll:22s} {len(docs):4d} docs -> {dist}"
              + (f"  ({per_tenant_unresolved} fallback)" if per_tenant_unresolved else ""))

    print(f"\nTotal documents to stamp: {total}")

    if total == 0:
        print("Nothing to do.")
        return

    # Backup + apply
    if write:
        ts = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
        backup_file = args.backup_file or f"scripts/backups/stamp-legacy-backup-{ts}.json"
        os.makedirs(os.path.dirname(backup_file), exist_ok=True)
        backup = {}
        for coll, (steps, docs) in sorted(affected.items()):
            backup[coll] = []
            for d in docs:
                copy = dict(d)
                copy["_id"] = str(copy["_id"])
                backup[coll].append(copy)  # NOTE: original docs keep ObjectId _id for the update pass
        with open(backup_file, "w", encoding="utf-8") as f:
            json.dump(backup, f, default=str, indent=1)
        print(f"Backup written: {backup_file}")

        for coll, (steps, docs) in sorted(affected.items()):
            col = db[coll]
            ids = [d["_id"] for d in docs]
            new_tid = defaultdict(set)
            for d in docs:
                t = resolve_tenant(d, steps, maps) or args.fallback_tenant
                new_tid[t].add(d["_id"])
            for t, id_set in new_tid.items():
                col.update_many(
                    {"_id": {"$in": list(id_set)}},
                    {"$set": {"TenantId": t}},
                )
        print("Migration applied.")

    # Verify
    bad = 0
    for coll in TENANT_SCOPED:
        if coll not in db.list_collection_names():
            continue
        q = {"$or": [{"TenantId": {"$exists": False}}, {"TenantId": None}, {"TenantId": 0}]}
        n = db[coll].count_documents(q)
        if n:
            bad += 1
            if not write:
                print(f"  [will remain] {coll}: {n} unstamped")
            else:
                print(f"  [REMAINING] {coll}: {n} unstamped")
    if write and bad == 0:
        print("Post-check: CLEAN - no unstamped/zero-TenantId docs remain in tenant-scoped collections.")
    elif not write:
        print("Post-check: (dry-run only - nothing was written)")

    # ---- Dedupe pass (optional): keep oldest doc per (TenantId, int-key) ----
    if args.dedupe:
        removed_total = 0
        removed_backup = {}
        for coll, key in DEDUPE_KEY_MAP.items():
            if coll not in db.list_collection_names():
                continue
            col = db[coll]
            groups = defaultdict(list)
            for d in col.find({}, {"TenantId": 1, key: 1}):
                tid = d.get("TenantId")
                k = d.get(key)
                if tid is None or k is None:
                    continue
                groups[(tid, k)].append((d["_id"], d))
            to_remove = []
            for ids in groups.values():
                if len(ids) > 1:
                    ids_sorted = sorted(ids, key=lambda x: x[0])  # oldest _id first
                    for _oid, doc in ids_sorted[1:]:
                        to_remove.append(_oid)
                        removed_backup.setdefault(coll, []).append({**doc, "_id": str(_oid)})
            if to_remove:
                if not write:
                    print(f"  [dedupe would remove] {coll}: {len(to_remove)} duplicate docs (keep oldest per TenantId+{key})")
                else:
                    col.delete_many({"_id": {"$in": to_remove}})
                    print(f"  [dedupe] {coll}: removed {len(to_remove)} duplicates")
                removed_total += len(to_remove)
        if write and removed_backup:
            dup_file = (args.backup_file or "scripts/backups/stamp-legacy-backup-<ts>.json").replace(
                "<ts>", datetime.datetime.now().strftime("%Y%m%d-%H%M%S"))
            os.makedirs(os.path.dirname(dup_file), exist_ok=True)
            with open(dup_file, "w", encoding="utf-8") as f:
                json.dump(removed_backup, f, default=str, indent=1)
            print(f"Dedupe backup written: {dup_file}")
        print(f"\nDedupe total removed: {removed_total}")

    if not write:
        print("Re-run with --apply to write changes (a JSON backup is created first).")


if __name__ == "__main__":
    main()
