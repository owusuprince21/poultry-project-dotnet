window.marketplaceUi = {
    scrollToBottom(element) {
        if (!element) {
            return;
        }

        requestAnimationFrame(() => {
            element.scrollTop = element.scrollHeight;
            setTimeout(() => {
                element.scrollTop = element.scrollHeight;
            }, 40);
        });
    },

    scrollBy(element, delta) {
        if (!element) {
            return;
        }

        element.scrollBy({ left: delta, behavior: "smooth" });
    },

    showToast(message, tone = "info", durationMs = 4200) {
        const text = (message || "").toString().trim();
        if (!text) {
            return;
        }

        let host = document.getElementById("marketplace-toast-host");
        if (!host) {
            host = document.createElement("div");
            host.id = "marketplace-toast-host";
            host.className = "toast-host";
            host.setAttribute("aria-live", "polite");
            document.body.appendChild(host);
        }

        const toast = document.createElement("div");
        toast.className = `app-toast is-${tone === "error" || tone === "success" || tone === "warning" ? tone : "info"}`;
        toast.textContent = text;
        host.appendChild(toast);

        requestAnimationFrame(() => toast.classList.add("is-visible"));

        window.setTimeout(() => {
            toast.classList.remove("is-visible");
            window.setTimeout(() => toast.remove(), 220);
        }, Math.max(1800, durationMs));
    }
};
