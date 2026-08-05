// Generic Form Validation Script
// Add this script to any page that needs form validation

function validateForm(formSelector) {
    const form = document.querySelector(formSelector);
    if (!form) return true;
    
    // Clear previous validation messages
    form.querySelectorAll('.invalid-feedback').forEach(el => el.remove());
    form.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid'));
    
    let isValid = true;
    let firstInvalidField = null;
    
    // Check all required fields
    form.querySelectorAll('input[required], textarea[required], select[required]').forEach(function(field) {
        const fieldContainer = field.closest('.col-md-6, .col-md-4, .col-md-3, .col-12, .mb-3, .form-group');
        let label = '';
        
        // Try to find label text
        if (fieldContainer) {
            const labelElement = fieldContainer.querySelector('label');
            if (labelElement) {
                label = labelElement.textContent.replace('*', '').trim();
            }
        }
        
        // Fallback to placeholder or field name
        if (!label) {
            label = field.placeholder || field.name || field.id || 'This field';
        }
        
        // Check if field is empty
        if (!field.value || field.value.trim() === '') {
            field.classList.add('is-invalid');
            
            // Create error message
            const errorDiv = document.createElement('div');
            errorDiv.className = 'invalid-feedback d-block';
            errorDiv.textContent = label + ' is required';
            
            // Insert after the field or after its parent container
            if (field.parentNode.classList.contains('input-group')) {
                field.parentNode.parentNode.insertBefore(errorDiv, field.parentNode.nextSibling);
            } else {
                field.parentNode.insertBefore(errorDiv, field.nextSibling);
            }
            
            if (!firstInvalidField) {
                firstInvalidField = field;
            }
            isValid = false;
        }
    });
    
    // Scroll to first invalid field
    if (!isValid && firstInvalidField) {
        firstInvalidField.scrollIntoView({ behavior: 'smooth', block: 'center' });
        setTimeout(() => firstInvalidField.focus(), 500);
    }
    
    return isValid;
}

// Auto-attach validation to forms with class 'validate-form'
document.addEventListener('DOMContentLoaded', function() {
    document.querySelectorAll('form.validate-form').forEach(function(form) {
        form.addEventListener('submit', function(e) {
            if (!validateForm('#' + form.id)) {
                e.preventDefault();
                e.stopPropagation();
            }
        });
    });
});

// Add styling for invalid fields and required asterisks
const style = document.createElement('style');
style.textContent = `
    .is-invalid {
        border-color: #DC2626 !important;
        box-shadow: 0 0 0 0.2rem rgba(220,38,38, 0.25) !important;
    }
    
    .invalid-feedback {
        color: #DC2626;
        font-size: 0.875rem;
        margin-top: 0.25rem;
        display: block;
    }
    
    .text-danger {
        color: #DC2626 !important;
    }
    
    label .text-danger {
        font-weight: bold;
    }
`;
document.head.appendChild(style);