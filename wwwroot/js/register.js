const form = document.getElementById("registerForm");
const message = document.getElementById("formMessage");

form.addEventListener("submit", async event => {
    event.preventDefault();
    if (form.getAttribute("aria-busy") === "true") return;
    message.className = "form-message";

    const email = form.email.value.trim();
    const password = form.password.value;

    if (!form.checkValidity()) { form.reportValidity(); return; }
    if (password !== form.confirmPassword.value) {
        message.textContent = "The passwords do not match.";
        message.classList.add("error");
        return;
    }

    const turnstileToken = form.querySelector('[name="cf-turnstile-response"]')?.value;
    if (!turnstileToken) {
        message.textContent = "Please complete the security check before creating your account.";
        message.classList.add("error");
        return;
    }
    let accountCreated = false;
    const submitButton = form.querySelector("button[type='submit']");
    const submitLabel = submitButton.textContent;
    submitButton.disabled = true;
    submitButton.textContent = "Creating account…";
    form.setAttribute("aria-busy", "true");
    message.classList.add("registration-pending");
    message.textContent = "Please wait — creating your account. This may take a few moments.";

    try {
        const response = await fetch("/api/auth/register", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ email, password, turnstileToken })
        });

        const result = await response.json().catch(() => ({}));
        if (!response.ok)
            throw new Error(result.message || "Unable to create the account.");

        accountCreated = true;
        form.querySelector(".cf-turnstile").hidden = true;
        window.turnstile?.remove();
        form.reset();
        message.classList.remove("registration-pending");
        message.textContent = "Account created. Check your email to verify it.";
        message.classList.add("success");
    } catch (error) {
        window.turnstile?.reset();
        message.classList.remove("registration-pending");
        message.textContent = error.message;
        message.classList.add("error");
    } finally {
        form.setAttribute("aria-busy", "false");
        message.classList.remove("registration-pending");
        submitButton.disabled = accountCreated;
        submitButton.textContent = accountCreated ? "Account created" : submitLabel;
    }
});
