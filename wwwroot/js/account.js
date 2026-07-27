const token = sessionStorage.getItem("nx_access_token");
const message = document.getElementById("accountMessage");
const data = document.getElementById("accountData");

if (!token) {
    location.replace("/login.html");
} else {
    loadAccount();
}

async function loadAccount() {
    try {
        const response = await fetch("/api/auth/me", {
            headers: { "Authorization": `Bearer ${token}` }
        });

        if (response.status === 401)
            throw new Error("Your session has expired.");

        const result = await response.json();
        if (!response.ok)
            throw new Error(result.message || "Unable to load your account.");

        document.getElementById("accountEmail").textContent = result.email;
        document.getElementById("subscriberId").textContent = result.subscriberId;

        const [summaryResponse, transactionsResponse] = await Promise.all([
            fetch("/api/account/summary", {
                headers: { "Authorization": `Bearer ${token}` }
            }),
            fetch("/api/account/transactions?take=10", {
                headers: { "Authorization": `Bearer ${token}` }
            })
        ]);

        if (!summaryResponse.ok || !transactionsResponse.ok)
            throw new Error("Unable to load account credits.");

        const summary = await summaryResponse.json();
        const transactionResult = await transactionsResponse.json();

        document.getElementById("creditBalance").textContent =
            new Intl.NumberFormat("en-GB", {
                style: "currency",
                currency: "GBP"
            }).format(summary.balanceGbp);

        const entitlement = summary.entitlements
            .sort((a, b) => new Date(b.expiresUtc || 0) - new Date(a.expiresUtc || 0))[0];

        if (entitlement) {
            document.getElementById("entitlementStatus").textContent = entitlement.type;
            document.getElementById("entitlementExpiry").textContent = entitlement.expiresUtc
                ? `Active until ${new Date(entitlement.expiresUtc).toLocaleString()}`
                : "";
        }

        const transactionsBody = document.getElementById("transactionsBody");
        for (const transaction of transactionResult.transactions) {
            const row = document.createElement("tr");
            row.innerHTML = `
                <td>${new Date(transaction.createdUtc).toLocaleDateString()}</td>
                <td>${escapeHtml(transaction.creditType)}</td>
                <td>${new Intl.NumberFormat("en-GB", {
                    style: "currency",
                    currency: "GBP"
                }).format(transaction.amountPaid)}</td>
            `;
            transactionsBody.appendChild(row);
        }

        document.getElementById("noTransactions").hidden =
            transactionResult.transactions.length !== 0;

        message.textContent = "";
        data.hidden = false;
        document.getElementById("creditData").hidden = false;
        document.getElementById("transactionsSection").hidden = false;
    } catch (error) {
        sessionStorage.removeItem("nx_access_token");
        message.textContent = error.message;
        message.classList.add("error");
        setTimeout(() => location.replace("/login.html"), 1200);
    }
}

document.getElementById("logoutButton").addEventListener("click", () => {
    sessionStorage.removeItem("nx_access_token");
    location.replace("/login.html");
});

function escapeHtml(value) {
    const element = document.createElement("span");
    element.textContent = value ?? "";
    return element.innerHTML;
}
