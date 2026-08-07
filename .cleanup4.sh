cd /c/Users/vinay/Downloads/CRM
rm -f .commit4.sh
git rm --cached .commit4.sh 2>/dev/null
rm -f .commit4.sh
git add -A
git status -sb | head -5
git commit -m "Remove temp helper script" 2>&1 | head -2
git push origin main 2>&1 | tail -2
git log --oneline -2
echo '=== final clean check ==='
git status -sb | head -5
ls -a | grep -E '^\.[a-z].*\.(sh|py)$' | head -5
echo done
