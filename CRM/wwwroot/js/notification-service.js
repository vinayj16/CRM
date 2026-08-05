// Firebase Cloud Messaging Service for Push Notifications
class FirebaseNotificationService {
    constructor() {
        this.isSupported = 'serviceWorker' in navigator && 'PushManager' in window;
        this.messaging = null;
        this.token = null;
        this.init();
    }

    async init() {
        if (!this.isSupported) {
            console.warn('Push notifications are not supported in this browser');
            return;
        }

        // Initialize Firebase (will be configured later)
        try {
            // Firebase initialization will go here
            console.log('Firebase notification service initialized');
        } catch (error) {
            console.error('Error initializing Firebase:', error);
        }
    }

    // Request permission and get FCM token
    async requestPermission() {
        if (!this.isSupported) {
            return false;
        }

        try {
            const permission = await Notification.requestPermission();
            if (permission === 'granted') {
                console.log('Notification permission granted');
                // Get FCM token will be implemented after Firebase setup
                return true;
            } else {
                console.log('Notification permission denied');
                return false;
            }
        } catch (error) {
            console.error('Error requesting notification permission:', error);
            return false;
        }
    }

    // Save FCM token to server
    async saveTokenToServer(token) {
        try {
            const response = await fetch('/api/notification/save-token', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({ token: token })
            });
            
            if (response.ok) {
                console.log('FCM token saved successfully');
                return true;
            } else {
                console.error('Failed to save FCM token');
                return false;
            }
        } catch (error) {
            console.error('Error saving FCM token:', error);
            return false;
        }
    }

    // Get current status
    getStatus() {
        return {
            isSupported: this.isSupported,
            hasPermission: Notification.permission === 'granted',
            hasToken: !!this.token
        };
    }
}

// Global notification handler for Firebase
class NotificationHandler {
    constructor() {
        this.firebaseService = new FirebaseNotificationService();
        this.setupFirebase();
    }

    setupFirebase() {
        // This will be called when Firebase is initialized
        window.notificationHandler = this;
    }

    // Handle incoming Firebase notifications (when app is in foreground)
    handleNotification(notification) {
        console.log('Received Firebase notification:', notification);

        // Update UI notification count
        this.updateNotificationCount(notification);

        // Show browser notification for immediate feedback
        this.showBrowserNotification(notification);

        // Update notification dropdown
        this.updateNotificationDropdown(notification);

        // Trigger toast notification for immediate feedback
        this.showToast(notification);
    }

    // Show browser notification as fallback
    showBrowserNotification(notification) {
        if ('Notification' in window && Notification.permission === 'granted') {
            const browserNotification = new Notification(notification.title, {
                body: notification.message,
                icon: this.getIconForType(notification.type?.toLowerCase() || 'info'),
                badge: '/favicon.ico',
                tag: `crm-${notification.type}-${notification.id}`,
                requireInteraction: notification.priority === 'High' || notification.priority === 'Urgent',
                data: {
                    link: notification.link
                }
            });

            // Auto-close after 5 seconds for non-urgent notifications
            if (notification.priority !== 'High' && notification.priority !== 'Urgent') {
                setTimeout(() => {
                    browserNotification.close();
                }, 5000);
            }

            // Handle click events
            browserNotification.onclick = () => {
                window.focus();
                if (notification.link) {
                    window.location.href = notification.link;
                }
                browserNotification.close();
            };
        }
    }

    // Update notification count in UI
    updateNotificationCount(notification) {
        const countElement = document.getElementById('notificationCount');
        if (countElement) {
            const currentCount = parseInt(countElement.textContent) || 0;
            countElement.textContent = currentCount + 1;
            countElement.style.display = currentCount + 1 > 0 ? 'inline' : 'none';
        }
    }

    // Get appropriate icon based on notification type
    getIconForType(type) {
        const iconMap = {
            'leadadded': '/images/icons/lead-added.png',
            'leadassigned': '/images/icons/lead-assigned.png',
            'quotationcreated': '/images/icons/quotation.png',
            'invoicecreated': '/images/icons/invoice.png',
            'paymentreceived': '/images/icons/payment.png',
            'bookingcreated': '/images/icons/booking.png',
            'followupdue': '/images/icons/followup.png',
            'systemalert': '/images/icons/system.png',
            'success': '/images/icons/success.png',
            'error': '/images/icons/error.png',
            'warning': '/images/icons/warning.png',
            'info': '/images/icons/info.png',
            'urgent': '/images/icons/urgent.png',
            'high': '/images/icons/high.png',
            'normal': '/images/icons/normal.png',
            'low': '/images/icons/low.png'
        };

        return iconMap[type] || '/images/icons/default-notification.png';
    }

    // Update notification dropdown
    updateNotificationDropdown(notification) {
        // Refresh the notification list
        if (typeof window.loadNotifications === 'function') {
            window.loadNotifications();
        }
    }

    // Show toast notification
    showToast(notification) {
        const toastContainer = document.getElementById('toastContainer') || this.createToastContainer();
        
        const toast = document.createElement('div');
        toast.className = `toast-notification toast-${notification.type?.toLowerCase() || 'info'} show`;
        toast.innerHTML = `
            <div class="toast-header">
                <strong class="me-auto">${notification.title}</strong>
                <small>${notification.createdOn}</small>
                <button type="button" class="btn-close" data-bs-dismiss="toast"></button>
            </div>
            <div class="toast-body">
                ${notification.message}
                ${notification.link ? `<br><a href="${notification.link}" class="btn btn-sm btn-primary mt-2">View Details</a>` : ''}
            </div>
        `;

        toastContainer.appendChild(toast);

        // Auto-remove after 5 seconds
        setTimeout(() => {
            toast.classList.remove('show');
            setTimeout(() => {
                if (toast.parentNode) {
                    toast.parentNode.removeChild(toast);
                }
            }, 300);
        }, 5000);

        // Handle close button
        const closeBtn = toast.querySelector('.btn-close');
        if (closeBtn) {
            closeBtn.addEventListener('click', () => {
                toast.classList.remove('show');
                setTimeout(() => {
                    if (toast.parentNode) {
                        toast.parentNode.removeChild(toast);
                    }
                }, 300);
            });
        }
    }

    // Create toast container if it doesn't exist
    createToastContainer() {
        const container = document.createElement('div');
        container.id = 'toastContainer';
        container.className = 'toast-container position-fixed top-0 end-0 p-3';
        container.style.zIndex = '9999';
        document.body.appendChild(container);
        return container;
    }
}

// Initialize notification handler when page loads
document.addEventListener('DOMContentLoaded', function() {
    if (!window.notificationHandler) {
        window.notificationHandler = new NotificationHandler();
    }
});

// Export for use in other scripts
window.FirebaseNotificationService = FirebaseNotificationService;
window.NotificationHandler = NotificationHandler;
