"""End-to-end test:
1. Upload CompanyLogo for T1 admin -> verify sidebar AND dashboard welcome show it
2. Upload profile picture for T1 admin -> verify persisted + ProfileImage endpoint serves it
"""
import sys, json, http.cookiejar, urllib.request, urllib.parse, re, uuid, struct, zlib, os
sys.stdout.reconfigure(encoding='utf-8')
BASE = 'http://localhost:5139'

def make_png():
    def chunk(typ, data):
        c = struct.pack('>I', len(data)) + typ + data
        c += struct.pack('>I', zlib.crc32(typ + data) & 0xffffffff)
        return c
    ihdr = struct.pack('>IIBBBBB', 1, 1, 8, 2, 0, 0, 0)
    raw = b'\x00\x00\xff\x00'
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

def multipart(fields, files):
    boundary = '----X' + uuid.uuid4().hex
    parts = []
    for k, v in fields.items():
        parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n')
    for k, (fname, ctype, content) in files.items():
        parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{k}"; filename="{fname}"\r\nContent-Type: {ctype}\r\n\r\n')
    body = ''.join(parts).encode() + (b''.join(c for _, _, c in files.values()) if files else b'') + f'\r\n--{boundary}--\r\n'.encode()
    return body, boundary

def do_post(path, fields=None, files=None, cookie=cs):
    body, boundary = multipart(fields or {}, files or {})
    req = urllib.request.Request(BASE + path, data=body, method='POST',
                                 headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cookie,
                                          'Content-Type': f'multipart/form-data; boundary={boundary}'})
    try:
        r = urllib.request.urlopen(req, timeout=25)
        return r.status, r.read().decode('utf-8', 'ignore')
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode('utf-8', 'ignore')

def fetch(path, cookie=cs):
    req = urllib.request.Request(BASE + path, headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cookie})
    try:
        r = urllib.request.urlopen(req, timeout=15)
        return r.status, r.read().decode('utf-8', 'ignore')
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode('utf-8', 'ignore')

# ---------- 1) Company logo upload ----------
st, resp = do_post('/Settings/UpdateSettings', fields={'CompanyName': 'Default CRM', 'UpdateType': 'settings'},
                   files={'CompanyLogo': ('logo.png', 'image/png', png)})
print('logo upload HTTP', st, '->', resp[:120])

cs_db = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
from pymongo import MongoClient
db = MongoClient(cs_db, serverSelectionTimeoutMS=8000)['crm']
logo = db.settings.find_one({'SettingKey': 'CompanyLogo', 'TenantId': 1})
path = (logo or {}).get('SettingValue') or ''
print('DB logo path:', path)

if path:
    # Sidebar
    st, body = fetch('/Dashboard/Index')
    print('sidebar shows logo:', f'src="{path}"' in body or f"src='{path}'" in body or path in body)
    # Dashboard welcome
    st, body2 = fetch('/Dashboard/Index')
    print('welcome msg shows logo:', path in body2)
    # logo serves
    rr = urllib.request.urlopen(BASE + path, timeout=10)
    print('logo served HTTP', rr.status, rr.headers.get('Content-Type'))

# ---------- 2) Profile picture upload ----------
st, resp = do_post('/Profile/UpdateProfile', fields={'FirstName': 'Admin', 'removeImage': ''},
                   files={'profileImage': ('me.png', 'image/png', png)})
print('profile upload HTTP', st, '->', resp[:120] if st != 302 else 'redirect')
prof = db.user_profiles.find_one({'UserId': 1})
print('profile image path:', (prof or {}).get('ProfileImagePath'))
print('profile image bytes len:', len((prof or {}).get('ProfileImage') or b''))
if (prof or {}).get('ProfileImagePath'):
    p = (prof or {}).get('ProfileImagePath')
    rr = urllib.request.urlopen(BASE + p, timeout=10)
    print('profile img served HTTP', rr.status, rr.headers.get('Content-Type'))
# Also test the ProfileImage endpoint
rr2 = urllib.request.urlopen(BASE + '/Profile/ProfileImage?userId=1', timeout=10)
print('ProfileImage endpoint HTTP', rr2.status, rr2.headers.get('Content-Type'))

# ---------- 3) Cleanup ----------
if path:
    db.settings.update_one({'_id': logo['_id']}, {'$set': {'SettingValue': ''}})
    try: os.remove(os.path.join('wwwroot', path.lstrip('/')))
    except Exception: pass
    print('cleaned logo setting + file')
if (prof or {}).get('ProfileImagePath'):
    pp = (prof or {}).get('ProfileImagePath')
    db.user_profiles.update_one({'_id': prof['_id']}, {'$set': {'ProfileImagePath': None, 'ProfileImage': None}})
    try: os.remove(os.path.join('wwwroot', pp.lstrip('/')))
    except Exception: pass
    print('cleaned profile image setting + file')
