const token = sessionStorage.getItem("nx_access_token");
const status = document.getElementById("paymentStatus");
const balance = document.getElementById("paymentBalance");
const checkoutSessionId =
    new URLSearchParams(location.search).get("session_id");

if (!token) {
    status.textContent =
        "Payment was returned. Sign in to view your updated credit.";
} else {
    waitForBalance();
}

async function waitForBalance() {
    for (let attempt = 0; attempt < 10; attempt++) {
        try {
            const headers = { "Authorization": `Bearer ${token}` };
            const [summaryResponse, transactionsResponse] = await Promise.all([
                fetch("/api/account/summary", { headers }),
                fetch("/api/account/transactions?take=20", { headers })
            ]);

            if (summaryResponse.ok) {
                const result = await summaryResponse.json();
                balance.textContent = `Available credit: ${
                    new Intl.NumberFormat("en-GB", {
                        style: "currency",
                        currency: "GBP",
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 4
                    }).format(result.balanceGbp)
                }`;
            }

            if (transactionsResponse.ok && checkoutSessionId) {
                const result = await transactionsResponse.json();
                const credited = (result.transactions || [])
                    .some(item => item.orderId === checkoutSessionId);
                if (credited) {
                    status.textContent =
                        "Your payment is confirmed and the credit has been added.";
                    return;
                }
            }
        } catch {
            // Stripe webhooks can arrive shortly after the browser returns.
        }

        if (attempt < 9)
            await new Promise(resolve => setTimeout(resolve, 1500));
    }

    status.textContent =
        "Payment completed, but confirmation is still pending. Your balance will update when the verified Stripe webhook is received.";
}
