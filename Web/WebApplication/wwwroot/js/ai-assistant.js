// NEET BEE floating AI Study Assistant: open/close, subject selection and a local message list.
// No AI backend is connected yet, so answers are placeholders and no API is called.
(function () {
    "use strict";

    var root = document.getElementById("nb-ai");
    if (!root) return;

    var fab = document.getElementById("nb-ai-fab");
    var panel = document.getElementById("nb-ai-panel");
    var messages = document.getElementById("nb-ai-messages");
    var form = document.getElementById("nb-ai-form");
    var input = document.getElementById("nb-ai-input");
    var subject = "Biology";

    // Opens or closes the chat panel and expands the button label.
    function setOpen(open) {
        fab.classList.toggle("open", open);
        fab.setAttribute("aria-expanded", open ? "true" : "false");
        panel.hidden = !open;
        if (open) input.focus();
    }

    // Adds a message using textContent, so user text is never rendered as HTML.
    function addMessage(text, who) {
        var el = document.createElement("div");
        el.className = "nb-ai-msg " + who;
        el.textContent = text;
        messages.appendChild(el);
        messages.scrollTop = messages.scrollHeight;
    }

    fab.addEventListener("click", function () {
        setOpen(panel.hidden);
    });

    root.querySelector("[data-nb-ai-close]").addEventListener("click", function () {
        setOpen(false);
    });

    // Subject selection updates the placeholder and the list.
    root.querySelectorAll(".nb-ai-subject").forEach(function (btn) {
        btn.addEventListener("click", function () {
            subject = btn.getAttribute("data-subject");
            root.querySelectorAll(".nb-ai-subject").forEach(function (b) {
                b.setAttribute("aria-pressed", b === btn ? "true" : "false");
            });
            input.placeholder = "Ask a " + subject + " question...";
            addMessage("Subject set to " + subject + ".", "bot");
        });
    });

    form.addEventListener("submit", function (event) {
        event.preventDefault();
        var text = input.value.trim();
        if (!text) return;
        addMessage(text, "user");
        addMessage("The AI answer service for " + subject + " is not connected yet. Your question was not sent.", "bot");
        input.value = "";
        input.focus();
    });

    document.getElementById("nb-ai-team-btn").addEventListener("click", function () {
        addMessage("Chat with NEET Bee Helpdesk is not connected yet. Your message was not sent.", "bot");
    });

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && !panel.hidden) setOpen(false);
    });
})();
