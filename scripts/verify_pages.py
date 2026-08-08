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

def fetch(cs, path):
    req = urllib.request.Request(BASE + path, headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cs})
    try:
        r = urllib.request.urlopen(req, timeout=15)
        return r.status, r.read().decode('utf-8', 'ignore')
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode('utf-8', 'ignore')
    except Exception as ex:
        return 'ERR', ''

# key page walk per role (correct routes)
WALK = [
    '/Dashboard/Index', '/Leads', '/Properties', '/Bookings', '/Invoices', '/Payments',
    '/Expenses', '/Revenue', '/profits', '/Quotations', '/Tasks', '/sitevisits',
    '/Agent/List', '/bankAccounts', '/AgentPayout', '/partnerpayouts',
    '/leadscoring', '/Ticket/Index', '/CompanyChat', '/TeamChat', '/Settings', '/ManageUsers',
]

def is_login_page(h):
    t = re.search(r'<title>(.*?)</title>', h, re.S)
    return bool(t and 'login' in t.group(1).lower())

def walk(label, u, p):
    cs = login_cookies(u, p)
    if not cs:
        print(f'{label}: LOGIN FAILED')
        return 0, 0
    ok = empty = 0
    for path in WALK:
        st, h = fetch(cs, path)
        if st == 200 and not is_login_page(h):
            ok += 1
        else:
            empty += 1
            print(f'   {label}: {path} -> HTTP {st} (login or missing)')
    return ok, empty

results = [
    ('T1 admin', 'admin', 'Test@123'),
    ('T2 admin2', 'admin2', 'Test@123'),
    ('T3 admin3', 'admin3', 'Test@123'),
    ('T4 admin4', 'admin4', 'Test@123'),
    ('T5 admin5', 'admin5', 'Test@123'),
    ('T6 admin6', 'admin6', 'Test@123'),
    ('T1 agent1', 'agent1', 'Test@123'),
    ('T1 sales1', 'sales1', 'Test@123'),
    ('T1 partner1', 'partner1', 'Test@123'),
    ('T2 agent_launch1', 'agent_launch1', 'Test@123'),
    ('T2 sales_launch', 'sales_launch', 'Test@123'),
    ('T3 partner3', 'partner3', 'Test@123'),
    ('T6 partner6', 'partner6', 'Test@123'),
]

print('=== PAGE WALK (each role x 22 module pages) ===')
print(f'Total pages in walk: {len(WALK)}')
for label, u, p in results:
    ok, bad = walk(label, u, p)
    print(f'[{"PASS" if bad == 0 else "GAPS"}] {label:18s}: {ok}/{len(WALK)} pages OK')
