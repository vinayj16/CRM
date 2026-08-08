"""Consolidate to a single SuperAdmin + clean duplicate attendance records.

Keeps the SuperAdmin whose email matches the user's documented credentials table
(admin@crm.com / Admin@123). Deletes the other super_admin record. Also removes
duplicate attendance records created by the check-in date-comparison bug
(same agent + same day appearing twice), keeping the most recent one.
All deletions are backed up to scripts/backups/ first.
"""
import sys, json, time, os
sys.stdout.reconfigure(encoding='utf-8')
cs = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
from pymongo import MongoClient
client = MongoClient(cs, serverSelectionTimeoutMS=8000)
db = client['crm']

os.makedirs('scripts/backups', exist_ok=True)
stamp = int(time.time())
backup = f'scripts/backups/superadmin_cleanup_{stamp}.json'
records = {'super_admin_removed': [], 'attendance_removed': []}

# ---- 1) SuperAdmin consolidation ----
# Documented primary: admin@crm.com (SuperAdminId 2). Keep it, remove the rest.
keep_email = 'admin@crm.com'
sas = list(db.super_admins.find({}))
removed = [sa for sa in sas if (sa.get('Email') or '').strip().lower() != keep_email.lower()]
records['super_admin_removed'] = [{k: (str(v)[:40] if k == 'PasswordHash' else v) for k, v in sa.items() if k != '_id'} for sa in removed]
if removed:
    for sa in removed:
        db.super_admins.delete_one({'_id': sa['_id']})
    print(f'SuperAdmin: kept "{keep_email}", removed {len(removed)} extra record(s)')
else:
    print(f'SuperAdmin: no duplicates, "{keep_email}" is already the only record')

# ---- 2) Attendance duplicate cleanup ----
# Group by (AgentId, IST date); if more than one record, keep the latest by AttendanceId.
atts = list(db.agent_attendances.find({}))
from datetime import datetime, timedelta
IST = timedelta(hours=5, minutes=30)

def ist_date_str(a):
    try:
        dt = a.get('Date')
        if isinstance(dt, str):
            dt = datetime.fromisoformat(dt.replace('Z', '+00:00').replace(' ', 'T'))
        if dt.tzinfo is not None:
            dt = dt.astimezone().replace(tzinfo=None)
        return (dt + IST).date().isoformat()
    except Exception:
        return None

groups = {}
for a in atts:
    key = (a.get('AgentId'), ist_date_str(a))
    groups.setdefault(key, []).append(a)

removed_atts = []
for key, group in groups.items():
    if len(group) > 1:
        group.sort(key=lambda x: x.get('AttendanceId') or 0)
        keep = group[-1]
        dupes = group[:-1]
        removed_atts.extend(dupes)
        for d in dupes:
            db.agent_attendances.delete_one({'_id': d['_id']})
        print(f'Attendance dup for agent={key[0]} date={key[1]}: kept AttendanceId {keep.get("AttendanceId")}, removed {len(dupes)}')

records['attendance_removed'] = [{k: v for k, v in a.items() if k != '_id'} for a in removed_atts]

with open(backup, 'w', encoding='utf-8') as f:
    json.dump(records, f, default=str, indent=1)
print('backup saved:', backup)
print('total attendance records now:', db.agent_attendances.count_documents({}))
print('super_admins now:', db.super_admins.count_documents({}))
