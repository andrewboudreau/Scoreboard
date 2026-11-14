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

    // Turn a stored game document into one shape the pages can rely on.
    function normalize(raw) {
        const events = Array.isArray(raw.events) ? raw.events : [];
        const final = [num(raw.team1?.score), num(raw.team2?.score)];
        const date = raw.weekDate || raw.id?.match(DATE_IN_ID)?.[1] || (raw.lastUpdated || '').split('T')[0] || '';

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
        return (await res.json())
            .map(normalize)
            .filter(isPlayed)
            .sort((a, b) => (b.date + (b.raw.lastUpdated || '')).localeCompare(a.date + (a.raw.lastUpdated || '')));
    }

    return { normalize, isPlayed, flow, summarize, players, esc, formatDate, formatTime, signed, teamChip, shareText, share, chartSvg, loadGames };
})();
