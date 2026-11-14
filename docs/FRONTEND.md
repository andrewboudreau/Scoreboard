# Frontend Architecture

How the browser side of the MDL Scoreboard is put together: pages, the `app.js` classes, how data reaches the server, and the stats pages. For the module/host loading system see the README.

Everything lives in `src/Scoreboard/wwwroot/` as plain HTML/CSS/JS. No build step, no bundler, no framework.

---

## Pages

| File | URL | Purpose |
|------|-----|---------|
| `index.html` + `app.js` + `styles.css` | `/Scoreboard/` | The scoreboard (scores, timer, teams panel, settings) |
| `stats.html` + `stats-core.js` + `insights.js` + `stats.css` | `/Scoreboard/stats` | Stats dashboard: overview, players, insights, game history |
| `game.html` + `stats-core.js` + `insights.js` + `stats.css` | `/Scoreboard/game?id={gameId}` | One game: result, quarters, chart, box score, play-by-play |
| `docs.html` | `/Scoreboard/docs` | User-facing docs (markdown rendered with marked.js) |

`/Scoreboard/ManagePlayers` redirects to `/Scoreboard/#teams`, which opens the teams panel.

HTML pages are embedded resources served by `Page()` in `ScoreboardModule.cs` with `Cache-Control: no-cache`. It replaces `{{v}}` with a per-build id, so `app.js?v={{v}}` and friends are cache-busted on every deploy. Static assets (JS, CSS, icons, audio) are served from `/_content/Scoreboard/`.

---

## Data Model

The server is the single source of truth. The browser reads and writes whole JSON documents through `/Scoreboard/api` (see `ScoreboardApi.cs`):

| Document | Endpoint | Shape |
|----------|----------|-------|
| Roster | `GET/PUT roster` | `{ team1Name, team2Name, players: [{ id, name, team, active, imageUrl? }] }` |
| Current game pointer | `PUT current-game` | `{ gameId }` (`GET` returns the game document it points to, or 204) |
| Game | `GET/PUT games/{id}` | see below |
| All games | `GET games` | array of game documents |

Teams are **BLACK** and **WHITE**, stored as `'1'` and `'2'`. A player is on team `'1'`, team `'2'`, or **out** (`active: false`; their last team is remembered).

A game document (example: `tests/Scoreboard.Tests.Playwright/Fixtures/full-game-state.json`):

```json
{
  "id": "game-2026-11-14-970d",
  "version": 1,
  "weekDate": "2026-11-14",
  "team1": { "name": "BLACK", "score": 44 },
  "team2": { "name": "WHITE", "score": 42 },
  "period": 5,
  "periodScores": ["20 - 21", "34 - 30", "44 - 42"],
  "timerMinutes": 5,
  "players": [{ "id": 1794637552563, "name": "Adams", "team": "1", "active": true, "points": 9 }],
  "events": [
    { "ts": "...", "type": "score", "team": 1, "player": "Adams", "playerId": 1794637552563,
      "action": "increment", "scores": [1, 0], "timer": 300 },
    { "ts": "...", "type": "score_set", "scores": [20, 21] },
    { "ts": "...", "type": "period_end", "scores": [20, 21], "period": 1 }
  ],
  "lastUpdated": "2026-11-14T06:25:57.002Z"
}
```

- `periodScores` holds cumulative `"a - b"` strings, one per ended quarter.
- `events` is the audit trail: `score` (a tap; `player`/`playerId` are null for a team-score tap), `score_set` (score typed in), `period_end`.
- Game ids are `game-YYYY-MM-DD-xxxx`; the date in the id is the game date, so re-saving an old game never moves it.
- Player points belong to the game, not the roster.

---

## app.js

### Classes

```
ScoreboardApp   coordinator: state, server refresh, game saving
  |-- Sync      debounced PUTs with retry (owned via app.sync)
  |-- Timer     countdown clock, buzzer
  |-- Teams     team scores and names
  |-- Settings  settings panel, quarter score entry
  |-- Players   teams panel + tappable player lists on the scoreboard
  |-- UI        fullscreen, period chips, font sizes, sync badge
```

A small `api` object wraps `fetch`: `api.get(path)` returns parsed JSON or `null` on 204/404; `api.put(path, data)` returns `true`/`false` and never throws. The app instance is on `window.scoreboardApp`.

### Sync

`Sync.save(path, build, delayMs = 1000)` queues a write. `build()` runs at send time, so the latest state goes out. Each path has its own debounce timer.

- A failed PUT stays pending and is retried on `online`, on the tab becoming visible, and every 60s. `pagehide` flushes with `keepalive`.
- A per-path generation counter keeps a change made during an upload from being dropped.
- `busy` is true while anything is pending. `changes` counts local edits, so a server refresh can tell it raced with one.
- `onStatus(false)` shows the **"Not saved yet, retrying…"** badge (`#sync-status`); it hides once everything is saved.

### Server refresh

`ScoreboardApp.refresh()` runs on load, when the tab becomes visible, on `online`, and every 15s while visible. It:

1. Flushes pending saves; skips the refresh if any are still pending.
2. Fetches `roster` and `current-game` in parallel.
3. On the first load only, runs `migrateDeviceData()`.
4. Discards the result if a local change happened during the fetch.
5. `applyServerState()` applies roster, team names, scores, periods, events and player points (no-op if nothing changed).

Until the first successful load (`loaded`), `saveRoster()` refuses to write, so a fresh device can never overwrite the shared roster with an empty one.

### Device data migration

Older versions kept data in the browser. On first load, if any of `playersList`, `gameEvents`, `currentGameId`, `scoreHistory`, `groupCode`, `lastUploadAttempt` exist in localStorage:

- `playersList` is uploaded as the roster **only** if the server roster is still the seeded default (`seeded: true`).
- `gameEvents` (or `scoreHistory`) + `currentGameId` are rebuilt into a game document and uploaded if that game isn't on the server yet; it becomes the current game if there is none.
- If every upload succeeded, all those keys are removed.

### Saving the game

- `recordScoreChange()`, `captureScore()`, `correctPeriodScore()`, `setScores()` all end in `syncGame()`, which saves `games/{id}` and the `current-game` pointer (3s debounce).
- `startNewGame()` (the **New Game** button) saves the old game under its own id (only if it has events), makes a new id, zeroes scores/points/periods/events, and saves immediately. The old game stays in history.
- `shareGame()` (**Copy Results Link**) forces a save and copies `/Scoreboard/game?id={gameId}`.

### Timer

- Tap: start/stop. Double tap (< 300ms): reset to the configured minutes.
- Starting the timer ends "post-period" mode and shows a 2s green glow; pressing shows a brief glow.
- At zero (or **Test Timer End**): `captureScore()`, play `audio/buzzer.mp3`, blink red 5x, reset.

### Scoring and periods

- Tap a team score: +1. Long-press (800ms, touch): −1, floored at 0. The −1 buttons in Settings do the same.
- Tap a player on the scoreboard: +1 to the player and their team (event carries `player` and `playerId`).
- `captureScore()` pushes `"a - b"` to `periodScores`, appends `period_end`, and enters **post-period** mode: until the timer is started again, further score changes also update the last period (the active chip is highlighted).
- Tap a period chip under the timer to overwrite that period with the current score (also updates the matching `period_end` event).
- **Clear Periods** empties `periodScores` and `events` (scores are kept).

### Quarter score entry (Settings)

For games where only team scores are kept. Two number inputs, prefilled with the current score when Settings opens:

- **End Q*n* at this score** — `endQuarterAt(a, b)`: sets the score (`score_set` event) then captures the period.
- **Just set the score** — `setScores(a, b)`: records `score_set`; during post-period it also corrects the last quarter.

### Teams panel (Players class)

Opened with the people icon (or `#teams` in the URL). Full screen on phones (≤ 700px).

- Header: count chips for BLACK / WHITE / Out (highlighted when teams differ by more than one), and **Shuffle**, **All out**, **Share**, **Edit**, **Done**.
- Each roster row (alphabetical, stable order): 📌 pin, name, −/points/+ (when in), and a **BLACK / Out / WHITE** toggle.
- **Add a player** goes on the smaller team.
- **Prediction** line under the counts (`#team-prediction`): "BLACK by 3 · 64% to win" from the history model (`Insights.fit`, needs 3+ games, cached 10 min); highlighted when the gap is 4+ points.
- **Balance**: searches every split of the unpinned players for the most even predicted game, preferring the fewest moves; does nothing if it would gain less than half a point ("Already even"). Without enough history it falls back to Shuffle.
- **Shuffle**: Fisher–Yates over everyone who's in and not pinned, filling the smaller team first (random on a tie). Pins are per device and not saved.
- **All out**: everyone out, pins cleared — start of the week, then tap in whoever shows up.
- **Share**: chat-ready list (`🏀 Teams for Sat, Nov 14`, then each team with a count). Share sheet on touch devices, clipboard elsewhere.
- **Edit** mode: per-row **Photo** (POST `players/{id}/image`; server crops to 96×96 PNG), **Rename**, **Delete**.

Every roster change calls `saveRoster()` (1s debounce) and re-renders the panel and the scoreboard lists. Team names edited on the scoreboard (`contenteditable`) or in Settings are saved with the roster.

### Settings panel

Quarter score entry, timer minutes, team names with −1 buttons, **New Game**, **Clear Periods**, **Test Buzzer**, **Test Timer End**, font/button size sliders, **Keep screen on**, **Copy Results Link**, **Stats & History**. Clicking outside closes it.

### localStorage

Only device preferences, restored by `loadSavedSettings()`:

| Key | Meaning |
|-----|---------|
| `scoreFontSize` | Score font size (rem, slider 5–25) |
| `timerFontSize` | Timer font size (rem, slider 6–30) |
| `playerButtonSize` | Player button padding (px, slider 10–50), applied via `--player-button-padding` |
| `keepScreenOn` | `"true"`/`"false"`; Screen Wake Lock, re-acquired when the tab becomes visible |

`stats.html` also stores the selected range in `statsRange`.

---

## Layout (index.html + styles.css)

```
body
  #sync-status                 fixed badge, top center
  .scoreboard                  relative, full viewport
    #team1-players-display     absolute, left  (BLACK players)
    #team2-players-display     absolute, right (WHITE players)
    .scores-container          z-index 1, pointer-events: none
    .timer-container           z-index 1, pointer-events: none (timer + period chips)
    .control-buttons           bottom-right, z-index 100 (teams, settings, fullscreen)
    #players-panel.side-panel  teams panel
    #settings-panel.side-panel
```

The score and timer containers sit above the player lists but pass clicks through (`pointer-events: none`); `.score`, `.team-name`, `.timer` and `.period-score` turn `pointer-events` back on. Player buttons show an avatar (or initial) and name, with points on the inner side (right for BLACK, left for WHITE).

Breakpoints: ≤ 768px shrinks scores/timer; landscape with height ≤ 600px stacks the teams; ≤ 700px makes the teams panel full screen and pins Settings above the control buttons.

---

## Stats pages

`stats.html` and `game.html` share `stats-core.js` (global `Stats`), `insights.js` (global `Insights`) and `stats.css`. Neither uses `app.js`; the scoreboard page loads `stats-core.js` and `insights.js` for the Teams panel prediction.

### stats-core.js

- `Stats.normalize(raw)` turns any stored game into one shape: `date`, `names`, `final`, `winner`, `margin`, `quarters` (per-quarter points and cumulative totals, from `period_end` events or `periodScores`), `unfinished` (score moved after the last quarter), `players` (in, on a team), `tracked`, `coverage`, `unattributed` (per-team points not credited to a player), `scoreEvents`.
  It handles **fully tracked** games (every basket credited), **partially tracked** games (some points uncredited), and **team-only** games (just quarter scores; `tracked` is false).
  A game counts as `tracked` only when at least half its points are credited to players; below that it is `partial` and season player stats skip it. If `players[].points` are all zero, points are rebuilt from credited `score` events.
  `names` is always `['BLACK', 'WHITE']`; whatever was typed is kept in `recordedNames` and listed in the Overview's **Team names** card.
- `Stats.loadGames()` fetches `/Scoreboard/api/games`, normalizes, drops unplayed games (`isPlayed`), sorts newest first.
- `summarize(games)`: wins, ties, points, closest/biggest game, current and longest win streaks, other team names typed, comebacks (won after trailing at half), per-quarter averages and quarters won.
- `players(games)`: per player GP, W-L-T, win %, average +/- (team margin) from every game; PPG and high only from tracked games; a per-game log.
- `flow(g)`: lead changes, ties, biggest lead per team. `chartSvg(g)`: score-over-time SVG (per event, or per quarter for team-only games).
- Helpers: `esc`, `formatDate`, `formatTime`, `signed`, `teamChip`, `shareText`, `share`.

### insights.js

Global `Insights`, used by `stats.html`, `game.html` and the Teams panel. Pure functions over normalized games.

- `fit(games)`: weighted ridge regression on game margins ("adjusted plus-minus"). Each player gets an **impact** in points per game, controlling for teammates and opponents; recent games count more (half-life 25 games), margins are capped at 15, and a team-size term absorbs 5-on-4 games. Works for team-only games because it only needs who played and the final score.
- `predict(model, ids1, ids2)` → `{ margin, p1 }`; `chance` turns a margin into a win probability using the model's residual spread.
- `backtest(games)`: predicts each game from only the games before it, so the page can say honestly how often the model picks the winner.
- `balance(model, players)`: exhaustive split search (up to 20 free players) that respects pins.
- `duos`, `rivalries`, `playerPairs(games, id)` (best teammates / toughest opponents), `scoring` (PPG, consistency, share of team points, 4th-quarter share — tracked games only), `attendance`, `trend` (points per game over time), `closeness` (margin buckets), `runs(g)` (longest scoring run in a game).

### stats.html

Tabs (`#overview`, `#players`, `#insights`, `#games`, kept in the URL hash) under a range filter (Last 10 / This year / All time).

- **Overview**: head-to-head record bar with streak, **Next game** prediction for the current roster, tiles (avg score, avg margin, closest game, biggest win), comebacks, by-quarter table.
- **Players**: sortable table with an Impact column, and a "Showing up" attendance card. Tap a row for a sheet: record, impact rank, PPG, attendance, scoring by quarter, best teammates, toughest opponents, game log.
- **Insights**: player impact diverging bars with the back-test result, best duos, rivalries, scoring table, points-per-game trend chart (tap for values), and how close the games are.
- **Games**: history grouped by month with a "Player stats" / "Team scores" badge.

The model and back-test always use every game; the range filter only changes what's summarized.

### game.html

Loads `games/{id}` and `current-game` together. Shows a hero scoreline with **Share result**, a quarters table (with a "Now" column if unfinished), the score chart with lead changes / biggest leads / longest scoring run, a box score (or **Who played** for team-only games, with "Not credited" for uncredited points), and a collapsible play-by-play.

If the game is the current game and was updated in the last 3 hours it shows **LIVE** and reloads every 15s.

---

## Tests

`tests/Scoreboard.Tests.Playwright/` (NUnit + Playwright, page object `ScoreboardPage.cs`). `FakeApi.cs` intercepts `/Scoreboard/api/**` and serves documents from memory, so tests never touch real storage. The host still has to be running to serve the pages.
