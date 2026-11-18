// Shows the deployed application version in the footer, e.g. "NEET BEE | Version: 2026-11-18-001 | DEV".
// The version is read from /version.json, which the GitHub Actions workflow writes into wwwroot at deploy time.
// Nothing is hardcoded here. If the file is missing (local development) or invalid, the footer line stays hidden
// and the page carries on normally; details go to the browser console only.
(function () {
    "use strict";

    var VERSION_URL = "/version.json";
    var TIMEOUT_MS = 5000;
    var pending = null;   // one request per page, shared by every caller

    // Returns a trimmed string, or "" for anything that is not a usable short string.
    function clean(value, maxLength) {
        if (typeof value !== "string") return "";
        var text = value.trim();
        return text.length > 0 && text.length <= maxLength ? text : "";
    }

    /**
     * Reads /version.json once per page and resolves with
     * { application, version, environment, branch, buildDate }, or null when unavailable.
     * Never rejects, so callers do not need their own error handling.
     */
    function loadAppVersion() {
        if (pending) return pending;

        var controller = typeof AbortController === "function" ? new AbortController() : null;
        var timer = controller ? setTimeout(function () { controller.abort(); }, TIMEOUT_MS) : null;

        pending = fetch(VERSION_URL, {
            cache: "no-store",                                   // always the file from this deployment
            headers: { "Accept": "application/json" },
            signal: controller ? controller.signal : undefined
        })
            .then(function (response) {
                if (!response.ok) throw new Error("HTTP " + response.status);
                return response.json();                          // throws on invalid JSON
            })
            .then(function (data) {
                if (!data || typeof data !== "object") throw new Error("not a JSON object");
                var info = {
                    application: clean(data.application, 60) || "NEET BEE",
                    version: clean(data.version, 64),
                    environment: clean(data.environment, 30),
                    branch: clean(data.branch, 100),
                    buildDate: clean(data.buildDate, 40)
                };
                if (!info.version) throw new Error("\"version\" is missing or empty");
                return info;
            })
            .catch(function (error) {
                var reason = error && error.name === "AbortError" ? "request timed out" : (error && error.message) || error;
                console.info("App version not shown (" + VERSION_URL + "): " + reason);
                return null;
            })
            .finally(function () {
                if (timer) clearTimeout(timer);
            });

        return pending;
    }

    // Short label and a CSS key for the environment badge.
    function environmentKey(environment) {
        var e = environment.toLowerCase();
        if (e === "prod" || e === "production" || e === "live") return "prod";
        if (e === "uat" || e === "staging" || e === "stage") return "uat";
        if (e === "qa" || e === "test") return "qa";
        if (e === "dev" || e === "development") return "dev";
        return "other";
    }

    function span(className, text) {
        var el = document.createElement("span");
        if (className) el.className = className;
        el.textContent = text;                                   // text only: values are never treated as HTML
        return el;
    }

    // Fills every [data-app-version] element (and #appVersion) on the page.
    function renderAppVersion(info) {
        var targets = document.querySelectorAll("#appVersion, [data-app-version]");
        if (!info || !targets.length) return;

        targets.forEach(function (target) {
            target.textContent = "";
            target.appendChild(span("nb-ver-app", info.application));
            target.appendChild(span("nb-ver-sep", "|"));
            var version = span("nb-ver-number", "Version: ");
            version.appendChild(span("nb-ver-value", info.version));
            target.appendChild(version);
            if (info.environment) {
                target.appendChild(span("nb-ver-sep", "|"));
                var env = span("nb-ver-env", info.environment);
                env.setAttribute("data-env", environmentKey(info.environment));
                target.appendChild(env);
            }

            // Extra build details as a tooltip, without cluttering the footer.
            var details = [];
            if (info.branch) details.push("Branch: " + info.branch);
            if (info.buildDate) {
                var built = new Date(info.buildDate);
                details.push("Built: " + (isNaN(built.getTime()) ? info.buildDate : built.toLocaleString()));
            }
            target.title = details.join("\n");
            target.setAttribute("aria-label", info.application + ", version " + info.version +
                (info.environment ? ", " + info.environment + " environment" : ""));
            target.hidden = false;
        });
    }

    // Reusable from other scripts: NeetBee.loadAppVersion().then(info => ...)
    window.NeetBee = window.NeetBee || {};
    window.NeetBee.loadAppVersion = loadAppVersion;
    window.NeetBee.renderAppVersion = renderAppVersion;

    function start() { loadAppVersion().then(renderAppVersion); }
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start);
    else start();
})();
