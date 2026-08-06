#!/bin/bash
cd /c/Users/vinay/Downloads/CRM
echo "=== final build ==="
dotnet build CRM.csproj -c Release 2>&1 | grep -E 'Warning|error|Build succeeded' | head -4
echo "EXIT:${PIPESTATUS[0]}"
echo "=== cleanup + status ==="
ls -a | grep -E '^\.(logo|upload|badge|finish|commit|verify|final|hero|dbg)' | head -5
git status --short
echo "=== commit & push ==="
git add -A
git commit -m "Fix Settings persistence (Mongo shim no-op SaveChanges): persist existing text settings + company/collapsed logo updates via Update(), delete old logo file on replace; wire sidebar to show uploaded company/collapsed logos with default fallback; add BrandingResolver.ResolveCollapsedLogo and partner CompanyName fallback so partner dashboards show their real company name"
git push origin main 2>&1 | tail -3
echo "=== last commits ==="
git log --oneline -2
echo DONE
