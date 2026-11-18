// Keeps the floating "Go Premium" button in the top-right corner, just below the sticky header,
// while the page scrolls. On Voice Q&A it moves below the floating player bar when that bar is showing.
$(function () {
    "use strict";

    var $fab = $("#nb-premium-fab");
    if (!$fab.length) return;
    var topbar = document.querySelector(".nb-topbar");

    function place() {
        var top = topbar ? Math.max(0, topbar.getBoundingClientRect().bottom) : 0;
        var player = document.getElementById("vqa-mini");
        if (player && !player.hidden) {
            var bar = player.getBoundingClientRect();
            if (bar.bottom > top) top = bar.bottom;
        }
        $fab[0].style.setProperty("--nb-fab-top", Math.round(top + 12) + "px");
    }

    // Run after other scroll handlers (such as the Voice Q&A player bar) have updated.
    var queued = false;
    function schedule() {
        if (queued) return;
        queued = true;
        window.setTimeout(function () { queued = false; place(); }, 0);
    }

    $(window).on("scroll resize", schedule);
    $(document).on("click", schedule);   // the phone header grows when its ⋮ menu opens
    place();
    $fab.addClass("is-ready");
    // Lets pages keep their own top-right controls clear of the button (see neetbee-voice-qa.css).
    $("body").addClass("has-premium-fab");
});
