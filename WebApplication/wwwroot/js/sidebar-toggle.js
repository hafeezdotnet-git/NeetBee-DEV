// Hide / show the dashboard sidebar with the arrow button on its border (wide screens only).
// The choice is remembered on this device; the layout applies it before first paint (see _DashboardLayout).
$(function () {
    "use strict";

    var STORAGE_KEY = "neetbee-sidebar";
    var $body = $("body");
    var $toggle = $("#nb-sidebar-toggle");
    var $sidebar = $("#nb-sidebar");
    if (!$toggle.length || !$sidebar.length) return;

    function isCollapsed() {
        return $body.hasClass("nb-sidebar-collapsed");
    }

    // Button label and state follow the sidebar: "<" hides it, ">" shows it again.
    function syncButton() {
        var collapsed = isCollapsed();
        var label = collapsed ? "Show menu" : "Hide menu";
        $toggle.attr({ "aria-expanded": String(!collapsed), "aria-label": label, title: label });
        // Keep keyboard focus out of the hidden menu.
        $sidebar.attr("inert", collapsed ? "" : null);
        if (collapsed) $sidebar.attr("aria-hidden", "true"); else $sidebar.removeAttr("aria-hidden");
    }

    function setCollapsed(collapsed) {
        $body.toggleClass("nb-sidebar-collapsed", collapsed);
        syncButton();
        try { localStorage.setItem(STORAGE_KEY, collapsed ? "collapsed" : "open"); } catch (e) { /* storage blocked */ }
        // Pages that position things from the content width (e.g. the Voice Q&A player bar) listen for resize.
        window.setTimeout(function () { $(window).trigger("resize"); }, 280);
    }

    // Line the arrow up with the first menu item (Dashboard), on the sidebar's right border.
    // Measured once the layout is ready; the hidden sidebar keeps its height, so the spot does not move.
    function placeToggle() {
        var $first = $sidebar.find(".nb-nav-item").first();
        var $layout = $toggle.closest(".nb-layout");
        if (!$first.length || !$layout.length) return;
        var top = $first.offset().top - $layout.offset().top + ($first.outerHeight() - $toggle.outerHeight()) / 2;
        if (top > 0) $layout[0].style.setProperty("--nb-toggle-top", Math.round(top) + "px");
    }
    placeToggle();
    $(window).on("resize", placeToggle);

    $toggle.on("click", function () {
        setCollapsed(!isCollapsed());
    });

    // On phones the sidebar is a drawer opened with ☰, so a collapsed desktop state must not hide it there.
    var phone = window.matchMedia("(max-width: 760px)");
    function onViewportChange() {
        if (phone.matches) {
            $sidebar.removeAttr("inert aria-hidden");
        } else {
            syncButton();
        }
    }
    if (phone.addEventListener) phone.addEventListener("change", onViewportChange);
    else if (phone.addListener) phone.addListener(onViewportChange);

    // Turn animations on only after the saved state has been applied, so the page does not animate on load.
    syncButton();
    onViewportChange();
    window.setTimeout(function () { $body.addClass("nb-sidebar-ready"); }, 50);
});
