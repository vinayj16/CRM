import sys, json, os, time
sys.stdout.reconfigure(encoding='utf-8')
cs = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
from pymongo import MongoClient
from datetime import datetime, timedelta
client = MongoClient(cs, serverSelectionTimeoutMS=8000)
db = client['crm']

# Admin-visible T1 leads = ChannelPartnerId is null/None
leads = list(db.leads.find({'TenantId': 1, 'ChannelPartnerId': None}).sort('LeadId', 1).limit(8))
print('T1 admin-visible leads available:', len(leads))

# Backup
os.makedirs('scripts/backups', exist_ok=True)
bak = f'scripts/backups/followup_seed_{int(time.time())}.json'
with open(bak, 'w', encoding='utf-8') as f:
    json.dump(leads, f, default=str, indent=1)
print('backup:', bak)

# Seed FollowUpDate spread over the next 7 days
now = datetime.utcnow()
for i, lead in enumerate(leads[:6]):
    d = (now + timedelta(days=i % 7)).replace(hour=11, minute=0, second=0, microsecond=0)
    db.leads.update_one({'_id': lead['_id']}, {'$set': {'FollowUpDate': d}})
    print("  LeadId=%s %s FollowUpDate -> %s" % (lead['LeadId'], lead['Name'][:30], d.date()))

# Also seed followup docs with unique FollowUpId (max+1)
max_id = db.followups.find_one({}, sort=[('FollowUpId', -1)])
next_id = (max_id.get('FollowUpId') or 0) + 1 if max_id else 1
for lead in leads[:3]:
    db.followups.insert_one({
        'FollowUpId': next_id,
        'TenantId': 1,
        'LeadId': lead['LeadId'],
        'FollowUpDate': now + timedelta(days=1),
        'FollowUpTime': '11:00 AM',
        'Comments': 'Follow-up scheduled - demo data',
        'ExecutiveId': 42,
        'CreatedOn': now,
        'Status': 'Scheduled'
    })
    next_id += 1
print('3 followup docs seeded')
print('T1 leads with FollowUpDate now:', db.leads.count_documents({'TenantId': 1, 'FollowUpDate': {'$ne': None}}))
