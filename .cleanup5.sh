cd /c/Users/vinay/Downloads/CRM
rm -f .cleanup4.sh
git rm -q --cached .cleanup4.sh 2>/dev/null || true
git rm -q --cached .cleanup5.sh 2>/dev/null || true
rm -f .cleanup4.sh
git add -A
git commit -m "Remove temp helper script" 2>&1 | head -2
git push origin main 2>&1 | tail -2
git log --oneline -3
