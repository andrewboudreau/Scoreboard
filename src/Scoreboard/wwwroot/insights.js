// Deeper analysis over normalized games (see stats-core.js). Pure functions, no DOM.
// Used by stats.html (Insights tab, player sheets) and the Teams panel (Balance button).
//
// The core model is a ridge regression on game margins, the same idea as basketball's
// "adjusted plus-minus": every game says  margin = sum(BLACK impacts) - sum(WHITE impacts).
// It only needs who played and the final score, so team-score-only games count fully.
// Ridge shrinks players with few games toward 0 (average), and recent games weigh more.

const Insights = (() => {
    const key = id => String(id);
    const chrono = games => [...games].reverse(); // games arrive newest first

    // ---- linear algebra ----

    // Solve A x = b (A symmetric positive definite after ridge) by Gaussian elimination.
    function solve(A, b) {
        const n = b.length;
        const M = A.map((row, i) => [...row, b[i]]);
        for (let c = 0; c < n; c++) {
            let p = c;
            for (let r = c + 1; r < n; r++) if (Math.abs(M[r][c]) > Math.abs(M[p][c])) p = r;
            [M[c], M[p]] = [M[p], M[c]];
            const d = M[c][c] || 1e-9;
            for (let r = 0; r < n; r++) {
                if (r === c) continue;
                const f = M[r][c] / d;
                if (f) for (let k = c; k <= n; k++) M[r][k] -= f * M[c][k];
            }
        }
        return M.map((row, i) => row[n] / (row[i] || 1e-9));
    }

    // ---- impact model ----

    const DEFAULTS = { lambda: 3, halfLife: 25, cap: 15, sizeLambda: 0.5 };

    // games: newest first. Returns { impact(id), size, sigma, players: [...], n }
    function fit(games, opts = {}) {
        const o = { ...DEFAULTS, ...opts };
        const list = chrono(games).filter(g => g.players.length);
        const ids = [];
        const index = new Map();
        const names = new Map();
        const gp = new Map();
        for (const g of list) {
            for (const p of g.players) {
                const k = key(p.id);
                if (!index.has(k)) { index.set(k, ids.length); ids.push(k); }
                names.set(k, p.name);
                gp.set(k, (gp.get(k) || 0) + 1);
            }
        }

        const P = ids.length;
        const n = P + 1; // last column: team size difference
        const A = Array.from({ length: n }, () => new Array(n).fill(0));
        const b = new Array(n).fill(0);
        const rows = [];

        list.forEach((g, i) => {
            const age = list.length - 1 - i;
            const w = Math.pow(0.5, age / o.halfLife);
            const x = new Map();
            let size = 0;
            for (const p of g.players) {
                const s = p.team === 1 ? 1 : -1;
                x.set(index.get(key(p.id)), s);
                size += s;
            }
            if (size) x.set(P, size);
            const y = Math.max(-o.cap, Math.min(o.cap, g.final[0] - g.final[1]));
            for (const [j, xj] of x) {
                b[j] += w * xj * y;
                for (const [k, xk] of x) A[j][k] += w * xj * xk;
            }
            rows.push({ x, y, w });
        });
        for (let j = 0; j < P; j++) A[j][j] += o.lambda;
        A[P][P] += o.sizeLambda;

        const beta = P ? solve(A, b) : [0];

        // Spread of real margins around the model's predictions, for win chances.
        // In-sample residuals flatter the model, so inflate them a little and keep a floor.
        let sw = 0, se = 0;
        for (const r of rows) {
            let pred = 0;
            for (const [j, xj] of r.x) pred += beta[j] * xj;
            se += r.w * (r.y - pred) ** 2;
            sw += r.w;
        }
        const sigma = Math.max(5, sw ? 1.25 * Math.sqrt(se / sw) : 8);

        const players = ids.map((k, j) => ({ id: k, name: names.get(k), impact: beta[j], gp: gp.get(k) }));
        const byId = new Map(players.map(p => [p.id, p]));
        return {
            n: list.length,
            sigma,
            size: beta[P] || 0,
            players: players.sort((a, b) => b.impact - a.impact),
            impact: id => byId.get(key(id))?.impact ?? 0,
            known: id => byId.has(key(id))
        };
    }

    // Win probability for a predicted margin (logistic approximation of a normal CDF)
    function chance(margin, sigma) {
        return 1 / (1 + Math.exp(-1.7 * margin / sigma));
    }

    function predict(model, team1Ids, team2Ids) {
        const sum = ids => ids.reduce((s, id) => s + model.impact(id), 0);
        const margin = sum(team1Ids) - sum(team2Ids) + model.size * (team1Ids.length - team2Ids.length);
        return { margin, p1: chance(margin, model.sigma) };
    }

    // How well would the model have called each game using only the games before it?
    function backtest(games, minTrain = 6) {
        const list = chrono(games).filter(g => g.players.length);
        let correct = 0, total = 0, absErr = 0;
        const calls = [];
        for (let i = minTrain; i < list.length; i++) {
            const g = list[i];
            const model = fit(chrono(list.slice(0, i)));
            const t1 = g.players.filter(p => p.team === 1).map(p => p.id);
            const t2 = g.players.filter(p => p.team === 2).map(p => p.id);
            const { margin, p1 } = predict(model, t1, t2);
            const actual = g.final[0] - g.final[1];
            absErr += Math.abs(actual - margin);
            if (actual !== 0 && Math.abs(margin) >= 0.05) {
                total++;
                if (Math.sign(actual) === Math.sign(margin)) correct++;
            }
            calls.push({ game: g, margin, p1, actual });
        }
        return { correct, total, mae: calls.length ? absErr / calls.length : null, calls: calls.reverse() };
    }

    // Most even split of the players who are in. players: [{ id, team: '1'|'2', pinned }]
    // Pinned players keep their team. Among equally even splits, prefer the fewest moves.
    function balance(model, players) {
        const pinned1 = players.filter(p => p.pinned && p.team === '1');
        const pinned2 = players.filter(p => p.pinned && p.team === '2');
        const free = players.filter(p => !p.pinned);
        const n = players.length;
        const sizes = n % 2 ? [Math.floor(n / 2), Math.ceil(n / 2)] : [n / 2];
        const imp = free.map(p => model.impact(p.id));
        const base = pinned1.reduce((s, p) => s + model.impact(p.id), 0) - pinned2.reduce((s, p) => s + model.impact(p.id), 0);
        const total = imp.reduce((s, v) => s + v, 0);

        let best = null;
        const consider = (mask, count1) => {
            const size1 = pinned1.length + count1;
            if (!sizes.includes(size1)) return;
            let s1 = 0, moves = 0;
            for (let i = 0; i < free.length; i++) {
                const on1 = (mask >> i) & 1;
                if (on1) s1 += imp[i];
                if ((on1 ? '1' : '2') !== free[i].team) moves++;
            }
            const margin = base + s1 - (total - s1) + model.size * (size1 - (n - size1));
            const score = Math.round(Math.abs(margin) * 10);
            if (!best || score < best.score || (score === best.score && moves < best.moves)) {
                best = { score, margin, moves, mask };
            }
        };

        if (free.length <= 20) {
            for (let mask = 0; mask < (1 << free.length); mask++) {
                let c = 0;
                for (let m = mask; m; m &= m - 1) c++;
                consider(mask, c);
            }
        } else {
            // Too many to enumerate: random restarts with greedy swaps
            for (let t = 0; t < 400; t++) {
                let mask = 0, c = 0;
                const want = sizes[0] - pinned1.length;
                const order = free.map((_, i) => i).sort(() => Math.random() - 0.5);
                for (const i of order.slice(0, want)) { mask |= 1 << i; c++; }
                consider(mask, c);
            }
        }
        if (!best) return null;
        const teams = new Map(players.filter(p => p.pinned).map(p => [key(p.id), p.team]));
        free.forEach((p, i) => teams.set(key(p.id), (best.mask >> i) & 1 ? '1' : '2'));
        return { teams, margin: best.margin, p1: chance(best.margin, model.sigma), moves: best.moves };
    }

    // ---- pairs ----

    // Teammates (same team) and matchups (opposite teams) for every pair of players.
    function pairs(games) {
        const together = new Map();
        const against = new Map();
        const names = new Map();
        for (const g of games) {
            const ps = g.players;
            ps.forEach(p => names.set(key(p.id), p.name));
            for (let i = 0; i < ps.length; i++) {
                for (let j = i + 1; j < ps.length; j++) {
                    let [a, b] = [ps[i], ps[j]];
                    if (key(a.id) > key(b.id)) [a, b] = [b, a];
                    const k = key(a.id) + '|' + key(b.id);
                    const diffA = a.team === 1 ? g.final[0] - g.final[1] : g.final[1] - g.final[0];
                    const map = a.team === b.team ? together : against;
                    const row = map.get(k) || { a: key(a.id), b: key(b.id), gp: 0, wins: 0, losses: 0, diff: 0 };
                    row.gp++;
                    // For teammates: their shared result. For opponents: from a's side.
                    if (diffA > 0) row.wins++; else if (diffA < 0) row.losses++;
                    row.diff += diffA;
                    map.set(k, row);
                }
            }
        }
        const finish = r => ({ ...r, aName: names.get(r.a), bName: names.get(r.b), avg: r.diff / r.gp, winPct: (r.wins + (r.gp - r.wins - r.losses) / 2) / r.gp });
        return { together: [...together.values()].map(finish), against: [...against.values()].map(finish) };
    }

    // Best duos: high average margin together, with a sample-size penalty so 1-0 doesn't top the list
    function duos(games, minGames = 3) {
        return pairs(games).together
            .filter(r => r.gp >= minGames)
            .map(r => ({ ...r, score: r.diff / (r.gp + 2) }))
            .sort((x, y) => y.score - x.score);
    }

    // Most lopsided head-to-head matchups between two players on opposite teams
    function rivalries(games, minGames = 3) {
        return pairs(games).against
            .filter(r => r.gp >= minGames)
            .map(r => {
                // present from the winning side
                const flip = r.wins < r.losses || (r.wins === r.losses && r.diff < 0);
                return flip
                    ? { ...r, a: r.b, b: r.a, aName: r.bName, bName: r.aName, wins: r.losses, losses: r.wins, diff: -r.diff, avg: -r.avg }
                    : r;
            })
            .map(r => ({ ...r, score: (r.wins - r.losses) / Math.sqrt(r.gp) }))
            .sort((x, y) => y.score - x.score || y.gp - x.gp);
    }

    // For one player: teammates and opponents sorted by results
    function playerPairs(games, id, minGames = 2) {
        const k = key(id);
        const { together, against } = pairs(games);
        const side = r => {
            const mine = r.a === k;
            return mine ? r : { ...r, a: r.b, b: r.a, aName: r.bName, bName: r.aName, wins: r.losses, losses: r.wins, diff: -r.diff, avg: -r.avg };
        };
        const withMe = together.filter(r => r.a === k || r.b === k).map(r => {
            const s = r.a === k ? r : { ...r, a: r.b, b: r.a, aName: r.bName, bName: r.aName };
            return s; // teammates share one result, no flip needed
        }).filter(r => r.gp >= minGames);
        const vsMe = against.filter(r => r.a === k || r.b === k).map(side).filter(r => r.gp >= minGames);
        const byAvg = (x, y) => y.avg - x.avg || y.gp - x.gp;
        return { teammates: withMe.sort(byAvg), opponents: vsMe.sort(byAvg) };
    }

    // ---- scoring (player-tracked games only) ----

    // Walk a game's events, tagging score events with the quarter they happened in.
    function scoringEvents(g) {
        let q = 1;
        const out = [];
        for (const e of g.events) {
            if (e.type === 'period_end') { q++; continue; }
            if (e.type === 'score' && e.playerId != null) out.push({ ...e, quarter: Math.min(q, 4), delta: e.action === 'decrement' ? -1 : 1 });
        }
        return out;
    }

    // Longest stretch of consecutive points by one team, from the play-by-play
    function runs(g) {
        const seq = g.scoreEvents.filter(e => e.type === 'score');
        if (seq.length < 6) return null;
        let prev = [0, 0], cur = { team: 0, n: 0 }, best = { team: 0, n: 0, at: null };
        for (const e of seq) {
            const s = [Number(e.scores[0]) || 0, Number(e.scores[1]) || 0];
            const d = [s[0] - prev[0], s[1] - prev[1]];
            prev = s;
            const team = d[0] > 0 && d[1] <= 0 ? 1 : d[1] > 0 && d[0] <= 0 ? 2 : 0;
            if (!team) { cur = { team: 0, n: 0 }; continue; }
            cur = cur.team === team ? { team, n: cur.n + d[team - 1] } : { team, n: d[team - 1] };
            if (cur.n > best.n) best = { team, n: cur.n, at: s };
        }
        return best.n ? best : null;
    }

    function scoring(games) {
        const rows = new Map();
        for (const g of games) {
            if (!g.tracked) continue;
            const evs = scoringEvents(g);
            const byQuarter = new Map();
            evs.forEach(e => {
                const k = key(e.playerId);
                const arr = byQuarter.get(k) || [0, 0, 0, 0];
                arr[e.quarter - 1] += e.delta;
                byQuarter.set(k, arr);
            });

            // Longest streak of consecutive team points by the same player
            const streak = new Map();
            const last = { 1: null, 2: null };
            for (const e of evs) {
                if (e.delta < 0) continue;
                const t = e.team === 2 ? 2 : 1;
                const k = key(e.playerId);
                last[t] = last[t]?.id === k ? { id: k, n: last[t].n + 1 } : { id: k, n: 1 };
                streak.set(k, Math.max(streak.get(k) || 0, last[t].n));
            }

            for (const p of g.players) {
                const k = key(p.id);
                const r = rows.get(k) || { id: k, name: p.name, games: 0, points: [], share: 0, quarters: [0, 0, 0, 0], qGames: 0, streak: 0 };
                r.name = p.name;
                r.games++;
                r.points.push(p.points);
                const teamPts = g.final[p.team - 1];
                r.share += teamPts ? p.points / teamPts : 0;
                const q = byQuarter.get(k);
                if (evs.length) {
                    r.qGames++;
                    if (q) q.forEach((v, i) => r.quarters[i] += v);
                }
                r.streak = Math.max(r.streak, streak.get(k) || 0);
                rows.set(k, r);
            }
        }
        return [...rows.values()].map(r => {
            const mean = r.points.reduce((s, v) => s + v, 0) / r.games;
            const sd = Math.sqrt(r.points.reduce((s, v) => s + (v - mean) ** 2, 0) / r.games);
            const qTotal = r.quarters.reduce((s, v) => s + v, 0);
            return {
                ...r,
                ppg: mean,
                sd,
                share: r.share / r.games,
                q4Share: qTotal > 0 ? r.quarters[3] / qTotal : null,
                quarterAvg: r.qGames ? r.quarters.map(v => v / r.qGames) : null
            };
        });
    }

    // ---- attendance ----

    function attendance(games) {
        const rows = new Map();
        games.forEach((g, i) => {
            for (const p of g.players) {
                const k = key(p.id);
                const r = rows.get(k) || { id: k, name: p.name, gp: 0, streak: 0, streakOpen: true, lastIndex: i };
                r.gp++;
                // games are newest first: the streak counts back from the latest game
                if (r.streakOpen && i === r.streak) r.streak++; else r.streakOpen = false;
                rows.set(k, r);
            }
        });
        return [...rows.values()].map(r => ({ ...r, pct: r.gp / games.length }))
            .sort((a, b) => b.gp - a.gp || b.streak - a.streak);
    }

    // ---- league trends ----

    function trend(games) {
        return chrono(games).map(g => ({ game: g, date: g.date, total: g.final[0] + g.final[1], margin: g.margin }));
    }

    // Share of games by final margin
    function closeness(games) {
        const buckets = [
            { label: '1–2 pts', test: m => m <= 2 },
            { label: '3–5 pts', test: m => m >= 3 && m <= 5 },
            { label: '6–9 pts', test: m => m >= 6 && m <= 9 },
            { label: '10+ pts', test: m => m >= 10 }
        ].map(b => ({ ...b, count: 0 }));
        let ties = 0;
        for (const g of games) {
            if (!g.margin) { ties++; continue; }
            buckets.find(b => b.test(g.margin)).count++;
        }
        return { buckets, ties, total: games.length };
    }

    return { fit, predict, chance, backtest, balance, pairs, duos, rivalries, playerPairs, runs, scoring, attendance, trend, closeness };
})();
