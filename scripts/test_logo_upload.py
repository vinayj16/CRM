"""Test company logo upload end-to-end:
1. Login as T1 admin
2. Upload a small PNG as CompanyLogo via multipart form
3. Verify the response + the file is web-accessible
4. Verify the logo path persists in settings and shows in the sidebar
"""
import sys, json, http.cookiejar, urllib.request, urllib.parse, re, uuid, struct, zlib
sys.stdout.reconfigure(encoding='utf-8')
BASE = 'http://localhost:5139'

# --- create a tiny valid PNG (1x1 red pixel) ---
def make_png():
    def chunk(typ, data):
        c = struct.pack('>I', len(data)) + typ + data
        c += struct.pack('>I', zlib.crc32(typ + data) & 0xffffffff)
        return c
    ihdr = struct.pack('>IIBBBBB', 1, 1, 8, 2, 0, 0, 0)
    raw = b'\x00\xff\x00\x00'
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', ihdr) +
            chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b''))

png = make_png()

jar = http.cookiejar.CookieJar()
op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
op.addheaders = [('User-Agent', 'Mozilla/5.0')]
op.open(BASE + '/Account/Login', timeout=10)
data = urllib.parse.urlencode({'Username': 'admin', 'Password': 'Test@123'}).encode()
op.open(urllib.request.Request(BASE + '/Account/Login', data=data), timeout=15)
cs = '; '.join(f'{c.name}={c.value}' for c in jar)

# multipart form
boundary = '----WebKitFormBoundary' + uuid.uuid4().hex
fields = [('CompanyName', 'Default CRM'), ('UpdateType', 'settings')]
parts = []
for k, v in fields:
    parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n')
parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="CompanyLogo"; filename="testlogo.png"\r\n'
             f'Content-Type: image/png\r\n\r\n')
body = ''.join(parts).encode() + png + f'\r\n--{boundary}--\r\n'.encode()

req = urllib.request.Request(BASE + '/Settings/UpdateSettings', data=body, method='POST',
                             headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cs,
                                      'Content-Type': f'multipart/form-data; boundary={boundary}'})
try:
    r = urllib.request.urlopen(req, timeout=20)
    resp = r.read().decode('utf-8', 'ignore')
    print('Upload HTTP', r.status)
    print('Response:', resp[:200])
except urllib.error.HTTPError as e:
    print('Upload HTTPError', e.code, e.read().decode('utf-8', 'ignore')[:300])

# verify logo persisted + served
cs_db = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
from pymongo import MongoClient
db = MongoClient(cs_db, serverSelectionTimeoutMS=8000)['crm']
logo = db.settings.find_one({'SettingKey': 'CompanyLogo', 'TenantId': 1})
print('DB CompanyLogo setting:', logo.get('SettingValue') if logo else None)
path = (logo or {}).get('SettingValue') or ''
if path:
    try:
        rr = urllib.request.urlopen(BASE + path, timeout=10)
        print('Logo served HTTP', rr.status, '| content-type:', rr.headers.get('Content-Type'))
    except Exception as e:
        print('Logo serve error:', e)

# check sidebar reflects it
req = urllib.request.Request(BASE + '/Dashboard/Index', headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cs})
body2 = urllib.request.urlopen(req, timeout=15).read().decode('utf-8', 'ignore')
if path:
    print('Sidebar contains logo path:', path in body2)

# cleanup: remove uploaded logo file + setting so we don't leave junk
if logo:
    db.settings.update_one({'_id': logo['_id']}, {'$set': {'SettingValue': ''}})
print('cleaned up logo setting (restored empty)')
