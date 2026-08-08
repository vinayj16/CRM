import sys, json, os, time
sys.stdout.reconfigure(encoding='utf-8')
cs = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
from pymongo import MongoClient
from datetime import datetime, timedelta
client = MongoClient(cs, serverSelectionTimeoutMS=8000)
db = client['crm']

now = datetime.utcnow()
today = now.date()

def next_id(coll, field):
    doc = db[coll].find_one({}, sort=[(field, -1)])
    return ((doc.get(field) or 0) + 1) if doc else 1

def backup(name, docs):
    os.makedirs('scripts/backups', exist_ok=True)
    p = f'scripts/backups/{name}_{int(time.time())}.json'
    with open(p, 'w', encoding='utf-8') as f:
        json.dump(docs, f, default=str, indent=1)
    print('  backup ->', p)

# ============ Gather context per tenant ============
tenants = {t['TenantId']: t for t in db.tenants.find({})}
users_by_t = {}
for t in range(1, 7):
    users_by_t[t] = {u['Username']: u for u in db.users.find({'TenantId': t})}

def admin_of(t):
    for k in ['admin', f'admin{t}']:
        if k in users_by_t[t]:
            return users_by_t[t][k]
    return list(users_by_t[t].values())[0]

def props_of(t):
    return list(db.properties.find({'TenantId': t}))

def leads_of(t):
    return list(db.leads.find({'TenantId': t}))

def agents_of(t):
    return list(db.agents.find({'TenantId': t}))

def bookings_of(t):
    return list(db.bookings.find({'TenantId': t}))

created = {}

def ins(coll, doc):
    db[coll].insert_one(doc)
    created[coll] = created.get(coll, 0) + 1

print('=== SEEDING PER-TENANT DATA (T2-T6 + extras) ===')
for t in [2, 3, 4, 5, 6]:
    print(f'--- Tenant {t}: {tenants[t].get("CompanyName")} ---')
    admin = admin_of(t)
    props = props_of(t)
    leads = leads_of(t)
    agents = agents_of(t)
    bookings = bookings_of(t)

    # 1) Expenses (2 each)
    for i, (etype, cat, desc, amt) in enumerate([
        ('Marketing', 'Digital Ads', 'Google Ads campaign - lead generation', 25000),
        ('Maintenance', 'Office', 'Office maintenance and upkeep', 8500),
    ]):
        ins('expenses', {
            'TenantId': t, 'ExpenseId': next_id('expenses', 'ExpenseId'),
            'Type': etype, 'Category': cat, 'Description': desc,
            'Amount': amt, 'Date': now - timedelta(days=i * 5),
        })

    # 2) Revenue (1 each, linked to booking)
    if bookings:
        b = bookings[0]
        ins('revenues', {
            'TenantId': t, 'RevenueId': next_id('revenues', 'RevenueId'),
            'Type': 'Booking', 'Source': 'Booking', 'Description': f'Booking {b.get("BookingNumber", "BK-" + str(b["BookingId"]))} revenue',
            'Amount': b.get('BookingAmount') or 500000, 'Date': now - timedelta(days=3),
        })
    else:
        ins('revenues', {
            'TenantId': t, 'RevenueId': next_id('revenues', 'RevenueId'),
            'Type': 'Booking', 'Source': 'Booking', 'Description': 'Booking revenue',
            'Amount': 500000, 'Date': now - timedelta(days=3),
        })

    # 3) Quotation + items (1 each)
    if leads and props:
        lead = leads[0]
        prop = props[0]
        base = 5500000
        qid = next_id('quotations', 'QuotationId')
        ins('quotations', {
            'TenantId': t, 'QuotationId': qid,
            'QuotationNumber': f'QT-{t}-{1000 + qid}',
            'LeadId': lead['LeadId'], 'PropertyId': prop['PropertyId'],
            'QuotationDate': now - timedelta(days=2), 'ValidUntil': now + timedelta(days=28),
            'BasePrice': base, 'TotalAmount': base, 'DiscountAmount': 0,
            'TaxAmount': base * 0.05, 'GrandTotal': base * 1.05,
            'Status': 'Sent', 'Notes': 'Demo quotation', 'CreatedBy': admin['UserId'],
            'CreatedOn': now - timedelta(days=2),
        })
        for itype, descr, amt in [('Base', 'Base price', base), ('Legal', 'Legal charges', 50000)]:
            ins('quotation_items', {
                'TenantId': t, 'ItemId': next_id('quotation_items', 'ItemId'),
                'QuotationId': qid, 'ItemType': itype, 'Description': descr,
                'Amount': amt, 'Quantity': 1, 'Total': amt,
            })

    # 4) Bank account (1 each)
    ins('bank_accounts', {
        'TenantId': t, 'AccountId': next_id('bank_accounts', 'AccountId'),
        'AccountHolderName': tenants[t].get('CompanyName', f'Company {t}'),
        'AccountNumber': str(9000000000 + t * 11111)[:11],
        'BankName': 'HDFC Bank', 'IFSCCode': 'HDFC0001234',
        'BranchName': 'Main Branch', 'AccountType': 'Current',
        'IsActive': True, 'CreatedOn': now - timedelta(days=10),
    })

    # 5) Property flats (2 each)
    if props:
        p = props[0]
        for i, (flat, bhk, price) in enumerate([('Tower-A 101', '2BHK', 4800000), ('Tower-A 102', '3BHK', 6200000)]):
            ins('property_flats', {
                'TenantId': t, 'FlatId': next_id('property_flats', 'FlatId'),
                'PropertyId': p['PropertyId'], 'BlockName': 'Tower-A', 'FloorName': f'Floor {i + 1}',
                'FlatName': flat, 'BHK': bhk, 'PropertyType': p.get('PropertyType', 'Apartment'),
                'AreaSqft': 1100 + i * 200, 'Price': price, 'FlatStatus': 'Available',
                'Status': 'Available', 'IsActive': True, 'CreatedOn': now - timedelta(days=8),
            })

    # 6) Lead FollowUpDate + followups (2 leads)
    for i, lead in enumerate(leads[:2]):
        d = (now + timedelta(days=i % 6)).replace(hour=10, minute=30, second=0, microsecond=0)
        db.leads.update_one({'_id': lead['_id']}, {'$set': {'FollowUpDate': d}})
    for i, lead in enumerate(leads[:2]):
        ins('followups', {
            'TenantId': t, 'FollowUpId': next_id('followups', 'FollowUpId'),
            'LeadId': lead['LeadId'], 'Stage': lead.get('Stage', 'FollowUp'),
            'Status': 'Scheduled', 'FollowUpDate': now + timedelta(days=i + 1),
            'FollowUpTime': '10:30 AM', 'Comments': 'Scheduled follow-up - demo',
            'ExecutiveId': admin['UserId'],
        })

    # 7) Site visit (1 each)
    if leads and props:
        ins('site_visits', {
            'TenantId': t, 'SiteVisitId': next_id('site_visits', 'SiteVisitId'),
            'LeadId': leads[0]['LeadId'], 'LeadName': leads[0].get('Name'),
            'ExecutiveId': admin['UserId'], 'ExecutiveName': admin.get('Username'),
            'PropertyId': props[0]['PropertyId'], 'PropertyName': props[0].get('PropertyName'),
            'ScheduledDate': now + timedelta(days=3), 'TimeSlot': '11:00 AM',
            'Status': 'Scheduled', 'Vehicle': 'Car', 'CreatedBy': admin['UserId'],
            'CreatedOn': now - timedelta(days=1),
        })

    # 8) Notifications (3 each)
    for ntitle, nmsg, ntype in [
        ('New lead assigned', 'A new lead has been assigned to you.', 'LeadAssigned'),
        ('Follow-up due today', 'You have follow-ups scheduled for today.', 'FollowUpDue'),
        ('Booking confirmed', 'A new booking was confirmed in your project.', 'BookingCreated'),
    ]:
        ins('notifications', {
            'TenantId': t, 'NotificationId': next_id('notifications', 'NotificationId'),
            'Title': ntitle, 'Message': nmsg, 'Type': ntype, 'IsRead': False,
            'CreatedOn': now - timedelta(hours=2), 'UserId': admin['UserId'],
            'Priority': 'Normal',
        })

    # 9) Support ticket (1 each)
    ins('support_tickets', {
        'TenantId': t, 'TicketId': next_id('support_tickets', 'TicketId'),
        'Subject': 'Requesting help with invoice GST configuration',
        'Description': 'Need assistance setting up GST rates on invoices for our company.',
        'Category': 'Technical', 'Priority': 'Normal', 'Status': 'Open',
        'CreatedBy': admin['UserId'], 'CreatedByUserId': admin['UserId'],
        'CreatedByUsername': admin.get('Username'), 'CreatedByEmail': admin.get('Email'),
        'CreatedOn': now - timedelta(days=1), 'Replies': [],
    })

    # 10) Agent payout (1 each)
    if agents:
        ag = agents[0]
        payout = 15000 + t * 1000
        ins('agent_payouts', {
            'TenantId': t, 'PayoutId': next_id('agent_payouts', 'PayoutId'),
            'AgentId': ag['AgentId'], 'Month': 'Aug-2026', 'Year': 2026,
            'BaseSalary': 12000, 'CommissionAmount': payout - 12000,
            'FinalPayout': payout, 'TotalSales': 2, 'WorkingDays': 22, 'PresentDays': 22,
            'Status': 'Paid', 'Type': 'Monthly', 'Amount': payout, 'Period': 'Aug-2026',
            'CreatedOn': now - timedelta(days=4),
        })

    # 11) Lead notes / histories / logs (on partner-visible leads)
    for lead in leads[:2]:
        ins('lead_notes', {
            'TenantId': t, 'NoteId': next_id('lead_notes', 'NoteId'),
            'LeadId': lead['LeadId'], 'NoteText': 'Customer interested in 2BHK facing garden.',
            'ExecutiveId': admin['UserId'], 'CreatedOn': now - timedelta(days=2),
        })
        for act in ['Lead created', 'Follow-up scheduled', 'Site visit requested']:
            ins('lead_histories', {
                'TenantId': t, 'HistoryId': next_id('lead_histories', 'HistoryId'),
                'LeadId': lead['LeadId'], 'Activity': act,
                'ActivityDate': now - timedelta(days=2), 'ExecutiveId': admin['UserId'],
            })
        ins('lead_logs', {
            'TenantId': t, 'LogId': next_id('lead_logs', 'LogId'),
            'LeadId': lead['LeadId'], 'LogText': 'Lead status updated to Active',
            'LogDate': now - timedelta(days=1), 'ExecutiveId': admin['UserId'],
        })

    # 12) Company message (1 each)
    ins('company_messages', {
        'TenantId': t, 'SenderId': admin['UserId'], 'SenderName': admin.get('Username'),
        'SenderRole': 'Admin', 'RecipientId': 0, 'RecipientName': '',
        'MessageText': 'Welcome to the team! Important updates will be posted here.',
        'IsRead': False, 'SentAt': now - timedelta(days=1), 'IsDeleted': False,
    })

    # 13) Webhook lead (1 each)
    ins('webhook_leads', {
        'TenantId': t, 'WebhookLeadId': next_id('webhook_leads', 'WebhookLeadId'),
        'Name': f'Webhook Lead {t}', 'Email': f'webhook{t}@example.com',
        'Contact': '9876500000', 'PreferredLocation': 'City Center',
        'BHK': '2BHK', 'Budget': '45-60 Lakhs', 'CompanyName': tenants[t].get('CompanyName'),
        'Requirements': 'Interested in 2BHK apartments', 'LeadType': 'Express Interest',
        'Status': 'Pending', 'CreatedOn': now - timedelta(days=1), 'Source': 'Website',
    })

    # 14) Campaign (1 each)
    ins('campaigns', {
        'TenantId': t, 'CampaignId': next_id('campaigns', 'CampaignId'),
        'CampaignName': f'Festive Offer {t}', 'Channel': 'WhatsApp', 'Status': 'Active',
        'StartDate': now - timedelta(days=7), 'EndDate': now + timedelta(days=20),
        'Budget': 30000, 'Clicks': 240, 'LeadsGenerated': 18, 'Conversions': 3,
        'CostPerLead': 1666, 'ROI': 2.4, 'MessageTemplate': 'Special festive pricing on select units!',
        'CreatedBy': admin['UserId'], 'CreatedOn': now - timedelta(days=7),
    })

# ============ T2: fix orphaned lead assignments (ExecutiveId=1 -> T2 users) ============
print('\\n--- T2: reassign orphaned lead ExecutiveIds ---')
t2_leads = list(db.leads.find({'TenantId': 2, 'ChannelPartnerId': None}))
assign = {47: 2, 48: 2, 50: 2, 15: 1, 16: 1}  # username->count by UserId
targets = []
for uid, cnt in assign.items():
    targets += [uid] * cnt
i = 0
changed = 0
for lead in t2_leads:
    if i >= len(targets):
        break
    old = lead.get('ExecutiveId')
    if old != targets[i]:
        db.leads.update_one({'_id': lead['_id']}, {'$set': {'ExecutiveId': targets[i]}})
        changed += 1
    i += 1
print(f'  reassigned {changed} T2 leads to tenant users (agent_launch1, agent_launch2, sales_launch, sales2, agent2)')

# ============ Payment plans + installments for bookings lacking a plan ============
print('\\n--- Payment plans & installments for bookings ---')
for t in range(1, 7):
    plans = {p.get('BookingId'): p for p in db.payment_plans.find({'TenantId': t})}
    for b in bookings_of(t):
        bid = b['BookingId']
        if bid in plans:
            continue
        total = b.get('TotalAmount') or 5000000
        pid = next_id('payment_plans', 'PlanId')
        ins('payment_plans', {
            'TenantId': t, 'PlanId': pid, 'BookingId': bid, 'TotalAmount': total,
            'PaidAmount': b.get('BookingAmount') or total * 0.1,
            'OutstandingAmount': total - (b.get('BookingAmount') or total * 0.1),
            'PlanType': 'EMI', 'CreatedOn': now - timedelta(days=3),
        })
        milestones = [('Booking Amount', 0.1), ('On Possession', 0.7), ('On Registration', 0.2)]
        for num, (mname, frac) in enumerate(milestones, start=1):
            ins('payment_installments', {
                'TenantId': t, 'InstallmentId': next_id('payment_installments', 'InstallmentId'),
                'PlanId': pid, 'InstallmentNumber': num, 'MilestoneName': mname,
                'DueDate': now + timedelta(days=num * 30), 'Amount': round(total * frac),
                'PaidAmount': round(total * frac) if num == 1 else 0,
                'Status': 'Paid' if num == 1 else 'Pending',
                'PaidDate': now - timedelta(days=1) if num == 1 else None,
                'CreatedOn': now - timedelta(days=3),
            })
        print(f'  T{t} booking {bid}: plan + {len(milestones)} installments')

print('\\n=== SUMMARY (docs created) ===')
for coll, n in sorted(created.items()):
    print(f'  {coll}: +{n}')
