#!/bin/bash
cd /c/Users/vinay/Downloads/CRM
rm -f .verify_sb.sh .showdiff2.sh .flow_all.sh .flow_crud.sh dash*.html
git add -A
git status --short | head -10
git commit -m "Fix new-company provisioning and pin sidebar logo

- SuperAdmin CreateTenant now assigns the next TenantId explicitly (the
  Mongo shim skips TenantId in AutoAssignIntId, so new companies were
  persisted with TenantId 0 and could never be found/linked)
- Trial subscription now resolves the plan by PlanType (case-insensitive)
  with a Free-plan fallback - plans are named 'Basic Plan' etc while the
  form posts 'basic', so new companies were locked out with
  'No active subscription'
- Syncs tenant.Plan to the resolved plan name and seeds a tenant-scoped
  CompanyName setting so the sidebar footer, welcome banner and PDF
  headers immediately show the new company's name
- Sidebar brand (logo) is now position:sticky at the top of the scroll
  container so it stays pinned while the menu scrolls
- Verified end-to-end: create company -> trial sub created -> create user
  with credentials -> user login works -> dashboard shows company name ->
  lead CRUD works"
git push origin main 2>&1 | tail -3
echo "=== status ==="
git status -sb | head -2
