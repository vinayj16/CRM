import sys, json, http.cookiejar, urllib.request, urllib.parse, re
sys.stdout.reconfigure(encoding='utf-8')
BASE = 'http://localhost:5139'

jar = http.cookiejar.CookieJar()
op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
op.addheaders = [('User-Agent', 'Mozilla/5.0')]
op.open(BASE + '/Account/Login', timeout=10)
data = urllib.parse.urlencode({'Username': 'agent1', 'Password': 'Test@123'}).encode()
op.open(urllib.request.Request(BASE + '/Account/Login', data=data), timeout=15)
cs = '; '.join(f'{c.name}={c.value}' for c in jar)

def post(path, payload):
    req = urllib.request.Request(BASE + path, data=urllib.parse.urlencode(payload).encode(),
                                 method='POST',
                                 headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cs,
                                          'Content-Type': 'application/x-www-form-urlencoded'})
    try:
        r = urllib.request.urlopen(req, timeout=15)
        return r.status, r.geturl(), r.read().decode('utf-8', 'ignore')
    except urllib.error.HTTPError as e:
        return e.code, e.headers.get('Location', ''), e.read().decode('utf-8', 'ignore')

# check-in (agent1 = UserId 43)
st, url, h = post('/Attendance/Login', {'agentId': '43', 'attendanceId': '0', 'date': '2026-08-08'})
print('Check-in POST:', st, '->', url[:80])
m = re.search(r'data-attendance-id="(\d+)"', h) or re.search(r'name="attendanceId" value="(\d+)"', h)
att = m.group(1) if m else None
print('attendance id:', att)
print('calendar mentions Present:', 'Present' in h, '| shows Start Your Day button:', 'Start Your Day' in h)

if att:
    st, url, h = post('/Attendance/Logout', {'attendanceId': att})
    print('Check-out POST:', st, '->', url[:80])

# verify in DB
import json as j
cs2 = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
from pymongo import MongoClient
db = MongoClient(cs2, serverSelectionTimeoutMS=8000)['crm']
for a in db.agent_attendances.find({'AgentId': 43}).sort('AttendanceId', -1).limit(1):
    print('DB attendance:', a.get('AttendanceId'), '| status:', a.get('Status'), '| date:', a.get('Date'))
logs = list(db.attendance_logs.find({'AgentId': 43}).sort('LogId', -1).limit(2))
for lg in logs:
    print('DB log:', lg.get('LogId'), '| type:', lg.get('Type'))
