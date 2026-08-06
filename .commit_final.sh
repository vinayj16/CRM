#!/bin/bash
cd /c/Users/vinay/Downloads/CRM
echo '=== stop app ==='
PID=$(netstat -ano | grep ':5201 ' | grep LISTEN | awk '{print $NF}' | head -1)
[ -n "$PID" ] && taskkill //PID $PID //F 2>&1 | head -1
echo '=== remove stray script ==='
rm -f .final_state.sh .audit_nop.sh .check*.sh .verify*.sh .edituser*.sh .users_page*.sh .jwt_check.sh .notif*.sh .links*.sh .clean_users.py .del_users.py .build.sh .start.sh .finalize.sh
echo '=== final build ==='
dotnet build CRM.csproj -c Release 2>&1 | grep -E 'error|Warning|Build succeeded' | head -4
echo "BUILD_EXIT:${PIPESTATUS[0]}"
echo '=== git status ==='
git status --short
echo '=== stage + commit + push ==='
git add -A
git commit -m 'Fix all dead notification/reminder links (404s): encode detail-route ids via IdObfuscator in SearchController booking results, PublicLeadsController, FollowUpNotificationService, ScheduledNotificationInitializerService, NotificationController; add clickable profile-picture upload button; brand-theme SweetAlert dialogs for logo removal + success toasts; wrap sidebar footer text so it is fully visible' 2>&1 | tail -2
git push origin main 2>&1 | tail -2
git log --oneline -1
