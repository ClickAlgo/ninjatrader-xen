const form = document.getElementById("loginForm");
const message = document.getElementById("formMessage");
const verified = new URLSearchParams(location.search).get("verified");

if (verified === "1") {
    const notice = document.getElementById("verificationNotice");
    notice.hidden = false;
    notice.className = "form-message success";
}

form.addEventListener("submit", async event => {
    event.preventDefault();
    message.className = "form-message";

    const submitButton = form.querySelector("button[type='submit']");
    submitButton.disabled = true;
    message.textContent = "Signing in…";

    try {
        const response = await fetch("/api/auth/login", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                email: form.email.value.trim(),
                password: form.password.value
            })
        });

        const result = await response.json().catch(() => ({}));
        if (!response.ok)
            throw new Error(result.message || "Unable to sign in.");

        sessionStorage.setItem("nx_access_token", result.token);
        location.href = "/account.html";
    } catch (error) {
        message.textContent = error.message;
        message.classList.add("error");
        submitButton.disabled = false;
    }
});
