// Follow-up Dashboard JavaScript
class FollowUpDashboard {
    constructor() {
        this.todayFollowUps = [];
        this.overdueFollowUps = [];
        this.init();
    }

    async init() {
        await this.loadTodayFollowUps();
        await this.loadOverdueFollowUps();
        this.setupEventListeners();
        this.startAutoRefresh();
    }

    async loadTodayFollowUps() {
        try {
            const response = await fetch('/api/FollowUpNotification/today-followups/@ViewBag.UserId');
            const data = await response.json();
            
            if (data.success) {
                this.todayFollowUps = data.followUps || [];
                this.renderTodayFollowUps();
                this.updateNotificationCount();
            }
        } catch (error) {
            console.error('Error loading today follow-ups:', error);
        }
    }

    async loadOverdueFollowUps() {
        try {
            const response = await fetch('/api/FollowUpNotification/overdue-followups/@ViewBag.UserId');
            const data = await response.json();
            
            if (data.success) {
                this.overdueFollowUps = data.followUps || [];
                this.renderOverdueFollowUps();
                this.updateNotificationCount();
            }
        } catch (error) {
            console.error('Error loading overdue follow-ups:', error);
        }
    }

    renderTodayFollowUps() {
        const container = document.getElementById('todayFollowUpsContainer');
        if (!container) return;

        if (this.todayFollowUps.length === 0) {
            container.innerHTML = `
                <div class="text-center text-muted py-3">
                    <i class="fas fa-calendar-check fa-2x mb-2"></i>
                    <p>No follow-ups scheduled for today</p>
                </div>
            `;
            return;
        }

        const html = this.todayFollowUps.map(followUp => `
            <div class="card mb-2 border-left-primary">
                <div class="card-body py-2">
                    <div class="d-flex justify-content-between align-items-start">
                        <div class="flex-grow-1">
                            <h6 class="mb-1">${followUp.leadName}</h6>
                            <div class="d-flex flex-wrap gap-1 mb-1">
                                <span class="badge bg-info">${followUp.stage || 'Not specified'}</span>
                                <span class="badge bg-secondary">${followUp.status || 'Not specified'}</span>
                            </div>
                            ${followUp.comments ? `<p class="mb-0 small text-muted">${followUp.comments}</p>` : ''}
                        </div>
                        <div class="text-end">
                            <small class="text-muted">${this.formatTime(followUp.followUpDate)} ${followUp.followUpTime || ''}</small>
                            <div>
                                <a href="/Leads/Details/${followUp.leadId}" class="btn btn-sm btn-outline-primary">
                                    <i class="fas fa-eye"></i> View
                                </a>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `).join('');

        container.innerHTML = html;
    }

    renderOverdueFollowUps() {
        const container = document.getElementById('overdueFollowUpsContainer');
        if (!container) return;

        if (this.overdueFollowUps.length === 0) {
            container.innerHTML = `
                <div class="text-center text-muted py-3">
                    <i class="fas fa-check-circle fa-2x mb-2"></i>
                    <p>No overdue follow-ups</p>
                </div>
            `;
            return;
        }

        const html = this.overdueFollowUps.map(followUp => `
            <div class="card mb-2 border-left-danger">
                <div class="card-body py-2">
                    <div class="d-flex justify-content-between align-items-start">
                        <div class="flex-grow-1">
                            <h6 class="mb-1">${followUp.leadName}</h6>
                            <div class="d-flex flex-wrap gap-1 mb-1">
                                <span class="badge bg-danger">${followUp.daysOverdue} days overdue</span>
                                <span class="badge bg-warning">${followUp.stage || 'Not specified'}</span>
                                <span class="badge bg-secondary">${followUp.status || 'Not specified'}</span>
                            </div>
                            ${followUp.comments ? `<p class="mb-0 small text-muted">${followUp.comments}</p>` : ''}
                        </div>
                        <div class="text-end">
                            <small class="text-muted">Due: ${this.formatDate(followUp.followUpDate)} ${followUp.followUpTime || ''}</small>
                            <div>
                                <a href="/Leads/Details/${followUp.leadId}" class="btn btn-sm btn-outline-danger">
                                    <i class="fas fa-exclamation-triangle"></i> Action
                                </a>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `).join('');

        container.innerHTML = html;
    }

    updateNotificationCount() {
        const totalCount = this.todayFollowUps.length + this.overdueFollowUps.length;
        const countElement = document.getElementById('followUpNotificationCount');
        
        if (countElement) {
            countElement.textContent = totalCount;
            countElement.style.display = totalCount > 0 ? 'inline-block' : 'none';
        }

        // Show browser notification for urgent items
        if (this.overdueFollowUps.length > 0) {
            this.showUrgentNotification();
        }
    }

    showUrgentNotification() {
        if ('Notification' in window && Notification.permission === 'granted') {
            const notification = new Notification('Urgent: Overdue Follow-Ups', {
                body: `You have ${this.overdueFollowUps.length} overdue follow-up${this.overdueFollowUps.length > 1 ? 's' : ''} that need immediate attention!`,
                icon: '/img/icons/icon-48x48.png',
                badge: '/favicon.ico',
                tag: 'overdue-followups',
                requireInteraction: true,
                data: { link: '/Leads/Index?overdue=true' }
            });

            notification.onclick = () => {
                window.focus();
                window.location.href = '/Leads/Index?overdue=true';
                notification.close();
            };
        }
    }

    setupEventListeners() {
        // Refresh button
        const refreshBtn = document.getElementById('refreshFollowUps');
        if (refreshBtn) {
            refreshBtn.addEventListener('click', () => {
                this.loadTodayFollowUps();
                this.loadOverdueFollowUps();
            });
        }
    }

    startAutoRefresh() {
        // Refresh every 5 minutes
        setInterval(() => {
            this.loadTodayFollowUps();
            this.loadOverdueFollowUps();
        }, 5 * 60 * 1000);
    }

    formatTime(dateString) {
        if (!dateString) return 'No time';
        const date = new Date(dateString);
        return date.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });
    }

    formatDate(dateString) {
        if (!dateString) return 'No date';
        const date = new Date(dateString);
        return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
    }
}

// Initialize dashboard when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    window.followUpDashboard = new FollowUpDashboard();
});
