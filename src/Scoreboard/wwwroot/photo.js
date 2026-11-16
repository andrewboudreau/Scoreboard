// Photo page: pick your name, choose a photo, crop it, maybe try an AI style, save.
// Server side is PhotoBooth.cs. ?k=<code> unlocks AI styles (kept on this phone), ?p=<playerId> skips the name list.

(() => {
    const $ = id => document.getElementById(id);
    const params = new URLSearchParams(location.search);

    const OUT = 1024;          // cropped photo size sent to the server
    const MAX_SOURCE = 2048;   // big phone photos are scaled down to this first

    // Who's using it, for the usage log and daily limits. Not a login.
    const device = localStorage.getItem('photoDevice') || (() => {
        const id = (crypto.randomUUID?.() || Math.random().toString(36).slice(2) + Date.now().toString(36));
        localStorage.setItem('photoDevice', id);
        return id;
    })();
    if (params.get('k')) localStorage.setItem('photoCode', params.get('k'));
    const code = params.get('k') || localStorage.getItem('photoCode') || '';

    // ---------------- tracking (visits, steps, clicks) -> admin page ----------------

    const queue = [];
    let current = '';
    function track(type, detail) {
        queue.push({ type, detail, playerId: me ? String(me.id) : null, at: Date.now() });
    }
    function flush() {
        if (!queue.length) return;
        const now = Date.now();
        const events = queue.splice(0, 50).map(e => ({ type: e.type, detail: e.detail, playerId: e.playerId, ago: now - e.at }));
        const body = JSON.stringify({ device, ua: navigator.userAgent, events });
        if (!navigator.sendBeacon?.('/Scoreboard/api/photo/events', new Blob([body], { type: 'application/json' }))) {
            fetch('/Scoreboard/api/photo/events', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body, keepalive: true }).catch(() => { });
        }
        if (queue.length) flush();
    }
    setInterval(flush, 8000);
    addEventListener('pagehide', flush);
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') flush(); });
    document.addEventListener('click', e => {
        const el = e.target.closest('button, a, label, input[type=file]');
        if (!el) return;
        const what = el.id || (el.dataset.style && `style:${el.dataset.style}`) || (el.dataset.back && `back:${el.dataset.back}`)
            || (el.classList.contains('person') ? 'person' : (el.textContent || '').trim().slice(0, 30) || el.tagName.toLowerCase());
        track('click', what);
    }, true);
    let players = [];
    let me = null;
    let source = null;          // canvas holding the chosen photo (oriented, scaled down)
    let crop = null;            // { rot, scale, cx, cy } in stage pixels
    let cropped = '';           // data URL of the crop (original)
    let results = {};           // style id -> data URL, so switching back costs nothing
    let chosen = 'original';
    let config = { ai: false, styles: [], remaining: 0 };
    let busy = false;

    // ---------------- steps ----------------

    const steps = ['who', 'pick', 'crop', 'style', 'done'];
    function show(step) {
        for (const s of steps) $(`step-${s}`).hidden = s !== step;
        $('error').hidden = true;
        if (step !== current) { current = step; track('nav', step); }
        const head = step === 'who';
        $('lead').hidden = !head;
        $('title').textContent = step === 'done' ? '' : step === 'who' ? 'Add your photo' : me ? `Hi, ${me.name.split(' ')[0]}!` : 'Add your photo';
        window.scrollTo(0, 0);
        if (step === 'crop') requestAnimationFrame(sizeStage);
    }

    function fail(msg) {
        const el = $('error');
        el.textContent = msg;
        el.hidden = false;
    }

    document.querySelectorAll('[data-back]').forEach(b => b.addEventListener('click', () => {
        me = null;
        history.replaceState(null, '', location.pathname + (code && params.get('k') ? `?k=${encodeURIComponent(code)}` : ''));
        show(b.dataset.back);
        $('search').value = '';
        renderPeople();
    }));

    // ---------------- 1. names ----------------

    function avatar(p, cls = 'avatar') {
        if (p.imageUrl) return `<img class="${cls}" src="${esc(p.imageUrl)}" alt="">`;
        return `<span class="${cls}">${esc((p.name || '?').trim().charAt(0).toUpperCase())}</span>`;
    }

    function renderPeople() {
        const q = $('search').value.trim().toLowerCase();
        const list = players.filter(p => !q || p.name.toLowerCase().includes(q));
        $('people').innerHTML = list.map(p => `
            <button class="person" role="listitem" data-id="${esc(String(p.id))}">
                ${avatar(p)}
                <span class="name">${esc(p.name)}</span>
                <span class="status">${p.imageUrl ? 'Change photo' : 'Add photo'}</span>
            </button>`).join('');
        $('people-empty').hidden = list.length > 0 || !players.length;
        $('people').querySelectorAll('.person').forEach(b => b.addEventListener('click', () => pick(b.dataset.id)));
    }

    function pick(id) {
        me = players.find(p => String(p.id) === String(id));
        if (!me) return;
        $('me-pick').innerHTML = `${avatar(me)}<span>${esc(me.name)}</span>`;
        show('pick');
    }

    $('search').addEventListener('input', renderPeople);

    // ---------------- 2. choose a photo ----------------

    // Computers have no camera to open, so only phones and tablets get the selfie button
    $('camera-btn').hidden = !matchMedia('(pointer: coarse)').matches;

    const onPicked = async e => {
        const input = e.target;
        const file = input.files[0];
        input.value = '';
        if (!file) return;
        try {
            source = await loadPhoto(file);
            crop = null;
            show('crop');
        } catch {
            fail("Couldn't open that photo. Try a different one.");
        }
    };
    $('file').addEventListener('change', onPicked);
    $('camera').addEventListener('change', onPicked);

    // Decode with the phone's orientation applied, then scale big photos down
    async function loadPhoto(file) {
        const url = URL.createObjectURL(file);
        try {
            const img = new Image();
            img.decoding = 'async';
            img.src = url;
            await img.decode();
            const k = Math.min(1, MAX_SOURCE / Math.max(img.naturalWidth, img.naturalHeight));
            const c = document.createElement('canvas');
            c.width = Math.round(img.naturalWidth * k);
            c.height = Math.round(img.naturalHeight * k);
            c.getContext('2d').drawImage(img, 0, 0, c.width, c.height);
            return c;
        } finally {
            URL.revokeObjectURL(url);
        }
    }

    // ---------------- 3. crop ----------------

    const stage = $('stage');
    const canvas = $('crop-canvas');
    const zoom = $('zoom');
    let S = 300; // stage size in CSS pixels

    function dims() {
        const odd = crop.rot % 2 === 1;
        return odd ? [source.height, source.width] : [source.width, source.height];
    }
    const minScale = () => S / Math.min(...dims());

    function sizeStage() {
        if (!source) return;
        const old = S;
        S = stage.clientWidth || 300;
        const dpr = Math.min(3, window.devicePixelRatio || 1);
        canvas.width = Math.round(S * dpr);
        canvas.height = Math.round(S * dpr);
        if (!crop) {
            crop = { rot: 0, scale: 0, cx: S / 2, cy: S / 2 };
            crop.scale = minScale();
        } else if (old !== S) {
            const k = S / old;
            crop.scale *= k; crop.cx *= k; crop.cy *= k;
        }
        clamp();
        draw();
    }
    window.addEventListener('resize', () => { if (!$('step-crop').hidden) sizeStage(); });

    // Keep the photo covering the whole square
    function clamp() {
        const min = minScale();
        crop.scale = Math.min(Math.max(crop.scale, min), min * 5);
        const [w, h] = dims();
        const hx = Math.max(0, (w * crop.scale - S) / 2), hy = Math.max(0, (h * crop.scale - S) / 2);
        crop.cx = Math.min(S / 2 + hx, Math.max(S / 2 - hx, crop.cx));
        crop.cy = Math.min(S / 2 + hy, Math.max(S / 2 - hy, crop.cy));
        zoom.value = String(crop.scale / min);
    }

    function paint(ctx, size) {
        const k = size / S;
        ctx.save();
        ctx.fillStyle = '#000';
        ctx.fillRect(0, 0, size, size);
        ctx.imageSmoothingQuality = 'high';
        ctx.translate(crop.cx * k, crop.cy * k);
        ctx.rotate(crop.rot * Math.PI / 2);
        ctx.scale(crop.scale * k, crop.scale * k);
        ctx.drawImage(source, -source.width / 2, -source.height / 2);
        ctx.restore();
    }

    function draw() {
        paint(canvas.getContext('2d'), canvas.width);
    }

    // Zoom keeping the point (px, py) of the stage still
    function zoomTo(scale, px = S / 2, py = S / 2) {
        const k = scale / crop.scale;
        crop.cx = px + (crop.cx - px) * k;
        crop.cy = py + (crop.cy - py) * k;
        crop.scale = scale;
        clamp();
        draw();
    }

    const pointers = new Map();
    let gesture = null;
    const local = e => {
        const r = stage.getBoundingClientRect();
        return [(e.clientX - r.left) * S / r.width, (e.clientY - r.top) * S / r.height];
    };
    function startGesture() {
        const pts = [...pointers.values()];
        if (pts.length === 1) gesture = { x: pts[0][0], y: pts[0][1], cx: crop.cx, cy: crop.cy };
        else if (pts.length >= 2) {
            const [a, b] = pts;
            gesture = {
                dist: Math.hypot(a[0] - b[0], a[1] - b[1]) || 1,
                mx: (a[0] + b[0]) / 2, my: (a[1] + b[1]) / 2,
                scale: crop.scale, cx: crop.cx, cy: crop.cy
            };
        }
    }
    stage.addEventListener('pointerdown', e => {
        stage.setPointerCapture(e.pointerId);
        pointers.set(e.pointerId, local(e));
        startGesture();
    });
    stage.addEventListener('pointermove', e => {
        if (!pointers.has(e.pointerId) || !gesture) return;
        pointers.set(e.pointerId, local(e));
        const pts = [...pointers.values()];
        if (pts.length === 1) {
            crop.cx = gesture.cx + pts[0][0] - gesture.x;
            crop.cy = gesture.cy + pts[0][1] - gesture.y;
        } else {
            const [a, b] = pts;
            const dist = Math.hypot(a[0] - b[0], a[1] - b[1]);
            const mx = (a[0] + b[0]) / 2, my = (a[1] + b[1]) / 2;
            const scale = Math.min(Math.max(gesture.scale * dist / gesture.dist, minScale()), minScale() * 5);
            const k = scale / gesture.scale;
            crop.scale = scale;
            crop.cx = mx + (gesture.cx - gesture.mx) * k;
            crop.cy = my + (gesture.cy - gesture.my) * k;
        }
        clamp();
        draw();
    });
    const lift = e => {
        pointers.delete(e.pointerId);
        startGesture();
    };
    stage.addEventListener('pointerup', lift);
    stage.addEventListener('pointercancel', lift);
    stage.addEventListener('wheel', e => {
        e.preventDefault();
        const [px, py] = local(e);
        zoomTo(crop.scale * Math.exp(-e.deltaY / 400), px, py);
    }, { passive: false });

    zoom.addEventListener('input', () => zoomTo(minScale() * Number(zoom.value)));
    $('rotate').addEventListener('click', () => {
        crop.rot = (crop.rot + 1) % 4;
        crop.cx = S / 2; crop.cy = S / 2;
        crop.scale = minScale();
        clamp();
        draw();
    });
    $('crop-change').addEventListener('click', () => $('file').click());
    $('crop-done').addEventListener('click', () => {
        const out = document.createElement('canvas');
        out.width = out.height = OUT;
        paint(out.getContext('2d'), OUT);
        cropped = out.toDataURL('image/jpeg', 0.9);
        results = { original: cropped };
        select('original');
        show('style');
    });

    // ---------------- 4. styles ----------------

    function renderStyles() {
        const chips = [{ id: 'original', label: 'Original' }, ...(config.ai ? config.styles : [])];
        $('styles').innerHTML = chips.map(s => `
            <button class="style-chip${s.id === chosen ? ' selected' : ''}${results[s.id] && s.id !== 'original' ? ' done' : ''}" data-style="${esc(s.id)}"
                ${busy || (!results[s.id] && config.remaining <= 0 && s.id !== 'original') ? 'disabled' : ''}>${esc(s.label)}</button>`).join('');
        $('styles').querySelectorAll('.style-chip').forEach(b => b.addEventListener('click', () => select(b.dataset.style)));
        $('remaining').textContent = config.ai ? `· ${config.remaining} AI tr${config.remaining === 1 ? 'y' : 'ies'} left today` : '';
        const note = $('ai-note');
        note.hidden = config.ai;
        note.textContent = config.reason || '';
        $('styles-area').hidden = !config.ai && !config.reason;
    }

    async function select(style) {
        if (busy) return;
        $('style-error').hidden = true;
        if (results[style]) {
            chosen = style;
            $('preview').src = results[style];
            renderStyles();
            return;
        }
        const label = config.styles.find(s => s.id === style)?.label || style;
        busy = true;
        renderStyles();
        $('working').hidden = false;
        $('working-text').textContent = `Painting ${label}…`;
        const slow = setTimeout(() => $('working-text').textContent = 'Still painting… about 30 seconds', 9000);
        try {
            const res = await fetch('/Scoreboard/api/photo/stylize', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ playerId: Number(me.id), style, image: cropped, device, code })
            });
            const data = await res.json().catch(() => ({}));
            if (typeof data.remaining === 'number') config.remaining = data.remaining;
            if (!res.ok) throw new Error(data.error || `Something went wrong (${res.status})`);
            results[style] = data.image;
            chosen = style;
            $('preview').src = data.image;
        } catch (e) {
            const err = $('style-error');
            err.textContent = e.message;
            err.hidden = false;
            refreshConfig();
        } finally {
            clearTimeout(slow);
            busy = false;
            $('working').hidden = true;
            renderStyles();
        }
    }

    $('recrop').addEventListener('click', () => show('crop'));

    $('save').addEventListener('click', async () => {
        if (busy) return;
        const btn = $('save');
        btn.disabled = true;
        btn.textContent = 'Saving…';
        try {
            const res = await fetch('/Scoreboard/api/photo/save', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ playerId: Number(me.id), style: chosen, image: results[chosen], device })
            });
            const data = await res.json().catch(() => ({}));
            if (!res.ok) throw new Error(data.error || `Couldn't save (${res.status})`);
            me.imageUrl = data.imageUrl;
            $('done-img').src = results[chosen];
            $('done-title').textContent = `Looking good, ${me.name.split(' ')[0]}!`;
            show('done');
        } catch (e) {
            const err = $('style-error');
            err.textContent = e.message;
            err.hidden = false;
        } finally {
            btn.disabled = false;
            btn.textContent = 'Save my photo';
        }
    });

    $('again').addEventListener('click', () => show('pick'));

    // ---------------- start ----------------

    async function refreshConfig() {
        try {
            const res = await fetch(`/Scoreboard/api/photo/config?device=${encodeURIComponent(device)}&code=${encodeURIComponent(code)}`);
            if (res.ok) config = await res.json();
        } catch { /* styles stay off */ }
        renderStyles();
    }

    function esc(s) {
        return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
    }

    (async () => {
        track('visit', params.has('p') ? 'direct link' : 'page');
        refreshConfig();
        try {
            const [res, linksRes] = await Promise.all([
                fetch('/Scoreboard/api/roster'),
                fetch('/Scoreboard/api/player-links').catch(() => null)
            ]);
            const roster = res.ok ? await res.json() : { players: [] };
            // One person under two ids (see stats): list them once, under the id the link points to
            const links = linksRes?.ok ? (await linksRes.json()).links || {} : {};
            const ids = new Set((roster.players || []).map(p => String(p?.id)));
            players = (roster.players || []).filter(p => p && p.name && !(links[p.id] != null && ids.has(String(links[p.id]))))
                .sort((a, b) => a.name.localeCompare(b.name));
        } catch {
            fail("Couldn't load the player list. Check your connection and reload.");
        }
        renderPeople();
        if (params.get('p')) pick(params.get('p'));
    })();
})();
