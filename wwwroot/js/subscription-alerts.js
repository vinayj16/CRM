// Global subscription limit alert functions
window.SubscriptionAlerts = {
    
    // Show sweet alert for plan expiration (leads)
    showPlanExpiredAlert: function(message, availablePlans) {
        let plansHtml = '';
        if (availablePlans && availablePlans.length > 0) {
            plansHtml = '<div class="mt-3"><h6>Available Plans:</h6>';
            availablePlans.forEach(plan => {
                plansHtml += `
                    <div class="card mb-2" style="border: 1px solid #E2E8F0;">
                        <div class="card-body p-3">
                            <h6 class="card-title mb-1">${plan.planName}</h6>
                            <p class="card-text mb-2">
                                <strong>Monthly:</strong> ₹${plan.monthlyPrice.toLocaleString()} | 
                                <strong>Yearly:</strong> ₹${plan.yearlyPrice.toLocaleString()}<br>
                                <strong>Max Leads:</strong> ${plan.maxLeadsPerMonth === -1 ? 'Unlimited' : plan.maxLeadsPerMonth + '/month'}
                            </p>
                            <button class="btn btn-primary btn-sm" onclick="SubscriptionAlerts.selectPlan(${plan.planId}, '${plan.planName}')">
                                Choose ${plan.planName}
                            </button>
                        </div>
                    </div>
                `;
            });
            plansHtml += '</div>';
        }

        Swal.fire({
            title: 'Plan Expired!',
            html: `
                <div class="text-start">
                    <p class="mb-3">${message}</p>
                    <p class="mb-3">To continue creating leads, please upgrade your plan:</p>
                    ${plansHtml}
                </div>
            `,
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'View My Plans',
            cancelButtonText: 'Close',
            confirmButtonColor: '#2589C9',
            cancelButtonColor: '#6B7B8D',
            width: '600px'
        }).then((result) => {
            if (result.isConfirmed) {
                window.location.href = '/Subscription/MyPlan';
            }
        });
    },

    // Show sweet alert for agent limit reached
    showAgentLimitAlert: function(message, availablePlans) {
        let plansHtml = '';
        if (availablePlans && availablePlans.length > 0) {
            plansHtml = '<div class="mt-3"><h6>Available Plans:</h6>';
            availablePlans.forEach(plan => {
                plansHtml += `
                    <div class="card mb-2" style="border: 1px solid #E2E8F0;">
                        <div class="card-body p-3">
                            <h6 class="card-title mb-1">${plan.planName}</h6>
                            <p class="card-text mb-2">
                                <strong>Monthly:</strong> ₹${plan.monthlyPrice.toLocaleString()} | 
                                <strong>Yearly:</strong> ₹${plan.yearlyPrice.toLocaleString()}<br>
                                <strong>Max Agents:</strong> ${plan.maxAgents === -1 ? 'Unlimited' : plan.maxAgents}
                            </p>
                            <button class="btn btn-primary btn-sm" onclick="SubscriptionAlerts.selectPlan(${plan.planId}, '${plan.planName}')">
                                Choose ${plan.planName}
                            </button>
                        </div>
                    </div>
                `;
            });
            plansHtml += '</div>';
        }

        Swal.fire({
            title: 'Agent Limit Reached!',
            html: `
                <div class="text-start">
                    <p class="mb-3">${message}</p>
                    <p class="mb-3">To add more agents, please upgrade your plan:</p>
                    ${plansHtml}
                </div>
            `,
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'View My Plans',
            cancelButtonText: 'Close',
            confirmButtonColor: '#2589C9',
            cancelButtonColor: '#6B7B8D',
            width: '600px'
        }).then((result) => {
            if (result.isConfirmed) {
                window.location.href = '/Subscription/MyPlan';
            }
        });
    },

    // Show sweet alert for storage limit reached
    showStorageLimitAlert: function(message, currentUsageGB, limitGB, availablePlans) {
        let plansHtml = '';
        if (availablePlans && availablePlans.length > 0) {
            plansHtml = '<div class="mt-3"><h6>Available Plans:</h6>';
            availablePlans.forEach(plan => {
                plansHtml += `
                    <div class="card mb-2" style="border: 1px solid #E2E8F0;">
                        <div class="card-body p-3">
                            <h6 class="card-title mb-1">${plan.planName}</h6>
                            <p class="card-text mb-2">
                                <strong>Monthly:</strong> ₹${plan.monthlyPrice.toLocaleString()} | 
                                <strong>Yearly:</strong> ₹${plan.yearlyPrice.toLocaleString()}<br>
                                <strong>Storage:</strong> ${plan.maxStorageGB === -1 ? 'Unlimited' : plan.maxStorageGB + 'GB'}
                            </p>
                            <button class="btn btn-primary btn-sm" onclick="SubscriptionAlerts.selectPlan(${plan.planId}, '${plan.planName}')">
                                Choose ${plan.planName}
                            </button>
                        </div>
                    </div>
                `;
            });
            plansHtml += '</div>';
        }

        Swal.fire({
            title: 'Storage Limit Reached!',
            html: `
                <div class="text-start">
                    <p class="mb-3">${message}</p>
                    <div class="alert alert-info mb-3">
                        <strong>Current Usage:</strong> ${currentUsageGB}GB / ${limitGB}GB
                    </div>
                    <p class="mb-3">To upload more files, please upgrade your plan:</p>
                    ${plansHtml}
                </div>
            `,
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'View My Plans',
            cancelButtonText: 'Close',
            confirmButtonColor: '#2589C9',
            cancelButtonColor: '#6B7B8D',
            width: '600px'
        }).then((result) => {
            if (result.isConfirmed) {
                window.location.href = '/Subscription/MyPlan';
            }
        });
    },

    // Show sweet alert for feature access denied
    showFeatureAccessAlert: function(featureName) {
        Swal.fire({
            title: 'Feature Not Available',
            html: `
                <div class="text-start">
                    <p class="mb-3">The <strong>${featureName}</strong> feature is not available in your current plan.</p>
                    <p class="mb-3">Please upgrade your plan to access this feature.</p>
                </div>
            `,
            icon: 'info',
            showCancelButton: true,
            confirmButtonText: 'View Plans',
            cancelButtonText: 'Close',
            confirmButtonColor: '#2589C9',
            cancelButtonColor: '#6B7B8D'
        }).then((result) => {
            if (result.isConfirmed) {
                window.location.href = '/Subscription/MyPlan';
            }
        });
    },

    // Handle plan selection from sweet alert
    selectPlan: function(planId, planName) {
        Swal.fire({
            title: `Upgrade to ${planName}?`,
            text: 'You will be redirected to complete the payment.',
            icon: 'question',
            showCancelButton: true,
            confirmButtonText: 'Yes, Upgrade Now',
            cancelButtonText: 'Cancel',
            confirmButtonColor: '#16A34A',
            cancelButtonColor: '#6B7B8D'
        }).then((result) => {
            if (result.isConfirmed) {
                window.location.href = `/Subscription/MyPlan?selectedPlan=${planId}`;
            }
        });
    }
};