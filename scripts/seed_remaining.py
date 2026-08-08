import sys, json, os, time
sys.stdout.reconfigure(encoding='utf-8')
cs = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
from pymongo import MongoClient
from datetime import datetime, timedelta
client = MongoClient(cs, serverSelectionTimeoutMS=8000)
db = client['crm']

now = datetime.utcnow()

def next_id(coll, field):
    doc = db[coll].find_one({}, sort=[(field, -1)])
    return ((doc.get(field) or 0) + 1) if doc else 1

def fnum(v, default=0):
    try:
        return float(v)
    except Exception:
        return default

users_by_t = {t: {u['Username']: u for u in db.users.find({'TenantId': t})} for t in range(1, 7)}

def admin_of(t):
    for k in ['admin', f'admin{t}']:
        if k in users_by_t[t]:
            return users_by_t[t][k]
    return list(users_by_t[t].values())[0]

def cp_of(t):
    return db.channel_partners.find_one({'TenantId': t})

def lead_of(t, n=0):
    return list(db.leads.find({'TenantId': t}))[n] if db.leads.count_documents({'TenantId': t}) else None

def prop_of(t):
    return db.properties.find_one({'TenantId': t})

def booking_of(t):
    return db.bookings.find_one({'TenantId': t})

created = {}
def ins(coll, doc):
    db[coll].insert_one(doc)
    created[coll] = created.get(coll, 0) + 1

# 1) Campaign for T1 (T2-T6 already seeded)
if db.campaigns.count_documents({'TenantId': 1}) == 0:
    ins('campaigns', {
        'TenantId': 1, 'CampaignId': next_id('campaigns', 'CampaignId'),
        'CampaignName': 'Summer Sale Drive', 'Channel': 'Email', 'Status': 'Active',
        'StartDate': now - timedelta(days=5), 'EndDate': now + timedelta(days=25),
        'Budget': 45000, 'Clicks': 320, 'LeadsGenerated': 24, 'Conversions': 5,
        'CostPerLead': 1875, 'ROI': 3.1, 'MessageTemplate': 'Summer sale on select apartments!',
        'CreatedBy': admin_of(1)['UserId'], 'CreatedOn': now - timedelta(days=5),
    })
    print('T1 campaign seeded')

# 2) Email settings per tenant (admin user)
for t in range(1, 7):
    if db.email_settings.count_documents({'UserId': admin_of(t)['UserId']}) == 0:
        ins('email_settings', {
            'EmailSettingId': next_id('email_settings', 'EmailSettingId'),
            'UserId': admin_of(t)['UserId'],
            'SmtpFrom': admin_of(t).get('Email') or f'admin{t}@crm.com',
            'SmtpPassword': 'app-password-placeholder',
            'SmtpHost': 'smtp.gmail.com', 'SmtpPort': 587, 'EnableSsl': True,
            'CreatedOn': now - timedelta(days=6),
        })
        print(f'T{t} email setting seeded')

# 3) Partner commissions (T3-T6) + fill any tenant missing
for t in range(1, 7):
    if db.partner_commissions.count_documents({'TenantId': t}) > 0:
        continue
    cp = cp_of(t)
    lead = lead_of(t)
    b = booking_of(t)
    if not cp or not lead:
        continue
    amt = fnum(b.get('BookingAmount'), 500000) if b else 500000
    ins('partner_commissions', {
        'TenantId': t, 'CommissionId': next_id('partner_commissions', 'CommissionId'),
        'PartnerId': cp.get('PartnerId') or cp.get('ChannelPartnerId'),
        'LeadId': lead['LeadId'], 'BookingId': b.get('BookingId') if b else None,
        'BookingAmount': amt, 'CommissionPercentage': 5,
        'CommissionAmount': amt * 0.05, 'Status': 'Approved',
        'CreatedOn': now - timedelta(days=2), 'ApprovedBy': admin_of(t)['UserId'],
        'ApprovedOn': now - timedelta(days=1),
    })
    print(f'T{t} partner commission seeded')

# 4) Partner payouts per tenant
for t in range(1, 7):
    cp = cp_of(t)
    if not cp:
        continue
    if db.partner_payouts.count_documents({'TenantId': t}) > 0:
        continue
    total_comm = 25000 + t * 1000
    ins('partner_payouts', {
        'TenantId': t, 'PayoutId': next_id('partner_payouts', 'PayoutId'),
        'PartnerId': cp.get('PartnerId') or cp.get('ChannelPartnerId'),
        'Month': 'Aug-2026', 'Year': 2026, 'FixedCommissionPerSale': 5000,
        'TotalSales': 2, 'TotalLeads': 8, 'ConvertedLeads': 2,
        'TotalCommission': total_comm, 'Amount': total_comm,
        'Status': 'Paid', 'CreatedOn': now - timedelta(days=3), 'ProcessedOn': now - timedelta(days=2),
    })
    print(f'T{t} partner payout seeded')

# 5) Chat sessions + messages (T2-T6)
for t in range(2, 7):
    if db.chat_sessions.count_documents({'TenantId': t}) > 0:
        continue
    guid = f'chat-{t}-{int(time.time())}'
    sid = next_id('chat_sessions', 'SessionId')
    ins('chat_sessions', {
        'SessionId': sid, 'SessionGuid': guid, 'session_guid': guid,
        'UserName': f'Visitor {t}', 'UserPhone': f'98765{t}0001',
        'StartedAt': now - timedelta(days=1), 'EndedAt': None,
        'Status': 'Active', 'MessageCount': 2, 'LastIntent': 'pricing',
        'IsLeadGenerated': True, 'AssignedAgentId': None, 'TenantId': t,
    })
    for sender, text, intent in [
        ('Visitor', 'What are the prices for 2BHK?', 'pricing'),
        ('Agent', 'Our 2BHK starts at 45 lakhs. Would you like a site visit?', 'site_visit'),
    ]:
        ins('chat_messages', {
            'conversation_id': guid, 'session_id': guid,
            'sender_type': sender, 'sender_id': None,
            'sender_name': 'Guest' if sender == 'Visitor' else 'Agent',
            'message_text': text, 'message_type': 'text',
            'intent': intent, 'is_read': False, 'sent_at': now - timedelta(days=1),
            'TenantId': t,
        })
    print(f'T{t} chat session + 2 messages seeded')

# 6) Property histories (T2-T6)
for t in range(2, 7):
    p = prop_of(t)
    if not p:
        continue
    if db.property_histories.count_documents({'TenantId': t}) > 0:
        continue
    for act in ['Property listed', 'Price updated', 'Unit sold']:
        ins('property_histories', {
            'TenantId': t, 'HistoryId': next_id('property_histories', 'HistoryId'),
            'PropertyId': p['PropertyId'], 'Activity': act,
            'ActivityDate': now - timedelta(days=4), 'ExecutiveId': admin_of(t)['UserId'],
        })
    print(f'T{t} property histories seeded')

print()
print('=== SUMMARY ===')
for coll, n in sorted(created.items()):
    print(f'  {coll}: +{n}')
