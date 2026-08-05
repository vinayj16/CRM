# CRM

Property-tech CRM — ASP.NET Core 10 (MVC) with a MongoDB data layer and a React Native mobile app (`CRM-Mobile/`).

## Repository layout

- `CRM/` — the ASP.NET Core 10 web app (this is what gets deployed)
- `CRM-Mobile/` — React Native mobile app (git submodule, not part of the web deploy)
- `Controllers/` — stray top-level folder (not part of the `CRM/` project)

## Deploying to Railway

Railpack (Railway's auto-build tool) cannot auto-detect the .NET project because `CRM.csproj` is nested inside `CRM/` — it only detects `.csproj` files at the repository root. The repo therefore ships with:

- **`Dockerfile`** — builds `CRM/CRM.csproj` (net10.0) and runs it, binding to the `$PORT` env var Railway injects
- **`railway.json`** — pins the builder to `DOCKERFILE` (config-as-code overrides the dashboard's Railpack setting)
- **`.dockerignore`** — keeps the build context small (excludes `.git`, `CRM-Mobile`, `bin`/`obj`, data dumps)

To deploy:

1. Push the repo to GitHub (Railway deploys the GitHub `main` branch).
2. In Railway → your service → **Variables**, add:
   - `MongoDb__ConnectionString` — your MongoDB Atlas URI. **Optional for the first deploy** (the committed `CRM/appsettings.json` already contains one), but recommended so you can rotate the Atlas password later.
   - `BaseUrl` — `https://<your-app>.up.railway.app` (used for email and push-notification links; defaults to `localhost:5139` otherwise).
3. Deploy. The service binds to Railway's `PORT` automatically (see `Program.cs`).

### Required / recommended runtime variables

| Variable | Purpose | Required |
| --- | --- | --- |
| `PORT` | Injected automatically by Railway | Auto |
| `MongoDb__ConnectionString` | MongoDB Atlas URI (overrides the committed `appsettings.json` value) | Recommended |
| `BaseUrl` | Public URL used in emails / push notifications | Recommended |
| `Razorpay__KeyId`, `Razorpay__KeySecret` | Payments | Only when used |
| `WhatsApp__AccountSid`, `WhatsApp__AuthToken`, `WhatsApp__FromNumber` | WhatsApp | Only when used |
| `EmailSettings__From`, `EmailSettings__Password` | Email | Only when used |

### Security notes

- `CRM/appsettings.json` is committed so the app works out of the box. It currently contains a MongoDB Atlas connection string — after deploying, **rotate that Atlas password** and set `MongoDb__ConnectionString` in Railway Variables instead.
- `CRM/firebase-credentials.json` (Firebase service account) is **gitignored** and not in the repo. Without it, push notifications are disabled but everything else works. To enable them on Railway, add the file to the repo or mount it another way.
- Make sure your MongoDB Atlas cluster allows connections from Railway's egress IPs (or `0.0.0.0/0` on the Free tier).

## Local development

```bash
cd CRM
dotnet restore
dotnet run
```

The app listens on `http://localhost:5139` locally (falls back to `$PORT` when set, e.g. on Railway).
