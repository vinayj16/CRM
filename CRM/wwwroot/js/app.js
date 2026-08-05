// Sidebar toggle functionality
document.addEventListener('DOMContentLoaded', function() {
    const sidebarToggle = document.querySelector('.sidebar-toggle');
    const sidebar = document.querySelector('.sidebar');
    const main = document.querySelector('.main');
    const desktopBreakpoint = 992;
    const sidebarStateKey = 'crm.sidebar.collapsed';

    if (!sidebar || !main) {
        return;
    }

    function isDesktop() {
        return window.innerWidth >= desktopBreakpoint;
    }

    function setDesktopCollapsed(isCollapsed, persistState) {
        sidebar.classList.toggle('collapsed', isCollapsed);
        main.classList.toggle('expanded', isCollapsed);

        document.dispatchEvent(new CustomEvent('crm:sidebar-state-changed', {
            detail: {
                collapsed: isCollapsed,
                isDesktop: isDesktop()
            }
        }));

        if (persistState && isDesktop()) {
            localStorage.setItem(sidebarStateKey, isCollapsed ? '1' : '0');
        }
    }

    // Shared API used by layout-level scripts for immediate, consistent updates.
    window.crmSidebar = {
        isDesktop: isDesktop,
        isCollapsed: function() {
            return sidebar.classList.contains('collapsed');
        },
        setCollapsed: function(isCollapsed, persistState) {
            setDesktopCollapsed(!!isCollapsed, !!persistState);
        },
        toggleCollapsed: function(persistState) {
            const shouldCollapse = !sidebar.classList.contains('collapsed');
            setDesktopCollapsed(shouldCollapse, !!persistState);
        }
    };

    function applySavedDesktopState() {
        const savedState = localStorage.getItem(sidebarStateKey) === '1';
        setDesktopCollapsed(savedState, false);
    }

    if (isDesktop()) {
        applySavedDesktopState();
    }

    // Create mobile sidebar backdrop dynamically
    let backdrop = document.querySelector('.sidebar-backdrop');
    if (!backdrop) {
        backdrop = document.createElement('div');
        backdrop.className = 'sidebar-backdrop';
        document.body.appendChild(backdrop);
    }

    if (sidebarToggle) {
        sidebarToggle.addEventListener('click', function(e) {
            e.preventDefault();
            e.stopPropagation();

            // Check if we're in mobile view
            if (!isDesktop()) {
                // Mobile: toggle 'show' class
                sidebar.classList.toggle('show');
                if (backdrop) {
                    backdrop.classList.toggle('show', sidebar.classList.contains('show'));
                }
            } else {
                // Desktop: toggle 'collapsed' class
                window.crmSidebar.toggleCollapsed(true);
            }
        });
    }

    // Close sidebar when clicking outside in mobile mode
    document.addEventListener('click', function(e) {
        if (!isDesktop() && sidebarToggle) {
            const isClickInsideSidebar = sidebar.contains(e.target);
            const isClickOnToggle = sidebarToggle.contains(e.target);
            
            if (!isClickInsideSidebar && !isClickOnToggle && sidebar.classList.contains('show')) {
                sidebar.classList.remove('show');
                if (backdrop) {
                    backdrop.classList.remove('show');
                }
            }
        }
    });

    // Close sidebar when tapping the backdrop
    if (backdrop) {
        backdrop.addEventListener('click', function() {
            sidebar.classList.remove('show');
            backdrop.classList.remove('show');
        });
    }

    // Prevent clicks inside sidebar from closing it
    if (sidebar) {
        sidebar.addEventListener('click', function(e) {
            e.stopPropagation();
        });
    }

    // Handle window resize
    window.addEventListener('resize', function() {
        if (isDesktop()) {
            // Remove mobile 'show' class on desktop
            sidebar.classList.remove('show');
            if (backdrop) {
                backdrop.classList.remove('show');
            }
            applySavedDesktopState();
        } else {
            // Remove desktop 'collapsed' class on mobile
            setDesktopCollapsed(false, false);
        }
    });
});

/* =====================================================
   GLOBAL ANTI-FORGERY TOKEN INTERCEPTOR
   =====================================================
   Auto-attaches the CSRF token to all AJAX requests.
   The token comes from @Html.AntiForgeryToken() rendered in _Layout.cshtml.
   ===================================================== */
(function() {
    'use strict';

    function getToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    // ── Intercept fetch() calls ──
    var origFetch = window.fetch;
    window.fetch = function(url, opts) {
        opts = opts || {};
        var method = (opts.method || 'GET').toUpperCase();
        if (method !== 'GET' && method !== 'HEAD' && method !== 'OPTIONS') {
            opts.headers = opts.headers || {};
            // Don't override if already set
            if (!opts.headers['RequestVerificationToken'] && !opts.headers['requestverificationtoken']) {
                var token = getToken();
                if (token) {
                    opts.headers['RequestVerificationToken'] = token;
                }
            }
        }
        return origFetch.call(this, url, opts);
    };

    // ── Intercept XMLHttpRequest calls ──
    var origOpen = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function(method, url, async, user, pass) {
        this._crmMethod = (method || 'GET').toUpperCase();
        return origOpen.apply(this, arguments);
    };

    var origSend = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.send = function(body) {
        if (this._crmMethod && this._crmMethod !== 'GET' && this._crmMethod !== 'HEAD' && this._crmMethod !== 'OPTIONS') {
            var token = getToken();
            if (token && !this._crmTokenSet) {
                this.setRequestHeader('RequestVerificationToken', token);
                this._crmTokenSet = true;
            }
        }
        return origSend.apply(this, arguments);
    };

    // ── jQuery interceptor if jQuery is loaded ──
    if (typeof jQuery !== 'undefined') {
        jQuery.ajaxSetup({
            beforeSend: function(xhr, settings) {
                var method = (settings.type || 'GET').toUpperCase();
                if (method !== 'GET' && method !== 'HEAD' && method !== 'OPTIONS') {
                    var token = getToken();
                    if (token && !xhr._crmTokenSet) {
                        xhr.setRequestHeader('RequestVerificationToken', token);
                        xhr._crmTokenSet = true;
                    }
                }
            }
        });
    }
})();

// Chart.js theme colors (for future use) - uses hex values for JS compatibility
if (typeof window !== 'undefined') {
    window.theme = {
        primary: '#1A6FA8',
        secondary: '#6B7B8D',
        success: '#1A6FA8',
        info: '#2589C9',
        warning: '#D4A45A',
        danger: '#B8883E',
        light: '#F0F4F8',
        dark: '#1E2A3A'
    };
}

// =====================================================
// Auto-refresh after CRUD operations - merged with CSRF interceptor
// =====================================================
(function() {
    'use strict';

    // CRUD endpoints that should auto-refresh after successful POST
    var CRUD_PATHS = [
        '/Leads/', '/Bookings/', '/Properties/', '/Payments/', '/Invoices/',
        '/Quotations/', '/Expenses/', '/Revenue/', '/Ticket/', '/Agent/',
        '/Attendance/', '/ManageUsers/', '/Settings/', '/Profile/'
    ];

    function isCrudUrl(url) {
        if (!url) return false;
        var lowerUrl = url.toLowerCase();
        return CRUD_PATHS.some(function(path) {
            return lowerUrl.indexOf(path.toLowerCase()) >= 0;
        });
    }

    function shouldSkipRefresh(url) {
        if (!url) return true;
        var skipPatterns = ['seed', 'get', 'mark', 'search', 'filter', 'export', 'login', 'logout', 'sendcontact', 'submitinquiry'];
        var lowerUrl = url.toLowerCase();
        return skipPatterns.some(function(pattern) {
            return lowerUrl.indexOf(pattern) >= 0;
        });
    }

    // ── Enhanced fetch interceptor: CSRF + auto-refresh (merged) ──
    var origFetch = window.fetch;
    window.fetch = function(url, opts) {
        opts = opts || {};
        var method = (opts.method || 'GET').toUpperCase();

        // Add CSRF token for non-GET requests
        if (method !== 'GET' && method !== 'HEAD' && method !== 'OPTIONS') {
            opts.headers = opts.headers || {};
            if (!opts.headers['RequestVerificationToken'] && !opts.headers['requestverificationtoken']) {
                var token = document.querySelector('input[name="__RequestVerificationToken"]');
                if (token && token.value) {
                    opts.headers['RequestVerificationToken'] = token.value;
                }
            }
        }

        return origFetch.call(this, url, opts).then(function(response) {
            // Auto-refresh for CRUD POST operations
            if (method === 'POST' && isCrudUrl(url) && !shouldSkipRefresh(url)) {
                var cloned = response.clone();
                cloned.json().then(function(data) {
                    if (data && data.success === true) {
                        setTimeout(function() { location.reload(); }, 1200);
                    }
                }).catch(function() {});
            }
            return response;
        });
    };

    // ── jQuery AJAX auto-refresh ──
    if (typeof jQuery !== 'undefined') {
        $(document).ajaxSuccess(function(event, xhr, settings) {
            if (!settings || settings.type !== 'POST') return;
            if (!isCrudUrl(settings.url)) return;
            if (shouldSkipRefresh(settings.url)) return;

            try {
                var resp = xhr.responseJSON;
                if (resp && resp.success === true) {
                    setTimeout(function() { location.reload(); }, 1200);
                }
            } catch(e) {}
        });
    }
})();
