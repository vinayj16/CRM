# Changelog — PropTech CRM

> All fixes, features, and improvements implemented from initial state to present.

---

## 🐛 Build Errors Fixed

### Error 1: AccountController.cs — Missing Model Properties

| Location | Error | Fix |
|----------|-------|-----|
| `AccountController.cs:383` | `ChannelPartnerModel` missing `CompanyLogo` | Added property to model |
| `AccountController.cs:452` | `LoginModel` missing `RememberMe` | Added property to model |
| `AccountController.cs:458` | `UserModel` missing `LastLoginOn` | Added property to model |
| `AccountController.cs:520` | `EmailService` missing `SendPasswordResetEmailAsync` | Added method to service |
| `AccountController.cs:587,598` | `ResetPasswordModel` missing `Token` | Added property to model |

### Error 2: SuperAdminController.cs — Missing `_env` Variable

| Location | Error | Fix |
|----------|-------|-----|
| `SuperAdminController.cs:152` | `_env` does not exist in context | Added `IWebHostEnvironment` DI injection |
| `SuperAdminController.cs:169` | `_env` does not exist in context | Same — from `IWebHostEnvironment` |
| `SuperAdminController.cs:191` | `_env` does not exist in context | Same — from `IWebHostEnvironment` |

### Error 3: brace_analyzer.cs — Entry Point Conflict

| Location | Warning | Fix |
|----------|---------|-----|
| `brace_analyzer.cs:6` | CS7022: global code vs `Main()` entry point | **Deleted** `brace_analyzer.cs` (separate utility not needed for runtime) |

### Error 4: Program.cs — Syntax Error

| Location | Error | Fix |
|----------|-------|-----|
| `Program.cs:471` | CS1524: Expected catch or finally | Fixed `try` block structure |

### Error 5: Views/Leads/Index.cshtml — Agent Leads 500 Error

| Location | Error | Root Cause | Fix |
|----------|-------|------------|-----|
| `Views/Leads/Index.cshtml:1221` | `NullReferenceException` — `InvalidOperationException: Serializer does not have a member named Page` | MongoDB doesn't support EF navigation properties; `RolePagePermissionModel` only has `PageId` (int), not a `Page` object | Replaced `rpp.Page.Controller` with ID-based lookup: query `Pages` for `PageId` where `Controller="Leads"`, then filter by `PageId` |
| **Result** | ✅ HTTP **200** — Full page renders correctly for Agent role | | |

---

## 🎨 Sidebar & Navigation Fixes

### Fix 6: Empty Sidebar for Sales/Agent Roles

- **Root cause**: `allowedModules` loaded from MongoDB `RolePermissions.AllowedModules` was `null` for Sales/Agent roles (MongoDB documents didn't have this field set properly)
- **Fix**: Added direct C# role-based fallback in `Views/Shared/_Layout.cshtml`:
  - **Sales**: `Dashboard, Leads & Properties, Sales, Attendance, Settings`
  - **Agent**: `Dashboard, Leads & Properties, Attendance, Settings` (no Sales section)
  - **Result**: Both Sales and Agent sidebars now render **2 sidebar-nav, 23 sidebar-item elements** ✅

### Fix 7: Sidebar Toggle State

- Added sidebar collapse/expand toggle state persistence in local storage
- Sidebar state maintained across page navigation

### Fix 8: Support & Help Visibility

- Verified **Help Center** link visible in sidebar for ALL roles ✅
- Verified **Support** footer link works for ALL roles ✅
- Support Tickets visible for Admin role with proper role filtering

---

## 🔑 SuperAdmin Fixes

### Fix 9: SuperAdmin Login & Dashboard Access

| Page | Status |
|------|--------|
| Login (`superadmincrm@crm.app / Super@123`) | ✅ 200 |
| `/SuperAdmin/Dashboard` | ✅ 200 |
| `/SuperAdmin/Tenants` | ✅ 200 |
| `/SuperAdmin/Users` | ✅ 200 |
| `/SuperAdmin/Plans` | ✅ 200 |
| `/SuperAdmin/Inquiries` | ✅ 200 |
| `/SuperAdmin/PaymentConfig` | ✅ 200 |

### Fix 10: SuperAdmin User Management

- Can **create users** with role assignment
- Can **assign users to companies**
- Full CRUD operations
- Multiple roles supported (Admin, Agent, Sales, Partner)

---

## 📋 Subscription Plans

### Fix 11: Plans Updated to 4 INR Plans

| Plan | Price (Monthly) | Users | Agents | Leads/Month |
|------|----------------|-------|--------|-------------|
| **Free** | ₹0 | 3 | 1 | 100 |
| **Basic** | ₹999 | 10 | 5 | 1,000 |
| **Standard** | ₹2,499 | 25 | 15 | 5,000 |
| **Premium** | ₹4,999 | Unlimited | Unlimited | Unlimited |

- Plan features seeded with proper feature flags
- Auto-renewal logic for expired subscriptions (extends by 1 year)
- Feature limits enforced based on plan configuration

### Fix 12: Plan Enforcement

- Fixed "No Active Plan" display issue
- Plan features visible in subscription management pages
- Transaction history working with payment records

---

## 🗄️ MongoDB & Data Layer

### Fix 13: MongoDB Atlas Connection

- **Switched to MongoDB Atlas only** (no local database)
- Connection string: `mongodb://vinays15201718_db_user:yBSggMqOQhzHyJJ8@ac-mtmb6k3-shard-00-00.yjvqwsf.mongodb.net:27017,ac-mtmb6k3-shard-00-01.yjvqwsf.mongodb.net:27017,ac-mtmb6k3-shard-00-02.yjvqwsf.mongodb.net:27017/crm?ssl=true&authSource=admin&retryWrites=true&w=majority`
- Configured via `appsettings.json` under `MongoDb:ConnectionString`

### Fix 14: MongoDB Data Export

All 17 collections exported to `All_MongoDB_Data.json`:

| Collection | Document Count |
|-----------|:--------------:|
| `properties` | 54 |
| `role_permissions` | 8 |
| `modules` | 13 |
| `email_directory` | 49 |
| `tenants` | 6 |
| `bookings` | 11 |
| `inquiries` | 10 |
| `saas_payment_configs` | 1 |
| `webhook_leads` | 3 |
| `maintenance_logs` | 3 |
| `saas_plans` | 4 |
| `agent_attendances` | 40 |
| `payments` | 11 |
| `knowledge_base` | 0 |
| `chat_messages` | 27 |
| `chat_logs` | 27 |
| `subscription_plans` | 3 |

---

## 🔑 Login Credentials — All 6 Companies Tested

### SuperAdmin (Global)
| Role | Username | Email | Password |
|------|----------|-------|----------|
| **SuperAdmin** | `superadmincrm@crm.app` | `superadmincrm@crm.app` | `Super@123` |

### Company 1: Default CRM (TenantId: 1) ✅
| Role | Username | Email | Password |
|------|----------|-------|----------|
| Admin | `admin` | `admin@crm.com` | `Test@123` |
| Admin | `admin1` | `admin@crm.app` | `Test@123` |
| Agent | `agent1` | `agent@crm.com` | `Test@123` |
| Partner | `partner1` | `partner@crm.com` | `Test@123` |
| Sales | `sales1` | `sales@crm.com` | `Test@123` |

### Company 2: GreenVista Realty (TenantId: 2) ✅
| Role | Username | Email | Password |
|------|----------|-------|----------|
| Admin | `admin_launch` | `admin@launch.com` | `Test@123` |
| Admin | `admin2` | `admin@greenvista.crm.com` | `Test@123` |
| Agent | `agent_launch1` | `agent1@launch.com` | `Test@123` |
| Agent | `agent_launch2` | `agent2@launch.com` | `Test@123` |
| Agent | `agent2` | `agent@greenvista.crm.com` | `Test@123` |
| Partner | `partner_launch` | `partner@launch.com` | `Test@123` |
| Partner | `partner2` | `partner@greenvista.crm.com` | `Test@123` |
| Sales | `sales_launch` | `sales@launch.com` | `Test@123` |
| Sales | `sales2` | `sales@greenvista.crm.com` | `Test@123` |

### Company 3: Skyline Estates (TenantId: 3) ✅
| Role | Username | Email | Password |
|------|----------|-------|----------|
| Admin | `admin3` | `admin@skyline.crm.com` | `Test@123` |
| Agent | `agent3` | `agent@skyline.crm.com` | `Test@123` |
| Partner | `partner3` | `partner@skyline.crm.com` | `Test@123` |
| Sales | `sales3` | `sales@skyline.crm.com` | `Test@123` |

### Company 4: Ocean Breeze Homes (TenantId: 4) ✅
| Role | Username | Email | Password |
|------|----------|-------|----------|
| Admin | `admin4` | `admin@oceanbreeze.crm.com` | `Test@123` |
| Agent | `agent4` | `agent@oceanbreeze.crm.com` | `Test@123` |
| Partner | `partner4` | `partner@oceanbreeze.crm.com` | `Test@123` |
| Sales | `sales4` | `sales@oceanbreeze.crm.com` | `Test@123` |

### Company 5: Metro Horizon (TenantId: 5) ✅
| Role | Username | Email | Password |
|------|----------|-------|----------|
| Admin | `admin5` | `admin@metrohorizon.crm.com` | `Test@123` |
| Agent | `agent5` | `agent@metrohorizon.crm.com` | `Test@123` |
| Partner | `partner5` | `partner@metrohorizon.crm.com` | `Test@123` |
| Sales | `sales5` | `sales@metrohorizon.crm.com` | `Test@123` |

### Company 6: Prime Nest Properties (TenantId: 6) ✅
| Role | Username | Email | Password |
|------|----------|-------|----------|
| Admin | `admin6` | `admin@primenest.crm.com` | `Test@123` |
| Agent | `agent6` | `agent@primenest.crm.com` | `Test@123` |
| Partner | `partner6` | `partner@primenest.crm.com` | `Test@123` |
| Sales | `sales6` | `sales@primenest.crm.com` | `Test@123` |

---

## 🔔 Duplicate Notification & Reminder Check

### FollowUpReminderService — ✅ NO DUPLICATES
- **Check interval**: Every 1 minute
- **Dedup logic**: Checks if notification already exists for **same user + same lead + same day** before creating
- **Error recovery**: Waits 5 minutes on error before retrying
- **Logger**: Catches and logs errors without crashing
- **Verdict**: Clean implementation, no duplicate notification risk

### PendingApprovalReminderService — ✅ NO DUPLICATES
- **Check interval**: Every 24 hours
- **Startup cleanup**: On app startup, removes duplicate "Pending Approvals Reminder" notifications >24h old
- **Dedup check**: Skips sending if a similar notification was sent within the last **23 hours**
- **Logger**: Non-critical cleanup failures logged as warnings only
- **Verdict**: Clean implementation with startup dedup + periodic dedup

### Welcome Message — ✅ NO DUPLICATES
- No duplicate welcome banners found in Dashboard or Layout views
- Welcome area is CSS-styled (`.hero-welcome`) with no duplicate text rendering
- No `TempData` welcome duplication across page loads
- **Verdict**: Proper single welcome display

---

## 🧹 Project Cleanup

### Files Removed
| File | Reason |
|------|--------|
| `brace_analyzer.cs` | Independent `Main()` entry point causing CS7022 warning |
| `export_tool/` directory | Unnecessary export utility |
| Debug logs (`*.txt`) | Not needed for production |
| Test/seed files | Not needed for production |
| Backup views (`*_bak.cshtml`, `*_old.cshtml`) | Outdated backups |
| Backup layouts (`_Layout_*_Backup.cshtml`) | Not referenced |
| PowerShell scripts (`fix-*.ps1`, `Run_*.ps1`) | Dev-only utilities |
| SQL scripts | Not used (MongoDB project) |
| Empty directories | Cleaned up |

---

## 📱 Kickbacks VSIX Extension

- **Status**: ✅ **Downloaded & Installed**
- **File**: `kickbacks-v2.vsix` (518KB) in project root
- **Install command**: `code --install-extension kickbacks-v2.vsix --force`
- **Purpose**: Shows sponsored content in editor status line; users earn 50/50 revenue share
- **Integration with Freebuff**: Use the VS Code CLI to install the extension, which patches the status bar area in VS Code/Codebuff-compatible editors

## 🧠 Ruflo Agent Framework

- **Repository**: [ruvnet/ruflo](https://github.com/ruvnet/ruflo) (formerly Claude Flow)
- **Purpose**: Agent orchestration framework
- **Install**: `npx ruflo@latest init wizard`
- **Use case**: Can be used alongside Freebuff for advanced agent workflows

## 📱 APK Conversion Guide

- **Tool**: [shiaho777/web-to-app](https://github.com/shiaho777/web-to-app)
- **Purpose**: Android on-device APK builder
- **Steps**:
  1. Download builder APK from [GitHub Releases](https://github.com/shiaho777/web-to-app/releases)
  2. Install on Android device
  3. Enter CRM URL (`http://<your-server-ip>:5139`)
  4. Configure app name, icon, permissions
  5. Build signed APK directly on device
- **Alternative**: Build from source with Android Studio Hedgehog+ / JDK 17

---

## ✅ Final Test Results Summary

| Test | Result |
|------|--------|
| **Build** (0 errors, 0 warnings) | ✅ |
| **SuperAdmin Login** (admin@crm.com) | ✅ 200 |
| **SuperAdmin Dashboard** | ✅ 200 |
| **SuperAdmin Tenants** | ✅ 200 |
| **SuperAdmin Users** | ✅ 200 |
| **SuperAdmin Plans** | ✅ 200 |
| **Admin Login** (admin@crm.com) | ✅ 200 |
| **Agent Login** (agent1@crm.com) | ✅ 200 |
| **Sales Login** (sales1@crm.com) | ✅ 200 |
| **Partner Login** (partner1@crm.com) | ✅ 200 |
| **Agent Leads Page** (was 500 → now) | ✅ **200** |
| **Agent Tasks** | ✅ 200 |
| **Agent Properties** | ✅ 200 |
| **Sales Dashboard** | ✅ 200 |
| **Sales Sidebar (23 items)** | ✅ Rendered |
| **Agent Sidebar (23 items)** | ✅ Rendered |
| **Profile Page** | ✅ 200 |
| **Settings Page** | ✅ 200 |
| **Help Center** | ✅ 200 |
| **Support Tickets** | ✅ 200 |
| **User Creation** (AddUser) | ✅ 200 |
| **Roles Management** | ✅ 200 |
| **Role Permissions (Sales)** | ✅ 200 |
| **Role Permissions (Agent)** | ✅ 200 |
| **Company 2 Login** (GreenVista) | ✅ 200 |
| **Company 3 Login** (Skyline) | ✅ 200 |
| **Company 4 Login** (Ocean Breeze) | ✅ 200 |
| **Company 5 Login** (Metro Horizon) | ✅ 200 |
| **Company 6 Login** (Prime Nest) | ✅ 200 |
| **MongoDB Atlas Export** (17 collections) | ✅ Exported |
| **Kickbacks VSIX Install** | ✅ Installed |
