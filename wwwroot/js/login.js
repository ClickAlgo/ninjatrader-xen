const form = document.getElementById("loginForm");
const message = document.getElementById("formMessage");
const verified = new URLSearchParams(location.search).get("verified");

function getDeviceFingerprint() {
    let fingerprint = localStorage.getItem("nx_device_fp");
    if (!fingerprint) {
        fingerprint = crypto.randomUUID().replaceAll("-", "");
        localStorage.setItem("nx_device_fp", fingerprint);
    }
    return fingerprint;
}

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
                password: form.password.value,
                deviceFingerprint: getDeviceFingerprint()
            })
        });

        const result = await response.json().catch(() => ({}));
        if (!response.ok)
            throw new Error(result.message || "Unable to sign in.");

        sessionStorage.setItem("nx_access_token", result.token);
        if (result.freeTrialGranted === true &&
            result.freeTrialExpiresUtc) {
            sessionStorage.setItem(
                "nx_trial_welcome",
                JSON.stringify({
                    expiresUtc: result.freeTrialExpiresUtc
                }));
        } else {
            sessionStorage.removeItem("nx_trial_welcome");
        }
        location.replace("/workspace.html");
    } catch (error) {
        message.textContent = error.message;
        message.classList.add("error");
        submitButton.disabled = false;
    }
});
