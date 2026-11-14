// Guided tour of the Teams panel (the "?" button, or a link ending in ?players&tour).
//
// It only points at things and explains them. It never taps a button, saves, or changes
// the roster: Next just moves to the next card. The highlighted control stays tappable,
// so the person can try the real thing if they want to.
//
// The panel re-renders its rows every time the server is polled, so nothing here holds on
// to an element: every frame the current step's selector is looked up again and the
// spotlight and card are moved to wherever it is now.
(function () {
    'use strict';

    const PANEL = '#players-panel';
    const EDIT_BTN = '#edit-roster-btn';
    const MARGIN = 12;  // card to screen edge
    const GAP = 12;     // card to spotlight
    const PAD = 6;      // spotlight around its target

    const escapeHtml = (s) => s.replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

    // Team names as the scoreboard shows them (BLACK / WHITE unless renamed)
    function teamNames() {
        const names = [...document.querySelectorAll('.scores .team-name')].map(el => escapeHtml(el.textContent.trim()));
        return [names[0] || 'BLACK', names[1] || 'WHITE'];
    }

    // target: CSS selector for what to spotlight (all matches are framed together); none = centered card
    function buildSteps() {
        const [black, white] = teamNames();
        return [
            {
                title: 'Setting up tonight’s teams',
                body: `This is where you pick who’s playing tonight and split them into <b>${black}</b> and <b>${white}</b>.<br><br>` +
                    'This quick tour only points things out. It won’t change anything.'
            },
            {
                target: '#all-out-btn',
                title: 'Start fresh each week',
                body: '<b>All out</b> marks everybody as not playing, so last week’s teams are cleared. ' +
                    'Nobody is deleted and no stats are lost.'
            },
            {
                target: '#players-list .roster-row:first-child:not(.editing-row)',
                title: 'Tap in whoever shows up',
                body: `Every player has a <b>${black} / OUT / ${white}</b> switch. Tap a team to put them on it, ` +
                    'or <b>OUT</b> if they’re not playing tonight.'
            },
            {
                target: '#team-counts',
                title: 'Keep the teams even',
                body: 'These show how many players are on each team and how many are out. ' +
                    'They get an orange outline when one team has 2 or more extra players.'
            },
            {
                target: '#balance-btn, #shuffle-btn',
                title: 'Or let the app pick teams',
                body: '<b>Balance</b> makes the fairest teams it can, based on past games. <b>Shuffle</b> splits everyone at random.<br><br>' +
                    'Want someone to stay put? Tap the 📌 next to their name first and they won’t be moved.'
            },
            {
                target: '#share-teams-btn',
                title: 'Send the teams to the group chat',
                body: '<b>Share</b> writes up tonight’s teams as a text message. On a phone it opens your share menu, ' +
                    'so you can pick Messages or your group chat. On a computer it copies the text for you to paste.'
            },
            {
                target: '.teams-panel .add-player',
                title: 'Someone new?',
                body: 'Type their name here and tap <b>Add</b>. They join the team with fewer players and stay on the list for next time.'
            },
            {
                target: EDIT_BTN,
                title: 'Fix a name, add a photo, remove a player',
                body: '<b>Edit</b> switches to the player list: tap a name to rename it, tap the circle to add a photo, ' +
                    'or tap <b>Delete</b> twice to remove someone for good. Tap <b>Done</b> to come back.<br><br>' +
                    'There’s also a direct link that opens straight into editing.'
            },
            {
                target: '#teams-help-btn',
                title: 'That’s it!',
                body: 'Everything saves by itself, and everyone sees the same teams.<br><br>' +
                    'Tap <b>?</b> any time to see this tour again.'
            }
        ];
    }

    let root = null;      // overlay element while a tour is running
    let els = null;       // its parts
    let steps = [];
    let index = 0;
    let frame = 0;
    let backup = 0;       // slow timer, in case the browser is holding back animation frames
    let lastLayout = '';
    let safe = { top: 0, right: 0, bottom: 0, left: 0 };

    function build() {
        root = document.createElement('div');
        root.id = 'tour';
        root.innerHTML = `
            <div class="tour-safe"></div>
            <div class="tour-block"></div><div class="tour-block"></div><div class="tour-block"></div><div class="tour-block"></div>
            <div class="tour-spot"></div>
            <div class="tour-card" id="tour-card" role="dialog" aria-labelledby="tour-title" aria-describedby="tour-body">
                <div class="tour-top">
                    <span class="tour-count" aria-live="polite"></span>
                    <button type="button" class="tour-skip">Skip tour</button>
                </div>
                <h3 class="tour-title" id="tour-title"></h3>
                <p class="tour-body" id="tour-body"></p>
                <div class="tour-nav">
                    <button type="button" class="tour-back">Back</button>
                    <button type="button" class="tour-next">Next</button>
                </div>
            </div>`;
        els = {
            safe: root.querySelector('.tour-safe'),
            blocks: [...root.querySelectorAll('.tour-block')],
            spot: root.querySelector('.tour-spot'),
            card: root.querySelector('.tour-card'),
            count: root.querySelector('.tour-count'),
            title: root.querySelector('.tour-title'),
            body: root.querySelector('.tour-body'),
            skip: root.querySelector('.tour-skip'),
            back: root.querySelector('.tour-back'),
            next: root.querySelector('.tour-next')
        };
        els.skip.addEventListener('click', end);
        els.back.addEventListener('click', () => show(index - 1));
        els.next.addEventListener('click', () => (index >= steps.length - 1 ? end() : show(index + 1)));
        // The app closes its panels on any click outside them; taps on the tour don't count
        root.addEventListener('click', e => e.stopPropagation());
        document.body.appendChild(root);
    }

    function start() {
        const app = window.scoreboardApp;
        const panel = document.querySelector(PANEL);
        if (!panel) return;
        if (root) end();
        // Showing the panel is navigation, not a change to anyone's data
        if (!panel.classList.contains('active')) {
            if (app) app.players.open();
            else panel.classList.add('active');
        }
        steps = buildSteps();
        build();
        readSafeArea();
        document.addEventListener('keydown', onKey, true);
        window.addEventListener('resize', readSafeArea);
        show(0);
        frame = requestAnimationFrame(tick);
        backup = setInterval(tick, 400, true);
    }

    function end() {
        if (!root) return;
        cancelAnimationFrame(frame);
        clearInterval(backup);
        document.removeEventListener('keydown', onKey, true);
        window.removeEventListener('resize', readSafeArea);
        root.remove();
        root = els = null;
        lastLayout = '';
    }

    function show(i) {
        index = Math.max(0, Math.min(steps.length - 1, i));
        lastLayout = '';
        const step = steps[index];
        const last = index === steps.length - 1;

        // Every step but the Edit one describes the teams view, so flip back if they tried Edit
        const panel = document.querySelector(PANEL);
        if (step.target !== EDIT_BTN && panel.classList.contains('editing')) {
            window.scoreboardApp?.players.setEditing(false);
        }

        els.count.textContent = `${index + 1} of ${steps.length}`;
        els.title.textContent = step.title;
        els.body.innerHTML = step.body;
        els.back.style.visibility = index === 0 ? 'hidden' : '';
        els.skip.hidden = last;
        els.next.textContent = last ? 'Done' : 'Next';

        scrollToTarget(step);
        // Glide between steps, then follow the target exactly
        root.classList.add('tour-moving');
        clearTimeout(show.timer);
        show.timer = setTimeout(() => root?.classList.remove('tour-moving'), 300);
        layout();
        els.next.focus({ preventScroll: true });
    }

    function onKey(e) {
        if (e.key === 'Escape') {
            e.preventDefault();
            end();
            return;
        }
        if (/^(INPUT|TEXTAREA)$/.test(e.target.tagName) || e.target.isContentEditable) return;
        if (e.key === 'ArrowRight' && index < steps.length - 1) show(index + 1);
        if (e.key === 'ArrowLeft' && index > 0) show(index - 1);
    }

    // env(safe-area-inset-*) as numbers, read off a hidden element padded with them
    function readSafeArea() {
        const cs = getComputedStyle(els.safe);
        safe = {
            top: parseFloat(cs.paddingTop) || 0,
            right: parseFloat(cs.paddingRight) || 0,
            bottom: parseFloat(cs.paddingBottom) || 0,
            left: parseFloat(cs.paddingLeft) || 0
        };
    }

    // Rows and the add box live in the panel's scrolling part: bring them to the middle of
    // what's visible below the sticky header. Only the panel is ever scrolled.
    function scrollToTarget(step) {
        const panel = document.querySelector(PANEL);
        const el = step.target && document.querySelector(step.target);
        const header = panel.querySelector('.teams-header');
        if (!el || !header || header.contains(el) || !panel.contains(el)) return;
        const top = Math.max(header.getBoundingClientRect().bottom, 0);
        const bottom = Math.min(panel.getBoundingClientRect().bottom, window.innerHeight);
        const r = el.getBoundingClientRect();
        panel.scrollTop += (r.top + r.height / 2) - (top + bottom) / 2;
    }

    // One box around everything the step points at, or null when none of it is on screen
    function targetRect(step, vw, vh) {
        if (!step.target) return null;
        let box = null;
        document.querySelectorAll(step.target).forEach(el => {
            const r = el.getBoundingClientRect();
            if (r.width < 1 || r.height < 1) return;
            if (r.bottom < 0 || r.top > vh || r.right < 0 || r.left > vw) return;
            box = box
                ? {
                    left: Math.min(box.left, r.left), top: Math.min(box.top, r.top),
                    right: Math.max(box.right, r.right), bottom: Math.max(box.bottom, r.bottom)
                }
                : { left: r.left, top: r.top, right: r.right, bottom: r.bottom };
        });
        if (!box) return null;
        const edge = 4;
        return {
            left: Math.max(edge, box.left - PAD),
            top: Math.max(edge, box.top - PAD),
            right: Math.min(vw - edge, box.right + PAD),
            bottom: Math.min(vh - edge, box.bottom + PAD)
        };
    }

    function place(el, left, top, width, height) {
        el.style.left = `${left}px`;
        el.style.top = `${top}px`;
        el.style.width = `${Math.max(0, width)}px`;
        el.style.height = `${Math.max(0, height)}px`;
    }

    function layout() {
        const vw = document.documentElement.clientWidth;
        const vh = window.innerHeight;
        const r = targetRect(steps[index], vw, vh);

        const width = Math.min(360, vw - safe.left - safe.right - 2 * MARGIN);
        els.card.style.width = `${width}px`;
        const height = els.card.offsetHeight;

        // Skip the style writes when nothing moved since the last frame
        const key = [vw, vh, width, height, r && [r.left, r.top, r.right, r.bottom].map(Math.round)].join();
        if (key === lastLayout) return;
        lastLayout = key;

        const minTop = safe.top + MARGIN;
        const maxTop = vh - safe.bottom - MARGIN - height;
        const minLeft = safe.left + MARGIN;
        const maxLeft = vw - safe.right - MARGIN - width;
        const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
        const [above, below, left, right] = els.blocks;

        if (!r) {
            // Nothing to point at (no players yet, say): dim everything and center the card
            root.classList.add('tour-no-target');
            place(els.spot, vw / 2, vh / 2, 0, 0);
            place(above, 0, 0, vw, vh);
            [below, left, right].forEach(b => place(b, 0, 0, 0, 0));
            els.card.style.left = `${clamp((vw - width) / 2, minLeft, maxLeft)}px`;
            els.card.style.top = `${clamp((vh - height) / 2, minTop, Math.max(minTop, maxTop))}px`;
            return;
        }

        root.classList.remove('tour-no-target');
        place(els.spot, r.left, r.top, r.right - r.left, r.bottom - r.top);
        // Four tap-catchers around the spotlight; the hole between them stays tappable
        place(above, 0, 0, vw, r.top);
        place(below, 0, r.bottom, vw, vh - r.bottom);
        place(left, 0, r.top, r.left, r.bottom - r.top);
        place(right, r.right, r.top, vw - r.right, r.bottom - r.top);

        // Card goes below the spotlight if it fits, else above. On a short screen (phone on its
        // side) it goes beside it instead; failing all that, wherever it covers the least.
        const roomBelow = vh - safe.bottom - MARGIN - (r.bottom + GAP);
        const roomAbove = r.top - GAP - minTop;
        const roomLeft = r.left - GAP - minLeft;
        const roomRight = vw - safe.right - MARGIN - (r.right + GAP);
        if (Math.max(roomBelow, roomAbove) < height && Math.max(roomLeft, roomRight) >= width) {
            els.card.style.left = `${roomRight >= width ? r.right + GAP : r.left - GAP - width}px`;
            els.card.style.top = `${clamp((r.top + r.bottom - height) / 2, minTop, Math.max(minTop, maxTop))}px`;
            return;
        }
        const goBelow = roomBelow >= height || (roomAbove < height && roomBelow >= roomAbove);
        const top = goBelow ? r.bottom + GAP : r.top - GAP - height;
        els.card.style.top = `${clamp(top, minTop, Math.max(minTop, maxTop))}px`;
        els.card.style.left = `${clamp((r.left + r.right - width) / 2, minLeft, Math.max(minLeft, maxLeft))}px`;
    }

    // Runs every animation frame, plus on the slow backup timer (which passes true)
    function tick(fromTimer) {
        if (!root) return;
        // Panel closed underneath us: nothing left to explain
        if (!document.querySelector(PANEL)?.classList.contains('active')) {
            end();
            return;
        }
        layout();
        if (fromTimer !== true) frame = requestAnimationFrame(tick);
    }

    window.TeamsTour = { start, end, get active() { return !!root; } };
})();
