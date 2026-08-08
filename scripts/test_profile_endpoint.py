"""Check the ProfileImage?userId= endpoint body and navbar avatar after upload."""
import sys, json, http.cookiejar, urllib.request, urllib.parse, uuid, struct, zlib, os
sys.stdout.reconfigure(encoding='utf-8')
BASE = 'http://localhost:5139'

def make_png():
    def chunk(typ, data):
        c = struct.pack('>I', len(data)) + typ + data
        c += struct.pack('>I', zlib.crc32(typ + data) & 0xffffffff)
        return c
    ihdr = struct.pack('>IIBBBBB', 1, 1, 8, 2, 0, 0, 0)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', ihdr) +
            chunk(b'IDAT', zlib.compress(b'\x00\x00\xff\x00')) + chunk(b'IEND', b''))

png = make_png()
jar = http.cookiejar.CookieJar()
op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
op.addheaders = [('User-Agent', 'Mozilla/5.0')]
op.open(BASE + '/Account/Login', timeout=10)
d = urllib.parse.urlencode({'Username': 'admin', 'Password': 'Test@123'}).encode()
op.open(urllib.request.Request(BASE + '/Account/Login', data=d), timeout=15)
cs = '; '.join(f'{c.name}={c.value}' for c in jar)

boundary = '----X' + uuid.uuid4().hex
body = (f'--{boundary}\r\nContent-Disposition: form-data; name="FirstName"\r\n\r\nAdmin\r\n'
        f'--{boundary}\r\nContent-Disposition: form-data; name="removeImage"\r\n\r\n\r\n'
        f'--{boundary}\r\nContent-Disposition: form-data; name="profileImage"; filename="me.png"\r\nContent-Type: image/png\r\n\r\n').encode() + png + f'\r\n--{boundary}--\r\n'.encode()
req = urllib.request.Request(BASE + '/Profile/UpdateProfile', data=body, method='POST',
                             headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cs,
                                      'Content-Type': f'multipart/form-data; boundary={boundary}'})
urllib.request.urlopen(req, timeout=25).read()

cs_db = json.load(open('appsettings.json', encoding='utf-8'))['MongoDb']['ConnectionString']
from pymongo import MongoClient
db = MongoClient(cs_db, serverSelectionTimeoutMS=8000)['crm']
prof = db.user_profiles.find_one({'UserId': 1})
path = (prof or {}).get('ProfileImagePath') or ''
print('path:', path)

# 1) Navbar avatar (layout renders the path)
req = urllib.request.Request(BASE + '/Dashboard/Index', headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cs})
html = urllib.request.urlopen(req, timeout=15).read().decode('utf-8', 'ignore')
print('navbar contains profile path:', path in html)

# 2) ProfileImage endpoint
try:
    req = urllib.request.Request(BASE + '/Profile/ProfileImage?userId=1', headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cs})
    r = urllib.request.urlopen(req, timeout=15)
    body2 = r.read()
    print('ProfileImage endpoint:', r.status, r.headers.get('Content-Type'), '| body starts:', body2[:20])
except urllib.error.HTTPError as e:
    print('ProfileImage endpoint HTTPError', e.code, e.headers.get('Content-Type'))
    print('  body:', e.read()[:200])

# cleanup
if path:
    db.user_profiles.update_one({'_id': prof['_id']}, {'$set': {'ProfileImagePath': None, 'ProfileImage': None}})
    try: os.remove(os.path.join('wwwroot', path.lstrip('/')))
    except Exception: pass
    print('cleaned up')
