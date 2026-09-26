window.poultryFarmUi = {
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

    async loadOpenMojiEmojis() {
        const fallback = [
            "😀", "😃", "😄", "😁", "😆", "😅", "😂", "🤣",
            "😊", "😇", "🙂", "🙃", "😉", "😍", "😘", "😎",
            "🤔", "😮", "😢", "😭", "😡", "🙏", "👏", "👍",
            "👎", "👌", "💪", "✅", "⚠️", "🔥", "⭐", "💡",
            "❤️", "💚", "💙", "🐔", "🐣", "🥚", "🌽", "💊",
            "💰", "📌", "📋", "📈", "🚚", "🧹", "🩺", "🕒"
        ];

        try {
            if (this._emojiList) {
                return this._emojiList;
            }

            const response = await fetch("/emoji-list.json", { cache: "force-cache" });
            if (!response.ok) {
                return fallback;
            }

            const emojis = await response.json();
            if (!Array.isArray(emojis) || emojis.length < 3000) {
                return fallback;
            }

            this._emojiList = emojis;
            return emojis;
        } catch {
            return fallback;
        }
    },

    async openAuthenticatedPdf(url, token) {
        const objectUrl = await this.loadAuthenticatedPdfObjectUrl(url, token);
        const opened = window.open(objectUrl, "_blank", "noopener");
        if (!opened) {
            this.downloadObjectUrl(objectUrl, "receipt.pdf");
        }

        setTimeout(() => URL.revokeObjectURL(objectUrl), 60000);
    },

    async loadAuthenticatedPdfObjectUrl(url, token) {
        const response = await fetch(url, {
            headers: {
                Authorization: `Bearer ${token}`
            }
        });

        if (!response.ok) {
            throw new Error(`PDF request failed with ${response.status}`);
        }

        const blob = await response.blob();
        return URL.createObjectURL(blob);
    },

    downloadObjectUrl(objectUrl, fileName) {
        const link = document.createElement("a");
        link.href = objectUrl;
        link.download = fileName || "receipt.pdf";
        link.click();
    },

    downloadBytes(base64, fileName, contentType) {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }

        const blob = new Blob([bytes], { type: contentType || "application/octet-stream" });
        const objectUrl = URL.createObjectURL(blob);
        this.downloadObjectUrl(objectUrl, fileName || "download.bin");
        setTimeout(() => URL.revokeObjectURL(objectUrl), 30000);
    },

    revokeObjectUrl(objectUrl) {
        if (objectUrl) {
            URL.revokeObjectURL(objectUrl);
        }
    }
};
