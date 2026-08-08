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
    except Exception:
        return None
    return '; '.join(f'{c.name}={c.value}' for c in jar)

def fetch(cs, path, method='GET', payload=None):
    headers = {'User-Agent': 'Mozilla/5.0', 'Cookie': cs}
    data = None
    if payload is not None:
        data = urllib.parse.urlencode(payload).encode()
        headers['Content-Type'] = 'application/x-www-form-urlencoded'
    req = urllib.request.Request(BASE + path, data=data, method=method, headers=headers)
    try:
        r = urllib.request.urlopen(req, timeout=15)
        return r.status, r.read().decode('utf-8', 'ignore')
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode('utf-8', 'ignore')

print('=== 1) PLANS PAGES (T1 admin) ===')
cs = login_cookies('admin', 'Test@123')
for path in ['/Subscription/Plans', '/Subscription/MyPlan', '/SaasSubscription/MyPlan']:
    st, h = fetch(cs, path)
    title = re.search(r'<title>(.*?)</title>', h, re.S)
    t = title.group(1).strip()[:45] if title else '?'
    has_plan_names = any(k in h for k in ['Free', 'Basic', 'Standard', 'Premium'])
    print(f'  {path:28s} HTTP {st} | {t} | plan names present: {has_plan_names} | len={len(h)}')

print()
print('=== 2) REFERRAL WALLET (T1 admin) ===')
st, h = fetch(cs, '/SaasSubscription/GetAdminReferralWallet')
try:
    j = json.loads(h)
    print(f'  HTTP {st} | success={j.get("success")} | balance={j.get("balance")} | referralCode={j.get("referralCode")} | referrals={len(j.get("referrals") or [])}')
except Exception as e:
    print(f'  HTTP {st} | raw: {h[:200]}')

print()
print('=== 3) ATTENDANCE (agent1 check-in / check-out) ===')
cs2 = login_cookies('agent1', 'Test@123')
st, h = fetch(cs2, '/Attendance/Calendar')
print(f'  Calendar page: HTTP {st} | len={len(h)}')
# find attendanceId if exists
m = re.search(r'data-attendance-id="(\d+)"', h)
att_id = m.group(1) if m else None
print(f'  existing attendance id: {att_id}')
# check-in attempt
st, h = fetch(cs2, '/Attendance/Login', 'POST', {'lat': 12.9716, 'lng': 77.5946})
print(f'  Check-in (POST /Attendance/Login): HTTP {st} | resp: {h[:200]}')
# calendar again to find the new attendance id, then check-out
st, h = fetch(cs2, '/Attendance/Calendar')
m = re.search(r'data-attendance-id="(\d+)"', h)
att_id2 = m.group(1) if m else None
print(f'  attendance id after check-in: {att_id2}')
if att_id2:
    st, h = fetch(cs2, '/Attendance/Logout', 'POST', {'attendanceId': att_id2})
    print(f'  Check-out (POST /Attendance/Logout): HTTP {st} | resp: {h[:200]}')
