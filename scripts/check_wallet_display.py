import sys, json, http.cookiejar, urllib.request, urllib.parse, re
sys.stdout.reconfigure(encoding='utf-8')
BASE = 'http://localhost:5139'
jar = http.cookiejar.CookieJar()
op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
op.addheaders = [('User-Agent', 'Mozilla/5.0')]
op.open(BASE + '/Account/Login', timeout=10)
data = urllib.parse.urlencode({'Username': 'admin', 'Password': 'Test@123'}).encode()
op.open(urllib.request.Request(BASE + '/Account/Login', data=data), timeout=15)
cs = '; '.join(f'{c.name}={c.value}' for c in jar)

req = urllib.request.Request(BASE + '/SaasSubscription/AdminReferrals', headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cs})
body = urllib.request.urlopen(req, timeout=15).read().decode('utf-8', 'ignore')
m = re.search(r'referral-stat-number">([^<]+)<', body)
print('server-rendered stat (first):', m.group(1) if m else 'none')
print('all referral-stat-number:', re.findall(r'referral-stat-number">([^<]+)<', body))

req2 = urllib.request.Request(BASE + '/Home/GetReferralWallet?tenantId=1', headers={'User-Agent': 'Mozilla/5.0', 'Cookie': cs})
print('GetReferralWallet:', urllib.request.urlopen(req2, timeout=15).read().decode('utf-8', 'ignore')[:300])
