# ============================================================
# CRM — ASP.NET Core 10 web app (deployed to Railway)
# The web project lives at the repo root.
# Railway is pinned to the Dockerfile builder via railway.json.
# ============================================================

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore only the project file first for better layer caching
COPY ["CRM.csproj", "./"]
RUN dotnet restore "CRM.csproj"

# Copy the rest of the repo and publish
COPY . .
RUN dotnet publish "CRM.csproj" -c Release --no-restore -o /app/publish

# firebase-credentials.json is gitignored; include it when present.
# FcmService degrades gracefully without it, so never fail the build over it.
RUN cp /src/firebase-credentials.json /app/publish/ 2>/dev/null || echo "firebase-credentials.json not found - push notifications disabled"

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

# The app reads the $PORT env var (Railway injects it at runtime) in Program.cs.
# EXPOSE is informational only - Railway routes traffic to the $PORT it injects.
EXPOSE 8080

ENTRYPOINT ["dotnet", "CRM.dll"]
