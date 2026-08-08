import sys, json, os, time
sys.stdout.reconfigure(encoding='utf-8')
cs = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
from pymongo import MongoClient
from datetime import datetime, timedelta
client = MongoClient(cs, serverSelectionTimeoutMS=8000)
db = client['crm']

now = datetime.utcnow()

def num(v, default=0):
    try:
        return float(v)
    except Exception:
        return default

def next_id(coll, field):
    doc = db[coll].find_one({}, sort=[(field, -1)])
    return ((doc.get(field) or 0) + 1) if doc else 1

created = 0
for t in range(1, 7):
    plans = {p.get('BookingId'): p for p in db.payment_plans.find({'TenantId': t})}
    for b in db.bookings.find({'TenantId': t}):
        bid = b['BookingId']
        if bid in plans:
            continue
        total = num(b.get('TotalAmount')) or 5000000
        paid = num(b.get('BookingAmount')) or total * 0.1
        pid = next_id('payment_plans', 'PlanId')
        db.payment_plans.insert_one({
            'TenantId': t, 'PlanId': pid, 'BookingId': bid, 'TotalAmount': total,
            'PaidAmount': paid, 'OutstandingAmount': total - paid,
            'PlanType': 'EMI', 'CreatedOn': now - timedelta(days=3),
        })
        milestones = [('Booking Amount', 0.1), ('On Possession', 0.7), ('On Registration', 0.2)]
        for n, (mname, frac) in enumerate(milestones, start=1):
            amt = round(total * frac)
            db.payment_installments.insert_one({
                'TenantId': t, 'InstallmentId': next_id('payment_installments', 'InstallmentId'),
                'PlanId': pid, 'InstallmentNumber': n, 'MilestoneName': mname,
                'DueDate': now + timedelta(days=n * 30), 'Amount': amt,
                'PaidAmount': amt if n == 1 else 0,
                'Status': 'Paid' if n == 1 else 'Pending',
                'PaidDate': now - timedelta(days=1) if n == 1 else None,
                'CreatedOn': now - timedelta(days=3),
            })
        created += 3
        print(f'  T{t} booking {bid}: plan {pid} + 3 installments')
print('total installments created:', created)
