cd /c/Users/vinay/Downloads/CRM
echo '=== status before ==='
git status -sb | head -10
echo '=== staged ==='
git add -A
git status -sb | head -10
echo '=== diff stat ==='
git diff --cached --stat
echo '=== committing ==='
git commit -m "Fix ID collision bugs: int AuditId auto-assign, sequential LeadIds for AddRange batches, explicit seed IDs"
echo '=== pushing ==='
git push origin main 2>&1 | tail -3
git log --oneline -1
