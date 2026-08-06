#!/bin/bash
cd /c/Users/vinay/Downloads/CRM || exit 1
git add -A
echo '=== staged files ==='
git diff --cached --stat | tail -18
echo '=== committing ==='
git commit -m "Fix cross-tenant branding scoping, settings persistence, and reminder/lead 404 links

- BrandingResolver: scope settings by TenantId so each company sees its own
  name/logo (admins share ChannelPartnerId=null, so tenant filter was required)
- Thread tenantId through _Layout, HomeController, DashboardController,
  AccountController, Quotations PDF headers, and partner welcome emails
- SettingsController: persist TenantId on new settings, scope lookups/removals,
  add tenant-aware GetSettingValue overload
- Leads/follow-up/reminder links now use encoded ids (fixes /leaddetails/404)
- HomeController public pages use tenant-scoped GetPublicSettings()
- Clean up duplicate/garbage settings docs; backfill unique SettingIds so the
  MongoDbSet shim's Update/Remove match the correct row" && git push origin main 2>&1 | tail -3
echo '=== final status ==='
git status -sb | head -3
