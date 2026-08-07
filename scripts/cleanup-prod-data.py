#!/usr/bin/env python3
"""
One-time production data cleanup for the CRM MongoDB.

Fixes two classes of data corruption introduced by the original data import
(and by the pre-fix Mongo shim which could not auto-assign int keys):

  1. Rows with the int key = 0  ->  assigned unique sequential IDs (per tenant)
  2. Duplicate rows sharing the same (TenantId, Key)  ->  keeps the OLDEST
     document (by _id), removes the copies

Why this is safe:
  - Never deletes when only ONE document exists for a (TenantId, Key).
  - Keeps the oldest insert per key (original seed data), removes only
    the repeated imports.
  - Ids are assigned per tenant so tenant-isolated data stays isolated.

Usage:
  python cleanup-prod-data.py --connection-string "mongodb://user:pass@host:27017/dbname"
  python cleanup-prod-data.py --connection-string "..." --dry-run   # preview only
  python cleanup-prod-data.py --connection-string "..." --collections leads,payments

Environment variable MONGO_CONNECTION_STRING also works.
"""
import argparse
import os
import re
import sys
from collections import Counter, defaultdict

try:
    from pymongo import MongoClient
    from pymongo.errors import PyMongoError
except ImportError:
    print("pymongo is required:  pip install pymongo")
    sys.exit(1)

# collection -> int key field
KEY_MAP = {
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
    "tasks": "TaskId",
    "support_tickets": "TicketId",
    "inquiries": "InquiryId",
    "users": "UserId",
    "channel_partners": "ChannelPartnerId",
    "tenants": "TenantId",
    "modules": "ModuleId",
    "pages": "PageId",
    "permissions": "PermissionId",
}


def parse_cs(cs: str) -> tuple:
    m = re.search(r"/([^/]+)$", cs.split("?")[0])
    dbname = m.group(1) if m else "crm"
    return cs, dbname


def fix_zero_keys(col, key, dry_run=False):
    """Assign unique sequential ids to rows where the int key is 0."""
    zero_count = col.count_documents({key: 0})
    if zero_count == 0:
        return 0, 0

    maxes = {}
    for d in col.find({}, {"TenantId": 1, key: 1}):
        tid = d.get("TenantId")
        k = d.get(key) or 0
        if tid not in maxes or k > maxes[tid]:
            maxes[tid] = k

    docs = list(col.find({key: 0}).sort("_id", 1))
    updated = 0
    for d in docs:
        tid = d.get("TenantId")
        maxes[tid] = maxes.get(tid, 0) + 1
        if not dry_run:
            col.update_one({"_id": d["_id"]}, {"$set": {key: maxes[tid]}})
        updated += 1
    return updated, zero_count


def dedupe(col, key, dry_run=False):
    """Remove duplicate rows sharing the same (TenantId, Key); keep the oldest."""
    groups = defaultdict(list)
    for d in col.find({}, {"TenantId": 1, key: 1}):
        groups[(d.get("TenantId"), d.get(key))].append(d["_id"])

    to_remove = []
    for ids in groups.values():
        if len(ids) > 1:
            to_remove.extend(sorted(ids)[1:])

    if not to_remove:
        return 0

    if not dry_run:
        col.delete_many({"_id": {"$in": to_remove}})
    return len(to_remove)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--connection-string", default=None, help="MongoDB connection string (or MONGO_CONNECTION_STRING env)")
    ap.add_argument("--dry-run", action="store_true", help="Preview only - make no changes")
    ap.add_argument("--collections", default="", help="Comma-separated subset of collections (default: all)")
    args = ap.parse_args()

    cs = args.connection_string or os.environ.get("MONGO_CONNECTION_STRING")
    if not cs:
        ap.error("Provide --connection-string or set MONGO_CONNECTION_STRING")

    cs, dbname = parse_cs(cs)
    client = MongoClient(cs, serverSelectionTimeoutMS=10000)
    db = client[dbname]
    print(f"Connected to database: {dbname}  (dry-run={args.dry_run})")

    wanted = set(c.lower() for c in args.collections.split(",")) if args.collections else None

    total_zero_fixed = 0
    total_dupes_removed = 0
    for coll, key in KEY_MAP.items():
        if wanted and coll.lower() not in wanted:
            continue
        if coll not in db.list_collection_names():
            continue
        col = db[coll]
        try:
            zero_fixed, zero_before = fix_zero_keys(col, key, dry_run=args.dry_run)
            dupes = dedupe(col, key, dry_run=args.dry_run)
        except PyMongoError as e:
            print(f"  {coll:16s} ERROR: {e}")
            continue
        total_zero_fixed += zero_fixed
        total_dupes_removed += dupes
        if zero_fixed or dupes:
            print(f"  {coll:16s} zeroKeys_fixed={zero_fixed:3d} (was {zero_before})  dupes_removed={dupes}")

    print(f"\nDONE. zero-key rows fixed: {total_zero_fixed}, duplicate rows removed: {total_dupes_removed}")
    if args.dry_run:
        print("Dry run - no changes were written. Re-run without --dry-run to apply.")

    # Final verification
    bad = 0
    for coll, key in KEY_MAP.items():
        if wanted and coll.lower() not in wanted:
            continue
        if coll not in db.list_collection_names():
            continue
        col = db[coll]
        zero = col.count_documents({key: 0})
        pairs = Counter((d.get("TenantId"), d.get(key)) for d in col.find({}, {"TenantId": 1, key: 1}))
        dups = sum(1 for v in pairs.values() if v > 1)
        if zero or dups:
            bad += 1
    print("Post-check:", "CLEAN - no zero keys or duplicate groups remain" if bad == 0 else f"{bad} collection(s) still have issues")


if __name__ == "__main__":
    main()
