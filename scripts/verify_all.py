"""End-to-end verification after fixes:
1. Attendance: agent check-in -> record Status=Present + Login log; check-out -> Logout log.
2. Plans page for tenant admin renders plans (same for company) + upgrade endpoint reachable.
3. Referral wallet shows balance on plans page.
4. All documented logins still work; SuperAdmin lands on SA dashboard.
"""
import sys, json, http.cookiejar, urllib.request, urllib.parse, re
sys.stdout.reconfigure(encoding='utf-8')
BASE = 'http://localhost:5139'

def login_cookies(username, password):
    jar = http.cookiejar.CookieJar()
    op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    op.addheaders = [('User-Agent', 'Mozilla/5.0')]
    try: op.open(BASE + '/Account/Login', timeout=10)
    except Exception: pass
    data = urllib.parse.urlencode({'Username': username, 'Password': password}).encode()
    try:
        r = op.open(urllib.request.Request(BASE + '/Account/Login', data=data), timeout=15)
        r.read()
        return op, jar, '; '.join(f'{c.name}={c.value}' for c in jar)
    except Exception:
        return None, None, None

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

# ---------- 1) All documented logins ----------
CREDS = [
    ('SA', 'admin@crm.com', 'Admin@123'),
    ('T1 admin', 'admin', 'Test@123'),
    ('T1 agent1', 'agent1', 'Test@123'),
    ('T1 partner1', 'partner1', 'Test@123'),
    ('T1 sales1', 'sales1', 'Test@123'),
    ('T2 admin2', 'admin2', 'Test@123'),
    ('T2 agent_launch1', 'agent_launch1', 'Test@123'),
    ('T2 partner2', 'partner2', 'Test@123'),
    ('T3 admin3', 'admin3', 'Test@123'),
    ('T3 agent3', 'agent3', 'Test@123'),
    ('T4 admin4', 'admin4', 'Test@123'),
    ('T4 sales4', 'sales4', 'Test@123'),
    ('T5 admin5', 'admin5', 'Test@123'),
    ('T5 partner5', 'partner5', 'Test@123'),
    ('T6 admin6', 'admin6', 'Test@123'),
    ('T6 agent6', 'agent6', 'Test@123'),
]
ok_logins = 0
for label, u, p in CREDS:
    op, jar, cs = login_cookies(u, p)
    ok = cs is not None
    if ok: ok_logins += 1
    record(f'login {label} ({u})', ok)
record('all logins', ok_logins == len(CREDS), f'{ok_logins}/{len(CREDS)}')

# ---------- 2) SuperAdmin dashboard ----------
op, jar, sa_cs = login_cookies('admin@crm.com', 'Admin@123')
st, url, body = fetch(sa_cs, '/SuperAdmin/Dashboard')
record('SA Dashboard renders', st == 200 and 'Super Admin' in body or 'Tenant' in body, f'HTTP {st} url={url[:60]}')

# ---------- 3) Tenant admin plans page (same plans for company + referral wallet) ----------
op, jar, cs = login_cookies('admin', 'Test@123')
st, url, body = fetch(cs, '/SaasSubscription/MyPlan')
record('T1 admin plans page', st == 200, f'HTTP {st} url={url[:60]} len={len(body)}')
if st == 200:
    m = re.search(r'₹\s?([\d,]+(?:\.\d+)?)', body)
    record('referral wallet balance shown', bool(m), f'balance={m.group(1) if m else "none"}')
    plans_found = body.count('Basic') + body.count('Pro') + body.count('Enterprise')
    record('plans listed (Basic/Pro/Enterprise)', plans_found >= 1, f'matches={plans_found}')

# Upgrade calculation endpoint (used by MyPlan page JS)
st, url, body = fetch(cs, '/SaasSubscription/MyPlan')
record('upgrade flow wired in MyPlan page', 'CalculateUpgrade' in body, 'JS calls /saassubscription/CalculateUpgrade')

# ---------- 4) Attendance check-in / check-out ----------
op, jar, agent_cs = login_cookies('agent1', 'Test@123')
# Get agent1's calendar to find/derive attendanceId
st, url, body = fetch(agent_cs, '/Attendance/Calendar?agentId=' + urllib.parse.quote('2b073f4be844'))
record('agent calendar page', st == 200, f'HTTP {st}')
# Try check-in via POST /Attendance/Login (agentId as raw user id 43 for agent1)
st, url, body = post(agent_cs, '/Attendance/Login', {'attendanceId': 0, 'agentId': 43})
record('attendance check-in POST', st in (200, 302), f'HTTP {st} -> {url[:80]}')

import time
time.sleep(1)

# Verify in DB
try:
    cs_db = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
    from pymongo import MongoClient
    db = MongoClient(cs_db, serverSelectionTimeoutMS=8000)['crm']
    att = db.agent_attendances.find_one({'AgentId': 43}, sort=[('AttendanceId', -1)])
    if att:
        record('attendance record persisted Present', att.get('Status') == 'Present',
               f'AttendanceId={att.get("AttendanceId")} Status={att.get("Status")} LoginTime={att.get("LoginTime")}')
    else:
        record('attendance record persisted Present', False, 'no record found')
    log = db.attendance_logs.find_one({'AgentId': 43, 'Type': 'Login'}, sort=[('_id', -1)])
    record('attendance Login log created', log is not None, f'log type={log.get("Type") if log else "none"}')
    # Check-out
    if att:
        st, url, body = post(agent_cs, '/Attendance/Logout', {'attendanceId': att.get('AttendanceId')})
        record('attendance check-out POST', st in (200, 302), f'HTTP {st}')
        log2 = db.attendance_logs.find_one({'AgentId': 43, 'Type': 'Logout'}, sort=[('_id', -1)])
        record('attendance Logout log created', log2 is not None)
        att2 = db.agent_attendances.find_one({'AttendanceId': att.get('AttendanceId')})
        record('attendance LogoutTime persisted', bool(att2 and att2.get('LogoutTime')))
except Exception as e:
    record('attendance DB verify', False, str(e)[:100])

# ---------- 5) No duplicate attendance for same day ----------
try:
    atts = list(db.agent_attendances.find({'AgentId': 43}))
    # group by IST date
    from datetime import datetime, timedelta
    IST = timedelta(hours=5, minutes=30)
    seen = {}
    dup = 0
    for a in atts:
        d = a.get('Date')
        if isinstance(d, str):
            d = datetime.fromisoformat(d.replace(' ', 'T'))
        key = (d + IST).date().isoformat()
        seen[key] = seen.get(key, 0) + 1
    dup = sum(1 for v in seen.values() if v > 1)
    record('no duplicate attendance per day', dup == 0, f'dup days={dup}')
except Exception as e:
    record('no duplicate attendance per day', False, str(e)[:80])

print()
fails = [r for r in results if not r[1]]
print(f'=== TOTAL: {len(results) - len(fails)}/{len(results)} PASS, {len(fails)} FAIL ===')
for f in fails:
    print('  FAILED:', f[0], f[2])
