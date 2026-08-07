#!/bin/bash
cd /c/Users/vinay/Downloads/CRM
rm -f .final.sh .showdiff.sh dash.html dash2.html dash3.html dash4.html
git add -A
git status --short | head -10
git commit -m "Fix header pageLoader veil, tenant footer name, welcome-banner logo

- pageLoader hidden immediately + 800ms failsafe so the white veil can
  never cover the header (SignalR CDN failure can no longer block it)
- SignalR init guarded with typeof check + try/catch
- Sidebar footer now shows the tenant-resolved company name and keeps
  full copyright text visible even when the sidebar is collapsed
- Dashboard welcome banner displays the uploaded company logo next to
  the company name (tenant-scoped), verified end-to-end"
git push origin main 2>&1 | tail -3
echo "=== status ==="
git status -sb | head -2
