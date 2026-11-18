// NEET BEE dashboard behaviour: theme, search, notifications, mobile menu and chat button.
// Plain JavaScript, no external dependencies.
(function () {
    "use strict";

    var THEME_KEY = "neetbee-theme";
    var body = document.body;

    // Shows a short status message using the shared toast element.
    function toast(message) {
        var el = document.querySelector(".toast");
        if (!el) return;
        el.textContent = message;
        el.classList.add("show");
        clearTimeout(toast.timer);
        toast.timer = setTimeout(function () { el.classList.remove("show"); }, 2400);
    }

    // Theme: dark is the default unless the visitor saved "light".
    function applyTheme(theme) {
        body.classList.toggle("dark", theme === "dark");
        // The light stylesheet is only active in light mode, so the dark theme is untouched.
        var lightCss = document.getElementById("nb-light-css");
        if (lightCss) lightCss.disabled = theme === "dark";
    }

    function currentTheme() {
        return body.classList.contains("dark") ? "dark" : "light";
    }

    var savedTheme = null;
    try { savedTheme = localStorage.getItem(THEME_KEY); } catch (e) { /* storage blocked */ }
    applyTheme(savedTheme === "light" ? "light" : "dark");

    var themeBtn = document.querySelector(".theme-toggle");
    if (themeBtn) {
        themeBtn.addEventListener("click", function () {
            var next = currentTheme() === "dark" ? "light" : "dark";
            applyTheme(next);
            try { localStorage.setItem(THEME_KEY, next); } catch (e) { /* storage blocked */ }
            toast(next === "dark" ? "Dark mode on" : "Light mode on");
        });
    }

    // Search: Enter opens the most relevant Study page; Escape clears the box.
    var searchInput = document.getElementById("site-search");
    var routes = [
        { match: /audio|podcast|listen/i, url: "/Study/AudioLearning" },
        { match: /previous|year|paper/i, url: "/Study/PreviousYearPapers" },
        { match: /mock|test/i, url: "/Study/MockTests" },
        { match: /price|plan|pricing|subscribe/i, url: "/Study/Pricing" },
        { match: /course|topic|lesson|chapter/i, url: "/Study/Courses" }
    ];
    if (searchInput) {
        searchInput.addEventListener("keydown", function (event) {
            if (event.key === "Escape") {
                searchInput.value = "";
                return;
            }
            if (event.key !== "Enter") return;
            event.preventDefault();
            var query = searchInput.value.trim();
            if (!query) return;
            var target = "/Study/McqPractice";
            for (var i = 0; i < routes.length; i++) {
                if (routes[i].match.test(query)) {
                    target = routes[i].url;
                    break;
                }
            }
            window.location.href = target;
        });
    }

    // Notifications popover.
    var notifyBtn = document.getElementById("nb-notify-btn");
    var notifyPanel = document.getElementById("nb-notify-panel");
    function setNotify(open) {
        if (!notifyBtn || !notifyPanel) return;
        notifyPanel.hidden = !open;
        notifyBtn.setAttribute("aria-expanded", open ? "true" : "false");
    }
    if (notifyBtn && notifyPanel) {
        notifyBtn.addEventListener("click", function (event) {
            event.stopPropagation();
            setNotify(notifyPanel.hidden);
        });
        document.addEventListener("click", function (event) {
            if (!notifyPanel.contains(event.target)) setNotify(false);
        });
    }

    // Mobile navigation drawer, with a backdrop and a close button.
    var menuBtn = document.querySelector("[data-nb-menu]");
    var sidebar = document.getElementById("nb-sidebar");
    var backdrop = document.getElementById("nb-backdrop");
    function setMenu(open) {
        if (!sidebar) return;
        sidebar.classList.toggle("open", open);
        if (menuBtn) menuBtn.setAttribute("aria-expanded", open ? "true" : "false");
        if (backdrop) backdrop.hidden = !open;
        body.classList.toggle("nb-menu-open", open);
    }
    if (menuBtn && sidebar) {
        menuBtn.addEventListener("click", function (event) {
            event.stopPropagation();
            setMenu(!sidebar.classList.contains("open"));
        });
    }
    document.querySelectorAll("[data-nb-close]").forEach(function (el) {
        el.addEventListener("click", function () { setMenu(false); });
    });
    if (backdrop) {
        backdrop.addEventListener("click", function () { setMenu(false); });
    }

    // Mobile header: the kebab button shows or hides the search box and account actions.
    var actionsBtn = document.querySelector("[data-nb-actions]");
    var topbar = actionsBtn ? actionsBtn.closest(".nb-topbar") : null;
    function setActions(open) {
        if (!topbar) return;
        topbar.classList.toggle("nb-actions-open", open);
        actionsBtn.setAttribute("aria-expanded", open ? "true" : "false");
        if (!open) setNotify(false);
    }
    if (actionsBtn && topbar) {
        actionsBtn.addEventListener("click", function (event) {
            event.stopPropagation();
            setActions(!topbar.classList.contains("nb-actions-open"));
        });
        // A tap outside the header closes the options.
        document.addEventListener("click", function (event) {
            if (!topbar.contains(event.target)) setActions(false);
        });
    }

    // Escape closes any open popover, the header options or the mobile menu.
    document.addEventListener("keydown", function (event) {
        if (event.key !== "Escape") return;
        setNotify(false);
        setActions(false);
        setMenu(false);
    });

})();
