// Voice Q&A page: reads questions, options and answers aloud.
// Uses Azure AI Speech through POST /Study/Speak when the server has a key; otherwise the browser's own
// speech synthesis. Only one speech session runs at a time: starting a new one stops the previous one.
// Play continues across pages (the next page is loaded in place) and saves the last question heard,
// so the next visit continues from the following question.
(() => {
    "use strict";

    const root = document.getElementById("vqa");
    if (!root) return;

    const $ = (id) => document.getElementById(id);
    const els = {
        voice: $("vqa-voice"),
        rate: $("vqa-rate"),
        mode: $("vqa-mode"),
        repeat: $("vqa-repeat"),
        play: $("vqa-play"),
        playTitle: $("vqa-play-title"),
        status: $("vqa-status"),
        pause: $("vqa-pause"),
        resume: $("vqa-resume"),
        stop: $("vqa-stop"),
        note: $("vqa-note"),
        engine: $("vqa-engine-label"),
        mini: $("vqa-mini"),
        miniPlay: $("vqa-mini-play"),
        miniTitle: $("vqa-mini-title"),
        miniStatus: $("vqa-mini-status"),
        miniPause: $("vqa-mini-pause"),
        miniResume: $("vqa-mini-resume"),
        miniStop: $("vqa-mini-stop"),
        prev: $("vqa-prev"),
        next: $("vqa-next"),
        miniPrev: $("vqa-mini-prev"),
        miniNext: $("vqa-mini-next")
    };

    const synth = "speechSynthesis" in window ? window.speechSynthesis : null;
    const speakUrl = root.dataset.speakUrl;
    const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    const antiForgery = tokenInput ? tokenInput.value : "";
    const filter = {
        subject: root.dataset.subject || null,
        year: root.dataset.year ? Number(root.dataset.year) : null
    };
    const totalQuestions = Number(root.dataset.total) || 0;

    let useAzure = root.dataset.azure === "true";
    let session = 0;          // bumped on every start/stop; stale callbacks compare against it
    let state = "idle";       // idle | playing | paused
    let queue = [];
    let index = 0;
    let continuous = false;   // true for Play: moves on to the next page and saves progress
    let audio = null;
    let finishCurrent = null; // resolves the segment being spoken (used by Stop)
    let lastHeard = root.dataset.lastHeard ? Number(root.dataset.lastHeard) : null;
    let lastSpoken = null;    // number of the question most recently highlighted
    let pass = 1;             // which reading of the current question (1 .. repeat count)
    const audioCache = new Map();

    // ---------- Helpers ----------
    const list = () => $("vqa-list");
    const cards = () => Array.from(root.querySelectorAll(".vqa-card"));
    const cardFor = (n) => root.querySelector(`.vqa-card[data-q="${n}"]`);
    const currentPage = () => Number(list()?.dataset.page) || 1;
    const totalPages = () => Number(list()?.dataset.totalPages) || 1;

    // What Play reads for each question, from the "Read" setting in the kebab menu.
    function segmentsFor(card, kind) {
        const q = card.querySelector(".vqa-q");
        const options = [...card.querySelectorAll(".vqa-option")];
        const answer = card.querySelector(".vqa-answer");
        const mode = els.mode ? els.mode.value : "full";
        if (kind === "answer") return [answer];
        if (kind === "question") return mode === "full" ? [q, ...options] : [q];
        if (mode === "answer") return [answer];
        if (mode === "qa") return [q, answer];
        return [q, ...options, answer];
    }

    // How many times each question (as set by the "Read" option) is read before moving on.
    function repeatCount() {
        const n = Number(els.repeat ? els.repeat.value : 1);
        return Number.isInteger(n) && n >= 1 && n <= 5 ? n : 1;
    }

    function showNote(text) {
        els.note.textContent = text;
        els.note.hidden = !text;
    }

    function setStatus(text) {
        els.status.textContent = text;
        if (els.miniStatus) els.miniStatus.textContent = text;
    }

    function setState(next) {
        state = next;
        root.classList.toggle("is-playing", next === "playing");
        root.classList.toggle("is-paused", next === "paused");
        const title = next === "playing" ? "Pause" : next === "paused" ? "Resume" : "Play";
        els.pause.disabled = next !== "playing";
        els.resume.disabled = next !== "paused";
        els.stop.disabled = next === "idle";
        els.playTitle.textContent = title;
        if (els.mini) {
            els.miniPause.disabled = els.pause.disabled;
            els.miniResume.disabled = els.resume.disabled;
            els.miniStop.disabled = els.stop.disabled;
            els.miniTitle.textContent = title;
            els.miniPlay.setAttribute("aria-label", title);
        }
        if (next === "idle") setStatus(idleText());
        updateSkipButtons();
    }

    function idleText() {
        const next = nextUnheard();
        if (next && next > 1) return `Press to continue from question ${next}`;
        return els.status.dataset.idle || "Press to play Question / Answer";
    }

    function clearHighlight() {
        root.querySelectorAll(".is-speaking").forEach((el) => el.classList.remove("is-speaking"));
        root.querySelectorAll(".vqa-card.is-active").forEach((el) => el.classList.remove("is-active"));
    }

    function highlight(seg) {
        clearHighlight();
        const card = seg.closest(".vqa-card");
        setOpen(card, true);
        card.classList.add("is-active");
        seg.classList.add("is-speaking");
        seg.scrollIntoView({ block: "nearest", behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth" });
        const n = card.dataset.q;
        lastSpoken = Number(n);
        updateSkipButtons();
        const part = seg.classList.contains("vqa-answer") ? "answer" : seg.classList.contains("vqa-option") ? "options" : "question";
        const times = repeatCount();
        setStatus(`Speaking question ${n} of ${totalQuestions} · ${part}${times > 1 ? ` · ${pass} of ${times}` : ""}`);
    }

    function setOpen(card, open) {
        const toggle = card.querySelector(".vqa-toggle");
        const body = card.querySelector(".vqa-card-body");
        card.classList.toggle("is-open", open);
        toggle.setAttribute("aria-expanded", open ? "true" : "false");
        body.hidden = !open;
    }

    // ---------- Azure speech ----------
    function fetchAudio(text, voice, rate) {
        const key = `${voice}|${rate}|${text}`;
        if (!audioCache.has(key)) {
            const p = fetch(speakUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json", "RequestVerificationToken": antiForgery },
                body: JSON.stringify({ text, voice, rate: Number(rate) })
            }).then(async (res) => {
                if (res.status === 503) throw Object.assign(new Error("Azure speech is not configured"), { fallback: true });
                if (!res.ok) throw Object.assign(new Error(`Speech request failed (${res.status})`), { fallback: true });
                return URL.createObjectURL(await res.blob());
            });
            p.catch(() => audioCache.delete(key));
            audioCache.set(key, p);
        }
        return audioCache.get(key);
    }

    async function speakAzure(seg, id) {
        const voice = els.voice.value;
        const rate = els.rate.value;
        const url = await fetchAudio(seg.dataset.say, voice, rate);
        if (id !== session) return;

        // Fetch the next piece while this one plays, so there is no gap.
        const nextSeg = queue[index + 1];
        if (nextSeg) fetchAudio(nextSeg.dataset.say, voice, rate).catch(() => { });

        await new Promise((resolve, reject) => {
            audio = sharedAudio;
            audio.src = url;
            finishCurrent = resolve;
            audio.onended = resolve;
            audio.onerror = () => reject(Object.assign(new Error("Audio playback failed"), { fallback: true }));
            // If Pause was pressed while the audio was downloading, wait for Resume.
            if (state !== "paused") audio.play().catch(reject);
        });
        audio = null;
    }

    // ---------- Browser speech ----------
    // Device voices differ by browser: Edge has Neerja / Prabhat, Safari (Mac, iPhone) has Rishi / Veena,
    // Android Chrome has Google's Indian English voices, Chrome and Firefox on Windows usually only US/UK voices.
    const FEMALE = /neerja|aashi|kavya|ananya|aarti|heera|veena|lekha|isha|zira|hazel|susan|samantha|karen|moira|catherine|female|woman/i;
    const MALE = /prabhat|aarav|kunal|rehaan|arjun|ravi|rishi|david|mark|george|daniel|james|\bmale\b/i;
    function pickBrowserVoice() {
        if (!synth) return null;
        const voices = synth.getVoices();
        const chosen = els.voice.selectedOptions[0];
        const hint = (chosen?.dataset.hint || "").toLowerCase();
        const wantFemale = /female/i.test(chosen?.textContent || "");
        const isIndian = (v) => /en[-_]in/i.test(v.lang) || /india/i.test(v.name);
        const sameGender = (v) => (wantFemale ? FEMALE.test(v.name) && !/\bmale\b/i.test(v.name) : MALE.test(v.name));
        const indian = voices.filter(isIndian);
        const english = voices.filter((v) => /^en/i.test(v.lang));
        const british = english.filter((v) => /^en[-_]GB/i.test(v.lang));
        // Exact voice, then an Indian English voice (same gender first), then a UK / other English voice.
        return voices.find((v) => isIndian(v) && v.name.toLowerCase().includes(hint))
            || indian.find(sameGender)
            || indian[0]
            || british.find(sameGender)
            || english.find(sameGender)
            || british[0]
            || english[0]
            || null;
    }

    function describeBrowserVoice() {
        if (useAzure) return;
        if (!synth) {
            showNote("This browser cannot speak text. Try Microsoft Edge or Google Chrome.");
            return;
        }
        const voice = pickBrowserVoice();
        const indian = voice && (/en[-_]in/i.test(voice.lang) || /india/i.test(voice.name));
        if (!voice) {
            showNote("This browser has no English voice installed, so questions cannot be read aloud here.");
        } else if (indian) {
            showNote("");   // an Indian English voice from this device is in use
        } else {
            showNote(`This browser has no Indian English voice, so "${voice.name}" is used. ` +
                "For Indian voices, open this page in Microsoft Edge, Safari or Chrome on Android.");
        }
    }

    // Splits text into pieces of up to ~180 characters at sentence (or comma / space) boundaries.
    function chunkText(text) {
        const pieces = [];
        for (const sentence of text.match(/[^.!?]+[.!?]*\s*/g) || [text]) {
            let rest = sentence.trim();
            while (rest.length > 180) {
                let cut = rest.lastIndexOf(", ", 180);
                if (cut < 60) cut = rest.lastIndexOf(" ", 180);
                if (cut < 60) cut = 180;
                pieces.push(rest.slice(0, cut + 1).trim());
                rest = rest.slice(cut + 1).trim();
            }
            if (rest) pieces.push(rest);
        }
        return pieces.length ? pieces : [text];
    }

    function speakBrowser(seg, id) {
        return new Promise((resolve, reject) => {
            if (!synth) { reject(new Error("Speech is not supported in this browser")); return; }
            const voice = pickBrowserVoice();
            const pieces = chunkText(seg.dataset.say);
            let next = 0;
            finishCurrent = resolve;
            const speakNext = () => {
                if (id !== session) { resolve(); return; }
                if (next >= pieces.length) { resolve(); return; }
                const u = new SpeechSynthesisUtterance(pieces[next++]);
                if (voice) u.voice = voice;
                u.lang = voice ? voice.lang : "en-IN";
                u.rate = Number(els.rate.value) || 1;
                u.onend = speakNext;
                u.onerror = (e) => (e.error === "interrupted" || e.error === "canceled" ? resolve() : reject(new Error(e.error)));
                synth.speak(u);
            };
            speakNext();
        });
    }

    // Safari and iPhones only allow speech / audio that starts from a tap. Starting something silent
    // inside the first tap unlocks them for the rest of the visit.
    let unlocked = false;
    const sharedAudio = new Audio();
    function unlockPlayback() {
        if (unlocked) return;
        unlocked = true;
        try {
            sharedAudio.src = "data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=";
            const p = sharedAudio.play();
            if (p && p.catch) p.catch(() => { });
        } catch { /* nothing to unlock */ }
        if (synth) {
            try { const u = new SpeechSynthesisUtterance(""); u.volume = 0; synth.speak(u); } catch { /* ignore */ }
        }
    }
    document.addEventListener("pointerdown", unlockPlayback, { once: true, capture: true });
    document.addEventListener("keydown", unlockPlayback, { once: true, capture: true });

    // ---------- Session control ----------
    function haltOutput() {
        if (audio) { audio.pause(); audio.onended = audio.onerror = null; audio = null; }
        sharedAudio.onended = sharedAudio.onerror = null;
        if (synth) synth.cancel();
        if (finishCurrent) { const f = finishCurrent; finishCurrent = null; f(); }
    }

    function stop() {
        session++;
        haltOutput();
        queue = [];
        index = 0;
        pass = 1;
        continuous = false;
        clearHighlight();
        setState("idle");
    }

    function start(segments, options = {}) {
        stop();
        const id = session;
        queue = segments.filter(Boolean);
        if (!queue.length) return;
        continuous = Boolean(options.continuous);
        setState("playing");
        // A short delay lets Chrome finish cancelling before the next utterance starts.
        setTimeout(() => run(id), synth && !useAzure ? 60 : 0);
    }

    // Play from a card to the end of the list, page after page.
    function playFrom(card) {
        const all = cards();
        const from = Math.max(0, all.indexOf(card));
        start(all.slice(from).flatMap((c) => segmentsFor(c, "all")), { continuous: true });
    }

    async function run(id) {
        while (id === session) {
            while (id === session && index < queue.length) {
                const seg = queue[index];
                highlight(seg);
                try {
                    if (useAzure) await speakAzure(seg, id);
                    else await speakBrowser(seg, id);
                } catch (err) {
                    if (id !== session) return;
                    if (useAzure && err.fallback) {
                        useAzure = false;
                        audio = null;
                        els.engine.textContent = "Device voice";
                        describeBrowserVoice();
                        continue; // retry the same piece with browser speech
                    }
                    showNote(`Could not play audio: ${err.message}`);
                    stop();
                    return;
                }
                if (id !== session) return;

                // End of this question: read it again until the repeat count is reached,
                // and count it as heard after the last reading.
                const card = seg.closest(".vqa-card");
                const nextSeg = queue[index + 1];
                if (!nextSeg || nextSeg.closest(".vqa-card") !== card) {
                    if (pass < repeatCount()) {
                        pass++;
                        while (index > 0 && queue[index - 1].closest(".vqa-card") === card) index--;
                        continue;
                    }
                    pass = 1;
                    if (continuous) markHeard(card);
                }
                index++;
            }

            // End of this page: carry on with the next one.
            if (id !== session || !continuous || currentPage() >= totalPages()) break;
            setStatus(`Loading page ${currentPage() + 1}…`);
            try {
                await loadPage(currentPage() + 1);
            } catch {
                if (id === session) { showNote("The next page could not be loaded."); stop(); }
                return;
            }
            if (id !== session) return;
            queue = cards().flatMap((c) => segmentsFor(c, "all"));
            index = 0;
        }
        if (id === session) { clearHighlight(); setState("idle"); }
    }

    function pause() {
        if (state !== "playing") return;
        if (audio) audio.pause();
        else if (synth && !useAzure) synth.pause();
        setState("paused");
        setStatus("Paused");
    }

    function resume() {
        if (state !== "paused") return;
        if (audio) audio.play().catch(() => { });
        else if (synth && !useAzure) synth.resume();
        setState("playing");
        const seg = queue[index];
        if (seg) highlight(seg);
    }

    // ---------- Previous / Next question ----------
    // The reference question: the one being read, else the last one read, else where Play would start.
    function referenceNumber() {
        const speaking = queue[index] && state !== "idle" ? queue[index].closest(".vqa-card") : null;
        if (speaking) return Number(speaking.dataset.q);
        if (lastSpoken) return lastSpoken;
        const next = nextUnheard();
        if (next && cardFor(next)) return next;
        const first = cards()[0];
        return first ? Number(first.dataset.q) : null;
    }

    function updateSkipButtons() {
        const ref = referenceNumber();
        const atStart = !ref || ref <= 1;
        const atEnd = !ref || ref >= totalQuestions;
        for (const b of [els.prev, els.miniPrev]) if (b) b.disabled = atStart;
        for (const b of [els.next, els.miniNext]) if (b) b.disabled = atEnd;
    }

    // Plays question n (loading its page in place when it is on another page), then carries on from there.
    async function skipTo(n) {
        if (n < 1 || n > totalQuestions) return;
        let card = cardFor(n);
        if (!card) {
            const pageSize = Number(root.dataset.pageSize) || 10;
            stop();
            setStatus(`Loading question ${n}…`);
            try { await loadPage(Math.ceil(n / pageSize)); } catch { showNote("That page could not be loaded."); return; }
            card = cardFor(n);
        }
        if (card) playFrom(card);
    }

    function skip(delta) {
        const ref = referenceNumber();
        if (ref) skipTo(ref + delta);
    }

    for (const [b, d] of [[els.prev, -1], [els.miniPrev, -1], [els.next, 1], [els.miniNext, 1]]) {
        if (b) b.addEventListener("click", () => skip(d));
    }

    // ---------- Listening progress ----------
    function nextUnheard() {
        if (lastHeard == null) return null;
        return lastHeard < totalQuestions ? lastHeard + 1 : null;
    }

    function postJson(url, body) {
        return fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json", "RequestVerificationToken": antiForgery },
            body: JSON.stringify(body)
        });
    }

    function markHeard(card) {
        const number = Number(card.dataset.q);
        lastHeard = number;
        updateResumeBox();
        postJson(root.dataset.progressUrl, { questionId: Number(card.dataset.id), subject: filter.subject, year: filter.year })
            .catch(() => { /* progress is a convenience; playback carries on */ });
    }

    function updateResumeBox() {
        const box = $("vqa-resume-box");
        const text = $("vqa-resume-text");
        if (!box || !text) return;
        box.hidden = false;
        const next = nextUnheard();
        text.innerHTML = "";
        if (next) {
            text.append("You have listened up to question ", Object.assign(document.createElement("strong"), { textContent: String(lastHeard) }), ` of ${totalQuestions}.`);
        } else {
            text.append("You have listened to all ", Object.assign(document.createElement("strong"), { textContent: String(totalQuestions) }), " questions.");
        }
        const cont = $("vqa-continue");
        if (cont) {
            cont.hidden = !next;
            if (next) cont.textContent = `Continue from question ${next}`;
        }
    }

    // Opens the page holding question n (loading it in place if needed), then plays from it.
    async function continueFrom(n) {
        let card = cardFor(n);
        if (!card) {
            const pageSize = Number(root.dataset.pageSize) || 5;
            const page = Math.ceil(n / pageSize);
            stop();
            setStatus(`Loading question ${n}…`);
            try { await loadPage(page); } catch { showNote("That page could not be loaded."); return; }
            card = cardFor(n);
        }
        if (card) playFrom(card);
    }

    const continueBtn = $("vqa-continue");
    if (continueBtn) continueBtn.addEventListener("click", () => { const n = nextUnheard(); if (n) continueFrom(n); });

    const restartBtn = $("vqa-restart");
    if (restartBtn) {
        restartBtn.addEventListener("click", async () => {
            stop();
            await postJson(root.dataset.resetUrl, { subject: filter.subject, year: filter.year }).catch(() => { });
            lastHeard = null;
            const box = $("vqa-resume-box");
            if (box) box.hidden = true;
            await continueFrom(1);
        });
    }

    // ---------- Loading another page in place (keeps speech running) ----------
    async function loadPage(page) {
        const url = new URL(window.location.href);
        url.searchParams.set("page", String(page));
        const res = await fetch(url, { headers: { "X-Requested-With": "fetch" }, credentials: "same-origin" });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const doc = new DOMParser().parseFromString(await res.text(), "text/html");
        for (const id of ["vqa-list", "vqa-progress", "vqa-pager-top", "vqa-pager-wrap"]) {
            const fresh = doc.getElementById(id);
            const current = $(id);
            if (fresh && current) current.replaceWith(document.importNode(fresh, true));
        }
        history.replaceState(null, "", url);
        applyBookmarks();
        syncTopPager();
    }

    // ---------- Events ----------
    function togglePlay() {
        if (state === "playing") pause();
        else if (state === "paused") resume();
        else {
            // Continue where the user left off when that question is on this page; otherwise from the top.
            const next = nextUnheard();
            playFrom((next && cardFor(next)) || cards()[0]);
        }
    }
    els.play.addEventListener("click", togglePlay);
    els.pause.addEventListener("click", pause);
    els.resume.addEventListener("click", resume);
    els.stop.addEventListener("click", stop);

    root.addEventListener("click", (event) => {
        const speakBtn = event.target.closest("[data-speak]");
        if (speakBtn) {
            start(segmentsFor(cardFor(speakBtn.dataset.q), speakBtn.dataset.speak));
            return;
        }

        const bookmark = event.target.closest(".vqa-bookmark");
        if (bookmark) {
            toggleBookmark(bookmark);
            return;
        }

        const head = event.target.closest(".vqa-card-head");
        if (head) {
            const card = head.closest(".vqa-card");
            setOpen(card, !card.classList.contains("is-open"));
        }
    });

    // ---------- Settings: voice, speed and reading mode (remembered on this device) ----------
    const SETTINGS_KEY = "neetbee-vqa-settings";
    function loadSettings() {
        let saved = {};
        try { saved = JSON.parse(localStorage.getItem(SETTINGS_KEY) || "{}"); } catch { /* storage blocked */ }
        for (const [key, el] of [["voice", els.voice], ["rate", els.rate], ["mode", els.mode], ["repeat", els.repeat]]) {
            if (el && saved[key] && [...el.options].some((o) => o.value === saved[key])) el.value = saved[key];
        }
    }
    function saveSettings() {
        const value = { voice: els.voice.value, rate: els.rate.value, mode: els.mode ? els.mode.value : "full",
            repeat: els.repeat ? els.repeat.value : "1" };
        try { localStorage.setItem(SETTINGS_KEY, JSON.stringify(value)); } catch { /* storage blocked */ }
    }
    els.voice.addEventListener("change", () => { saveSettings(); describeBrowserVoice(); });
    els.rate.addEventListener("change", saveSettings);
    if (els.mode) els.mode.addEventListener("change", saveSettings);
    if (els.repeat) els.repeat.addEventListener("change", saveSettings);

    // ---------- Kebab menu ----------
    const menuBtn = $("vqa-menu-btn");
    const menuPanel = $("vqa-menu-panel");
    function setMenu(open, fromKeyboard) {
        if (!menuBtn || !menuPanel) return;
        menuPanel.hidden = !open;
        menuBtn.setAttribute("aria-expanded", open ? "true" : "false");
        // Move focus into the menu only for keyboard users; on phones it would pop the picker or zoom.
        if (open && fromKeyboard) els.voice.focus();
    }
    if (menuBtn && menuPanel) {
        menuBtn.addEventListener("click", (event) => {
            event.stopPropagation();
            setMenu(menuPanel.hidden, event.detail === 0);
        });
        document.addEventListener("click", (event) => {
            if (!menuPanel.hidden && !menuPanel.contains(event.target)) setMenu(false);
        });
    }

    document.addEventListener("keydown", (event) => {
        if (event.altKey || event.ctrlKey || event.metaKey) return;
        if (event.target.closest("input, select, textarea, [contenteditable]")) return;
        if (event.key === "[") { event.preventDefault(); skip(-1); }
        else if (event.key === "]") { event.preventDefault(); skip(1); }
    });

    // Escape closes the menu first; otherwise it stops speech.
    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape") return;
        if (menuPanel && !menuPanel.hidden) {
            setMenu(false);
            menuBtn.focus();
        } else if (state !== "idle") {
            stop();
        }
    });
    window.addEventListener("pagehide", stop);

    // ---------- Bookmarks (kept in this browser only) ----------
    const BOOKMARK_KEY = "neetbee-vqa-bookmarks";
    function readBookmarks() {
        try { return new Set(JSON.parse(localStorage.getItem(BOOKMARK_KEY) || "[]")); } catch { return new Set(); }
    }
    function toggleBookmark(btn) {
        const marks = readBookmarks();
        const n = btn.dataset.bookmark;
        const on = !marks.has(n);
        if (on) marks.add(n); else marks.delete(n);
        try { localStorage.setItem(BOOKMARK_KEY, JSON.stringify([...marks])); } catch { /* storage blocked */ }
        btn.setAttribute("aria-pressed", on ? "true" : "false");
    }
    function applyBookmarks() {
        const saved = readBookmarks();
        root.querySelectorAll(".vqa-bookmark").forEach((btn) => {
            btn.setAttribute("aria-pressed", saved.has(btn.dataset.bookmark) ? "true" : "false");
        });
    }

    // ---------- Subject / year filters ----------
    // Changing a filter reloads the page with the new question set; empty values are left out of the URL.
    const filterForm = $("vqa-filters");
    if (filterForm) {
        root.classList.add("js-filters");
        // Phones show a subject dropdown instead of the buttons; copy its choice to the matching button
        // before the form's change handler submits.
        const subjectSelect = $("vqa-subject");
        if (subjectSelect) {
            subjectSelect.addEventListener("change", () => {
                const radio = filterForm.querySelector(`input[name="subject"][value="${CSS.escape(subjectSelect.value)}"]`);
                if (radio) radio.checked = true;
            });
        }
        filterForm.addEventListener("change", () => {
            stop();
            if (typeof filterForm.requestSubmit === "function") filterForm.requestSubmit();
            else filterForm.submit();
        });
        filterForm.addEventListener("submit", () => {
            filterForm.querySelectorAll("select, input[type=radio]:checked").forEach((field) => {
                if (!field.value) field.disabled = true;
            });
        });
    }

    // ---------- Mini player: shown while the main Play controls are scrolled out of view ----------
    const controlsBlock = $("vqa-controls");
    if (els.mini && controlsBlock && cards().length) {
        els.miniPlay.addEventListener("click", togglePlay);
        els.miniPause.addEventListener("click", pause);
        els.miniResume.addEventListener("click", resume);
        els.miniStop.addEventListener("click", stop);

        const topbar = document.querySelector(".nb-topbar");
        // Line the bar up with the page column, just below the sticky site header.
        const place = () => {
            const box = root.getBoundingClientRect();
            const top = topbar ? Math.max(0, topbar.getBoundingClientRect().bottom) : 0;
            els.mini.style.setProperty("--vqa-mini-top", `${Math.round(top + 8)}px`);
            els.mini.style.setProperty("--vqa-mini-left", `${Math.round(box.left)}px`);
            els.mini.style.setProperty("--vqa-mini-width", `${Math.round(box.width)}px`);
        };
        // Show the bar whenever the main Play controls have scrolled up behind the sticky header.
        // Checked on every scroll and resize (two cheap position reads), so it reappears every time.
        const update = () => {
            const headerBottom = topbar ? Math.max(0, topbar.getBoundingClientRect().bottom) : 0;
            const hidden = controlsBlock.getBoundingClientRect().bottom > headerBottom;
            if (els.mini.hidden !== hidden) els.mini.hidden = hidden;
            if (!hidden) place();
        };
        window.addEventListener("scroll", update, { passive: true });
        window.addEventListener("resize", update);
        update();
    }

    // ---------- Rows per page ----------
    // Remembered in a cookie (read by the server); keeps the first question on screen in view.
    root.addEventListener("change", (event) => {
        const select = event.target.closest(".vqa-rows-select");
        if (!select) return;
        const size = Number(select.value);
        document.cookie = `neetbee-vqa-size=${size}; path=/; max-age=31536000; SameSite=Lax`;
        const first = Number(select.dataset.first) || 1;
        const url = new URL(window.location.href);
        url.searchParams.set("size", String(size));
        url.searchParams.set("page", String(Math.floor((first - 1) / size) + 1));
        stop();
        window.location.assign(url);
    });

    // ---------- Top pager: show / hide ----------
    const TOP_PAGER_KEY = "neetbee-vqa-top-pager";
    function syncTopPager() {
        const hidden = root.classList.contains("vqa-top-pager-collapsed");
        root.querySelectorAll("[data-pager-toggle]").forEach((b) => b.setAttribute("aria-expanded", hidden ? "false" : "true"));
    }
    root.addEventListener("click", (event) => {
        if (!event.target.closest("[data-pager-toggle]")) return;
        const hidden = !root.classList.contains("vqa-top-pager-collapsed");
        root.classList.toggle("vqa-top-pager-collapsed", hidden);
        try { localStorage.setItem(TOP_PAGER_KEY, hidden ? "hidden" : "shown"); } catch { /* storage blocked */ }
        syncTopPager();
    });
    try { if (localStorage.getItem(TOP_PAGER_KEY) === "hidden") root.classList.add("vqa-top-pager-collapsed"); } catch { /* storage blocked */ }
    syncTopPager();

    // ---------- Start-up ----------
    loadSettings();
    applyBookmarks();
    setState("idle");
    if (!cards().length) els.play.disabled = true;
    // Open the card the user will continue from.
    const resumeCard = nextUnheard() && cardFor(nextUnheard());
    if (resumeCard) {
        cards().forEach((c) => setOpen(c, c === resumeCard));
        resumeCard.classList.add("is-resume");
    }
    if (!useAzure) {
        if (synth && synth.getVoices().length === 0) {
            if (synth.addEventListener) synth.addEventListener("voiceschanged", describeBrowserVoice, { once: true });
            setTimeout(describeBrowserVoice, 1200);
        }
        describeBrowserVoice();
    }
})();
