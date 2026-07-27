const token = sessionStorage.getItem("nx_access_token");
if (!token)
    location.replace("/login.html");

const amountButtons = [...document.querySelectorAll(".amount-button")];
const checkoutButton = document.getElementById("checkoutButton");
const summary = document.getElementById("topupSummary");
const message = document.getElementById("topupMessage");
let selectedAmount = 5;

if (new URLSearchParams(location.search).has("cancelled"))
    message.textContent = "Checkout was cancelled. No payment was taken.";

for (const button of amountButtons) {
    button.addEventListener("click", () => {
        selectedAmount = Number(button.dataset.amount);
        amountButtons.forEach(item =>
            item.classList.toggle("selected", item === button));
        summary.textContent =
            `£${selectedAmount} will be added to your AI credit.`;
        message.textContent = "";
    });
}

checkoutButton.addEventListener("click", async () => {
    if (checkoutButton.disabled)
        return;

    checkoutButton.disabled = true;
    checkoutButton.textContent = "Opening Stripe…";
    message.textContent = "";

    try {
        const response = await fetch("/api/payments/create-checkout", {
            method: "POST",
            headers: {
                "Authorization": `Bearer ${token}`,
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ amountGbp: selectedAmount })
        });

        if (response.status === 401) {
            sessionStorage.removeItem("nx_access_token");
            location.replace("/login.html");
            return;
        }

        const result = await response.json();
        if (!response.ok || !result.url)
            throw new Error(result.message || result.detail ||
                "Unable to start checkout.");

        location.assign(result.url);
    } catch (error) {
        message.textContent = error.message;
        message.classList.add("error");
        checkoutButton.disabled = false;
        checkoutButton.textContent = "Continue to secure checkout";
    }
});
