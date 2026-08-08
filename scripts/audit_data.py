import sys, json
sys.stdout.reconfigure(encoding='utf-8')
cs = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
from pymongo import MongoClient
client = MongoClient(cs, serverSelectionTimeoutMS=8000)
db = client['crm']

COLLECTIONS = [
    'leads', 'properties', 'property_flats', 'builders', 'bookings', 'quotations',
    'quotation_items', 'invoices', 'payments', 'payment_installments', 'payment_plans',
    'expenses', 'revenues', 'agents', 'channel_partners', 'users', 'user_profiles',
    'settings', 'branding', 'bank_accounts', 'tenant_subscriptions', 'notifications',
    'followups', 'site_visits', 'campaigns', 'testimonials', 'support_tickets',
    'email_settings', 'email_templates', 'audit_logs', 'role_permissions',
    'property_histories', 'lead_notes', 'lead_histories', 'lead_logs',
    'partner_commissions', 'partner_payouts', 'agent_payouts', 'company_messages',
    'chat_sessions', 'referral_earnings', 'webhook_leads', 'knowledge_base',
]

print('=== DATA COMPLETENESS AUDIT (docs per collection per tenant) ===')
print()
header = 'collection'.ljust(28) + ''.join(f'T{t}'.rjust(7) for t in range(1, 7)) + '   TOTAL'
print(header)
print('-' * len(header))
for coll in COLLECTIONS:
    if coll not in db.list_collection_names():
        print(coll.ljust(28) + '  (no collection)')
        continue
    total = db[coll].count_documents({})
    row = coll.ljust(28)
    for t in range(1, 7):
        n = db[coll].count_documents({'TenantId': t})
        row += str(n).rjust(7)
    # also count docs with no TenantId or TenantId=0
    no_tid = db[coll].count_documents({'TenantId': {'$in': [None, 0]}})
    row += '   ' + str(total)
    if no_tid:
        row += f'  (noTid:{no_tid})'
    print(row)

print()
print('=== per-tenant gaps (collections with 0 docs for a tenant but non-zero elsewhere) ===')
relevant = [c for c in COLLECTIONS if c in db.list_collection_names()]
for t in range(1, 7):
    empty = [c for c in relevant if db[c].count_documents({'TenantId': t}) == 0]
    print(f'T{t}: empty collections: {empty}')
