"""Each tenant should have exactly one Active subscription.
If a tenant has multiple Active subscriptions, keep the most recent (highest SubscriptionId)
and mark older ones as 'Expired' (they represent prior plan periods).
"""
import json, sys, time, os
sys.stdout.reconfigure(encoding='utf-8')
cs = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
from pymongo import MongoClient
db = MongoClient(cs, serverSelectionTimeoutMS=8000)['crm']

os.makedirs('scripts/backups', exist_ok=True)
bak = f'scripts/backups/active_subs_fix_{int(time.time())}.json'
changed = []

subs = list(db.tenant_subscriptions.find({'Status': 'Active'}))
from collections import defaultdict
by_tenant = defaultdict(list)
for s in subs:
    by_tenant[s.get('TenantId')].append(s)

with open(bak, 'w', encoding='utf-8') as f:
    json.dump([{k: v for k, v in s.items() if k != '_id'} for s in subs], f, default=str, indent=1)
print('backup:', bak)

for tid, group in by_tenant.items():
    if len(group) <= 1:
        continue
    group.sort(key=lambda s: s.get('SubscriptionId') or 0)
    keep = group[-1]
    for old in group[:-1]:
        db.tenant_subscriptions.update_one(
            {'_id': old['_id']},
            {'$set': {'Status': 'Expired', 'ExpiredReason': 'Auto-deactivated: duplicate active subscription (kept SubscriptionId ' + str(keep.get('SubscriptionId')) + ')'}}
        )
        changed.append((old.get('SubscriptionId'), 'Active -> Expired'))
        print(f'tenant {tid}: SubscriptionId {old.get("SubscriptionId")} (Plan {old.get("PlanId")}, Amount {old.get("Amount")}) -> Expired; kept {keep.get("SubscriptionId")}')

if not changed:
    print('no duplicates found - every tenant already has a single Active subscription')

# verify
for tid in sorted({s.get('TenantId') for s in subs}):
    act = list(db.tenant_subscriptions.find({'TenantId': tid, 'Status': 'Active'}))
    print(f'tenant {tid}: Active subscriptions = {len(act)}', [a.get('PlanId') for a in act])
