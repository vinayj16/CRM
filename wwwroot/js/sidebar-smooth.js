// SIDEBAR DROPDOWN STATE PERSISTENCE - ULTRA AGGRESSIVE FIX
// Prevents ALL flashing, re-expansion, and page reloading

(function() {
    'use strict';
    
    // Save dropdown state before ANY navigation
    function saveDropdownState() {
        const openDropdowns = [];
        
        // Save all currently open dropdowns
        document.querySelectorAll('.sidebar-item.has-dropdown .collapse.show').forEach(collapse => {
            if (collapse.id) {
                openDropdowns.push(collapse.id);
            }
        });
        
        // Also save active dropdowns (even if not manually opened)
        document.querySelectorAll('.sidebar-item.has-dropdown.has-active .collapse').forEach(collapse => {
            if (collapse.id && !openDropdowns.includes(collapse.id)) {
                openDropdowns.push(collapse.id);
            }
        });
        
        sessionStorage.setItem('openDropdowns', JSON.stringify(openDropdowns));
    }
    
    // Restore dropdown state INSTANTLY
    function restoreDropdownStateInstantly() {
        const openDropdowns = JSON.parse(sessionStorage.getItem('openDropdowns') || '[]');
        
        openDropdowns.forEach(function(dropdownId) {
            const collapse = document.getElementById(dropdownId);
            if (collapse) {
                const parent = collapse.closest('.sidebar-item.has-dropdown');
                const link = parent?.querySelector('.sidebar-link');
                
                // Force immediate display - NO animation whatsoever
                collapse.classList.add('show');
                collapse.classList.remove('collapse'); // Remove collapse class temporarily
                collapse.style.cssText = 'display: block !important; height: auto !important; opacity: 1 !important; transition: none !important;';
                
                if (link) {
                    link.classList.remove('collapsed');
                    link.setAttribute('aria-expanded', 'true');
                    link.style.transition = 'none';
                    link.style.background = 'transparent';
                    link.style.backgroundColor = 'transparent';
                    
                    const chevron = link.querySelector('[data-feather^="chevron"]');
                    if (chevron) {
                        chevron.setAttribute('data-feather', 'chevron-up');
                    }
                }
                
                // Re-add collapse class after a moment
                setTimeout(function() {
                    collapse.classList.add('collapse');
                }, 50);
            }
        });
        
        // Handle active dropdowns
        document.querySelectorAll('.sidebar-item.has-dropdown.has-active').forEach(function(item) {
            const collapse = item.querySelector('.collapse');
            if (collapse && !collapse.classList.contains('show')) {
                collapse.classList.add('show');
                collapse.style.cssText = 'display: block !important; height: auto !important; opacity: 1 !important; transition: none !important;';
                
                const link = item.querySelector('.sidebar-link');
                if (link) {
                    link.classList.remove('collapsed');
                    link.setAttribute('aria-expanded', 'true');
                    link.style.background = 'transparent';
                    link.style.backgroundColor = 'transparent';
                    
                    const chevron = link.querySelector('[data-feather^="chevron"]');
                    if (chevron) {
                        chevron.setAttribute('data-feather', 'chevron-up');
                    }
                }
            }
        });
        
        // Re-render feather icons
        if (typeof feather !== 'undefined') {
            feather.replace();
        }
    }
    
    // Run restoration IMMEDIATELY - multiple times for redundancy
    restoreDropdownStateInstantly();
    setTimeout(restoreDropdownStateInstantly, 0);
    setTimeout(restoreDropdownStateInstantly, 10);
    setTimeout(restoreDropdownStateInstantly, 50);
    setTimeout(restoreDropdownStateInstantly, 100);
    
    // Also run on DOMContentLoaded
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', restoreDropdownStateInstantly);
    }
    
    // Re-enable transitions after page is fully loaded
    setTimeout(function() {
        document.querySelectorAll('.collapse.show').forEach(function(collapse) {
            collapse.style.transition = '';
        });
        document.querySelectorAll('.sidebar-link').forEach(function(link) {
            link.style.transition = '';
        });
    }, 200);
    
    // Save state before ANY navigation
    document.addEventListener('click', function(e) {
        const link = e.target.closest('a[href]');
        if (link && !link.closest('.sidebar-item.has-dropdown > .sidebar-link')) {
            saveDropdownState();
        }
    });
    
    // Handle dropdown toggle clicks
    document.addEventListener('click', function(e) {
        const dropdownToggle = e.target.closest('.sidebar-item.has-dropdown > .sidebar-link');
        if (dropdownToggle && document.body.classList.contains('page-loaded')) {
            e.preventDefault();
            e.stopPropagation();
            
            const parent = dropdownToggle.closest('.sidebar-item.has-dropdown');
            const collapse = parent.querySelector('.collapse');
            const chevron = dropdownToggle.querySelector('[data-feather^="chevron"]');
            
            // Don't toggle if sidebar is collapsed
            if (document.querySelector('.sidebar')?.classList.contains('collapsed')) {
                return;
            }
            
            // Toggle with smooth animation (only for user clicks)
            if (collapse.classList.contains('show')) {
                collapse.classList.remove('show');
                dropdownToggle.classList.add('collapsed');
                dropdownToggle.setAttribute('aria-expanded', 'false');
                if (chevron) chevron.setAttribute('data-feather', 'chevron-down');
            } else {
                collapse.classList.add('show');
                dropdownToggle.classList.remove('collapsed');
                dropdownToggle.setAttribute('aria-expanded', 'true');
                if (chevron) chevron.setAttribute('data-feather', 'chevron-up');
            }
            
            // Re-render feather icons
            if (typeof feather !== 'undefined') {
                feather.replace();
            }
            
            // Save state
            saveDropdownState();
        }
    });
    
    // Prevent Bootstrap from animating on page load
    const style = document.createElement('style');
    style.id = 'no-collapse-animation';
    style.textContent = `
        .collapsing {
            transition: none !important;
            display: block !important;
        }
    `;
    document.head.appendChild(style);
    
    // Remove the style after page is loaded to allow smooth user interactions
    setTimeout(function() {
        const styleEl = document.getElementById('no-collapse-animation');
        if (styleEl) {
            styleEl.remove();
        }
    }, 300);
    
})();
