/**
 * Feature Gate - Global subscription/plan feature access control
 * 
 * When a page or action requires a specific plan feature, call:
 *   FeatureGate.requireFeature("whatsapp", "WhatsApp Integration")
 * 
 * This checks via the CheckFeatureAccess API and shows an upgrade modal
 * if the feature is not available in the user's current plan.
 */
window.FeatureGate = (function () {
    'use strict';

    // Cache for available plans (fetched once)
    var _availablePlans = null;
    var _planFetchPromise = null;

    /**
     * Fetch available plans (cached)
     */
    function getAvailablePlans() {
        if (_availablePlans) return Promise.resolve(_availablePlans);
        if (_planFetchPromise) return _planFetchPromise;

        _planFetchPromise = fetch('/saassubscription/getplans')
            .then(function (r) { return r.json(); })
            .then(function (data) {
                _availablePlans = data;
                return data;
            })
            .catch(function () {
                _availablePlans = [];
                return [];
            });

        return _planFetchPromise;
    }

    /**
     * Check if the current tenant's plan includes a specific feature
     * @param {string} feature - Feature name (whatsapp, facebook, email, customapi, advancedreports, prioritysupport, dataexport)
     * @returns {Promise<{hasAccess: boolean, message: string, planName: string}>}
     */
    function checkFeature(feature) {
        return fetch('/saassubscription/CheckFeatureAccess?feature=' + encodeURIComponent(feature))
            .then(function (r) { return r.json(); })
            .then(function (data) {
                // Cache available plans from response if provided
                if (data.availablePlans && Array.isArray(data.availablePlans) && data.availablePlans.length > 0) {
                    _availablePlans = data.availablePlans;
                }
                return {
                    hasAccess: data.hasAccess,
                    hasSubscription: data.hasSubscription,
                    planName: data.planName || 'Unknown',
                    message: data.message || '',
                    feature: data.feature
                };
            })
            .catch(function () {
                return { hasAccess: true, hasSubscription: true, planName: 'Unknown', message: '' };
            });
    }

    /**
     * Show a SweetAlert2 upgrade modal
     */
    function showUpgradeModal(featureName, planName, message) {
        if (typeof Swal === 'undefined') {
            if (confirm(message + '\n\nClick OK to view available plans and upgrade.')) {
                window.location.href = '/SaasSubscription/MyPlan';
            }
            return;
        }

        Swal.fire({
            title: '\uD83D\uDD12 Feature Not Available',
            html: '' +
                '<div style="text-align:left">' +
                '<p style="margin-bottom:12px">' + (message || 'The <strong>' + featureName + '</strong> feature is not included in your current plan.') + '</p>' +
                '<div style="background:#FFFFFF;padding:12px;border-radius:8px;margin-bottom:12px">' +
                '<p style="margin:0;font-size:0.85rem"><strong>Current Plan:</strong> ' + (planName || 'Unknown') + '</p>' +
                '</div>' +
                '<p style="margin-bottom:8px;font-size:0.9rem">To access this feature, please upgrade your plan:</p>' +
                '<div id="featureGatePlans" style="max-height:200px;overflow-y:auto">' +
                '<p style="text-align:center;color:#6B7B8D;font-size:0.85rem">Loading available plans...</p>' +
                '</div>' +
                '</div>',
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'View All Plans',
            cancelButtonText: 'Close',
            confirmButtonColor: '#2589C9',
            cancelButtonColor: '#6B7B8D',
            width: '520px',
            didOpen: function () {
                // Load available plans into the modal
                getAvailablePlans().then(function (plans) {
                    var container = document.getElementById('featureGatePlans');
                    if (!container) return;
                    if (!plans || plans.length === 0) {
                        container.innerHTML = '<p style="text-align:center;color:#6B7B8D;font-size:0.85rem">No plans available</p>';
                        return;
                    }
                    var html = '';
                    plans.forEach(function (p) {
                        var isCurrent = p.planName === planName;
                        html += '' +
                            '<div style="display:flex;align-items:center;justify-content:space-between;padding:6px 8px;margin:4px 0;border-radius:6px;' +
                            (isCurrent ? 'background:#E2E8F0;' : 'background:#FFFFFF;border:1px solid #E2E8F0;') + '">' +
                            '<div>' +
                            '<strong style="font-size:0.85rem">' + p.planName + '</strong>' +
                            (isCurrent ? ' <span style="font-size:0.7rem;color:#2589C9;font-weight:600">(Current)</span>' : '') +
                            '<br><span style="font-size:0.75rem;color:#6B7B8D">\u20B9' + Number(p.monthlyPrice).toLocaleString() + '/mo</span>' +
                            '</div>' +
                            (isCurrent ? '' : '<button class="btn btn-sm btn-primary" style="font-size:0.75rem;padding:3px 10px" onclick="FeatureGate.upgradeTo(' + p.planId + ',\'' + (p.planName || '').replace(/'/g, "\\'") + '\')">Choose</button>') +
                            '</div>';
                    });
                    container.innerHTML = html;
                });
            }
        }).then(function (result) {
            if (result.isConfirmed) {
                window.location.href = '/SaasSubscription/MyPlan';
            }
        });
    }

    return {
        /**
         * Require a specific feature. Shows upgrade modal if not available.
         * @param {string} feature - Feature name
         * @param {string} displayName - Human-readable feature name for the modal
         * @returns {Promise<boolean>} - Resolves to true if access is granted
         */
        requireFeature: function (feature, displayName) {
            return checkFeature(feature).then(function (result) {
                if (result.hasAccess) return true;
                showUpgradeModal(displayName || feature, result.planName, result.message);
                return false;
            });
        },

        /**
         * Redirect to a specific plan selection
         */
        upgradeTo: function (planId, planName) {
            window.location.href = '/SaasSubscription/MyPlan?selectedPlan=' + planId;
        },

        /**
         * Quick check without showing any UI
         */
        hasFeature: function (feature) {
            return checkFeature(feature).then(function (r) { return r.hasAccess; });
        }
    };
})();
