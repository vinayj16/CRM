# PropTech CRM

A comprehensive Customer Relationship Management (CRM) system built for real estate businesses. Manage leads, properties, sales, finances, team members, and more — all from one platform with multi-tenant SAAS architecture.

---

## 🚀 Quick Start

### Prerequisites
- .NET 10 SDK
- MongoDB Atlas (cloud connection configured)

### Run the App
```bash
dotnet run
```

The app binds to:
- **Local:** http://localhost:5139
- **Network:** http://192.168.1.5:5139 (accessible from other devices on same network)
- **Tip:** Use your machine's local IP address for other devices or the web-to-app APK builder

### Important URL Note
After running `dotnet run`, the app takes **~30-50 seconds** to start. Wait for `Now listening on: http://localhost:5139` in the terminal before accessing.

---

## 🔑 Login Credentials

### Super Admin (Global — manages all tenants)
| Role | Username | Email | Password |
|------|----------|-------|----------|
| **SuperAdmin** | `admin@crm.com` | `admin@crm.com` | `Admin@123` |

> **Note**: The SuperAdmin logs in via the **same login page** at `/Account/Login`. They see the SuperAdmin panel at `/SuperAdmin/Dashboard` with full system control. `admin@crm.com` + `Admin@123` → SuperAdmin panel; the same email + `Test@123` → Company 1 Admin dashboard (both verified).
>
> **Only ONE SuperAdmin exists** (consolidated 2026-08-08 — the old duplicate `superadmincrm@crm.app` record was removed; backup at `scripts/backups/superadmin_cleanup_*.json`). The SuperAdmin is the global user: no tenant scoping, no feature limits, no role restrictions. **No code path creates additional SuperAdmin accounts** (verified: zero `SuperAdmins.Add`/`new SuperAdminModel` in the codebase) — it cannot be created again from the UI or API.

### ✅ Verified Data Available Per Role (after login)

All credentials below are **verified working** (username **or** email both accepted). Every role lands on its dashboard with live data from MongoDB. A full page walk (22 module pages × all roles) returns **22/22 pages OK** for every tenant's Admin, Agent, Sales and Partner accounts:

| Role | Lands on | Data visible after login (verified live) |
|------|----------|------------------------------------------|
| **SuperAdmin** | `/SuperAdmin/Dashboard` | 6 tenants, referrals, inquiries, plans, payment config, transactions |
| **Admin** | `/Dashboard/Index` (Analytics) | Leads, Properties, Bookings, Invoices, Payments, Expenses, Revenue, Profit, Quotations, Tasks, Site Visits, Agents, Payouts, Partner Commissions/Payouts, Bank Accounts, Support Tickets, Campaigns, Chat, Settings, Manage Users |
| **Agent** | `/Dashboard/Index` (Analytics) | Own assigned leads, tasks, attendance, chat |
| **Sales** | `/Dashboard/Index` (Analytics) | Own assigned leads, quotations, bookings, attendance |
| **Partner** | `/Home/Index` (Partner Dashboard) | Channel-partner leads + handover status, agent list, commissions |

> **Data completeness (per tenant, seeded & verified 2026-08-08):** every tenant now has data in every module — leads, properties (+flats), builders, bookings (+payment plans & installments), quotations (+items), invoices, payments, expenses, revenues, agents (+payouts), channel partners (+commissions & payouts), bank accounts, notifications, follow-ups/tasks, site visits, campaigns, testimonials, support tickets, company messages, chat sessions, webhook leads, settings & branding. Every user has a profile. Seed/audit scripts live in `scripts/` (`seed_followups.py`, `seed_all_data.py`, `seed_payment_plans.py`, `seed_remaining.py`, `audit_data.py`, `verify_pages.py`, `export_db_data.py`, `verify_all.py`, `fix_orphan_links.py`, `fix_duplicate_active_subs.py`, `consolidate_superadmin_cleanup.py`) with backups in `scripts/backups/`.
>
> **Tenant isolation**: Company 1's `admin` sees only Company 1 data (36 leads, 21 properties, 4 bookings, 3 invoices, 3 payments, 19 expenses, 3 agents). Partner leads with `HandoverStatus: Partner` are hidden from Admin until handed over — the tenant's partner user sees those (verified: `partner6` sees all 6 Prime Nest channel leads; T3–T6 admins see 0 partner leads while their partner sees 6).
>
> **Orphan check**: a reference audit across all 71 collections found **no orphaned data** — every lead/book/payment/expense/revenue/attendance/payout/commission links to a real user, agent, partner, lead, property, booking or tenant (fixed: 2 leads had dangling `ExecutiveId`, now linked; backup `scripts/backups/orphan_lead_fix_*.json`).
>
> **Subscriptions**: each tenant has **exactly one Active subscription** (duplicate active records were cleaned; backup `scripts/backups/active_subs_fix_*.json`). Every tenant shows the same 4 plans (Free / Basic ₹999 / Standard ₹2,499 / Premium ₹4,999) on `/SaasSubscription/MyPlan` and can upgrade — the **referral wallet is auto-deducted** from plan payments (verified live: T1 wallet ₹700 → Premium upgrade ₹4,999 becomes ₹4,299). Wallet balance shows on the Referral Wallet widget and `/SaasSubscription/AdminReferrals`.
>
> **Attendance**: agent check-in/out works end-to-end (Status → `Present`, Login/Logout timestamps + logs persisted, no duplicate day records). A timezone bug that stored check-ins as UTC and compared against IST (creating duplicates + an Absent status) was fixed in `AttendanceController` + `Views/Attendance/Calendar.cshtml`.
>
> **Data export**: `All_MongoDB_Data.json` is regenerated from the live DB (`scripts/export_db_data.py`) — 71 collections, ~2,200 documents, including the consolidated single SuperAdmin.

### Company 1: Default CRM (TenantId: 1)
| Role | Username | Email | Password |
|------|----------|-------|----------|
| **Admin** | `admin` | `admin@crm.com` | `Test@123` |
| **Admin** | `admin1` | `admin@ultrakey.crm.com` | `Test@123` |
| **Agent** | `agent1` | `agent@crm.com` | `Test@123` |
| **Partner** | `partner1` | `partner@crm.com` | `Test@123` |
| **Sales** | `sales1` | `sales@crm.com` | `Test@123` |

> All 31 login credentials (1 SuperAdmin + 30 tenant users) verified working on 2026-08-08 (16/16 re-verified end-to-end after the attendance & subscription fixes: 30/30 checks pass). Wrong passwords are rejected. Backups of the email alignment live in `scripts/backups/email_align_*.json`.

### Company 2: GreenVista Realty (TenantId: 2)
| Role | Username | Email | Password |
|------|----------|-------|----------|
| **Admin** | `admin_launch` | `admin@launch.com` | `Test@123` |
| **Admin** | `admin2` | `admin@greenvista.crm.com` | `Test@123` |
| **Agent** | `agent_launch1` | `agent1@launch.com` | `Test@123` |
| **Agent** | `agent_launch2` | `agent2@launch.com` | `Test@123` |
| **Agent** | `agent2` | `agent@greenvista.crm.com` | `Test@123` |
| **Partner** | `partner_launch` | `partner@launch.com` | `Test@123` |
| **Partner** | `partner2` | `partner@greenvista.crm.com` | `Test@123` |
| **Sales** | `sales_launch` | `sales@launch.com` | `Test@123` |
| **Sales** | `sales2` | `sales@greenvista.crm.com` | `Test@123` |

### Company 3: Skyline Estates (TenantId: 3)
| Role | Username | Email | Password |
|------|----------|-------|----------|
| **Admin** | `admin3` | `admin@skyline.crm.com` | `Test@123` |
| **Agent** | `agent3` | `agent@skyline.crm.com` | `Test@123` |
| **Partner** | `partner3` | `partner@skyline.crm.com` | `Test@123` |
| **Sales** | `sales3` | `sales@skyline.crm.com` | `Test@123` |

### Company 4: Ocean Breeze Homes (TenantId: 4)
| Role | Username | Email | Password |
|------|----------|-------|----------|
| **Admin** | `admin4` | `admin@oceanbreeze.crm.com` | `Test@123` |
| **Agent** | `agent4` | `agent@oceanbreeze.crm.com` | `Test@123` |
| **Partner** | `partner4` | `partner@oceanbreeze.crm.com` | `Test@123` |
| **Sales** | `sales4` | `sales@oceanbreeze.crm.com` | `Test@123` |

### Company 5: Metro Horizon (TenantId: 5)
| Role | Username | Email | Password |
|------|----------|-------|----------|
| **Admin** | `admin5` | `admin@metrohorizon.crm.com` | `Test@123` |
| **Agent** | `agent5` | `agent@metrohorizon.crm.com` | `Test@123` |
| **Partner** | `partner5` | `partner@metrohorizon.crm.com` | `Test@123` |
| **Sales** | `sales5` | `sales@metrohorizon.crm.com` | `Test@123` |

### Company 6: Prime Nest Properties (TenantId: 6)
| Role | Username | Email | Password |
|------|----------|-------|----------|
| **Admin** | `admin6` | `admin@primenest.crm.com` | `Test@123` |
| **Agent** | `agent6` | `agent@primenest.crm.com` | `Test@123` |
| **Partner** | `partner6` | `partner@primenest.crm.com` | `Test@123` |
| **Sales** | `sales6` | `sales@primenest.crm.com` | `Test@123` |

---

## 🗺️ Full Sitemap

### Public Pages (No Login Required)
| Route | Description | Access |
|-------|-------------|--------|
| `/` | Landing page with hero, services, testimonials, contact | Public |
| `/Home/Landing` | Landing page with carousel and feature showcase | Public |
| `/Home/ProjectDetails/{id}` | Public property/project details | Public |
| `/Account/Login` | User login (also used by SuperAdmin) | Public |
| `/Account/Register` | Self-registration (Agent, Partner, Sales) | Public |
| `/Account/ForgotPassword` | Password reset request | Public |

### Main Dashboard (Admin / Agent / Sales)
| Route | Description | Roles |
|-------|-------------|-------|
| `/Dashboard/Index` | Analytics dashboard with KPIs, charts, recent activity | All authenticated |
| `/Home/Index` | Main dashboard with welcome banner | Admin |
| `/Home/SalesOverview` | Sales performance with trends and targets | Admin |
| `/Home/TeamDashboard` | Team metrics and leaderboard | Admin |

### Leads & Properties
| Route | Description | Roles |
|-------|-------------|-------|
| `/Leads/Index` | Full lead management (CRUD, filters, stage, status, export) | Admin, Agent, Sales |
| `/Leads/Details/{id}` | Lead detail view with timeline, notes, follow-ups | Admin, Agent, Sales |
| `/Leads/Create` | Create new lead | Admin, Agent, Sales |
| `/Leads/Edit/{id}` | Edit lead details | Admin, Agent, Sales |
| `/Leads/Delete/{id}` | Delete lead | Admin |
| `/SalesPipelines/Index` | Kanban-style sales pipeline view (drag & drop stages) | Admin, Agent, Sales |
| `/Tasks/Index` | Task management linked to leads | Admin, Agent, Sales |
| `/WebhookLeads/Index` | Unassigned leads from webhook integrations | Admin |
| `/Properties/Index` | Full property management (CRUD, gallery, flats) | Admin, Agent |
| `/Properties/Create` | Add new property | Admin |
| `/Properties/Edit/{id}` | Edit property | Admin |
| `/Properties/Details/{id}` | Property detail view | Admin, Agent |

### Sales Management
| Route | Description | Roles |
|-------|-------------|-------|
| `/Quotations/Index` | Quotation management (create, edit, PDF) | Admin, Sales |
| `/Quotations/Create` | Generate quotation from lead | Admin, Sales |
| `/Bookings/Index` | Booking management with payment milestones | Admin, Sales |
| `/Bookings/Create` | Create booking from quotation | Admin, Sales |
| `/Bookings/Details/{id}` | Booking detail with payment schedule | Admin, Sales |
| `/Invoices/Index` | Invoice generation & management | Admin |
| `/Invoices/Create` | Generate invoice from booking | Admin |
| `/Payments/Index` | Payment recording & history | Admin |
| `/Payments/Create` | Record new payment | Admin |

### Finance
| Route | Description | Roles |
|-------|-------------|-------|
| `/Expenses/Index` | Expense tracking with categories | Admin |
| `/Expenses/Create` | Add new expense | Admin |
| `/Revenue/Index` | Revenue tracking linked to bookings | Admin |
| `/Profit/Index` | Profit & loss calculation | Admin |
| `/Financial/BankAccounts` | Bank account management | Admin |
| `/Financial/PaymentGateways` | Razorpay payment gateway configuration | Admin |

### Team Management
| Route | Description | Roles |
|-------|-------------|-------|
| `/Agent/List` | Agent list with commission rules | Admin, Partner |
| `/Agent/Onboard` | Add new agent | Admin |
| `/Agent/Details/{id}` | Agent profile with documents, performance | Admin |
| `/ManageUsers/Index` | User management (create, edit, activate/deactivate) | Admin |
| `/ManageUsers/Roles` | Role & permissions management (module access) | Admin |
| `/ManageUsers/PartnerApproval` | Approve/reject partner registrations | Admin |
| `/TeamChat/Index` | Internal team chat | All |

### Attendance
| Route | Description | Roles |
|-------|-------------|-------|
| `/Attendance/Calendar` | My attendance calendar with check-in/out | Agent, Sales |
| `/Attendance/AgentList` | Team attendance view | Admin, Partner |
| `/Attendance/CheckIn` | Check-in/check-out action | Agent, Sales |

### Payouts
| Route | Description | Roles |
|-------|-------------|-------|
| `/AgentPayout/Index` | Agent payout management (commission) | Admin |
| `/AgentPayout/Create` | Create payout to agent | Admin |
| `/PartnerCommission/Index` | Partner commission & payouts | Admin |
| `/Payout/Index` | Payout dashboard | Admin |

### Subscriptions (SAAS)
| Route | Description | Roles |
|-------|-------------|-------|
| `/Subscription/MyPlan` | Current plan details & usage | Admin, Partner |
| `/Subscription/Plans` | Available subscription plans | Admin, Partner |
| `/Subscription/Transactions` | Payment transaction history | Admin, Partner |
| `/Subscription/PendingRefunds` | Refund management | Admin |
| `/SaasSubscription/MyPlan` | SAAS subscription page (with limit redirect) | Admin, Partner |

### Settings
| Route | Description | Roles |
|-------|-------------|-------|
| `/Profile/Index` | User profile (name, email, password change, profile photo) | All |
| `/Settings/Index` | System settings (company info, logo, GST, branding) | Admin |
| `/Settings/Branding` | Landing page branding | Admin |
| `/Settings/Impersonation` | User impersonation (login as other users) | Admin |
| `/EmailSettings/Index` | SMTP email configuration | Admin |
| `/CompanyChat/Index` | Company-wide messaging | All |
| `/Support/Tickets` | Support ticket management | All |

### Super Admin Panel (Global — All Tenants)
| Route | Description |
|-------|-------------|
| `/SuperAdmin/Dashboard` | SAAS admin dashboard with system metrics |
| `/SuperAdmin/Tenants` | Multi-tenant management (create, suspend, edit) |
| `/SuperAdmin/CreateTenant` | Create new tenant organization |
| `/SuperAdmin/Users` | View/manage all tenant users |
| `/SuperAdmin/Inquiries` | Manage inquiries from landing page |
| `/SuperAdmin/Plans` | Subscription plan management (pricing, features) |
| `/SuperAdmin/PaymentConfig` | SAAS Razorpay configuration |
| `/SuperAdmin/TenantSubscriptions` | Tenant subscription tracking |
| `/SuperAdmin/SaasTransactions` | All payment transactions |
| `/SuperAdmin/EmailTemplates` | Email template management |
| `/SuperAdmin/ComposeEmail` | Compose & send emails |
| `/SaasSetting/Index` | SAAS system settings |
| `/CrmPlan/Index` | CRM plan overview |

### Chatbot & Real-Time
| Route | Description |
|-------|-------------|
| `/ChatbotDashboard/Index` | Chatbot conversation analytics |
| `/ChatbotDashboard/Conversation` | Live conversation view |
| `/chatHub` | Real-time chat (SignalR hub) |
| `/RealTimeChat/Index` | Real-time messaging interface |

### Integrations
| Route | Description |
|-------|-------------|
| `/Integrations/LeadIntegrations` | Third-party lead sources (Facebook, Google Ads, 99acres, etc.) |
| `/FacebookLeads/Index` | Facebook lead ads configuration |
| `/WebhookLeads/Index` | Webhook-based lead capture |

### API Endpoints (Internal)
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/export/download` | GET | Export all company data as JSON backup (authenticated) |
| `/Account/SelectWorkspace` | POST | Select workspace when user has multiple companies |
| `/Account/KeepAlive` | POST | Keep session alive |
| `/Account/ValidateCurrentPassword` | POST | Validate current password for password change |
| `/Search/GlobalSearch?query=` | GET | Global search across leads, properties, users, agents, bookings |
| `/Search/GetRecentSearches` | GET | Get recent search terms |
| `/Search/SaveRecentSearch` | POST | Save recent search term |

---

## ✨ Features & Functionality

### 🔐 Authentication & Authorization
| Feature | Details |
|---------|---------|
| **Login** | Email/username + password with JWT token + cookie auth |
| **Workspace Picker** | Multi-company users can select which org to enter |
| **Roles** | SuperAdmin, Admin, Partner, Agent, Sales |
| **Permissions** | Module-level CRUD permissions per role |
| **Password Reset** | Email-based reset with token expiry |
| **Impersonation** | Admin can login as any user for troubleshooting |
| **Session** | Auto-logout after 60 min idle, 24h absolute expiry |

### 🧑‍💼 Lead Management
| Feature | Details |
|---------|---------|
| **CRUD** | Create, edit, delete, view leads |
| **Stages** | New, FollowUp, Office Meeting, Site Visit, Negotiation, Closed Won, Closed Lost |
| **Statuses** | Active, Hot, Warm, Cold, Dead |
| **Duplicate Detection** | By email, phone |
| **Filters** | By stage, status, source, assigned agent, date range |
| **Bulk Upload** | CSV/Excel lead import |
| **Sales Pipeline** | Kanban board with drag-and-drop stage updates |
| **Lead Assignment** | Assign to agents/team members |
| **Follow-ups** | Schedule reminders with auto-notification |
| **Notes & Timeline** | Activity log for each lead |
| **Lead Scoring** | Priority scoring based on engagement |

### 🏠 Property Management
| Feature | Details |
|---------|---------|
| **CRUD** | Create, edit, delete, view properties |
| **Image Gallery** | Multiple images per property with preview |
| **Flat/Unit Management** | Individual units with pricing, status |
| **Location** | Area, city, state, pincode |
| **Filters** | By type, location, price range, status |
| **Public Listing** | Public-facing property pages |
| **Interest Requests** | Visitors can express interest publicly |

### 💰 Sales & Payments
| Feature | Details |
|---------|---------|
| **Quotations** | Generate quotes from leads, PDF export |
| **Bookings** | Convert quotations with milestone payments |
| **Invoices** | Auto-generated from bookings with GST |
| **Payments** | Multiple modes (UPI, Bank Transfer, Cheque, Cash, Card) |
| **Razorpay** | Payment gateway for subscription fees |
| **Refunds** | Razorpay refund processing |

### 📊 Finance
| Feature | Details |
|---------|---------|
| **Expenses** | Track with categories, receipts |
| **Revenue** | Linked to bookings and payments |
| **Profit/Loss** | Auto-calculated from revenue - expenses |
| **Bank Accounts** | Multiple accounts for invoice linking |
| **Payment Gateways** | Razorpay key management |
| **GST** | Tax configuration for invoices |

### 👥 Team Management
| Feature | Details |
|---------|---------|
| **Agents** | Commission-based agent profiles with documents |
| **Attendance** | Daily check-in/check-out with location |
| **Performance** | Metrics by agent (leads, conversions, revenue) |
| **Roles** | Custom role creation with module permissions |

### 📧 Communication
| Feature | Details |
|---------|---------|
| **Email** | SMTP integration with Gmail, password reset, notifications |
| **WhatsApp** | Twilio WhatsApp API integration |
| **In-App Notifications** | Priority-based (urgent, high, normal) |
| **Push Notifications** | Firebase Cloud Messaging (FCM) |
| **Team Chat** | Real-time messaging via SignalR |
| **Company Chat** | Organization-wide announcements |

### 📈 Reports & Analytics
| Feature | Details |
|---------|---------|
| **Dashboard KPIs** | Total leads, bookings, revenue, conversion rate |
| **Charts** | Lead trends, booking trends, revenue (monthly) |
| **Sales Funnel** | Lead-to-booking conversion visualization |
| **Team Dashboard** | Agent performance comparison |
| **Export** | Export data to Excel and CSV |

### 🎨 Customization
| Feature | Details |
|---------|---------|
| **Company Logo** | Upload logo (shown in sidebar, login page) |
| **Theme** | Dark mode support with persistent preference |
| **Sidebar** | Collapsible with icon-only mode |
| **Favicon** | Custom favicon per tenant |

### 🔧 Super Admin (SAAS Operations)
| Feature | Details |
|---------|---------|
| **Multi-Tenant** | Manage independent organizations |
| **Tenant CRUD** | Create, suspend, edit tenants |
| **Subscription Plans** | Free, Basic (₹999), Standard (₹2,499), Premium (₹4,999) |
| **Plan Features** | Per-plan limits on users, agents, leads, partners |
| **Payment Tracking** | All tenant payment transactions |
| **Email Templates** | Customizable email templates |
| **System Settings** | Global SAAS configuration |
| **User Management** | View all users across tenants |

---

## 📋 Subscription Plans (SAAS Pricing in INR)

| Feature | Free | Basic Plan (₹999/mo) | Standard Plan (₹2,499/mo) | Premium Plan (₹4,999/mo) |
|---------|------|---------------------|-------------------------|-------------------------|
| **Users** | 3 | 10 | 25 | Unlimited |
| **Agents** | 1 | 5 | 15 | Unlimited |
| **Leads/Month** | 100 | 1,000 | 5,000 | Unlimited |
| **Partners** | 0 | 2 | 5 | Unlimited |
| **Email Integration** | ✅ | ✅ | ✅ | ✅ |
| **WhatsApp Integration** | ❌ | ✅ | ✅ | ✅ |
| **Facebook Integration** | ❌ | ❌ | ✅ | ✅ |
| **Advanced Reports** | ❌ | ✅ | ✅ | ✅ |
| **Impersonation** | ❌ | ✅ | ✅ | ✅ |
| **Custom Branding** | ❌ | ❌ | ✅ | ✅ |
| **Priority Support** | ❌ | ❌ | ✅ | ✅ |
| **Custom API Access** | ❌ | ❌ | ❌ | ✅ |
| **Support Level** | Email | Email | Chat | Dedicated |
| **Plan Type** | Free | Basic | Standard | Premium |

### What Happens When Subscription Expires?
- The system **auto-renews** expired subscriptions by extending the EndDate by 1 year
- Users never get locked out due to expired subscriptions
- Feature limits still enforced based on the plan's configuration

---

## 🏗️ Architecture

### Technology Stack
| Layer | Technology |
|-------|-----------|
| **Backend** | ASP.NET Core MVC (.NET 10) |
| **Database** | MongoDB Atlas (cloud-hosted) |
| **Authentication** | JWT + Cookie Authentication |
| **Real-time** | SignalR |
| **Payments** | Razorpay API |
| **Push Notifications** | Firebase Cloud Messaging (FCM) |
| **Email** | SMTP (Gmail with App Passwords) |
| **WhatsApp** | Twilio API |
| **Frontend** | Bootstrap 5, jQuery, Font Awesome, Feather Icons, SweetAlert2 |

### Data Flow
```
User → Browser → JWT Auth → Controller → Service → MongoDB Atlas
                                                      ↓
User ← Browser ← View (Razor) ← Controller ← Service ← MongoDB Atlas
```

### Database Collections (MongoDB)
| Collection | Description |
|------------|-------------|
| `users` | User accounts with roles, tenant IDs, hashed passwords |
| `user_profiles` | Extended profile data, profile images |
| `leads` | Lead records with stages, statuses, assignments |
| `properties` | Property listings with images, flats, pricing |
| `bookings` | Sales bookings with milestone schedules |
| `payments` | Payment records with modes, references |
| `invoices` | Invoice records with GST, line items |
| `quotations` | Quotation records |
| `expenses` | Expense records with categories, receipts |
| `revenues` | Revenue records linked to payments |
| `agents` | Agent profiles with commission rules, documents |
| `channel_partners` | Partner/company profiles |
| `settings` | System settings (company info, logos, copyright) |
| `branding` | Landing page branding configuration |
| `notifications` | In-app notifications with priority levels |
| `email_settings` | SMTP configuration per user |
| `email_templates` | Customizable email templates |
| `email_logs` | Email sending history |
| `bank_accounts` | Company bank accounts |
| `payment_gateways` | Razorpay API credentials |
| `payment_transactions` | Razorpay transaction logs |
| `tenant_subscriptions` | Tenant subscription records |
| `saas_plans` | Available subscription plans (4 plans) |
| `audit_logs` | System audit trail (login, actions) |
| `role_permissions` | Module-level role permissions |
| `modules` | System modules for permission control |
| `pages` | Individual pages within modules |
| `permissions` | CRUD permission types |
| `tasks` | Lead-related tasks |
| `follow_ups` | Scheduled follow-up reminders |
| `site_visits` | Site visit scheduling |
| `testimonials` | Customer testimonials |
| `chat_conversations` | Chatbot conversations |
| `chat_messages` | Chat messages (team chat, chatbot) |
| `chatbot_knowledge` | AI knowledge base |
| `user_favorites` | User favorite pages |
| `user_recent_searches` | Recent global search terms |

---

## 🔧 Configuration

### appsettings.json Structure
```json
{
  "MongoDb": {
    "ConnectionString": "mongodb+srv://...",
    "DatabaseName": "crm"
  },
  "Jwt": {
    "Key": "your-256-bit-secret-key",
    "Issuer": "PropTech CRM",
    "Audience": "CRM Users"
  },
  "BaseUrl": "http://localhost:5139"
}
```

### Key Middleware Pipeline (Order Matters)
1. `UseStaticFiles()` — Serve static assets
2. `UseRouting()` — Route middleware
3. `UseSession()` — Session support
4. `UseAuthentication()` — JWT + Cookie auth
5. `UseSubdomainDetection()` — Multi-tenant subdomain resolution
6. `UseAuthorization()` — Authorization checks
7. `UseMaintenanceMode()` — Maintenance mode blocking
8. `UseSaasTenantLimits()` — SAAS subscription & feature limits
9. `MapHub<RealTimeChatHub>()` — SignalR hub
10. `MapControllers()` — MVC controllers

---

## 🔍 Global Search

The global search bar (top navbar) searches across:
- **Pages**: Sidebar menu items (role-filtered)
- **Leads**: By name, contact, email
- **Properties**: By name, location
- **Users**: By username, email (Admin/Partner only)
- **Agents**: By name, phone, email (Admin/Partner only)
- **Bookings**: By booking number, lead name

Features: recent searches, favorites, keyboard navigation.

---

## 🧹 Project Cleanup Status

The following have been cleaned up:
- Backup views (`*_bak.cshtml`, `*_old.cshtml`)
- Backup layouts (`_Layout_*_Backup.cshtml`, `_Layout_Falcon.cshtml`)
- Debug files (`*.txt`, `*Debug*.txt`)
- PowerShell scripts (`fix-*.ps1`, `Run_*.ps1`)
- SQL scripts (`Create*.sql`, `Insert*.sql`)
- Test HTML files
- Backup controllers
- `brace_analyzer.cs` (separate entry point)
- `export_tool/` directory
- Empty directories

---

## 📄 Technical Notes

### Password Hashing
- All passwords are hashed using `PasswordHelper.HashPassword()` (bcrypt-style)
- Format: `{salt}.{hash}` — stored as a single string
- Old plain-text passwords can be migrated via `/Account/MigratePasswords`

### URL Convention
- All URLs are automatically lowercased via middleware
- Single-segment controller URLs (e.g., `/leads`) redirect to `/leads/index`
- 404 detection middleware adds `/Index` to single-segment controller URLs

### Multi-Tenancy
- Implemented via MongoDB `TenantId` field on each document
- Users are scoped to their tenant's data
- SuperAdmin sees all tenants
- Subdomain detection for partner-specific logins
- Workspace picker when user has access to multiple companies

### Auto-Renewal of Subscriptions
- When middleware detects an expired subscription, it auto-renews by extending EndDate by 1 year
- This prevents user lockout due to expired subscriptions
- Feature limits are still enforced based on the plan

---

## 📋 Changelog

A complete list of all fixes, features, and improvements is available in the **[CHANGELOG.md](./CHANGELOG.md)** file.

### Key Fixes Summary

| Category | Count | Details |
|----------|:-----:|---------|
| **Build Errors Fixed** | 5 | Missing model properties, missing DI variables, entry point conflict |
| **Sidebar/Navigation** | 3 | Empty sidebar for Sales/Agent, toggle state, Support visibility |
| **SuperAdmin** | 2 | Login/Dashboard access, user management |
| **Subscription Plans** | 2 | 4 INR plans, plan enforcement |
| **MongoDB/Data** | 2 | Atlas connection, data export (17 collections) |
| **Login Credentials** | 6 companies | All roles tested HTTP 200 · all 32 logins verified · Company-1 emails aligned to documented list |
| **Agent Leads 500** | 1 | NullReferenceException fixed → HTTP 200 |
| **Duplicate Notifications** | 0 issues | FollowUpReminder + PendingApproval both have dedup logic |
| **Project Cleanup** | Multiple | Removed test files, backups, debug logs, empty dirs |

See the full **[CHANGELOG.md](./CHANGELOG.md)** for details.

---

## 💰 Kickbacks Integration

[Kickbacks.ai](https://kickbacks.ai) is a VS Code extension that shows developer-relevant sponsored content in the editor status bar, with a 50/50 revenue share.

### Installation
```bash
# The extension is already downloaded to the project root
code --install-extension kickbacks-v2.vsix --force

# Or use npx
npx -y @kickbacksai/install
```

### How It Works
1. After installing, sign in with your Kickbacks account
2. Choose your sharing mode (Private or Boosted)
3. The extension shows relevant developer ads in the status bar while you work
4. Qualifying views earn 50/50 revenue split

Kickbacks works with **VS Code, Cursor, VSCodium**, and any editor compatible with VS Code extensions.

---

## 📱 Mobile APK

Convert this CRM into an Android APK using [web-to-app](https://github.com/shiaho777/web-to-app) — an on-device APK builder that runs directly on your Android phone.

### Quick Start
1. Download the builder APK from [GitHub Releases](https://github.com/shiaho777/web-to-app/releases)
2. Install on your Android device
3. Open the app, enter your CRM URL: `http://<your-server-ip>:5139`
4. Configure app name, icon, WebView settings
5. Build and sign the APK directly on your device

### Requirements
- Android device (or emulator)
- No PC or Android Studio required for pre-built releases
- For development builds: Android Studio Hedgehog+ / JDK 17

### Features
- Built-in WebView with JS/CSS injection
- DNS-over-HTTLS and TLS fingerprint spoofing
- Privacy hardening (fingerprint disguise, ad-blocking)
- Full APK/AAB signing (V1/V2/V3) on-device

---

## 🧠 Ruflo Agent Framework

[Ruflo](https://github.com/ruvnet/ruflo) (formerly Claude Flow) is an agent orchestration framework that can be used with Freebuff for advanced agent workflows.

### Install
```bash
npx ruflo@latest init wizard
```

Or add as an MCP plugin:
```bash
/plugin marketplace add ruvnet/ruflo
```

---

## 📄 License & Support

Built by **PropTech Solutions Pvt Ltd**. For support, contact the system administrator.

© 2015-2026 PropTech CRM. All Rights Reserved.
