const form = document.getElementById("registerForm");
const message = document.getElementById("formMessage");

form.addEventListener("submit", async event => {
    event.preventDefault();
    message.className = "form-message";

    const email = form.email.value.trim();
    const password = form.password.value;

    if (password !== form.confirmPassword.value) {
        message.textContent = "The passwords do not match.";
        message.classList.add("error");
        return;
    }

    const submitButton = form.querySelector("button[type='submit']");
    submitButton.disabled = true;
    message.textContent = "Creating your account…";

    try {
        const response = await fetch("/api/auth/register", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ email, password })
        });

        const result = await response.json().catch(() => ({}));
        if (!response.ok)
            throw new Error(result.message || "Unable to create the account.");

        form.reset();
        message.textContent = "Account created. Check your email to verify it.";
        message.classList.add("success");
    } catch (error) {
        message.textContent = error.message;
        message.classList.add("error");
    } finally {
        submitButton.disabled = false;
    }
});
