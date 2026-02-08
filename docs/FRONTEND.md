# Frontend Architecture

This document describes the layout, JavaScript class structure, user interactions, persistence, and page-specific details of the MDL Scoreboard frontend. It does not cover the ASP.NET module/host loading system.

The frontend is a set of static HTML/CSS/JS files served from `src/Scoreboard/wwwroot/`. There is no build step, no bundler, and no framework -- everything is vanilla JavaScript with ES6 classes.

---

## Pages Overview

| File | URL Path | Purpose |
|------|----------|---------|
| `index.html` | `/Scoreboard/` | Main scoreboard SPA |
| `game.html` | `/Scoreboard/Game?s={code}` | Public shared game results page |
| `stats.html` | `/Scoreboard/Stats` | Game history list and detail viewer |
| `manage-players.html` | `/Scoreboard/ManagePlayers` | Default player CRUD with drag-and-drop |

`index.html` loads `app.js` and `styles.css` as external files. The other three pages are self-contained -- each has its own inline `<style>` and `<script>` blocks with no shared dependencies.

---

## 1. Layout Architecture (index.html + styles.css)

### Root Structure

The body is a full-viewport flex container (`100vw x 100vh`, `overflow: hidden`) with a black background. Everything lives inside a single `.scoreboard` div that fills the viewport and uses `position: relative` as the positioning context for absolutely-placed children.

```
body (flex column, centered)
  .scoreboard (relative, full height, max-width 1200px)
    .team-players-list#team1-players-display  (absolute, left)
    .team-players-list#team2-players-display  (absolute, right)
    .scores-container                         (z-index: 1, pointer-events: none)
      .scores (flex row)
        .team (team name + score)
        .team (team name + score)
    .timer-container                          (z-index: 1, pointer-events: none)
      .timer
      .score-history
    .control-buttons                          (absolute, bottom-right, z-index: 100)
    #players-panel.side-panel                 (absolute, bottom-right, z-index: 99)
    #settings-panel.side-panel                (absolute, bottom-right, z-index: 99)
```

### Layering and Pointer-Events Pass-Through

The layout uses a deliberate z-index and `pointer-events` strategy to allow the player lists to sit behind the scores while remaining clickable in their own areas:

- **`.scores-container`** and **`.timer-container`** have `z-index: 1` and `pointer-events: none`. This makes the containers visually above the player lists but transparent to clicks.
- **`.score`**, **`.team-name`**, **`.timer`**, and **`.period-score`** elements re-enable `pointer-events: auto` so they remain tappable despite their parent having `pointer-events: none`.
- **`.team-players-list`** elements are `position: absolute` with `top: 10px; bottom: 10px`, pinned to the left and right edges (width 250px each). They have no explicit z-index, so they render below the z-index: 1 score/timer containers but remain interactive in the areas not occluded.
- **`.control-buttons`** sits at `z-index: 100` (absolute, bottom-right) to always be on top.
- **`.side-panel`** sits at `z-index: 99` (absolute, bottom-right, hidden by default, shown via `.active` class).

### Frosted Glass Effect

The `.score` and `.timer` elements use a frosted glass aesthetic:

```css
.score {
    background: rgba(30, 30, 30, 0.85);
    backdrop-filter: blur(6px);
    -webkit-backdrop-filter: blur(6px);
    border: 1px solid rgba(255, 255, 255, 0.06);
    border-radius: 12px;
}

.timer {
    background: radial-gradient(ellipse at center, rgba(15, 15, 15, 0.7) 0%, rgba(0, 0, 0, 0.5) 100%);
    backdrop-filter: blur(6px);
    -webkit-backdrop-filter: blur(6px);
    text-shadow: 0 0 60px rgba(255,255,255,0.15), ...;
    border: 1px solid rgba(255, 255, 255, 0.06);
}
```

This creates semi-transparent dark panels that blur any player list content showing through behind them.

### Player List Panels (On-Screen)

The `.team-players-list` containers have a semi-transparent dark background (`rgba(51,51,51,0.7)`), border-radius, and `overflow-y: auto` for scrollable player lists. Each player button (`.player-item-display`) has a colored left border to indicate team membership: blue (`#007bff`) for team 1, red (`#dc3545`) for team 2.

---

## 2. JavaScript Class Architecture (app.js)

All JavaScript for the main scoreboard page lives in `app.js`, organized into seven classes. The `ScoreboardApp` class is the central coordinator, and every other class receives a reference to it via its constructor.

### Class Dependency Diagram

```
ScoreboardApp (central coordinator)
  |-- Timer          (countdown clock logic)
  |-- Teams          (score state + score DOM)
  |-- Players        (player list management + side panel DOM)
  |-- Settings       (settings panel DOM + event wiring)
  |-- UI             (fullscreen, font size updates, score history display)
  |-- BlobSync       (Azure Blob Storage read/write via SAS URLs)
  |-- SyncManager    (dirty-flag tracking, periodic/online/visibility sync)
```

### Initialization Flow

```
DOMContentLoaded
  -> new ScoreboardApp()
       -> new Timer(this)         // binds to #timer, sets up click handler
       -> new Teams(this)         // binds to .score elements, sets up click/touch
       -> new Players(this)       // binds to players panel, builds display
       -> new Settings(this)      // binds to settings panel, all sliders/buttons
       -> new UI(this)            // binds fullscreen button
       -> new BlobSync(this)      // no DOM, pure data layer
       -> new SyncManager(this)   // sets up online/visibility/periodic listeners
       -> loadSavedSettings()     // restores localStorage state
       -> initGroupConnection()   // auto-joins group from URL param or localStorage
```

The app instance is stored on `window.scoreboardApp` for debugging access.

### ScoreboardApp

The central coordinator that owns all component instances and global state:

- **`periodScores`** -- array of strings like `"3 - 5"` capturing the score at each period end.
- **`events`** -- array of event objects (score changes and period endings) with timestamps.
- **`playersList`** -- array of player objects `{ id, name, team, active, points }`.
- **`currentShareCode`** -- the most recent share code for the current game.
- **`wakeLock`** -- reference to the active `WakeLockSentinel`, if any.

Key methods:
- `loadSavedSettings()` -- restores font sizes, player button size, wake lock, players list, and game events from localStorage.
- `initGroupConnection()` -- checks URL query `?code=`, URL hash `#code`, or `localStorage.groupCode` and auto-joins the group.
- `loadGroupData()` -- downloads `roster.json` and today's game state from blob storage.
- `syncGame()` -- builds a consolidated game state object and does a debounced upload to blob.
- `captureScore()` -- records end-of-period event, pushes to `periodScores`, triggers sync.
- `recordScoreChange(teamNumber, isIncrement, playerName)` -- appends a score event, persists to localStorage, triggers sync.
- `shareGame()` -- uploads current game state, calls the share API, copies the share URL to clipboard.
- `requestWakeLock()` / `releaseWakeLock()` / `toggleWakeLock()` -- Screen Wake Lock API management.

### Timer

Manages the countdown timer with `setInterval` at 1-second ticks.

**State:** `timeLeft` (seconds), `isRunning`, `interval` (interval ID), `lastTap` (timestamp for double-tap detection).

**Key behaviors:**
- Single tap on timer -> start/stop.
- Double tap on timer (< 300ms between taps) -> reset to configured minutes.
- When timer reaches zero: calls `app.captureScore()`, plays the buzzer audio, resets to configured time.
- `playAlarm()` -- plays `buzzer.mp3` and blinks the timer background red 5 times at 250ms intervals.
- `simulateTimerEnd()` -- testing shortcut that triggers the end-of-timer sequence without waiting.

### Teams

Manages team scores and the score/name DOM elements.

**State:** `team1Points`, `team2Points`.

**Key behaviors:**
- Click on a `.score` element -> increment that team's score by 1.
- Long press (touchstart + 800ms timeout) on a `.score` element -> decrement that team's score by 1 (touch devices only).
- `contenteditable` team name divs sync bidirectionally with the settings panel text inputs.
- All score changes call `app.recordScoreChange()` to log the event.

### Players

Manages the player panel (table inside `#players-panel`) and the on-screen player button lists (`#team1-players-display`, `#team2-players-display`).

**Two distinct UI areas:**
1. **Side panel table** (`updatePlayersList()`) -- full CRUD table with columns: Name, Team (dropdown), Active (checkbox), Points (+/- buttons), Remove button. Sorted by team, then active status, then name.
2. **On-screen player buttons** (`updatePlayersDisplay()`) -- simplified buttons on the left/right edges showing only active players, sorted by name. Tapping a player button increments their points and their team's score simultaneously.

**Player data model:**
```js
{ id: Number, name: String, team: "1"|"2", active: Boolean, points: Number }
```

**Key methods:**
- `addPlayer()` -- creates player with `Date.now()` as ID, defaults to team 1, active.
- `removePlayer(id)` -- splices from array, saves, re-renders.
- `incrementPlayerPoints(id)` -- increments player points AND the corresponding team score, records event with player name attribution.
- `savePlayersState()` -- writes to localStorage and does a debounced blob upload of `roster.json`.
- `exportPlayers()` -- copies JSON to clipboard.
- `importPlayers()` -- prompts for JSON paste, validates, replaces player list.
- `loadDefaultPlayers()` -- fetches from `/Scoreboard/api/default-players`, replaces player list.

### Settings

Manages the settings panel and wires up all settings-related event listeners.

**Responsibilities:**
- Timer configuration (set minutes).
- Team name text inputs (bidirectional sync with contenteditable divs).
- Score decrement buttons (-1 for each team).
- Reset scores / reset history / test buzzer / simulate timer end buttons.
- Font size sliders (score, timer, player button).
- Keep screen on checkbox.
- Group management (create, join, share link, share game, leave).
- Navigate to Manage Default Players page.

**Panel toggle:** Clicking the settings gear icon toggles `.active` class. Clicking outside the panel closes it.

**Group UI updates:** `updateGroupUI()` swaps between the "join" form and the "connected" info display depending on `blobSync.isConnected`.

### UI

A smaller utility class handling:
- **Fullscreen toggle** -- uses the Fullscreen API with vendor-prefixed fallbacks (`webkit`, `ms`).
- **Score history display** -- renders `periodScores` as clickable `.period-score` spans. Clicking a period score calls `app.correctPeriodScore(index)` to overwrite that period's recorded score with the current score values.
- **Font/button size updates** -- applies inline `fontSize` styles and sets the CSS custom property `--player-button-padding`.

### BlobSync

Pure data-layer class for reading/writing JSON blobs to Azure Blob Storage via SAS URLs.

**State:** `groupId`, `groupName`, `isAdmin`, `readUrl`, `writeUrl`, `code`, `saveTimeouts`.

**Key methods:**
- `join(code)` -- calls `/Scoreboard/api/groups/join?code=`, stores SAS URLs and group metadata, persists code to localStorage.
- `createGroup(name)` -- POSTs to `/Scoreboard/api/groups`, then joins with the returned admin code.
- `leave()` -- clears all state and removes `groupCode` from localStorage.
- `download(path)` -- GETs `{containerUrl}/{groupId}/{path}` with SAS token. Auto-refreshes SAS on 403.
- `upload(path, data)` -- PUTs JSON blob with `x-ms-blob-type: BlockBlob` header. Auto-refreshes SAS on 403.
- `debouncedUpload(path, data, delayMs)` -- wraps `upload` with a per-path `setTimeout` to batch rapid changes (default 3s delay, game sync uses 5s).

**SAS URL structure:** The server returns a container-level SAS URL. `_blobUrl()` appends `/{groupId}/{path}` to the container pathname, keeping the query-string SAS token intact.

### SyncManager

Ensures game state reaches blob storage reliably, even across network disruptions.

**Tracks a `dirty` flag.** When dirty and conditions are right, triggers `app.syncGame()`.

**Three sync triggers:**
1. **`online` event** -- when the browser regains connectivity.
2. **`visibilitychange` event** -- when the tab becomes visible again.
3. **Periodic interval** -- every 2 minutes, checks if dirty + online + connected.

---

## 3. User Interactions

### Scoring

| Action | Where | Result |
|--------|-------|--------|
| Tap score number | Main scoreboard | Increment team score by 1 |
| Long press score number (800ms) | Main scoreboard (touch only) | Decrement team score by 1 |
| Tap player button | Left/right player lists | Increment player points + team score |
| Click +/- in player table | Players panel | Increment/decrement player points + team score |
| Click -1 button in settings | Settings panel | Decrement team score |

All scoring actions record an event with timestamp, team, action type, player name (if applicable), current scores, and timer value.

### Timer

| Action | Result |
|--------|--------|
| Single tap timer | Start or stop countdown |
| Double tap timer (< 300ms) | Reset timer to configured minutes |
| Timer reaches zero | Capture period score, play buzzer, blink red, auto-reset |

### Team Names

Team names use `contenteditable="true"` divs in the main display. Editing the team name on the scoreboard updates the corresponding text input in the settings panel, and vice versa.

### Period Score Correction

Clicking a period score chip (e.g., "3 - 5") in the score history bar overwrites that period's recorded scores with the current team scores. This allows correcting mistakes after a period ends.

### Panel Management

- Players button toggles the players panel and closes the settings panel.
- Settings button toggles the settings panel.
- Clicking outside either panel closes it (via document-level click listener checking `contains()`).

---

## 4. Settings and Persistence

### localStorage Keys

| Key | Type | Description |
|-----|------|-------------|
| `scoreFontSize` | string (number) | Score display font size in rem |
| `timerFontSize` | string (number) | Timer display font size in rem |
| `playerButtonSize` | string (number) | Player button padding in px |
| `keepScreenOn` | `"true"` or `"false"` | Whether to request wake lock |
| `playersList` | JSON string | Array of player objects |
| `gameEvents` | JSON string | Array of score/period events |
| `groupCode` | string | Group code for auto-rejoin on page load |

### Font Size and Button Size Sliders

Three range inputs in the settings panel control display sizing:

- **Score Font Size** -- range 5-15rem, step 0.5, default 10rem. Applied via inline `style.fontSize` on all `.score` elements.
- **Timer Font Size** -- range 6-20rem, step 0.5, default 14rem. Applied via inline `style.fontSize` on the `#timer` element.
- **Player Button Size** -- range 10-30px, step 1, default 15px. Applied by setting the CSS custom property `--player-button-padding` on `document.documentElement`. The `.player-item-display` rule uses `padding: var(--player-button-padding, 15px)`.

All three persist to localStorage immediately on slider input.

### Keep Screen On

Uses the Screen Wake Lock API (`navigator.wakeLock.request('screen')`). Re-acquires the lock on `visibilitychange` (when returning to the tab) if the checkbox is still checked.

### Data Migration

On startup, `loadSavedSettings()` migrates the legacy `scoreHistory` localStorage key to `gameEvents`, and cleans up the obsolete `lastUploadAttempt` key.

---

## 5. Player Management (Main Scoreboard)

### Data Flow

```
User action (add/remove/edit/import)
  -> Modify app.playersList in memory
  -> savePlayersState()
       -> localStorage.setItem('playersList', ...)
       -> blobSync.debouncedUpload('roster.json', ...)  [if connected]
  -> updatePlayersList()     [re-render settings panel table]
  -> updatePlayersDisplay()  [re-render on-screen player buttons]
```

### On-Screen Player Buttons

Active players are displayed in two absolutely-positioned panels on the left (team 1) and right (team 2) edges of the scoreboard. Each button shows the player name and their current point total. Tapping a button:

1. Increments the player's `points` property.
2. Increments the corresponding team's score.
3. Records a score event attributed to that player.
4. Saves to localStorage and triggers blob sync.
5. Re-renders both the button display and the panel table.

### Export / Import

- **Export** copies the full `playersList` JSON array to the clipboard.
- **Import** prompts the user to paste JSON, validates structure (requires `name`, `team`, `active` properties), assigns IDs if missing, and replaces the current list.
- **Load Defaults** fetches from `/Scoreboard/api/default-players` and replaces the current list (after confirmation).

---

## 6. Group / Sync System

### Group Lifecycle

1. **Create:** User enters a group name -> POST `/Scoreboard/api/groups` -> receives `adminCode` and `readerCode` -> auto-joins with admin code -> uploads current roster to blob.
2. **Join:** User enters a code (or arrives via `?code=` URL) -> GET `/Scoreboard/api/groups/join?code=` -> receives `groupId`, `groupName`, `isAdmin`, `sasUrls` -> stores code in localStorage -> downloads roster and active game from blob.
3. **Leave:** Clears all BlobSync state and removes `groupCode` from localStorage.

### Sync Architecture

Game state is a single consolidated JSON blob at `games/game-{YYYY-MM-DD}.json` within the group's blob container. It contains:

```json
{
  "id": "game-2024-01-15",
  "version": 1,
  "weekDate": "2024-01-15",
  "team1": { "name": "BLACK", "score": 12 },
  "team2": { "name": "WHITE", "score": 9 },
  "period": 3,
  "periodScores": ["4 - 3", "8 - 6"],
  "timerMinutes": 15,
  "players": [ ... ],
  "events": [ ... ],
  "shareCode": "abc123",
  "lastUpdated": "2024-01-15T20:30:00.000Z"
}
```

Every scoring action calls `syncManager.markDirty()` followed by `syncGame()`. The `syncGame()` method uses `blobSync.debouncedUpload()` with a 5-second delay, so rapid score changes are batched into a single PUT request.

### SAS Token Refresh

If a blob request returns HTTP 403, BlobSync automatically calls `/Scoreboard/api/groups/{groupId}/sas/refresh?code=` to get fresh SAS URLs, then retries the request once.

---

## 7. Game Sharing (game.html)

`game.html` is a standalone public page for viewing shared game results. It has no dependency on `app.js` or `styles.css`.

### Flow

1. Reads the `s` query parameter for the share code.
2. Fetches `/Scoreboard/api/shares/{code}`.
3. Renders the result with inline JavaScript.

### Rendered Sections

- **Final Score** -- team names, scores, date. Winning team's score is highlighted green.
- **Period Breakdown** -- table showing per-period scores parsed from `"X - Y"` strings.
- **Player Stats** -- active players with points > 0, grouped by team, sorted by points descending.
- **Event Timeline** -- chronological list of all score and period events with timestamps and running score.

### Sharing Initiation (from main app)

When the user clicks "Share Game Results" in the settings panel:

1. The current game state is force-uploaded to blob storage (not debounced).
2. POST `/Scoreboard/api/games/share` with `groupId` and `gameId`.
3. The server returns a `shareCode` and `shareUrl`.
4. The share URL is copied to clipboard (or shown in a prompt as fallback).
5. The `shareCode` is stored in the game state for future reference.

---

## 8. Stats Page (stats.html)

`stats.html` is a standalone game history viewer. It has no dependency on `app.js` or `styles.css`.

### Data Loading

1. Reads `groupCode` from localStorage. If absent, shows a "connect to a group" prompt.
2. Joins the group to get SAS URLs.
3. Lists all blobs with prefix `{groupId}/games/` using the Azure Blob Storage REST API (`restype=container&comp=list`).
4. Fetches each game blob in parallel, sorts by `lastUpdated` descending.

### List View

Game cards showing: date, team names and scores (winners highlighted green), period count, duration (calculated from first to last event timestamps), scorer count, event count. Clicking a card navigates to the detail view.

### Detail View

- **Score Progression Graph** -- drawn on a `<canvas>` element using the Canvas 2D API. Plots team 1 scores in blue (`#228be6`) and team 2 scores in pink (`#e64980`) as line graphs. Handles device pixel ratio for sharp rendering. Shows grid lines and a legend.
- **Period Breakdown** -- same table format as game.html.
- **Player Stats** -- includes colored point bars (blue for team 1, pink for team 2) scaled relative to the max scorer.
- **Scoring Timeline** -- similar to game.html's event timeline but with colored team dots and seconds in timestamps.

A "Back to games" button returns to the list view by toggling `display` between `#list-view` and `#detail-view`.

---

## 9. Manage Players Page (manage-players.html)

`manage-players.html` is a standalone admin page for managing the server-side default player list. It has no dependency on `app.js` or `styles.css`.

### Layout

Three columns using a flex container:
- **Team 1** (red header)
- **Team 2** (blue header)
- **No Team** (gray header)

Each column contains a draggable list of player items. Below the columns is an "Add New Player" form and action buttons (Save, Export, Back).

### Drag and Drop

Uses the native HTML5 Drag and Drop API:
- `dragstart` -- stores the dragged player's ID, adds `.dragging` class (rotates the item 5 degrees).
- `dragover` -- prevents default to allow dropping.
- `dragenter`/`dragleave` -- adds/removes `.drag-over` highlight class on team sections.
- `drop` -- calls `movePlayer()` which POSTs to `/Scoreboard/api/default-players/move`.

### API Integration

| Action | Endpoint | Method |
|--------|----------|--------|
| Load players | `/Scoreboard/api/default-players` | GET |
| Add player | `/Scoreboard/api/default-players/add` | POST |
| Move player | `/Scoreboard/api/default-players/move` | POST |
| Delete player | `/Scoreboard/api/default-players/delete` | POST |
| Save all | `/Scoreboard/api/default-players/save` | POST |

The Export button downloads the current player list as a `default-players.json` file using `Blob` + `URL.createObjectURL`.

---

## 10. Responsive Design

### Breakpoints

**Mobile (max-width: 768px):**
```css
.team-name { font-size: 1.5rem; }    /* down from 2rem */
.score { font-size: 5rem; min-width: 120px; padding: 10px 20px; }  /* down from 10rem */
.timer { font-size: 6rem; min-width: 280px; padding: 10px 20px; }  /* down from 14rem */
```

**Landscape on small screens (orientation: landscape, max-height: 600px):**
- `.scores-container` becomes a column layout at 60% width.
- `.scores` switches to column direction.
- Teams stack vertically instead of side-by-side.
- Timer font reduces to 8rem.

### General Viewport Handling

- `user-select: none` on all elements prevents text selection during taps.
- `overflow: hidden` on body prevents any scrolling of the main scoreboard.
- Player lists use `overflow-y: auto` for their own independent scrolling.
- The side panel has `max-height: 80vh` and `overflow-y: auto` so settings/players scroll independently.
- The manage-players page uses `flex-wrap: wrap` on its columns container so it stacks on narrow screens.

---

## 11. CSS Custom Properties

The application uses one CSS custom property:

### `--player-button-padding`

- **Defined on:** `document.documentElement` (`:root`), set via JavaScript.
- **Used by:** `.player-item-display { padding: var(--player-button-padding, 15px); }`
- **Controlled by:** The "Player Button Size" range slider in settings (10-30px range).
- **Persisted in:** `localStorage.playerButtonSize`.

This is the mechanism for dynamically sizing the on-screen player buttons. The fallback value is `15px` when the property is not set. The JavaScript call `document.documentElement.style.setProperty('--player-button-padding', value + 'px')` updates the property, and all player buttons respond immediately without re-rendering.

The score and timer font sizes are applied via direct inline `style.fontSize` assignments rather than CSS custom properties.

---

## Key Files Reference

| File | Absolute Path |
|------|---------------|
| Main HTML | `C:\Users\andre\source\repos\Scoreboard\src\Scoreboard\wwwroot\index.html` |
| Main CSS | `C:\Users\andre\source\repos\Scoreboard\src\Scoreboard\wwwroot\styles.css` |
| Main JS | `C:\Users\andre\source\repos\Scoreboard\src\Scoreboard\wwwroot\app.js` |
| Game Results | `C:\Users\andre\source\repos\Scoreboard\src\Scoreboard\wwwroot\game.html` |
| Stats/History | `C:\Users\andre\source\repos\Scoreboard\src\Scoreboard\wwwroot\stats.html` |
| Manage Players | `C:\Users\andre\source\repos\Scoreboard\src\Scoreboard\wwwroot\manage-players.html` |
