// Shared "Please wait..." indicator (markup: Views/Shared/_LoadingIndicator.cshtml, styles: css/app.css).
//
//   showLoading("Signing in");   hideLoading();
//
// It also shows by itself when:
//   - a form is submitted (and no other script cancelled the submit). The pressed button is disabled
//     so the form cannot be sent twice.
//   - a link or a type="button" element with data-loading is clicked (e.g. "Continue with Google").
// Message: data-loading="..." on the pressed button, link or the form; otherwise "Please wait".
// Opt out: data-no-loading on the form or the button.
(function () {
    "use strict";

    var DEFAULT_MESSAGE = "Please wait";
    var disabled = [];        // buttons disabled by a submit, re-enabled by hideLoading()

    function overlay() { return document.getElementById("loadingIndicator"); }

    function showLoading(message) {
        var el = overlay();
        if (!el) return;
        var text = document.getElementById("loadingMessage");
        if (text) text.textContent = (typeof message === "string" && message.trim()) ? message.trim() : DEFAULT_MESSAGE;
        el.hidden = false;
        document.documentElement.setAttribute("aria-busy", "true");
    }

    function hideLoading() {
        var el = overlay();
        if (el) el.hidden = true;
        document.documentElement.removeAttribute("aria-busy");
        disabled.forEach(function (btn) { btn.disabled = false; });
        disabled = [];
        document.querySelectorAll("form[data-loading-busy]").forEach(function (f) { f.removeAttribute("data-loading-busy"); });
    }

    // Runs fn (a function returning a promise, or a promise) with the indicator shown.
    function withLoading(work, message) {
        showLoading(message);
        var p;
        try { p = Promise.resolve(typeof work === "function" ? work() : work); }
        catch (e) { p = Promise.reject(e); }
        return p.finally(hideLoading);
    }

    function optedOut(el) { return !!(el && el.hasAttribute("data-no-loading")); }

    function messageFor(el, fallbackEl) {
        return (el && el.getAttribute("data-loading")) || (fallbackEl && fallbackEl.getAttribute("data-loading")) || DEFAULT_MESSAGE;
    }

    // Client-side validation: the browser already blocks invalid forms unless they have novalidate.
    // When jQuery Validation is on the page, ask it as well.
    function passesValidation(form) {
        var jq = window.jQuery;
        if (jq && jq.fn && typeof jq.fn.valid === "function" && jq(form).data("validator")) {
            return jq(form).valid();
        }
        return true;
    }

    // Bubble phase on document: a form's own submit handler runs first, so a script that handles the form
    // itself (event.preventDefault(), e.g. the AI assistant chat) is left alone.
    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement)) return;

        if (form.hasAttribute("data-loading-busy")) {     // already sending: stop the double submit
            event.preventDefault();
            return;
        }
        if (event.defaultPrevented) return;

        var submitter = event.submitter || null;
        if (optedOut(form) || optedOut(submitter)) return;
        if ((submitter && submitter.getAttribute("formtarget") || form.target || "").toLowerCase() === "_blank") return;
        if (!passesValidation(form)) return;

        form.setAttribute("data-loading-busy", "");
        showLoading(messageFor(submitter, form));

        // Disable after the browser has read the form, so the pressed button's name/value is still sent.
        if (submitter && "disabled" in submitter) {
            setTimeout(function () {
                if (form.hasAttribute("data-loading-busy")) { submitter.disabled = true; disabled.push(submitter); }
            }, 0);
        }
    });

    // Opt-in for links and plain buttons that start work, e.g. <a data-loading="Redirecting to Google">.
    document.addEventListener("click", function (event) {
        if (event.defaultPrevented || event.button !== 0) return;
        var el = event.target.closest && event.target.closest("a[data-loading], button[data-loading][type=button]");
        if (!el || optedOut(el)) return;
        if (el.tagName === "A") {
            if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;   // new tab/window
            if ((el.getAttribute("target") || "").toLowerCase() === "_blank" || el.hasAttribute("download")) return;
            var href = el.getAttribute("href");
            if (!href || href.charAt(0) === "#") return;
        }
        showLoading(messageFor(el));
    });

    // Back/forward cache restores the page exactly as it was left, overlay included: clear it.
    window.addEventListener("pageshow", hideLoading);

    window.showLoading = showLoading;
    window.hideLoading = hideLoading;
    window.withLoading = withLoading;
})();
