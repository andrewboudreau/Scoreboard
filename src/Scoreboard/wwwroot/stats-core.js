// Shared game-stats logic for stats.html and game.html.
// Games come in two flavors and everything here works for both:
//   - player-tracked: score events carry playerId and players[].points add up
//   - team-only: just team scores, usually captured at the end of each quarter
// Teams are 1 (BLACK) and 2 (WHITE).

const Stats = (() => {
    const DATE_IN_ID = /^game-(\d{4}-\d{2}-\d{2})/;

    function num(v) {
        const n = Number(v);
        return Number.isFinite(n) ? n : 0;
    }

    // Cumulative [team1, team2] score at the end of each quarter.
    function quarterTotals(raw) {
        const ends = (raw.events || []).filter(e => e.type === 'period_end' && Array.isArray(e.scores));
        if (ends.length) return ends.map(e => [num(e.scores[0]), num(e.scores[1])]);
        return (raw.periodScores || []).map(s => {
            const [a, b] = String(s).split('-').map(x => num(x.trim()));
            return [a, b];
        });
    }

    const TRACKED_COVERAGE = 0.5;

    // Points per player id from credited score events (each event carries the running score).
    function eventPoints(events) {
        const pts = new Map();
        let prev = [0, 0];
        for (const e of events) {
            if (!Array.isArray(e.scores)) continue;
            const now = [num(e.scores[0]), num(e.scores[1])];
            const t = Number(e.team);
            if (e.type === 'score' && e.playerId != null && (t === 1 || t === 2)) {
                const key = String(e.playerId);
                pts.set(key, (pts.get(key) || 0) + now[t - 1] - prev[t - 1]);
            }
            prev = now;
        }
        return pts;
    }

    // Events this far apart belong to different nights. Matches NEW_NIGHT_GAP_MS in app.js.
    const SESSION_GAP_MS = 8 * 3600 * 1000;
    // Games with fewer total points than this are warm-ups or tests; stats leave them out.
    const MIN_POINTS = 10;

    const pad2 = n => String(n).padStart(2, '0');

    // YYYY-MM-DD on this device's calendar. Games are evenings, so the UTC date is often a day late.
    function localDate(ts) {
        const d = new Date(ts);
        return isNaN(d) ? '' : `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;
    }

    // Older versions kept adding to the same saved game until someone tapped New game, so one
    // saved game can hold several nights. Split it at long gaps between events. The newest
    // night keeps the saved id; earlier ones become "<id>~1", "<id>~2", ...
    function splitNights(raw) {
        const all = Array.isArray(raw.events) ? raw.events : [];
        const nights = [];
        let cur = [], lastT = null;
        for (const e of all) {
            const t = Date.parse(e?.ts || '');
            if (Number.isFinite(t)) {
                if (lastT !== null && t - lastT >= SESSION_GAP_MS && cur.length) { nights.push(cur); cur = []; }
                lastT = t;
            }
            cur.push(e);
        }
        if (cur.length) nights.push(cur);
        if (nights.length < 2) return [raw];

        // A night of nothing but taps back down (someone fiddling with an old game) isn't a game.
        const played = evs => evs.some(e => e?.type === 'period_end' || e?.type === 'score_set'
            || (e?.type === 'score' && e.action !== 'decrement'));
        const newestPlayed = played(nights[nights.length - 1]);
        const kept = nights.filter(played);
        if (!kept.length) return [raw];

        const roster = new Map((raw.players || []).filter(Boolean).map(p => [String(p.id ?? p.name), p]));
        const scoresOf = e => [num(e.scores[0]), num(e.scores[1])];
        let carried = [0, 0]; // running score when the previous night ended

        return kept.map((night, i) => {
            const newest = i === kept.length - 1;
            let evs = night;
            let base = carried;
            if (i > 0) {
                // Instead of starting a new game, people often tapped every score back down to zero.
                // Skip that wind-down: the night starts where the score bottoms out before the first
                // quarter ends. If the score just kept running, the base is where last night ended.
                const firstEnd = evs.findIndex(e => e?.type === 'period_end');
                const lead = firstEnd < 0 ? evs.length : firstEnd;
                let low = carried[0] + carried[1], lowAt = -1;
                for (let k = 0; k < lead; k++) {
                    if (!Array.isArray(evs[k]?.scores)) continue;
                    const sc = scoresOf(evs[k]);
                    if (sc[0] + sc[1] <= low) { low = sc[0] + sc[1]; lowAt = k; base = sc; }
                }
                evs = evs.slice(lowAt + 1);
            }
            const withScores = evs.filter(e => Array.isArray(e?.scores));
            const end = withScores.length ? scoresOf(withScores[withScores.length - 1]) : base;
            carried = end;

            let final = [Math.max(0, end[0] - base[0]), Math.max(0, end[1] - base[1])];
            if (newest && newestPlayed) {
                const saved = [num(raw.team1?.score), num(raw.team2?.score)];
                if (saved[0] >= base[0] && saved[1] >= base[1]) final = [saved[0] - base[0], saved[1] - base[1]];
            }

            // Saved player points cover every night, so recount from this night's events
            // (normalize falls back to events when all points are zero). Players keep the team they scored for.
            const players = newest ? (raw.players || []).filter(Boolean).map(p => ({ ...p, points: 0 })) : [];
            for (const e of evs) {
                const team = Number(e?.team);
                if (e?.type !== 'score' || e.playerId == null || (team !== 1 && team !== 2)) continue;
                const key = String(e.playerId);
                let p = players.find(x => String(x.id ?? x.name) === key);
                if (!p) {
                    const r = roster.get(key);
                    p = { id: r?.id ?? e.playerId, name: r?.name || e.player || e.playerName || '?', active: true, points: 0 };
                    players.push(p);
                }
                p.team = String(team);
            }

            return {
                ...raw,
                id: newest ? raw.id : `${raw.id}~${i + 1}`,
                splitFrom: raw.id,
                weekDate: undefined,
                team1: { ...raw.team1, score: final[0] },
                team2: { ...raw.team2, score: final[1] },
                periodScores: [],
                players,
                events: evs.map(e => Array.isArray(e?.scores)
                    ? { ...e, scores: [num(e.scores[0]) - base[0], num(e.scores[1]) - base[1]] } : e),
                lastUpdated: newest && newestPlayed ? raw.lastUpdated : (evs[evs.length - 1]?.ts || raw.lastUpdated)
            };
        });
    }

    // Every game in one saved document (usually one; more for old documents that span nights).
    function gamesFromDoc(raw) {
        return splitNights(raw).map(normalize);
    }

    // Turn a stored game document into one shape the pages can rely on.
    function normalize(raw) {
        const events = Array.isArray(raw.events) ? raw.events : [];
        const final = [num(raw.team1?.score), num(raw.team2?.score)];
        // The night it was played: the first event on this device's calendar, else the saved date.
        const firstTs = events.find(e => Number.isFinite(Date.parse(e?.ts || '')))?.ts;
        const idDate = raw.id?.match(DATE_IN_ID)?.[1];
        let date = firstTs ? localDate(firstTs) : '';
        if (!date && idDate) {
            // No event times. Older ids hold the UTC day the game was created, and older saves set
            // lastUpdated when the NEXT game started. If it was saved the same UTC day, use that time;
            // otherwise assume an evening start (01:30 UTC is mid-evening across the US).
            const saved = raw.lastUpdated || '';
            date = localDate(saved.startsWith(idDate) ? saved : `${idDate}T01:30:00Z`);
        }
        date = date || raw.weekDate || (raw.lastUpdated || '').split('T')[0] || '';

        const totals = quarterTotals(raw);
        const quarters = totals.map((t, i) => {
            const prev = i ? totals[i - 1] : [0, 0];
            return { points: [t[0] - prev[0], t[1] - prev[1]], total: t };
        });

        // Points scored after the last quarter ended (game not finished, or late corrections)
        const lastTotal = totals.length ? totals[totals.length - 1] : [0, 0];
        const unfinished = totals.length > 0 && (final[0] !== lastTotal[0] || final[1] !== lastTotal[1]);

        const players = (raw.players || [])
            .filter(p => p && p.active !== false && (p.team === '1' || p.team === '2'))
            .map(p => ({ id: p.id ?? p.name, name: p.name || '?', team: p.team === '1' ? 1 : 2, points: num(p.points) }));

        // Some saved games have zeroed players[].points but credited score events; fall back to those.
        if (!players.some(p => p.points > 0)) {
            const fromEvents = eventPoints(events);
            for (const p of players) p.points = Math.max(0, fromEvents.get(String(p.id)) || 0);
        }

        const attributed = players.reduce((s, p) => s + p.points, 0);
        const total = final[0] + final[1];
        const coverage = total ? Math.min(1, attributed / total) : 0;
        // Player stats only count games where most of the points were credited to someone.
        // A game with a basket or two credited is really a team-scores game.
        const tracked = coverage >= TRACKED_COVERAGE;

        return {
            id: raw.id,
            date,
            // Always BLACK and WHITE; whatever was typed is kept for the team-name history.
            names: ['BLACK', 'WHITE'],
            recordedNames: [String(raw.team1?.name || 'BLACK').trim(), String(raw.team2?.name || 'WHITE').trim()],
            final,
            winner: final[0] > final[1] ? 1 : final[1] > final[0] ? 2 : 0,
            margin: Math.abs(final[0] - final[1]),
            quarters,
            unfinished,
            players,
            tracked,
            partial: !tracked && attributed > 0,
            coverage,
            unattributed: [
                Math.max(0, final[0] - players.filter(p => p.team === 1).reduce((s, p) => s + p.points, 0)),
                Math.max(0, final[1] - players.filter(p => p.team === 2).reduce((s, p) => s + p.points, 0))
            ],
            scoreEvents: events.filter(e => Array.isArray(e.scores)),
            events,
            raw
        };
    }

    // A game worth counting: something was scored or a quarter was recorded.
    function isPlayed(g) {
        return g.final[0] + g.final[1] > 0 || g.quarters.length > 0;
    }

    // Lead changes, ties and biggest leads, from the scoring sequence (or quarter totals).
    function flow(g) {
        const seq = g.scoreEvents.length ? g.scoreEvents.map(e => [num(e.scores[0]), num(e.scores[1])])
            : g.quarters.map(q => q.total);
        let leader = 0, leadChanges = 0, ties = 0;
        const biggest = [0, 0];
        for (const [a, b] of seq) {
            const now = a > b ? 1 : b > a ? 2 : 0;
            if (now === 0 && leader !== 0) ties++;
            if (now !== 0 && leader !== 0 && now !== leader) leadChanges++;
            if (now !== 0) leader = now;
            if (a - b > biggest[0]) biggest[0] = a - b;
            if (b - a > biggest[1]) biggest[1] = b - a;
        }
        return { leadChanges, ties, biggest };
    }

    // Team-level summary. Works for every game, tracked or not.
    function summarize(games) {
        const s = {
            games: games.length,
            wins: [0, 0], ties: 0,
            points: [0, 0],
            closest: null, biggest: null,
            streak: { team: 0, count: 0 },
            comebacks: [],
            otherNames: [], // names typed for a team other than BLACK/WHITE: { team, name, games, last }
            quarters: [] // per quarter index: { games, points: [a,b], won: [a,b] }
        };

        // games are newest first; the streak counts back from the latest game
        if (games[0]?.winner) {
            const team = games[0].winner;
            let count = 0;
            for (const g of games) {
                if (g.winner !== team) break;
                count++;
            }
            s.streak = { team, count };
        }

        const otherNames = new Map();
        for (const g of games) {
            g.recordedNames.forEach((name, i) => {
                if (!name || name.toUpperCase() === g.names[i]) return;
                const key = `${i + 1}:${name.toUpperCase()}`;
                const row = otherNames.get(key) || { team: i + 1, name, games: 0, last: g };
                row.games++;
                otherNames.set(key, row);
            });
            if (g.winner) s.wins[g.winner - 1]++; else s.ties++;
            s.points[0] += g.final[0];
            s.points[1] += g.final[1];
            if (g.winner && (!s.closest || g.margin < s.closest.margin)) s.closest = g;
            if (!s.biggest || g.margin > s.biggest.margin) s.biggest = g;

            // Won after trailing at half (or after Q3 if that's all we have)
            const half = g.quarters[1] || null;
            if (g.winner && half) {
                const [a, b] = half.total;
                const trailing = g.winner === 1 ? a < b : b < a;
                if (trailing) s.comebacks.push({ game: g, deficit: Math.abs(a - b) });
            }

            g.quarters.forEach((q, i) => {
                const row = s.quarters[i] ||= { games: 0, points: [0, 0], won: [0, 0] };
                row.games++;
                row.points[0] += q.points[0];
                row.points[1] += q.points[1];
                if (q.points[0] > q.points[1]) row.won[0]++;
                else if (q.points[1] > q.points[0]) row.won[1]++;
            });
        }
        // Longest win streak per team (oldest to newest; a tie breaks it)
        s.longest = [null, null];
        let run = null;
        for (const g of [...games].reverse()) {
            if (run && g.winner === run.team) { run.count++; run.to = g; }
            else run = g.winner ? { team: g.winner, count: 1, from: g, to: g } : null;
            if (run && (!s.longest[run.team - 1] || run.count > s.longest[run.team - 1].count)) s.longest[run.team - 1] = { ...run };
        }

        s.otherNames = [...otherNames.values()].sort((a, b) => a.team - b.team || b.games - a.games);
        return s;
    }

    // Per-player numbers. Games played, record and +/- come from team results, so they
    // work for every game; points only count games where player scoring was tracked.
    function players(games) {
        const byId = new Map();
        // oldest first so the latest name wins
        for (const g of [...games].reverse()) {
            for (const p of g.players) {
                const key = String(p.id);
                const row = byId.get(key) || {
                    id: key, name: p.name, gp: 0, wins: 0, losses: 0, ties: 0, plusMinus: 0,
                    trackedGp: 0, points: 0, high: null, log: []
                };
                row.name = p.name;
                row.gp++;
                const won = g.winner === p.team, lost = g.winner && g.winner !== p.team;
                if (won) row.wins++; else if (lost) row.losses++; else row.ties++;
                const diff = p.team === 1 ? g.final[0] - g.final[1] : g.final[1] - g.final[0];
                row.plusMinus += diff;
                if (g.tracked) {
                    row.trackedGp++;
                    row.points += p.points;
                    if (!row.high || p.points > row.high.points) row.high = { points: p.points, game: g };
                }
                row.log.unshift({ game: g, team: p.team, result: won ? 'W' : lost ? 'L' : 'T', diff, points: g.tracked ? p.points : null });
                byId.set(key, row);
            }
        }
        return [...byId.values()].map(r => ({
            ...r,
            winPct: r.gp ? (r.wins + r.ties / 2) / r.gp : 0,
            avgPlusMinus: r.gp ? r.plusMinus / r.gp : 0,
            ppg: r.trackedGp ? r.points / r.trackedGp : null
        }));
    }

    // ---- formatting ----

    function esc(s) {
        return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
    }

    function formatDate(date, opts = { weekday: 'short', month: 'short', day: 'numeric' }) {
        if (!date) return '';
        const d = new Date(date + 'T12:00:00');
        if (isNaN(d)) return date;
        const sameYear = d.getFullYear() === new Date().getFullYear();
        return d.toLocaleDateString(undefined, sameYear ? opts : { ...opts, year: 'numeric' });
    }

    function formatTime(ts) {
        const d = new Date(ts);
        return isNaN(d) ? '' : d.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
    }

    function signed(n, digits = 0) {
        const v = digits ? n.toFixed(digits) : String(Math.round(n));
        return n > 0 ? `+${v}` : v;
    }

    function teamChip(team, name) {
        return `<span class="chip chip-${team}">${esc(name)}</span>`;
    }

    // Plain-text result for the group chat
    function shareText(g, url) {
        const [a, b] = g.final;
        const lines = [`🏀 ${g.names[0]} ${a} – ${b} ${g.names[1]} · ${formatDate(g.date)}`];
        if (g.quarters.length) {
            lines.push('Quarters: ' + g.quarters.map(q => `${q.points[0]}-${q.points[1]}`).join(', '));
        }
        if (g.tracked) {
            const top = [...g.players].sort((x, y) => y.points - x.points).filter(p => p.points > 0).slice(0, 3);
            if (top.length) lines.push('Top: ' + top.map(p => `${p.name} ${p.points}`).join(', '));
        }
        if (url) lines.push(url);
        return lines.join('\n');
    }

    // Share sheet on phones/iPads, clipboard elsewhere. Returns 'shared' | 'copied' | 'cancelled' | 'failed'.
    async function share(text) {
        if (navigator.share && matchMedia('(pointer: coarse)').matches) {
            try {
                await navigator.share({ text });
                return 'shared';
            } catch (e) {
                if (e.name === 'AbortError') return 'cancelled';
            }
        }
        try {
            await navigator.clipboard.writeText(text);
            return 'copied';
        } catch {
            prompt('Copy:', text);
            return 'failed';
        }
    }

    // ---- chart ----

    // Score-over-time SVG. Uses every scoring event when there are any, else quarter totals.
    function chartSvg(g) {
        const byEvent = g.scoreEvents.length >= 2;
        const seq = byEvent
            ? [[0, 0], ...g.scoreEvents.map(e => [num(e.scores[0]), num(e.scores[1])])]
            : [[0, 0], ...g.quarters.map(q => q.total)];
        if (seq.length < 2) return '';

        const W = 360, H = 190, pad = { l: 26, r: 8, t: 10, b: 22 };
        const max = Math.max(4, ...seq.flat());
        const x = i => pad.l + (i / (seq.length - 1)) * (W - pad.l - pad.r);
        const y = v => pad.t + (1 - v / max) * (H - pad.t - pad.b);
        const line = t => seq.map((p, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(p[t]).toFixed(1)}`).join('');

        let grid = '';
        const step = Math.max(1, Math.ceil(max / 4 / 5) * 5);
        for (let v = 0; v <= max; v += step) {
            grid += `<line x1="${pad.l}" x2="${W - pad.r}" y1="${y(v)}" y2="${y(v)}" class="grid"/>` +
                `<text x="${pad.l - 6}" y="${y(v) + 4}" class="axis" text-anchor="end">${v}</text>`;
        }

        // Quarter boundaries
        let marks = '';
        if (byEvent) {
            let i = 0, q = 0;
            for (const e of g.scoreEvents) {
                i++;
                if (e.type === 'period_end') {
                    q++;
                    marks += `<line x1="${x(i)}" x2="${x(i)}" y1="${pad.t}" y2="${H - pad.b}" class="qline"/>` +
                        `<text x="${x(i)}" y="${H - 6}" class="axis" text-anchor="middle">Q${q}</text>`;
                }
            }
        } else {
            g.quarters.forEach((_, i) => {
                marks += `<text x="${x(i + 1)}" y="${H - 6}" class="axis" text-anchor="middle">Q${i + 1}</text>`;
            });
        }

        const dots = byEvent ? '' : seq.slice(1).map((p, i) =>
            `<circle cx="${x(i + 1)}" cy="${y(p[0])}" r="4" class="dot1"/><circle cx="${x(i + 1)}" cy="${y(p[1])}" r="4" class="dot2"/>`).join('');

        return `<svg viewBox="0 0 ${W} ${H}" class="chart" role="img" aria-label="Score over time">
            ${grid}${marks}
            <path d="${line(1)}" class="line2"/>
            <path d="${line(0)}" class="line1"/>
            ${dots}
        </svg>`;
    }

    async function loadGames() {
        const res = await fetch('/Scoreboard/api/games');
        if (!res.ok) throw new Error(`Couldn't load games (${res.status})`);
        const played = (await res.json())
            .flatMap(gamesFromDoc)
            .filter(isPlayed)
            .sort((a, b) => (b.date + (b.raw.lastUpdated || '')).localeCompare(a.date + (a.raw.lastUpdated || '')));
        const games = played.filter(g => !isShort(g));
        games.shortGames = played.filter(isShort);
        return games;
    }

    // A finished warm-up or test: too few points to count in the stats. Its game page still works.
    // A game still being played tonight is never short.
    function isShort(g) {
        const age = Date.now() - Date.parse(g.raw.lastUpdated || '');
        return g.final[0] + g.final[1] < MIN_POINTS && !(age < SESSION_GAP_MS);
    }

    return { normalize, gamesFromDoc, isShort, MIN_POINTS, isPlayed, flow, summarize, players, esc, formatDate, formatTime, signed, teamChip, shareText, share, chartSvg, loadGames };
})();
