"""Fix orphaned ExecutiveId references on leads so every lead is linked to a real user."""
import json, sys, time, os
sys.stdout.reconfigure(encoding='utf-8')
cs = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
from pymongo import MongoClient
db = MongoClient(cs, serverSelectionTimeoutMS=8000)['crm']

# Lead 4 (partner lead, partner 1) -> partner1 user (UserId 44)
# Lead 113 (flow test lead, tenant 1, no partner) -> admin (UserId 1)
fixes = [(4, 44), (113, 1)]

os.makedirs('scripts/backups', exist_ok=True)
bak = f'scripts/backups/orphan_lead_fix_{int(time.time())}.json'
orig = []
for lead_id, _ in fixes:
    l = db.leads.find_one({'LeadId': lead_id})
    if l:
        orig.append({k: v for k, v in l.items() if k != '_id'})
with open(bak, 'w', encoding='utf-8') as f:
    json.dump(orig, f, default=str, indent=1)
print('backup:', bak)

for lead_id, exec_id in fixes:
    lead = db.leads.find_one({'LeadId': lead_id})
    if not lead:
        print(f'lead {lead_id} not found')
        continue
    old = lead.get('ExecutiveId')
    db.leads.update_one({'LeadId': lead_id}, {'$set': {'ExecutiveId': exec_id}})
    print(f'lead {lead_id}: ExecutiveId {old} -> {exec_id}')
