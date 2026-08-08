"""Regenerate All_MongoDB_Data.json from the live database + orphan-data check.

Mirrors the existing file structure: { exportDate, totalCollections, summary, data }.
Also audits references (UserId, TenantId, AgentId, LeadId, etc.) for orphans.
"""
import sys, json, time
from datetime import datetime, timezone
sys.stdout.reconfigure(encoding='utf-8')
cs = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
from pymongo import MongoClient
client = MongoClient(cs, serverSelectionTimeoutMS=10000)
db = client['crm']

# ---------- 1) Collect all collections + docs ----------
collections = sorted(db.list_collection_names())
data = {}
summary = {}
for c in collections:
    docs = list(db[c].find({}))
    clean = []
    for d in docs:
        d = dict(d)
        d.pop('_id', None)
        # Normalize ObjectId/Bson types for JSON dump
        clean.append(d)
    data[c] = clean
    summary[c] = len(clean)

# ---------- 2) Orphan checks ----------
user_ids = set(u.get('UserId') for u in data.get('users', []))
user_ids |= set(s.get('SuperAdminId') for s in data.get('super_admins', []))
tenant_ids = set(t.get('TenantId') for t in data.get('tenants', []) if isinstance(t.get('TenantId'), int))
# Agents and channel partners live in their own collections with their own ID spaces
agent_ids = set(a.get('AgentId') for a in data.get('agents', []))
partner_ids = set(p.get('PartnerId') for p in data.get('channel_partners', []))
# tenant-less legacy docs are allowed (TenantId 0/missing); real tenants are 1..6

orphans = []

def check_docs(coll, id_field, id_set, label):
    bad = []
    for d in data.get(coll, []):
        v = d.get(id_field)
        if isinstance(v, int) and v > 0 and v not in id_set:
            bad.append(v)
    if bad:
        orphans.append(f'{coll}.{id_field} -> missing {label}: {sorted(set(bad))[:10]}')

check_docs('leads', 'ExecutiveId', user_ids, 'user')
check_docs('leads', 'ChannelPartnerId', partner_ids, 'partner')
check_docs('bookings', 'LeadId', set(l.get('LeadId') for l in data.get('leads', [])), 'lead')
check_docs('bookings', 'PropertyId', set(p.get('PropertyId') for p in data.get('properties', [])), 'property')
check_docs('payments', 'BookingId', set(b.get('BookingId') for b in data.get('bookings', [])), 'booking')
check_docs('followups', 'LeadId', set(l.get('LeadId') for l in data.get('leads', [])), 'lead')
check_docs('agent_attendances', 'AgentId', user_ids, 'user')
check_docs('attendance_logs', 'AgentId', user_ids, 'user')
check_docs('agent_payouts', 'AgentId', agent_ids, 'agent')
check_docs('partner_payouts', 'PartnerId', partner_ids, 'partner')
check_docs('partner_commissions', 'PartnerId', partner_ids, 'partner')

# TenantId sanity: every tenant-scoped doc should reference a real tenant or be legacy (0/missing)
tenant_scoped = ['leads', 'properties', 'bookings', 'payments', 'expenses', 'revenues',
                 'invoices', 'followups', 'notifications', 'site_visits', 'support_tickets',
                 'quotations', 'bank_accounts', 'agent_attendances', 'attendance_logs']
for c in tenant_scoped:
    bad = []
    for d in data.get(c, []):
        t = d.get('TenantId')
        if isinstance(t, int) and t > 0 and t not in tenant_ids:
            bad.append(t)
    if bad:
        orphans.append(f'{c}.TenantId -> missing tenant: {sorted(set(bad))[:10]}')

print('==== ORPHAN CHECK ====')
if orphans:
    for o in orphans:
        print('  !', o)
else:
    print('  No orphaned references found.')
print(f'  collections: {len(collections)}, total docs: {sum(summary.values())}')

# ---------- 3) Write export file ----------
export = {
    'exportDate': datetime.now(timezone.utc).isoformat(),
    'totalCollections': len(collections),
    'summary': summary,
    'data': data,
}
with open('All_MongoDB_Data.json', 'w', encoding='utf-8') as f:
    json.dump(export, f, default=str, indent=2)
print('export written to All_MongoDB_Data.json')
print('sample summary:', json.dumps({k: summary[k] for k in list(summary)[:12]}))
