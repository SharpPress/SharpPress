(() => {
    const root = document.documentElement;
    const body = document.body;
    const menuButton = document.querySelector("[data-menu-toggle]");

    const setMenu = open => {
        if (!menuButton) return;
        body.classList.toggle("menu-open", open);
        menuButton.setAttribute("aria-expanded", String(open));
    };

    const isDark = () => root.dataset.theme
        ? root.dataset.theme === "dark"
        : window.matchMedia("(prefers-color-scheme: dark)").matches;

    document.addEventListener("click", event => {
        if (event.target.closest("[data-menu-toggle]")) {
            setMenu(!body.classList.contains("menu-open"));
            return;
        }

        if (event.target.closest("[data-menu-close]")) {
            setMenu(false);
            return;
        }

        if (event.target.closest("[data-theme-toggle]")) {
            const theme = isDark() ? "light" : "dark";
            root.dataset.theme = theme;
            try {
                localStorage.setItem("theme", theme);
            } catch {
                return;
            }
        }
    });

    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && body.classList.contains("menu-open")) {
            setMenu(false);
            menuButton.focus();
        }
    });

    document.addEventListener("change", event => {
        const toggle = event.target.closest("[data-show-password]");
        if (!toggle) return;

        toggle.dataset.showPassword.split(" ").forEach(id => {
            const input = document.getElementById(id);
            if (input) input.type = toggle.checked ? "text" : "password";
        });
    });

    const dialog = document.getElementById("confirm-dialog");
    if (!dialog) return;

    window.confirmAction = message => new Promise(resolve => {
        dialog.querySelector("[data-confirm-message]").textContent = message;
        dialog.returnValue = "";
        dialog.addEventListener("close", () => resolve(dialog.returnValue === "confirm"), { once: true });
        dialog.showModal();
    });

    document.addEventListener("submit", async event => {
        const form = event.target;
        const submitter = event.submitter;
        const message = submitter?.dataset.confirm ?? form.dataset.confirm;
        if (!message || form.dataset.confirmed === "true") return;

        event.preventDefault();
        if (!await window.confirmAction(message)) return;

        form.dataset.confirmed = "true";
        form.requestSubmit(submitter);
        delete form.dataset.confirmed;
    });
})();
