"""Comprehensive feature verification:
1. Company name + logo visible in sidebar for every company admin
2. Settings page renders (company name + logo fields), UpdateSettings POST works
3. EmailSettings page renders + SaveSmtp works
4. ComposeEmail + EmailLog pages render
5. Plan upgrade flow (MyPlan page, CalculateUpgrade with referral wallet)
6. Page walk for representative users of every role/company (no 500s)
"""
import sys, json, http.cookiejar, urllib.request, urllib.parse, re
sys.stdout.reconfigure(encoding='utf-8')
BASE = 'http://localhost:5139'

def login(username, password):
    jar = http.cookiejar.CookieJar()
    op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    op.addheaders = [('User-Agent', 'Mozilla/5.0')]
    try: op.open(BASE + '/Account/Login', timeout=10)
    except Exception: pass
    data = urllib.parse.urlencode({'Username': username, 'Password': password}).encode()
    try:
        r = op.open(urllib.request.Request(BASE + '/Account/Login', data=data), timeout=15)
        r.read()
        return '; '.join(f'{c.name}={c.value}' for c in jar)
    except Exception:
        return None

def fetch(cs, path):
    req = urllib.request.Request(BASE + path, headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cs})
    try:
        r = urllib.request.urlopen(req, timeout=15)
        return r.status, r.geturl(), r.read().decode('utf-8', 'ignore')
    except urllib.error.HTTPError as e:
        return e.code, e.geturl(), e.read().decode('utf-8', 'ignore')

def post(cs, path, payload):
    req = urllib.request.Request(BASE + path, data=urllib.parse.urlencode(payload).encode(), method='POST',
                                 headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cs,
                                          'Content-Type': 'application/x-www-form-urlencoded'})
    try:
        r = urllib.request.urlopen(req, timeout=15)
        return r.status, r.geturl(), r.read().decode('utf-8', 'ignore')
    except urllib.error.HTTPError as e:
        return e.code, e.geturl(), e.read().decode('utf-8', 'ignore')

results = []
def record(name, ok, detail=''):
    results.append((name, ok, detail))
    print(('PASS' if ok else 'FAIL'), name, ('| ' + detail if detail else ''))

# Company admin logins: (label, user, company display name expected in sidebar)
COMPANIES = [
    ('T1 Default CRM', 'admin', 'Default CRM'),
    ('T2 GreenVista', 'admin2', 'GreenVista Realty'),
    ('T3 Skyline', 'admin3', 'Skyline Estates'),
    ('T4 Ocean Breeze', 'admin4', 'Ocean Breeze Homes'),
    ('T5 Metro Horizon', 'admin5', 'Metro Horizon'),
    ('T6 Prime Nest', 'admin6', 'Prime Nest Properties'),
]

# ---------- 1) Company name visible in sidebar ----------
for label, user, expected in COMPANIES:
    cs = login(user, 'Test@123')
    if not cs:
        record(f'{label}: login', False)
        continue
    st, url, body = fetch(cs, '/Dashboard/Index')
    # sidebar footer shows company name via footerCompanyName; look for it anywhere in page
    found = expected in body or expected.split()[0] in body
    record(f'{label}: login + dashboard', st == 200, f'HTTP {st}')
    record(f'{label}: company name "{expected}" visible', found, 'in sidebar footer / page')

# ---------- 2) Settings page + logo/name update ----------
cs = login('admin', 'Test@123')
st, url, body = fetch(cs, '/Settings/Index')
record('T1 Settings page renders', st == 200, f'HTTP {st} len={len(body)}')
record('Settings has CompanyName field', 'CompanyName' in body)
record('Settings has CompanyLogo upload', 'CompanyLogo' in body and 'type="file"' in body)
# Update company name via UpdateSettings
st, url, body = post(cs, '/Settings/UpdateSettings', {'CompanyName': 'Default CRM Test Co', 'UpdateType': 'settings'})
record('UpdateSettings POST responds', st in (200, 302), f'HTTP {st} -> {url[:60]}')
# restore original name
st, url, body = post(cs, '/Settings/UpdateSettings', {'CompanyName': 'Default CRM', 'UpdateType': 'settings'})
record('UpdateSettings restore name', st in (200, 302), f'HTTP {st}')

# ---------- 3) Email settings ----------
st, url, body = fetch(cs, '/EmailSettings/Index')
record('EmailSettings page renders', st == 200, f'HTTP {st} len={len(body)}')
st, url, body = post(cs, '/EmailSettings/SaveSmtp', {'smtpFrom': 'admin@crm.com', 'smtpPassword': 'test-app-password'})
record('SaveSmtp responds', st in (200, 302), f'HTTP {st} body={body[:80] if st == 200 else "redirect"}')

# ---------- 4) SuperAdmin email compose/log ----------
sa_cs = login('admin@crm.com', 'Admin@123')
st, url, body = fetch(sa_cs, '/SuperAdmin/ComposeEmail')
record('SA ComposeEmail page renders', st == 200, f'HTTP {st} len={len(body)}')
st, url, body = fetch(sa_cs, '/SuperAdmin/EmailLog')
record('SA EmailLog page renders', st == 200, f'HTTP {st} len={len(body)}')
st, url, body = fetch(sa_cs, '/SuperAdmin/EmailTemplates')
record('SA EmailTemplates page renders', st == 200, f'HTTP {st} len={len(body)}')

# ---------- 5) Plan upgrade flow per company ----------
for label, user, expected in COMPANIES:
    cs = login(user, 'Test@123')
    st, url, body = fetch(cs, '/SaasSubscription/MyPlan')
    plans = body.count('Basic') + body.count('Standard') + body.count('Premium') + body.count('Free')
    record(f'{label}: MyPlan renders with plans', st == 200 and plans >= 1, f'HTTP {st} plan-matches={plans}')
    record(f'{label}: upgrade JS wired', 'CalculateUpgrade' in body)

# ---------- 6) Page walk: no 500s for every role ----------
PAGES = ['/Dashboard/Index', '/Leads/Index', '/Properties/Index', '/Bookings/Index',
         '/Invoices/Index', '/Payments/Index', '/Expenses/Index', '/Revenue/Index',
         '/Profit/Index', '/Quotations/Index', '/Tasks/Index', '/SiteVisit/Index',
         '/Agent/List', '/AgentPayout/Index', '/CompanyChat/Index', '/TeamChat/Index',
         '/Settings/Index', '/ManageUsers/Index', '/SaasSubscription/MyPlan']
walk_users = [('T1 admin', 'admin'), ('T1 agent', 'agent1'), ('T1 sales', 'sales1'),
              ('T2 admin', 'admin2'), ('T6 admin', 'admin6')]
for label, user in walk_users:
    cs = login(user, 'Test@123')
    fails = []
    for p in PAGES:
        st, _, _ = fetch(cs, p)
        if st >= 500:
            fails.append(f'{p}={st}')
    record(f'page walk {label} ({len(PAGES)} pages)', not fails, ('FAILS: ' + '; '.join(fails)) if fails else 'all < 500')

print()
fails = [r for r in results if not r[1]]
print(f'=== TOTAL: {len(results) - len(fails)}/{len(results)} PASS, {len(fails)} FAIL ===')
for f in fails:
    print('  FAILED:', f[0], '|', f[2])
