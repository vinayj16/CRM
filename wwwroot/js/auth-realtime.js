document.addEventListener("DOMContentLoaded", function () {
    document.body.classList.add("auth-ready");

    var forms = document.querySelectorAll(".login-form-section form");
    forms.forEach(function (form) {
        wireLiveValidation(form);
        wireSubmitLoading(form);
    });

    wirePasswordToggles();
    wireCapsLockHints();
    wirePasswordStrength();
});

function wireLiveValidation(form) {
    var fields = form.querySelectorAll(".form-control");

    fields.forEach(function (field) {
        var validate = function () {
            updateFilledState(field);
            updateFieldValidity(field);
        };

        field.addEventListener("input", validate);
        field.addEventListener("blur", validate);
        validate();
    });
}

function updateFilledState(field) {
    var wrapper = field.closest(".input-wrapper");
    if (!wrapper) {
        return;
    }

    var value = (field.value || "").trim();
    wrapper.classList.toggle("is-filled", value.length > 0);
}

function updateFieldValidity(field) {
    var wrapper = field.closest(".input-wrapper");
    if (!wrapper) {
        return;
    }

    wrapper.classList.remove("field-valid", "field-invalid");

    var value = (field.value || "").trim();
    if (!value) {
        return;
    }

    var isValid = true;
    var type = (field.getAttribute("type") || "").toLowerCase();

    if (type === "email") {
        isValid = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
    } else if (field.name && field.name.toLowerCase().includes("phone")) {
        isValid = /^\d{10}$/.test(value);
    } else if (type === "password") {
        isValid = value.length >= 6;
    } else if (field.tagName === "SELECT") {
        isValid = value !== "";
    }

    if (isValid) {
        wrapper.classList.add("field-valid");
    } else {
        wrapper.classList.add("field-invalid");
    }
}

function wireSubmitLoading(form) {
    if (form.id === "registerForm") return;
    form.addEventListener("submit", function (event) {
        if (!form.checkValidity()) {
            return;
        }

        var submitBtn = form.querySelector("button[type='submit'].btn-primary");
        if (!submitBtn || submitBtn.classList.contains("is-loading")) {
            return;
        }

        var originalHtml = submitBtn.innerHTML;
        submitBtn.dataset.originalHtml = originalHtml;
        submitBtn.classList.add("is-loading");
        submitBtn.disabled = true;
        submitBtn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Processing...';

        // Safety fallback if submission is interrupted by client-side validation scripts.
        window.setTimeout(function () {
            if (!document.body.contains(submitBtn)) {
                return;
            }
            if (submitBtn.classList.contains("is-loading")) {
                submitBtn.classList.remove("is-loading");
                submitBtn.disabled = false;
                submitBtn.innerHTML = submitBtn.dataset.originalHtml || originalHtml;
            }
        }, 9000);
    });
}

function wirePasswordToggles() {
    var toggles = document.querySelectorAll(".password-toggle");
    toggles.forEach(function (toggle) {
        toggle.addEventListener("click", function () {
            var wrapper = toggle.closest(".input-wrapper");
            var passwordInput = wrapper ? wrapper.querySelector("input[type='password'], input[type='text']") : null;
            if (!passwordInput) {
                return;
            }

            var icon = toggle.querySelector("i");
            var isPassword = passwordInput.type === "password";
            passwordInput.type = isPassword ? "text" : "password";

            if (icon) {
                icon.classList.toggle("fa-eye", !isPassword);
                icon.classList.toggle("fa-eye-slash", isPassword);
            }
        });
    });
}

function wireCapsLockHints() {
    var passwordInputs = document.querySelectorAll("input[type='password']");
    passwordInputs.forEach(function (input) {
        var wrapper = input.closest(".input-wrapper");
        if (!wrapper) {
            return;
        }

        var hint = document.createElement("small");
        hint.className = "caps-lock-hint";
        hint.textContent = "Caps Lock is ON";
        wrapper.appendChild(hint);

        ["keydown", "keyup", "focus", "blur"].forEach(function (eventName) {
            input.addEventListener(eventName, function (event) {
                var caps = typeof event.getModifierState === "function" && event.getModifierState("CapsLock");
                hint.classList.toggle("show", !!caps);
                if (eventName === "blur") {
                    hint.classList.remove("show");
                }
            });
        });
    });
}

function wirePasswordStrength() {
    if (!window.location.pathname.toLowerCase().includes("/account/register")) {
        return;
    }

    var passwordInput = document.querySelector("input[name='Password']");
    if (!passwordInput) {
        return;
    }

    var wrapper = passwordInput.closest(".input-wrapper");
    if (!wrapper) {
        return;
    }

    var strengthHost = document.createElement("div");
    strengthHost.className = "password-strength";
    strengthHost.innerHTML = '<div class="password-strength-bar"></div><small class="password-strength-text">Use 8+ chars with letters, numbers and symbols</small>';
    wrapper.appendChild(strengthHost);

    var bar = strengthHost.querySelector(".password-strength-bar");
    var text = strengthHost.querySelector(".password-strength-text");

    passwordInput.addEventListener("input", function () {
        var value = passwordInput.value || "";
        var score = 0;

        if (value.length >= 8) {
            score += 25;
        }
        if (/[A-Z]/.test(value)) {
            score += 20;
        }
        if (/[a-z]/.test(value)) {
            score += 15;
        }
        if (/\d/.test(value)) {
            score += 20;
        }
        if (/[^A-Za-z0-9]/.test(value)) {
            score += 20;
        }

        bar.style.width = score + "%";
        bar.classList.remove("weak", "medium", "strong");

        if (score < 40) {
            bar.classList.add("weak");
            text.textContent = value ? "Weak password" : "Use 8+ chars with letters, numbers and symbols";
        } else if (score < 75) {
            bar.classList.add("medium");
            text.textContent = "Medium strength password";
        } else {
            bar.classList.add("strong");
            text.textContent = "Strong password";
        }
    });
}
