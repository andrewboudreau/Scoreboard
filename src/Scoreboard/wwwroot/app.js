// Server API: the server just stores JSON documents (roster, games, current-game pointer).
const api = {
    async get(path) {
        const res = await fetch(`/Scoreboard/api/${path}`);
        if (res.status === 204 || res.status === 404) return null;
        if (!res.ok) throw new Error(`GET ${path} failed: ${res.status}`);
        return res.json();
    },

    async put(path, data, keepalive = false) {
        try {
            const res = await fetch(`/Scoreboard/api/${path}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data),
                keepalive
            });
            return res.ok;
        } catch {
            return false;
        }
    }
};

// Debounced saves that retry when the device comes back online.
// save(path, build) queues a write; build() runs at send time so the latest state goes out.
class Sync {
    constructor(onStatus = () => {}) {
        this.pending = {};   // path -> build function
        this.gen = {};       // path -> change counter, so a change made during an upload isn't dropped
        this.timers = {};
        this.changes = 0;    // total local changes, so a server refresh can tell if it raced with one
        this.onStatus = onStatus; // called with true (all saved) or false (a save failed, will retry)

        window.addEventListener('online', () => this.flush());
        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible') this.flush();
        });
        // Last chance to send unsaved changes when the tab closes.
        window.addEventListener('pagehide', () => this.flush(true));
        setInterval(() => this.flush(), 60 * 1000);
    }

    get busy() {
        return Object.keys(this.pending).length > 0;
    }

    save(path, build, delayMs = 1000) {
        this.changes++;
        this.pending[path] = build;
        this.gen[path] = (this.gen[path] || 0) + 1;
        clearTimeout(this.timers[path]);
        this.timers[path] = setTimeout(() => this.send(path), delayMs);
    }

    cancel(path) {
        clearTimeout(this.timers[path]);
        delete this.pending[path];
    }

    async send(path, keepalive = false) {
        const build = this.pending[path];
        if (!build) return true;
        clearTimeout(this.timers[path]);
        const gen = this.gen[path];
        const ok = await api.put(path, build(), keepalive);
        if (ok && this.gen[path] === gen) delete this.pending[path];
        if (!ok) this.onStatus(false);
        else if (!this.busy) this.onStatus(true);
        return ok;
    }

    flush(keepalive = false) {
        if (!navigator.onLine) return Promise.resolve();
        return Promise.all(Object.keys(this.pending).map(p => this.send(p, keepalive)));
    }
}

class ScoreboardApp {
    constructor() {
        // Global state (set before components, which read it while rendering)
        this.periodScores = [];
        this.postPeriod = false;
        this.events = [];
        this.wakeLock = null;
        this.playersList = [];
        this.loaded = false;  // true once the shared roster/game came from the server
        this.sync = new Sync(ok => this.ui?.setSyncStatus(ok));

        // Initialize components
        this.timer = new Timer(this);
        this.teams = new Teams(this);
        this.players = new Players(this);
        this.settings = new Settings(this);
        this.ui = new UI(this);

        // Initialize app
        this.loadSavedSettings();
        this.refresh();
        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible') this.refresh();
        });
        window.addEventListener('online', () => this.refresh());
        setInterval(() => {
            if (document.visibilityState === 'visible') this.refresh();
        }, 15 * 1000);
        if (window.location.hash === '#teams') {
            history.replaceState(null, '', window.location.pathname);
            this.players.open();
        }
    }

    // Device preferences (font sizes, screen wake lock). Everything else lives on the server.
    loadSavedSettings() {
        // Font sizes
        if (localStorage.getItem('scoreFontSize')) {
            const savedScoreFontSize = localStorage.getItem('scoreFontSize');
            this.ui.updateScoreFontSize(savedScoreFontSize);
            this.settings.scoreFontSizeSlider.value = savedScoreFontSize;
        }

        if (localStorage.getItem('timerFontSize')) {
            const savedTimerFontSize = localStorage.getItem('timerFontSize');
            this.ui.updateTimerFontSize(savedTimerFontSize);
            this.settings.timerFontSizeSlider.value = savedTimerFontSize;
        }

        if (localStorage.getItem('playerButtonSize')) {
            const savedPlayerButtonSize = localStorage.getItem('playerButtonSize');
            this.ui.updatePlayerButtonSize(savedPlayerButtonSize);
            this.settings.playerButtonSizeSlider.value = savedPlayerButtonSize;
        }

        // Screen wake lock
        if (localStorage.getItem('keepScreenOn') === 'true') {
            this.settings.keepScreenOnCheckbox.checked = true;
            this.requestWakeLock();
        }
    }

    // Pull the shared roster and current game from the server and show them.
    // Runs on load, when the page comes back to the foreground, and every 15s,
    // so every phone/iPad shows the same teams and score.
    async refresh() {
        if (this.refreshing) return;
        this.refreshing = true;
        try {
            await this.sync.flush();
            if (this.sync.busy) return; // our own changes haven't reached the server yet
            const changesBefore = this.sync.changes;

            let [roster, game] = await Promise.all([api.get('roster'), api.get('current-game')]);
            if (!this.loaded) ({ roster, game } = await this.migrateDeviceData(roster, game));

            // A change made on this device while we were fetching wins over what we fetched.
            if (this.sync.changes !== changesBefore) return;

            this.applyServerState(roster, game);
            this.loaded = true;
        } catch (e) {
            console.warn('Could not reach the server', e);
        } finally {
            this.refreshing = false;
        }
    }

    applyServerState(roster, game) {
        const snapshot = JSON.stringify([roster, game]);
        if (snapshot === this.lastServerSnapshot) return;
        this.lastServerSnapshot = snapshot;

        if (roster) {
            this.playersList = (roster.players || []).map(p => ({ ...p, points: 0 }));
            if (roster.team1Name && roster.team2Name) {
                this.teams.setTeamNames(roster.team1Name, roster.team2Name);
            }
        }

        if (game) {
            if (game.id !== this._currentGameId) this.postPeriod = false;
            this._currentGameId = game.id;
            this.teams.team1Points = game.team1?.score || 0;
            this.teams.team2Points = game.team2?.score || 0;
            this.periodScores = game.periodScores || [];
            this.events = game.events || [];

            // Player points belong to the game, not the roster
            const points = new Map((game.players || []).map(p => [p.id, p.points || 0]));
            this.playersList.forEach(p => p.points = points.get(p.id) || 0);
        } else {
            this._currentGameId = null;
            this.teams.team1Points = 0;
            this.teams.team2Points = 0;
            this.periodScores = [];
            this.events = [];
        }

        this.teams.updateScoreDisplay();
        this.ui.updateScoreHistory();
        this.players.render();
    }

    // One-time move of data that older versions kept only in this browser (roster, current game)
    // up to the server. The server copy wins if it already has one.
    async migrateDeviceData(roster, game) {
        const keys = ['playersList', 'gameEvents', 'currentGameId', 'scoreHistory', 'groupCode', 'lastUploadAttempt'];
        if (!keys.some(k => localStorage.getItem(k) !== null)) return { roster, game };

        const read = key => {
            try { return JSON.parse(localStorage.getItem(key)); } catch { return null; }
        };
        const players = Array.isArray(read('playersList')) ? read('playersList') : [];
        const events = read('gameEvents') || read('scoreHistory');
        const gameId = localStorage.getItem('currentGameId');
        let ok = true;

        if (roster?.seeded && players.length > 0) {
            roster = {
                team1Name: roster.team1Name,
                team2Name: roster.team2Name,
                players: players.map(({ points, ...p }) => p)
            };
            ok = await api.put('roster', roster) && ok;
        }

        if (Array.isArray(events) && events.length > 0 && gameId && /^[A-Za-z0-9_-]{1,80}$/.test(gameId)
            && await api.get(`games/${gameId}`) === null) {
            const last = [...events].reverse().find(e => Array.isArray(e.scores)) || { scores: [0, 0] };
            const periods = events.filter(e => e.type === 'period_end').map(e => `${e.scores[0]} - ${e.scores[1]}`);
            const localGame = {
                id: gameId,
                version: 1,
                weekDate: (events[0].ts || new Date().toISOString()).split('T')[0],
                team1: { name: roster?.team1Name || 'BLACK', score: last.scores[0] },
                team2: { name: roster?.team2Name || 'WHITE', score: last.scores[1] },
                period: periods.length + 1,
                periodScores: periods,
                players,
                events,
                lastUpdated: events[events.length - 1].ts || new Date().toISOString(),
                migratedFromDevice: true
            };
            ok = await api.put(`games/${gameId}`, localGame) && ok;
            if (!game) {
                ok = await api.put('current-game', { gameId }) && ok;
                game = localGame;
            }
        }

        if (ok) keys.forEach(k => localStorage.removeItem(k));
        return { roster, game };
    }

    // Roster changed (players, teams, team names): save to the server.
    saveRoster() {
        // Never overwrite the shared roster with a device that hasn't loaded it yet.
        if (!this.loaded) {
            this.ui.setSyncStatus(false);
            this.refresh();
            return;
        }
        this.sync.save('roster', () => ({
            team1Name: this.teams.team1NameElement.textContent.trim(),
            team2Name: this.teams.team2NameElement.textContent.trim(),
            players: this.playersList.map(({ points, ...p }) => p)
        }));
    }

    // Generate a unique game ID: date + random hex suffix
    generateGameId() {
        const date = new Date().toISOString().split('T')[0];
        const rand = Math.random().toString(16).slice(2, 6);
        return `game-${date}-${rand}`;
    }

    // The current game's id; a new one is made when there is no game yet
    get currentGameId() {
        if (!this._currentGameId) this._currentGameId = this.generateGameId();
        return this._currentGameId;
    }

    set currentGameId(value) {
        this._currentGameId = value;
    }

    // The day the current game was played. Game ids start with it (game-YYYY-MM-DD-xxxx),
    // so re-saving an old game never moves it to today.
    gameDate() {
        return this.currentGameId.match(/^game-(\d{4}-\d{2}-\d{2})/)?.[1]
            ?? new Date().toISOString().split('T')[0];
    }

    // Build the game state object that gets saved
    _buildGameState() {
        return {
            id: this.currentGameId,
            version: 1,
            weekDate: this.gameDate(),
            team1: {
                name: this.teams.team1NameElement.textContent,
                score: this.teams.team1Points
            },
            team2: {
                name: this.teams.team2NameElement.textContent,
                score: this.teams.team2Points
            },
            period: this.periodScores.length + 1,
            periodScores: this.periodScores,
            timerMinutes: parseInt(this.settings.timerMinutesInput.value),
            players: this.playersList,
            events: this.events,
            lastUpdated: new Date().toISOString()
        };
    }

    // Start a new game: the old game stays saved in history, scores reset
    startNewGame() {
        // Save the old game's final state under its own id before switching.
        // Only if it had any events, so empty games don't clutter the history.
        if (this._currentGameId) {
            const oldPath = `games/${this._currentGameId}`;
            const finalState = this._buildGameState();
            if (this.events.length > 0) this.sync.save(oldPath, () => finalState, 0);
            else this.sync.cancel(oldPath);
        }

        this.currentGameId = this.generateGameId();

        this.teams.reset();
        this.playersList.forEach(p => p.points = 0);
        this.periodScores = [];
        this.postPeriod = false;
        this.events = [];

        this.players.render();
        this.ui.updateScoreHistory();

        this.syncGame(0);
    }

    // Save game state + current-game pointer (debounced)
    syncGame(delayMs = 3000) {
        const gameId = this.currentGameId;
        this.sync.save(`games/${gameId}`, () => this._buildGameState(), delayMs);
        this.sync.save('current-game', () => ({ gameId }), delayMs);
    }

    // Type in the score directly (team-only games). Returns false if nothing changed.
    setScores(team1, team2) {
        if (team1 === this.teams.team1Points && team2 === this.teams.team2Points) return false;
        this.teams.team1Points = team1;
        this.teams.team2Points = team2;
        this.teams.updateScoreDisplay();
        this.events.push({ ts: new Date().toISOString(), type: 'score_set', scores: [team1, team2] });
        if (this.postPeriod && this.periodScores.length > 0) {
            this.correctPeriodScore(this.periodScores.length - 1);
        }
        this.syncGame();
        return true;
    }

    // End the current quarter at a typed-in score
    endQuarterAt(team1, team2) {
        this.postPeriod = false; // the new score belongs to this quarter, not a correction of the last
        this.setScores(team1, team2);
        this.captureScore();
    }

    // Capture score at end of timer
    captureScore() {
        const period = this.periodScores.length + 1;
        this.periodScores.push(`${this.teams.team1Points} - ${this.teams.team2Points}`);

        this.events.push({
            ts: new Date().toISOString(),
            type: 'period_end',
            scores: [this.teams.team1Points, this.teams.team2Points],
            period: period
        });

        this.postPeriod = true;
        this.ui.updateScoreHistory();
        this.syncGame();
    }

    // Correct a period score to current team scores
    correctPeriodScore(periodIndex) {
        this.periodScores[periodIndex] = `${this.teams.team1Points} - ${this.teams.team2Points}`;

        // Find the matching period_end event and update its scores
        let periodCount = 0;
        for (let i = 0; i < this.events.length; i++) {
            if (this.events[i].type === 'period_end') {
                if (periodCount === periodIndex) {
                    this.events[i].scores = [this.teams.team1Points, this.teams.team2Points];
                    break;
                }
                periodCount++;
            }
        }

        this.ui.updateScoreHistory();
        this.syncGame();
    }

    // Reset score history
    resetScoreHistory() {
        this.periodScores = [];
        this.postPeriod = false;
        this.ui.updateScoreHistory();
    }

    // Clear game events
    clearScoreHistory() {
        this.events = [];
        this.postPeriod = false;
        this.syncGame();
    }

    // Record score change as an event
    recordScoreChange(teamNumber, isIncrement, playerName = undefined, playerId = undefined) {
        const event = {
            ts: new Date().toISOString(),
            type: 'score',
            team: teamNumber,
            player: playerName || null,
            playerId: playerId || null,
            action: isIncrement ? 'increment' : 'decrement',
            scores: [this.teams.team1Points, this.teams.team2Points],
            timer: this.timer.timeLeft
        };

        this.events.push(event);

        // Auto-update last period score during post-period
        if (this.postPeriod && this.periodScores.length > 0) {
            this.correctPeriodScore(this.periodScores.length - 1);
        }

        this.syncGame();
    }

    // Copy a public link to this game's results
    async shareGame() {
        this.syncGame(0);
        await this.sync.send(`games/${this.currentGameId}`);
        const shareUrl = `${window.location.origin}/Scoreboard/game?id=${encodeURIComponent(this.currentGameId)}`;
        navigator.clipboard.writeText(shareUrl).then(() => {
            alert('Results link copied to clipboard!');
        }).catch(() => {
            prompt('Copy this link:', shareUrl);
        });
    }

    // Wake Lock API functions
    async requestWakeLock() {
        if ('wakeLock' in navigator) {
            try {
                this.wakeLock = await navigator.wakeLock.request('screen');
                console.log('Wake Lock is active');
                // The browser drops the lock when the page is hidden; re-take it on return.
                // Registered once: the handler itself checks the checkbox.
                if (!this.wakeLockListener) {
                    this.wakeLockListener = () => this.handleVisibilityChange();
                    document.addEventListener('visibilitychange', this.wakeLockListener);
                }
            } catch (err) {
                console.error(`Failed to get wake lock: ${err.message}`);
            }
        } else {
            console.warn('Wake Lock API not supported in this browser');
            alert('Keep screen on feature is not supported in your browser');
            this.settings.keepScreenOnCheckbox.checked = false;
        }
    }

    releaseWakeLock() {
        if (this.wakeLock !== null) {
            this.wakeLock.release()
                .then(() => {
                    console.log('Wake Lock has been released');
                    this.wakeLock = null;
                });
        }
    }

    handleVisibilityChange() {
        if (document.visibilityState === 'visible' && this.settings.keepScreenOnCheckbox.checked) {
            this.requestWakeLock();
        }
    }

    toggleWakeLock() {
        if (this.settings.keepScreenOnCheckbox.checked) {
            this.requestWakeLock();
            localStorage.setItem('keepScreenOn', 'true');
        } else {
            this.releaseWakeLock();
            localStorage.setItem('keepScreenOn', 'false');
        }
    }
}

class Timer {
    constructor(app) {
        this.app = app;

        // Elements
        this.timerDisplay = document.getElementById('timer');
        this.alarm = document.getElementById('alarm');

        // Timer state
        this.timeLeft = 15 * 60; // Default: 15 minutes in seconds
        this.isRunning = false;
        this.interval = null;
        this.lastTap = 0;

        // Initialize
        this.updateDisplay();
        this.setupEventListeners();
    }

    setupEventListeners() {
        this.timerDisplay.addEventListener('click', this.handleTimerTap.bind(this));

        // Immediate glow on press/touch for tactile feedback
        const addGlow = () => this.timerDisplay.classList.add('timer-pressed');
        const removeGlow = () => this.timerDisplay.classList.remove('timer-pressed');
        this.timerDisplay.addEventListener('mousedown', addGlow);
        this.timerDisplay.addEventListener('mouseup', removeGlow);
        this.timerDisplay.addEventListener('mouseleave', removeGlow);
        this.timerDisplay.addEventListener('touchstart', addGlow, { passive: true });
        this.timerDisplay.addEventListener('touchend', removeGlow);
        this.timerDisplay.addEventListener('touchcancel', removeGlow);
    }

    startStop() {
        if (this.isRunning) {
            // Stop timer
            clearInterval(this.interval);
            this.isRunning = false;

        } else {
            // Start timer
            this.isRunning = true;
            this.app.postPeriod = false;
            this.app.ui.updateScoreHistory();

            // Glow on start, fade out over 2 seconds
            this.timerDisplay.classList.remove('timer-start-glow', 'fading');
            // Force reflow so the glow is applied before we trigger the fade
            void this.timerDisplay.offsetWidth;
            this.timerDisplay.classList.add('timer-start-glow');
            requestAnimationFrame(() => {
                this.timerDisplay.classList.add('fading');
            });
            setTimeout(() => {
                this.timerDisplay.classList.remove('timer-start-glow', 'fading');
            }, 2050);

            this.interval = setInterval(() => {
                this.timeLeft--;
                this.updateDisplay();

                if (this.timeLeft <= 0) {
                    clearInterval(this.interval);
                    this.isRunning = false;
        

                    // Capture score and play alarm
                    this.app.captureScore();
                    this.playAlarm();

                    // Reset timer to configured time
                    this.timeLeft = parseInt(this.app.settings.timerMinutesInput.value) * 60;
                    this.updateDisplay();
                }
            }, 1000);
        }
    }

    reset() {
        clearInterval(this.interval);
        this.isRunning = false;
        this.timerDisplay.classList.remove('timer-running', 'timer-start-glow', 'fading');
        this.timeLeft = parseInt(this.app.settings.timerMinutesInput.value) * 60;
        this.updateDisplay();
        this.timerDisplay.style.backgroundColor = '#333';
    }

    updateDisplay() {
        this.timerDisplay.textContent = this.formatTime(this.timeLeft);
    }

    formatTime(seconds) {
        const minutes = Math.floor(seconds / 60);
        const remainingSeconds = seconds % 60;
        return `${minutes.toString().padStart(2, '0')}:${remainingSeconds.toString().padStart(2, '0')}`;
    }

    playAlarm() {
        this.alarm.play();

        // Blink red a few times
        let blinkCount = 0;
        const blinkInterval = setInterval(() => {
            if (blinkCount >= 5) {
                clearInterval(blinkInterval);
                this.timerDisplay.style.backgroundColor = '#333';
                return;
            }

            if (blinkCount % 2 === 0) {
                // Turn red
                this.timerDisplay.style.backgroundColor = '#f00';
            } else {
                // Turn back to normal
                this.timerDisplay.style.backgroundColor = '#333';
            }

            blinkCount++;
        }, 250);
    }

    simulateTimerEnd() {
        // Stop the timer if it's running
        if (this.isRunning) {
            clearInterval(this.interval);
            this.isRunning = false;

        }

        // Capture score and play alarm
        this.app.captureScore();
        this.playAlarm();

        // Reset timer to configured time
        this.timeLeft = parseInt(this.app.settings.timerMinutesInput.value) * 60;
        this.updateDisplay();
    }

    handleTimerTap() {
        const now = new Date().getTime();
        const timeSince = now - this.lastTap;

        if (timeSince < 300 && timeSince > 0) {
            // Double tap - reset timer
            this.reset();
        } else {
            // Single tap - start/stop
            this.startStop();
        }

        this.lastTap = now;
    }

    setTime(minutes) {
        if (minutes > 0 && minutes <= 60) {
            clearInterval(this.interval);
            this.isRunning = false;

            this.timeLeft = minutes * 60;
            this.updateDisplay();
            this.timerDisplay.style.backgroundColor = '#333';
        }
    }
}

class Teams {
    constructor(app) {
        this.app = app;

        // Team scores
        this.team1Points = 0;
        this.team2Points = 0;

        // Team elements
        this.team1ScoreElement = document.getElementById('score-team1');
        this.team2ScoreElement = document.getElementById('score-team2');
        this.team1NameElement = document.querySelector('.team:nth-of-type(1) .team-name');
        this.team2NameElement = document.querySelector('.team:nth-of-type(2) .team-name');

        // Initialize
        this.updateScoreDisplay();
        this.setupEventListeners();
    }

    setupEventListeners() {
        // Score increment on click
        this.team1ScoreElement.addEventListener('click', () => this.incrementScore(1));
        this.team2ScoreElement.addEventListener('click', () => this.incrementScore(2));

        // Score decrement on long press
        let pressTimer;

        this.team1ScoreElement.addEventListener('touchstart', () => {
            pressTimer = window.setTimeout(() => {
                this.decrementScore(1);
            }, 800);
        });

        this.team2ScoreElement.addEventListener('touchstart', () => {
            pressTimer = window.setTimeout(() => {
                this.decrementScore(2);
            }, 800);
        });

        // Clear timeouts on touch end
        this.team1ScoreElement.addEventListener('touchend', () => {
            clearTimeout(pressTimer);
        });

        this.team2ScoreElement.addEventListener('touchend', () => {
            clearTimeout(pressTimer);
        });

        // Team names edited on the scoreboard
        this.team1NameElement.addEventListener('input', () => this.namesChanged());
        this.team2NameElement.addEventListener('input', () => this.namesChanged());
    }

    incrementScore(teamNumber) {
        if (teamNumber == 1) {
            this.team1Points++;
        } else {
            this.team2Points++;
        }
        this.updateScoreDisplay();
        
        // Record the score change in history
        this.app.recordScoreChange(teamNumber, true);
    }

    decrementScore(teamNumber) {
        if (teamNumber == 1 && this.team1Points > 0) {
            this.team1Points--;
        } else if (teamNumber == 2 && this.team2Points > 0) {
            this.team2Points--;
        }
        this.updateScoreDisplay();
        
        // Record the score change in history
        this.app.recordScoreChange(teamNumber, false);
    }

    updateScoreDisplay() {
        this.team1ScoreElement.textContent = this.team1Points;
        this.team2ScoreElement.textContent = this.team2Points;
    }

    setTeamNames(team1Name, team2Name) {
        this.team1NameElement.textContent = team1Name;
        this.team2NameElement.textContent = team2Name;
        this.app.settings.team1NameInput.value = team1Name;
        this.app.settings.team2NameInput.value = team2Name;
        this.app.players.render();
    }

    // Keep the settings inputs and team builder in step, and save names with the roster
    namesChanged() {
        this.app.settings.team1NameInput.value = this.team1NameElement.textContent;
        this.app.settings.team2NameInput.value = this.team2NameElement.textContent;
        this.app.players.render();
        this.app.saveRoster();
    }

    reset() {
        this.team1Points = 0;
        this.team2Points = 0;
        this.updateScoreDisplay();
    }
}

class Settings {
    constructor(app) {
        this.app = app;

        // Elements
        this.settingsBtn = document.getElementById('settings-btn');
        this.settingsPanel = document.getElementById('settings-panel');
        this.timerMinutesInput = document.getElementById('timer-minutes');
        this.setTimerBtn = document.getElementById('set-timer-btn');
        this.team1NameInput = document.getElementById('team1-name');
        this.team2NameInput = document.getElementById('team2-name');
        this.resetScoresBtn = document.getElementById('reset-scores-btn');
        this.resetHistoryBtn = document.getElementById('reset-history-btn');
        this.testBuzzerBtn = document.getElementById('test-buzzer-btn');
        this.scoreFontSizeSlider = document.getElementById('score-font-size');
        this.timerFontSizeSlider = document.getElementById('timer-font-size');
        this.playerButtonSizeSlider = document.getElementById('player-button-size');
        this.keepScreenOnCheckbox = document.getElementById('keep-screen-on');

        this.shareGameBtn = document.getElementById('share-game-btn');

        // Quarter score entry
        this.qTeam1 = document.getElementById('q-team1');
        this.qTeam2 = document.getElementById('q-team2');
        this.qTeam1Label = document.getElementById('q-team1-label');
        this.qTeam2Label = document.getElementById('q-team2-label');
        this.endQuarterBtn = document.getElementById('end-quarter-btn');
        this.endQuarterBtn.addEventListener('click', () => {
            const scores = this.readQuarterEntry();
            if (!scores) return;
            this.app.endQuarterAt(...scores);
            this.refreshQuarterEntry();
            this.app.players.flash(this.endQuarterBtn, 'Saved ✓');
        });
        document.getElementById('set-score-btn').addEventListener('click', e => {
            const scores = this.readQuarterEntry();
            if (scores && this.app.setScores(...scores)) this.app.players.flash(e.target, 'Saved ✓');
        });

        // Initialize
        this.updateInputsFromCurrent();
        this.setupEventListeners();
    }

    updateInputsFromCurrent() {
        // Get current values from the UI
        this.team1NameInput.value = this.app.teams?.team1NameElement?.textContent || 'BLACK';
        this.team2NameInput.value = this.app.teams?.team2NameElement?.textContent || 'WHITE';
        this.timerMinutesInput.value = Math.floor((this.app.timer?.timeLeft || 900) / 60);
    }

    setupEventListeners() {
        // Settings panel toggle
        this.settingsBtn.addEventListener('click', () => this.toggle());

        // Close settings when clicking outside
        document.addEventListener('click', (e) => {
            if (!this.settingsPanel.contains(e.target) &&
                e.target !== this.settingsBtn &&
                !this.settingsBtn.contains(e.target)) {
                this.settingsPanel.classList.remove('active');
            }
        });

        // Settings buttons
        this.setTimerBtn.addEventListener('click', () => {
            this.app.timer.setTime(parseInt(this.timerMinutesInput.value));
        });

        // Team score decrement buttons
        document.getElementById('team1-decrement').addEventListener('click', () => {
            this.app.teams.decrementScore(1);
        });

        document.getElementById('team2-decrement').addEventListener('click', () => {
            this.app.teams.decrementScore(2);
        });

        // Team name sync from settings to UI
        this.team1NameInput.addEventListener('input', () => {
            this.app.teams.team1NameElement.textContent = this.team1NameInput.value;
            this.app.teams.namesChanged();
        });

        this.team2NameInput.addEventListener('input', () => {
            this.app.teams.team2NameElement.textContent = this.team2NameInput.value;
            this.app.teams.namesChanged();
        });

        this.resetScoresBtn.addEventListener('click', () => {
            this.app.startNewGame();
        });

        this.resetHistoryBtn.addEventListener('click', () => {
            this.app.resetScoreHistory();
            this.app.clearScoreHistory(); // Also clear detailed history
        });

        this.testBuzzerBtn.addEventListener('click', () => {
            this.app.timer.playAlarm();
        });

        // Simulate timer end button
        document.getElementById('simulate-timer-end-btn').addEventListener('click', () => {
            this.app.timer.simulateTimerEnd();
        });

        // Font size sliders
        this.scoreFontSizeSlider.addEventListener('input', (e) => {
            this.app.ui.updateScoreFontSize(e.target.value);
        });

        this.timerFontSizeSlider.addEventListener('input', (e) => {
            this.app.ui.updateTimerFontSize(e.target.value);
        });

        this.playerButtonSizeSlider.addEventListener('input', (e) => {
            this.app.ui.updatePlayerButtonSize(e.target.value);
        });

        // Wake lock checkbox
        this.keepScreenOnCheckbox.addEventListener('change', () => {
            this.app.toggleWakeLock();
        });

        this.shareGameBtn.addEventListener('click', () => this.app.shareGame());
        document.getElementById('stats-btn').addEventListener('click', () => {
            window.location.href = '/Scoreboard/stats';
        });
    }

    toggle() {
        this.settingsPanel.classList.toggle('active');
        if (this.settingsPanel.classList.contains('active')) this.refreshQuarterEntry();
    }

    // Prefill the quarter score inputs with the current score
    refreshQuarterEntry() {
        const teams = this.app.teams;
        this.qTeam1Label.textContent = teams.team1NameElement.textContent;
        this.qTeam2Label.textContent = teams.team2NameElement.textContent;
        this.qTeam1.value = teams.team1Points;
        this.qTeam2.value = teams.team2Points;
        this.endQuarterBtn.textContent = `End Q${this.app.periodScores.length + 1} at this score`;
    }

    readQuarterEntry() {
        const a = parseInt(this.qTeam1.value, 10), b = parseInt(this.qTeam2.value, 10);
        if (!(a >= 0 && b >= 0)) {
            alert('Enter both scores.');
            return null;
        }
        return [a, b];
    }
}

// Team builder panel + the tappable player lists on the scoreboard.
// A player's status is team '1', team '2', or out (active === false; team is remembered).
class Players {
    constructor(app) {
        this.app = app;

        // Elements
        this.playersBtn = document.getElementById('players-btn');
        this.playersPanel = document.getElementById('players-panel');
        this.listEl = document.getElementById('players-list');
        this.countsEl = document.getElementById('team-counts');
        this.playerNameInput = document.getElementById('player-name-input');
        this.addPlayerBtn = document.getElementById('add-player-btn');
        this.shuffleBtn = document.getElementById('shuffle-btn');
        this.balanceBtn = document.getElementById('balance-btn');
        this.predictionEl = document.getElementById('team-prediction');
        this.allOutBtn = document.getElementById('all-out-btn');
        this.shareTeamsBtn = document.getElementById('share-teams-btn');
        this.editRosterBtn = document.getElementById('edit-roster-btn');
        this.photoInput = document.getElementById('player-photo-input');
        this.team1PlayersDisplay = document.getElementById('team1-players-display');
        this.team2PlayersDisplay = document.getElementById('team2-players-display');

        this.pinned = new Set();   // player ids kept on their team when shuffling
        this.model = null;         // impact model from past games (insights.js), for Balance
        this.modelAt = 0;
        this.editing = false;      // edit mode: rename / photo / delete
        this.photoPlayerId = null;

        this.setupEventListeners();
        this.render();
    }

    setupEventListeners() {
        this.playersBtn.addEventListener('click', () => this.toggle());

        // Close the panel when clicking outside it
        document.addEventListener('click', (e) => {
            if (!this.playersPanel.contains(e.target) && !this.playersBtn.contains(e.target)) {
                this.playersPanel.classList.remove('active');
            }
        });

        this.addPlayerBtn.addEventListener('click', () => this.addPlayer());
        this.playerNameInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') this.addPlayer();
        });

        this.shuffleBtn.addEventListener('click', () => this.shuffle());
        this.balanceBtn.addEventListener('click', () => this.balance());
        this.allOutBtn.addEventListener('click', () => this.allOut());
        this.shareTeamsBtn.addEventListener('click', () => this.shareTeams());
        this.editRosterBtn.addEventListener('click', () => {
            this.editing = !this.editing;
            this.render();
        });
        this.photoInput.addEventListener('change', () => this.uploadPhoto());
        document.getElementById('close-teams-btn').addEventListener('click', (e) => {
            e.stopPropagation();
            this.playersPanel.classList.remove('active');
        });
    }

    toggle() {
        if (this.playersPanel.classList.contains('active')) {
            this.playersPanel.classList.remove('active');
        } else {
            this.open();
        }
    }

    open() {
        this.playersPanel.classList.add('active');
        document.getElementById('settings-panel').classList.remove('active');
        this.loadModel().then(() => this.renderPrediction());
    }

    // Fit the impact model on past games (refreshed at most every 10 minutes)
    async loadModel(force = false) {
        if (typeof Insights === 'undefined') return null;
        if (!force && this.model && Date.now() - this.modelAt < 10 * 60 * 1000) return this.model;
        try {
            const games = await Stats.loadGames();
            this.model = games.length >= 3 ? Insights.fit(games) : null;
            this.modelAt = Date.now();
        } catch {
            // offline: keep whatever we had
        }
        return this.model;
    }

    // Who's in, as the model wants them
    playingForModel() {
        return this.app.playersList
            .filter(p => Players.statusOf(p) !== 'out')
            .map(p => ({ id: p.id, team: p.team, pinned: this.pinned.has(p.id) }));
    }

    // "BLACK by 1.4 · 58%" for the current teams
    renderPrediction() {
        const el = this.predictionEl;
        const playing = this.playingForModel();
        const t1 = playing.filter(p => p.team === '1').map(p => p.id);
        const t2 = playing.filter(p => p.team === '2').map(p => p.id);
        if (!el || !this.model || !t1.length || !t2.length) {
            if (el) el.hidden = true;
            return;
        }
        const { margin, p1 } = Insights.predict(this.model, t1, t2);
        const name1 = this.app.teams.team1NameElement.textContent.trim();
        const name2 = this.app.teams.team2NameElement.textContent.trim();
        const fav = margin >= 0 ? name1 : name2;
        const pct = Math.round(100 * (margin >= 0 ? p1 : 1 - p1));
        el.textContent = Math.abs(margin) < 0.5
            ? 'Prediction: dead even'
            : `Prediction: ${fav} by ${Math.abs(margin).toFixed(1)} · ${pct}% to win`;
        el.classList.toggle('lopsided', Math.abs(margin) >= 4);
        el.hidden = false;
    }

    // Most even split of everyone who's in, using how past games went; pinned players stay put.
    async balance() {
        const playing = this.playingForModel();
        if (playing.length < 2) return;
        const model = await this.loadModel();
        if (!model) {
            alert('Balance needs at least 3 games of history. Using Shuffle instead.');
            this.shuffle();
            return;
        }
        const result = Insights.balance(model, playing);
        if (!result) return;
        // Don't reshuffle people for a sliver of a point
        const t1 = playing.filter(p => p.team === '1').map(p => p.id);
        const t2 = playing.filter(p => p.team === '2').map(p => p.id);
        const current = Insights.predict(model, t1, t2).margin;
        if (Math.abs(t1.length - t2.length) <= 1 && Math.abs(current) - Math.abs(result.margin) < 0.5) {
            this.flash(this.balanceBtn, 'Already even');
            return;
        }
        this.app.playersList.forEach(p => {
            const team = result.teams.get(String(p.id));
            if (team) p.team = team;
        });
        this.changed();
        this.flash(this.balanceBtn, result.moves ? `Moved ${result.moves}` : 'Already even');
    }

    // ---- Roster state ----

    find(id) {
        return this.app.playersList.find(p => p.id === id);
    }

    static statusOf(player) {
        return player.active && (player.team === '1' || player.team === '2') ? player.team : 'out';
    }

    playersOn(status) {
        return this.app.playersList
            .filter(p => Players.statusOf(p) === status)
            .sort((a, b) => a.name.toLowerCase().localeCompare(b.name.toLowerCase()));
    }

    // Roster changed: save it and redraw
    changed() {
        this.app.saveRoster();
        this.render();
    }

    setStatus(id, status) {
        const player = this.find(id);
        if (!player) return;
        if (status === 'out') {
            player.active = false;
            this.pinned.delete(id);
        } else {
            player.active = true;
            player.team = status;
        }
        this.changed();
    }

    // The team with fewer players (team 1 on a tie)
    smallerTeam() {
        return this.playersOn('2').length < this.playersOn('1').length ? '2' : '1';
    }

    addPlayer() {
        const name = this.playerNameInput.value.trim();
        if (!name) return;

        this.app.playersList.push({
            id: Date.now(),
            name,
            team: this.smallerTeam(),
            active: true,
            points: 0
        });
        this.changed();

        this.playerNameInput.value = '';
        this.playerNameInput.focus();
    }

    removePlayer(id) {
        this.app.playersList = this.app.playersList.filter(p => p.id !== id);
        this.pinned.delete(id);
        this.changed();
    }

    renamePlayer(id) {
        const player = this.find(id);
        const name = player && prompt('Rename player:', player.name)?.trim();
        if (!name) return;
        player.name = name;
        this.changed();
    }

    choosePhoto(id) {
        this.photoPlayerId = id;
        this.photoInput.value = '';
        this.photoInput.click();
    }

    async uploadPhoto() {
        const file = this.photoInput.files[0];
        const player = this.find(this.photoPlayerId);
        if (!file || !player) return;

        const form = new FormData();
        form.append('image', file);
        try {
            const res = await fetch(`/Scoreboard/api/players/${player.id}/image`, { method: 'POST', body: form });
            const data = await res.json();
            if (!res.ok) throw new Error(data.error || res.status);
            player.imageUrl = data.imageUrl;
            this.changed();
        } catch (e) {
            alert('Photo upload failed: ' + e.message);
        }
    }

    togglePin(id) {
        if (this.pinned.has(id)) this.pinned.delete(id);
        else this.pinned.add(id);
        this.render();
    }

    // Randomly split everyone who's in into even teams; pinned players stay put.
    shuffle() {
        const playing = this.app.playersList.filter(p => Players.statusOf(p) !== 'out');
        const free = playing.filter(p => !this.pinned.has(p.id));
        const counts = { '1': 0, '2': 0 };
        playing.filter(p => this.pinned.has(p.id)).forEach(p => counts[p.team]++);

        for (let i = free.length - 1; i > 0; i--) {
            const j = Math.floor(Math.random() * (i + 1));
            [free[i], free[j]] = [free[j], free[i]];
        }

        free.forEach(p => {
            const team = counts['1'] === counts['2']
                ? (Math.random() < 0.5 ? '1' : '2')
                : (counts['1'] < counts['2'] ? '1' : '2');
            p.team = team;
            counts[team]++;
        });
        this.changed();
    }

    // Start of the week: mark everyone out, then tap in whoever shows up
    allOut() {
        this.app.playersList.forEach(p => p.active = false);
        this.pinned.clear();
        this.changed();
    }

    // Chat-ready team list, e.g. for the group text
    teamsText() {
        const date = new Date().toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
        const team = (icon, name, status) => {
            const players = this.playersOn(status).map(p => p.name);
            return `${icon} ${name} (${players.length})\n${players.join('\n') || '-'}`;
        };
        return [
            `🏀 Teams for ${date}`,
            team('⚫', this.app.teams.team1NameElement.textContent.trim(), '1'),
            team('⚪', this.app.teams.team2NameElement.textContent.trim(), '2')
        ].join('\n\n');
    }

    // Phones/iPads: open the share sheet (Messages, WhatsApp...). Elsewhere: copy to clipboard.
    async shareTeams() {
        const text = this.teamsText();
        if (navigator.share && matchMedia('(pointer: coarse)').matches) {
            try {
                await navigator.share({ text });
                return;
            } catch (e) {
                if (e.name === 'AbortError') return; // user closed the share sheet
            }
        }
        try {
            await navigator.clipboard.writeText(text);
            this.flash(this.shareTeamsBtn, 'Copied!');
        } catch {
            prompt('Copy teams:', text);
        }
    }

    flash(button, text) {
        const original = button.textContent;
        button.textContent = text;
        setTimeout(() => button.textContent = original, 1200);
    }

    // ---- Scoring ----

    incrementPlayerPoints(id) {
        this.changePlayerPoints(id, 1);
    }

    changePlayerPoints(id, delta) {
        const player = this.find(id);
        if (!player || Players.statusOf(player) === 'out') return;
        if (delta < 0 && player.points <= 0) return;

        player.points += delta;
        const teams = this.app.teams;
        if (player.team === '1') teams.team1Points = Math.max(0, teams.team1Points + delta);
        else teams.team2Points = Math.max(0, teams.team2Points + delta);
        teams.updateScoreDisplay();

        this.app.recordScoreChange(parseInt(player.team), delta > 0, player.name, player.id);
        this.render();
    }

    // ---- Rendering ----

    render() {
        this.renderPanel();
        this.renderScoreboard();
        this.renderPrediction();
    }

    renderPanel() {
        const team1Name = this.app.teams.team1NameElement.textContent;
        const team2Name = this.app.teams.team2NameElement.textContent;
        const n1 = this.playersOn('1').length;
        const n2 = this.playersOn('2').length;
        const nOut = this.playersOn('out').length;

        this.countsEl.innerHTML = '';
        [[team1Name, n1, 'team1'], [team2Name, n2, 'team2'], ['Out', nOut, 'out']].forEach(([label, n, cls]) => {
            const chip = document.createElement('span');
            chip.className = `count-chip ${cls}`;
            chip.textContent = `${label} ${n}`;
            this.countsEl.appendChild(chip);
        });
        this.countsEl.classList.toggle('uneven', Math.abs(n1 - n2) > 1);

        this.editRosterBtn.textContent = this.editing ? 'Done' : 'Edit';
        this.editRosterBtn.classList.toggle('active', this.editing);
        this.playersPanel.classList.toggle('editing', this.editing);

        // Alphabetical and stable, so rows don't jump around while you tap
        const players = [...this.app.playersList]
            .sort((a, b) => a.name.toLowerCase().localeCompare(b.name.toLowerCase()));

        this.listEl.innerHTML = '';
        if (players.length === 0) {
            const empty = document.createElement('div');
            empty.className = 'roster-empty';
            empty.textContent = 'No players yet. Add some below.';
            this.listEl.appendChild(empty);
        }

        players.forEach(player => {
            const status = Players.statusOf(player);
            const row = document.createElement('div');
            row.className = `roster-row status-${status}`;
            row.dataset.playerId = player.id;

            const name = document.createElement('span');
            name.className = 'roster-name';
            name.textContent = player.name;

            if (this.editing) {
                row.appendChild(name);
                row.appendChild(this.button('Photo', 'roster-btn', () => this.choosePhoto(player.id)));
                row.appendChild(this.button('Rename', 'roster-btn', () => this.renamePlayer(player.id)));
                row.appendChild(this.button('Delete', 'roster-btn remove-player', () => this.removePlayer(player.id)));
                this.listEl.appendChild(row);
                return;
            }

            const pin = this.button('📌', 'pin-btn', () => this.togglePin(player.id));
            pin.title = 'Pin: keep on this team when shuffling';
            pin.classList.toggle('pinned', this.pinned.has(player.id));
            pin.disabled = status === 'out';
            row.appendChild(pin);
            row.appendChild(name);

            const points = document.createElement('span');
            points.className = 'roster-points';
            if (status !== 'out') {
                points.appendChild(this.button('−', 'points-btn decrement-btn', () => this.changePlayerPoints(player.id, -1)));
                const value = document.createElement('span');
                value.className = 'points-display';
                value.textContent = player.points || 0;
                points.appendChild(value);
                points.appendChild(this.button('+', 'points-btn increment-btn', () => this.changePlayerPoints(player.id, 1)));
            }
            row.appendChild(points);

            const toggle = document.createElement('div');
            toggle.className = 'team-toggle';
            [['1', team1Name, 'team1'], ['out', 'Out', 'out'], ['2', team2Name, 'team2']].forEach(([value, label, cls]) => {
                const btn = this.button(label, `${cls}${status === value ? ' selected' : ''}`, () => this.setStatus(player.id, value));
                btn.dataset.team = value;
                toggle.appendChild(btn);
            });
            row.appendChild(toggle);

            this.listEl.appendChild(row);
        });
    }

    button(text, className, onClick) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = className;
        btn.textContent = text;
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            onClick();
        });
        return btn;
    }

    renderScoreboard() {
        this.team1PlayersDisplay.innerHTML = '';
        this.team2PlayersDisplay.innerHTML = '';

        const createPlayerButton = (player, container, isTeam2) => {
            const playerItem = document.createElement('div');
            playerItem.className = 'player-item-display';
            playerItem.dataset.playerId = player.id;

            let avatarEl;
            if (player.imageUrl) {
                avatarEl = document.createElement('img');
                avatarEl.className = 'player-avatar';
                avatarEl.src = player.imageUrl;
                avatarEl.alt = player.name;
            } else {
                avatarEl = document.createElement('div');
                avatarEl.className = 'player-avatar-placeholder';
                avatarEl.textContent = player.name.charAt(0).toUpperCase();
            }

            const playerName = document.createElement('div');
            playerName.className = 'player-name-display';
            playerName.textContent = player.name;

            // Stack avatar above name
            const identity = document.createElement('div');
            identity.className = 'player-identity';
            identity.appendChild(avatarEl);
            identity.appendChild(playerName);

            const playerPoints = document.createElement('div');
            playerPoints.className = 'player-points-display';
            playerPoints.textContent = player.points || 0;

            if (isTeam2) {
                // Team 2 (right side): points | [avatar/name]
                playerItem.appendChild(playerPoints);
                playerItem.appendChild(identity);
            } else {
                // Team 1 (left side): [avatar/name] | points
                playerItem.appendChild(identity);
                playerItem.appendChild(playerPoints);
            }

            playerItem.addEventListener('click', () => this.incrementPlayerPoints(player.id));
            container.appendChild(playerItem);
        };

        this.playersOn('1').forEach(p => createPlayerButton(p, this.team1PlayersDisplay, false));
        this.playersOn('2').forEach(p => createPlayerButton(p, this.team2PlayersDisplay, true));
    }
}

class UI {
    constructor(app) {
        this.app = app;

        // UI elements
        this.fullscreenBtn = document.getElementById('fullscreen-btn');
        this.scoreHistoryElement = document.getElementById('score-history');
        this.syncStatusElement = document.getElementById('sync-status');

        // Set up event listeners
        this.setupEventListeners();
    }

    // Show a small badge while changes haven't reached the server (they retry automatically)
    setSyncStatus(ok) {
        this.syncStatusElement.hidden = ok;
    }

    setupEventListeners() {
        this.fullscreenBtn.addEventListener('click', () => this.toggleFullscreen());
    }

    toggleFullscreen() {
        if (!document.fullscreenElement) {
            // Enter fullscreen
            if (document.documentElement.requestFullscreen) {
                document.documentElement.requestFullscreen();
            } else if (document.documentElement.webkitRequestFullscreen) {
                document.documentElement.webkitRequestFullscreen();
            } else if (document.documentElement.msRequestFullscreen) {
                document.documentElement.msRequestFullscreen();
            }
        } else {
            // Exit fullscreen
            if (document.exitFullscreen) {
                document.exitFullscreen();
            } else if (document.webkitExitFullscreen) {
                document.webkitExitFullscreen();
            } else if (document.msExitFullscreen) {
                document.msExitFullscreen();
            }
        }
    }

    updateScoreHistory() {
        this.scoreHistoryElement.innerHTML = '';

        // Display each period score
        this.app.periodScores.forEach((score, index) => {
            const periodElement = document.createElement('span');
            periodElement.className = 'period-score';
            if (this.app.postPeriod && index === this.app.periodScores.length - 1) {
                periodElement.classList.add('period-score-active');
            }
            periodElement.textContent = score;
            periodElement.style.cursor = 'pointer';
            periodElement.addEventListener('click', () => {
                this.app.correctPeriodScore(index);
            });
            this.scoreHistoryElement.appendChild(periodElement);
        });
    }

    updateScoreFontSize(value) {
        const scoreElements = document.querySelectorAll('.score');
        scoreElements.forEach(el => {
            el.style.fontSize = `${value}rem`;
        });
        document.getElementById('score-font-size-value').textContent = value;
        localStorage.setItem('scoreFontSize', value);
    }

    updateTimerFontSize(value) {
        this.app.timer.timerDisplay.style.fontSize = `${value}rem`;
        document.getElementById('timer-font-size-value').textContent = value;
        localStorage.setItem('timerFontSize', value);
    }

    updatePlayerButtonSize(value) {
        document.documentElement.style.setProperty('--player-button-padding', `${value}px`);
        document.getElementById('player-button-size-value').textContent = value;
        localStorage.setItem('playerButtonSize', value);
    }
}

// Initialize the app when the DOM is fully loaded
document.addEventListener('DOMContentLoaded', () => {
    window.scoreboardApp = new ScoreboardApp();
});
