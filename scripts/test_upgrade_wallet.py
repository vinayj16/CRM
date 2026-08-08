"""Test the CalculateUpgrade endpoint (referral wallet deduction) for T1 admin."""
import sys, json, http.cookiejar, urllib.request, urllib.parse
sys.stdout.reconfigure(encoding='utf-8')
BASE = 'http://localhost:5139'

jar = http.cookiejar.CookieJar()
op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
op.addheaders = [('User-Agent', 'Mozilla/5.0')]
op.open(BASE + '/Account/Login', timeout=10)
data = urllib.parse.urlencode({'Username': 'admin', 'Password': 'Test@123'}).encode()
op.open(urllib.request.Request(BASE + '/Account/Login', data=data), timeout=15)
cs = '; '.join(f'{c.name}={c.value}' for c in jar)

# First find a SaaS plan id
req = urllib.request.Request(BASE + '/SaasSubscription/MyPlan', headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cs})
body = urllib.request.urlopen(req, timeout=15).read().decode('utf-8', 'ignore')
import re
plan_ids = re.findall(r'data-plan-id="?(\d+)"?', body)
print('plan ids on page:', plan_ids[:6])

# Find tenant's current subscription plan to pick a DIFFERENT plan for upgrade
cs_db = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
from pymongo import MongoClient
db = MongoClient(cs_db, serverSelectionTimeoutMS=8000)['crm']
sub = db.tenant_subscriptions.find_one({'TenantId': 1, 'Status': {'$in': ['Active', 'Active']}})
cur_plan = sub.get('PlanId') if sub else None
print('current PlanId for T1:', cur_plan)

# Get plans from saas_plans
plans = list(db.saas_plans.find({}))
print('saas plans:', [(p.get('PlanId'), p.get('PlanName'), p.get('Price')) for p in plans])

# Pick an upgrade target (different plan)
target = None
for p in plans:
    pid = p.get('PlanId')
    if pid != cur_plan:
        target = pid
        break

if target:
    payload = urllib.parse.urlencode({'tenantId': 1, 'newPlanId': target, 'billingCycle': 'monthly', 'upgradeType': 'existing'}).encode()
    req = urllib.request.Request(BASE + '/SaasSubscription/CalculateUpgrade', data=payload, method='POST',
                                 headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cs,
                                          'Content-Type': 'application/x-www-form-urlencoded'})
    try:
        r = urllib.request.urlopen(req, timeout=20)
        resp = json.loads(r.read().decode('utf-8', 'ignore'))
        print('CalculateUpgrade response keys:', list(resp.keys()))
        calc = resp.get('calculation') or {}
        print('rewardPoints (referral wallet):', calc.get('rewardPoints'))
        print('adjustedAmount:', calc.get('adjustedAmount'))
        print('paymentRequired:', calc.get('paymentRequired'))
        print('upgradeType:', resp.get('upgradeType'))
    except urllib.error.HTTPError as e:
        print('HTTPError', e.code, e.read().decode('utf-8', 'ignore')[:300])
else:
    print('no upgrade target plan found')
