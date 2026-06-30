(() => {
    function getInputs(target) {
        const group = target.closest(".two-factor-code");
        if (!group) {
            return [];
        }

        return Array.from(group.querySelectorAll("input"));
    }

    function focusInput(inputs, index) {
        const input = inputs[index];
        if (!input) {
            return;
        }

        input.focus();
        input.select();
    }

    document.addEventListener("input", event => {
        const target = event.target;
        if (!(target instanceof HTMLInputElement) || !target.closest(".two-factor-code")) {
            return;
        }

        const inputs = getInputs(target);
        const index = inputs.indexOf(target);
        const digits = target.value.replace(/\D/g, "");

        if (digits.length > 1) {
            digits.slice(0, inputs.length - index).split("").forEach((digit, offset) => {
                inputs[index + offset].value = digit;
                inputs[index + offset].dispatchEvent(new Event("change", { bubbles: true }));
            });

            focusInput(inputs, Math.min(index + digits.length, inputs.length - 1));
            return;
        }

        target.value = digits;

        if (digits.length === 1) {
            target.dispatchEvent(new Event("change", { bubbles: true }));
            focusInput(inputs, index + 1);
        }
    });

    document.addEventListener("keydown", event => {
        const target = event.target;
        if (!(target instanceof HTMLInputElement) || !target.closest(".two-factor-code")) {
            return;
        }

        const inputs = getInputs(target);
        const index = inputs.indexOf(target);

        if (event.key === "Backspace" && target.value.length === 0) {
            focusInput(inputs, index - 1);
        }

        if (event.key === "ArrowLeft") {
            event.preventDefault();
            focusInput(inputs, index - 1);
        }

        if (event.key === "ArrowRight") {
            event.preventDefault();
            focusInput(inputs, index + 1);
        }
    });

    document.addEventListener("paste", event => {
        const target = event.target;
        if (!(target instanceof HTMLInputElement) || !target.closest(".two-factor-code")) {
            return;
        }

        const pasted = event.clipboardData?.getData("text")?.replace(/\D/g, "");
        if (!pasted) {
            return;
        }

        event.preventDefault();

        const inputs = getInputs(target);
        pasted.slice(0, inputs.length).split("").forEach((digit, index) => {
            inputs[index].value = digit;
            inputs[index].dispatchEvent(new Event("change", { bubbles: true }));
        });

        focusInput(inputs, Math.min(pasted.length, inputs.length - 1));
    });
})();
