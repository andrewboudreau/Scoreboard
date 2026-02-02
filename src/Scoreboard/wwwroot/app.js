
class BlobSync {
    constructor(app) {
        this.app = app;
        this.groupId = null;
        this.groupName = null;
        this.isAdmin = false;
        this.readUrl = null;
        this.writeUrl = null;
        this.code = null;
        this.saveTimeouts = {};
    }

    get isConnected() {
        return this.groupId !== null && this.readUrl !== null;
    }

    async join(code) {
        const response = await fetch(`/Scoreboard/api/groups/join?code=${encodeURIComponent(code)}`);
        if (!response.ok) {
            const err = await response.json().catch(() => ({}));
            throw new Error(err.error || 'Invalid code');
        }
        const data = await response.json();
        this.groupId = data.groupId;
        this.groupName = data.groupName;
        this.isAdmin = data.isAdmin;
        this.readUrl = data.sasUrls.readUrl;
        this.writeUrl = data.sasUrls.writeUrl;
        this.code = code;
        localStorage.setItem('groupCode', code);
    }

    async createGroup(name) {
        const response = await fetch('/Scoreboard/api/groups', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name })
        });
        if (!response.ok) throw new Error('Failed to create group');
        const data = await response.json();
        await this.join(data.adminCode);
        return data;
    }

    leave() {
        this.groupId = null;
        this.groupName = null;
        this.isAdmin = false;
        this.readUrl = null;
        this.writeUrl = null;
        this.code = null;
        localStorage.removeItem('groupCode');
    }

    // Build blob URL: insert groupId/path into the container SAS URL
    _blobUrl(sasUrl, path) {
        const url = new URL(sasUrl);
        url.pathname += '/' + this.groupId + '/' + path;
        return url.toString();
    }

    // Get fresh SAS URLs from the server (code is the anti-forgery token)
    async _refreshSas() {
        try {
            const res = await fetch(
                `/Scoreboard/api/groups/${this.groupId}/sas/refresh?code=${encodeURIComponent(this.code)}`
            );
            if (!res.ok) return false;
            const data = await res.json();
            this.readUrl = data.readUrl;
            this.writeUrl = data.writeUrl;
            return true;
        } catch { return false; }
    }

    async download(path) {
        if (!this.isConnected) return null;
        try {
            let res = await fetch(this._blobUrl(this.readUrl, path));
            if (res.status === 403 && await this._refreshSas()) {
                res = await fetch(this._blobUrl(this.readUrl, path));
            }
            if (!res.ok) return null;
            return await res.json();
        } catch (e) {
            console.error(`BlobSync: download ${path} failed`, e);
            return null;
        }
    }

    async upload(path, data) {
        if (!this.isConnected || !this.writeUrl) return false;
        const opts = {
            method: 'PUT',
            headers: { 'x-ms-blob-type': 'BlockBlob', 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        };
        try {
            let res = await fetch(this._blobUrl(this.writeUrl, path), opts);
            if (res.status === 403 && await this._refreshSas()) {
                res = await fetch(this._blobUrl(this.writeUrl, path), opts);
            }
            return res.ok;
        } catch (e) {
            console.error(`BlobSync: upload ${path} failed`, e);
            return false;
        }
    }

    debouncedUpload(path, data, delayMs = 3000) {
        if (this.saveTimeouts[path]) clearTimeout(this.saveTimeouts[path]);
        this.saveTimeouts[path] = setTimeout(() => {
            this.upload(path, data);
            delete this.saveTimeouts[path];
        }, delayMs);
    }
}

class SyncManager {
    constructor(app) {
        this.app = app;
        this.dirty = false;
        this.periodicInterval = null;

        window.addEventListener('online', () => {
            if (this.dirty) this.app.syncGame();
        });

        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible' && this.dirty && navigator.onLine) {
                this.app.syncGame();
            }
        });

        this.periodicInterval = setInterval(() => {
            if (this.shouldSync() && this.dirty) {
                this.app.syncGame();
            }
        }, 2 * 60 * 1000);
    }

    shouldSync() {
        return navigator.onLine && this.app.blobSync.isConnected;
    }

    markDirty() {
        this.dirty = true;
    }

    markClean() {
        this.dirty = false;
    }
}

class ScoreboardApp {
    constructor() {
        // Initialize components
        this.timer = new Timer(this);
        this.teams = new Teams(this);
        this.players = new Players(this);
        this.settings = new Settings(this);
        this.ui = new UI(this);
        this.blobSync = new BlobSync(this);
        this.syncManager = new SyncManager(this);
        this.weeklySetup = new WeeklySetup(this);

        // Global state
        this.periodScores = [];
        this.events = [];
        this.currentShareCode = null;
        this.wakeLock = null;
        this.playersList = [];

        // Initialize app
        this.loadSavedSettings();
        this.initGroupConnection();
    }

    // Load saved settings from localStorage
    loadSavedSettings() {
        // Initialize playersList
        this.playersList = [];

        // Migrate old scoreHistory → gameEvents
        if (localStorage.getItem('scoreHistory') && !localStorage.getItem('gameEvents')) {
            localStorage.setItem('gameEvents', localStorage.getItem('scoreHistory'));
            localStorage.removeItem('scoreHistory');
        } else if (localStorage.getItem('scoreHistory')) {
            localStorage.removeItem('scoreHistory');
        }

        // Clean up legacy keys
        localStorage.removeItem('lastUploadAttempt');

        // Load saved game events
        if (localStorage.getItem('gameEvents')) {
            try {
                this.events = JSON.parse(localStorage.getItem('gameEvents'));
            } catch (e) {
                console.error('Error loading game events:', e);
                this.events = [];
            }
        }

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

        // Screen wake lock
        if (localStorage.getItem('keepScreenOn') === 'true') {
            this.settings.keepScreenOnCheckbox.checked = true;
            this.requestWakeLock();
        }

        // Load saved players
        if (localStorage.getItem('playersList')) {
            this.playersList = JSON.parse(localStorage.getItem('playersList'));
            // Update player list after players component is initialized
            setTimeout(() => {
                if (this.players) {
                    this.players.updatePlayersList();
                    this.players.updatePlayersDisplay();
                }
            }, 0);
        }
    }

    // Check URL or localStorage for a group code and auto-join
    async initGroupConnection() {
        const urlParams = new URLSearchParams(window.location.search);
        let code = urlParams.get('code');

        if (!code && window.location.hash) {
            code = window.location.hash.substring(1);
        }

        if (!code) {
            code = localStorage.getItem('groupCode');
        }

        if (code) {
            try {
                await this.blobSync.join(code);
                if (urlParams.has('code') || window.location.hash) {
                    history.replaceState(null, '', window.location.pathname);
                }
                this.settings.updateGroupUI();
                await this.loadGroupData();
            } catch (e) {
                console.error('Failed to join group:', e);
                localStorage.removeItem('groupCode');
            }
        }
    }

    // Download group data (roster, weekly config, active game) from blob
    async loadGroupData() {
        if (!this.blobSync.isConnected) return;

        // Load roster
        const roster = await this.blobSync.download('roster.json');
        if (roster && roster.players) {
            this.playersList = roster.players;
            localStorage.setItem('playersList', JSON.stringify(this.playersList));
            if (this.players) {
                this.players.updatePlayersList();
                this.players.updatePlayersDisplay();
            }
        }

        // Load current week's config
        const date = this.weeklySetup.getDefaultDate();
        const weekConfig = await this.blobSync.download(`week/${date}.json`);
        if (weekConfig) {
            this.weeklySetup.available = new Set(weekConfig.available || []);
            this.weeklySetup.teams = weekConfig.teams || { white: [], black: [] };
            this.weeklySetup.forced = weekConfig.forced || [];
            this.weeklySetup.dateInput.value = date;
        }

        // Load active game state (consolidated format with events)
        const gameId = `game-${date}`;
        const gameState = await this.blobSync.download(`games/${gameId}.json`);
        if (gameState) {
            this.teams.team1Points = gameState.team1?.score || 0;
            this.teams.team2Points = gameState.team2?.score || 0;
            this.teams.updateScoreDisplay();
            if (gameState.team1?.name) this.teams.team1NameElement.textContent = gameState.team1.name;
            if (gameState.team2?.name) this.teams.team2NameElement.textContent = gameState.team2.name;
            this.periodScores = gameState.periodScores || [];
            this.events = gameState.events || [];
            this.currentShareCode = gameState.shareCode || null;
            localStorage.setItem('gameEvents', JSON.stringify(this.events));
            this.ui.updateScoreHistory();
        }
    }

    // Get the current game ID based on weekly date
    get currentGameId() {
        const date = this.weeklySetup.dateInput.value || new Date().toISOString().split('T')[0];
        return `game-${date}`;
    }

    // Sync consolidated game state + events to blob (fire-and-forget)
    syncGame() {
        if (!this.blobSync.isConnected) return;
        if (!this.syncManager.shouldSync()) {
            this.syncManager.markDirty();
            return;
        }

        const state = {
            id: this.currentGameId,
            version: 1,
            weekDate: this.weeklySetup.dateInput.value,
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
            shareCode: this.currentShareCode,
            lastUpdated: new Date().toISOString()
        };

        this.blobSync.debouncedUpload(`games/${this.currentGameId}.json`, state, 5000);
        this.syncManager.markClean();
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
        localStorage.setItem('gameEvents', JSON.stringify(this.events));

        this.ui.updateScoreHistory();
        this.syncManager.markDirty();
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

        localStorage.setItem('gameEvents', JSON.stringify(this.events));
        this.ui.updateScoreHistory();
        this.syncManager.markDirty();
        this.syncGame();
    }

    // Reset score history
    resetScoreHistory() {
        this.periodScores = [];
        this.ui.updateScoreHistory();
    }

    // Clear game events
    clearScoreHistory() {
        this.events = [];
        this.currentShareCode = null;
        localStorage.removeItem('gameEvents');
    }

    // Record score change as an event
    recordScoreChange(teamNumber, isIncrement, playerName = undefined) {
        const event = {
            ts: new Date().toISOString(),
            type: 'score',
            team: teamNumber,
            player: playerName || null,
            action: isIncrement ? 'increment' : 'decrement',
            scores: [this.teams.team1Points, this.teams.team2Points],
            timer: this.timer.timeLeft
        };

        this.events.push(event);
        localStorage.setItem('gameEvents', JSON.stringify(this.events));

        // Sync consolidated game to blob
        this.syncManager.markDirty();
        this.syncGame();
    }

    // Share current game results — creates a share code via server
    async shareGame() {
        if (!this.blobSync.isConnected) {
            alert('Join a group to share game results.');
            return;
        }

        // Ensure latest state is uploaded before sharing
        await this.blobSync.upload(`games/${this.currentGameId}.json`, {
            id: this.currentGameId,
            version: 1,
            weekDate: this.weeklySetup.dateInput.value,
            team1: { name: this.teams.team1NameElement.textContent, score: this.teams.team1Points },
            team2: { name: this.teams.team2NameElement.textContent, score: this.teams.team2Points },
            period: this.periodScores.length + 1,
            periodScores: this.periodScores,
            timerMinutes: parseInt(this.settings.timerMinutesInput.value),
            players: this.playersList,
            events: this.events,
            shareCode: this.currentShareCode,
            lastUpdated: new Date().toISOString()
        });

        try {
            const res = await fetch('/Scoreboard/api/games/share', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ groupId: this.blobSync.groupId, gameId: this.currentGameId })
            });
            if (!res.ok) throw new Error('Failed to create share link');
            const data = await res.json();

            this.currentShareCode = data.shareCode;
            this.syncGame();

            const shareUrl = `${window.location.origin}${data.shareUrl}`;
            navigator.clipboard.writeText(shareUrl).then(() => {
                alert('Share link copied to clipboard!');
            }).catch(() => {
                prompt('Copy this link:', shareUrl);
            });
        } catch (e) {
            alert('Failed to share game: ' + e.message);
        }
    }

    // Wake Lock API functions
    async requestWakeLock() {
        if ('wakeLock' in navigator) {
            try {
                this.wakeLock = await navigator.wakeLock.request('screen');
                console.log('Wake Lock is active');
                document.addEventListener('visibilitychange', this.handleVisibilityChange.bind(this));
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
            document.removeEventListener('visibilitychange', this.handleVisibilityChange);
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
    }

    startStop() {
        if (this.isRunning) {
            // Stop timer
            clearInterval(this.interval);
            this.isRunning = false;
        } else {
            // Start timer
            this.isRunning = true;
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

        // Team name sync from UI to settings
        this.team1NameElement.addEventListener('input', () => {
            this.app.settings.team1NameInput.value = this.team1NameElement.textContent;
        });

        this.team2NameElement.addEventListener('input', () => {
            this.app.settings.team2NameInput.value = this.team2NameElement.textContent;
        });
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
        this.keepScreenOnCheckbox = document.getElementById('keep-screen-on');

        // Group elements
        this.groupInfoDiv = document.getElementById('group-info');
        this.groupJoinDiv = document.getElementById('group-join');
        this.groupNameDisplay = document.getElementById('group-name-display');
        this.groupCodeDisplay = document.getElementById('group-code-display');
        this.groupAdminBadge = document.getElementById('group-admin-badge');
        this.groupCodeInput = document.getElementById('group-code-input');
        this.joinGroupBtn = document.getElementById('join-group-btn');
        this.createGroupBtn = document.getElementById('create-group-btn');
        this.shareGroupBtn = document.getElementById('share-group-btn');
        this.shareGameBtn = document.getElementById('share-game-btn');
        this.leaveGroupBtn = document.getElementById('leave-group-btn');

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
        });

        this.team2NameInput.addEventListener('input', () => {
            this.app.teams.team2NameElement.textContent = this.team2NameInput.value;
        });

        this.resetScoresBtn.addEventListener('click', () => {
            this.app.teams.reset();
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

        // Wake lock checkbox
        this.keepScreenOnCheckbox.addEventListener('change', () => {
            this.app.toggleWakeLock();
        });

        // Manage default players button
        const manageDefaultPlayersBtn = document.getElementById('manage-default-players-btn');
        if (manageDefaultPlayersBtn) {
            manageDefaultPlayersBtn.addEventListener('click', () => {
                window.location.href = '/Scoreboard/ManagePlayers';
            });
        }

        // Group management
        this.joinGroupBtn.addEventListener('click', () => this.handleJoinGroup());
        this.createGroupBtn.addEventListener('click', () => this.handleCreateGroup());
        this.shareGroupBtn.addEventListener('click', () => this.handleShareGroup());
        this.shareGameBtn.addEventListener('click', () => this.app.shareGame());
        this.leaveGroupBtn.addEventListener('click', () => this.handleLeaveGroup());
        this.groupCodeInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') this.handleJoinGroup();
        });
    }

    toggle() {
        this.settingsPanel.classList.toggle('active');
    }

    updateGroupUI() {
        if (this.app.blobSync.isConnected) {
            this.groupInfoDiv.style.display = 'block';
            this.groupJoinDiv.style.display = 'none';
            this.groupNameDisplay.textContent = this.app.blobSync.groupName;
            this.groupCodeDisplay.textContent = this.app.blobSync.code;
            this.groupAdminBadge.style.display = this.app.blobSync.isAdmin ? 'inline' : 'none';
            this.shareGameBtn.style.display = 'inline-block';
        } else {
            this.groupInfoDiv.style.display = 'none';
            this.groupJoinDiv.style.display = 'block';
            this.shareGameBtn.style.display = 'none';
        }
    }

    async handleJoinGroup() {
        const code = this.groupCodeInput.value.trim();
        if (!code) return;

        try {
            await this.app.blobSync.join(code);
            this.updateGroupUI();
            await this.app.loadGroupData();
            this.groupCodeInput.value = '';
        } catch (e) {
            alert('Failed to join group: ' + e.message);
        }
    }

    async handleCreateGroup() {
        const name = prompt('Enter group name:');
        if (!name) return;

        try {
            const data = await this.app.blobSync.createGroup(name);
            this.updateGroupUI();
            // Upload current roster to the new group
            if (this.app.playersList.length > 0) {
                await this.app.blobSync.upload('roster.json', { players: this.app.playersList });
            }
            alert(`Group created!\nAdmin code: ${data.adminCode}\nShare this code with your group.`);
        } catch (e) {
            alert('Failed to create group: ' + e.message);
        }
    }

    handleShareGroup() {
        if (!this.app.blobSync.isConnected) return;

        const url = `${window.location.origin}${window.location.pathname}?code=${this.app.blobSync.code}`;
        navigator.clipboard.writeText(url).then(() => {
            alert('Share link copied to clipboard!');
        }).catch(() => {
            prompt('Copy this link:', url);
        });
    }

    handleLeaveGroup() {
        if (confirm('Leave this group? You will need the code to rejoin.')) {
            this.app.blobSync.leave();
            this.updateGroupUI();
        }
    }
}

class Players {
    constructor(app) {
        this.app = app;

        // Elements
        this.playersBtn = document.getElementById('players-btn');
        this.playersPanel = document.getElementById('players-panel');
        this.playersList = document.getElementById('players-list');
        this.playerNameInput = document.getElementById('player-name-input');
        this.addPlayerBtn = document.getElementById('add-player-btn');
        this.exportPlayersBtn = document.getElementById('export-players-btn');
        this.importPlayersBtn = document.getElementById('import-players-btn');
        this.loadDefaultPlayersBtn = document.getElementById('load-default-players-btn');
        this.team1PlayersDisplay = document.getElementById('team1-players-display');
        this.team2PlayersDisplay = document.getElementById('team2-players-display');

        // Ensure playersList is initialized
        if (!this.app.playersList) {
            this.app.playersList = [];
        }

        // Initialize
        this.setupEventListeners();
        this.updateTeamSelectOptions();
        this.updatePlayersDisplay();
    }

    setupEventListeners() {
        // Players panel toggle
        this.playersBtn.addEventListener('click', () => this.toggle());

        // Close players panel when clicking outside
        document.addEventListener('click', (e) => {
            if (!this.playersPanel.contains(e.target) &&
                e.target !== this.playersBtn &&
                !this.playersBtn.contains(e.target)) {
                this.playersPanel.classList.remove('active');
            }
        });

        // Add player button
        this.addPlayerBtn.addEventListener('click', () => this.addPlayer());

        // Add player on Enter key
        this.playerNameInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                this.addPlayer();
            }
        });

        // Export/Import players
        this.exportPlayersBtn.addEventListener('click', () => this.exportPlayers());
        this.importPlayersBtn.addEventListener('click', () => this.importPlayers());
        this.loadDefaultPlayersBtn.addEventListener('click', () => this.loadDefaultPlayers());

        // Update team display when team names change
        this.app.teams.team1NameElement.addEventListener('input', () => this.updatePlayersList());
        this.app.teams.team2NameElement.addEventListener('input', () => this.updatePlayersList());
    }

    toggle() {
        this.playersPanel.classList.toggle('active');
        // Close settings panel if open
        document.getElementById('settings-panel').classList.remove('active');
    }

    updateTeamSelectOptions() {
        // Update existing player rows if any
        this.updatePlayersList();
    }

    savePlayersState() {
        localStorage.setItem('playersList', JSON.stringify(this.app.playersList));
        if (this.app.blobSync.isConnected) {
            this.app.blobSync.debouncedUpload('roster.json', { players: this.app.playersList });
        }
    }

    addPlayer() {
        const playerName = this.playerNameInput.value.trim();

        if (playerName) {
            // Create a new player object
            const newPlayer = {
                id: Date.now(), // Use timestamp as unique ID
                name: playerName,
                team: '1', // Default to team 1
                active: true,
                points: 0
            };

            // Add to players list
            this.app.playersList.push(newPlayer);

            // Save state (localStorage + blob sync)
            this.savePlayersState();

            // Update the UI
            this.updatePlayersList();
            this.updatePlayersDisplay();

            // Clear input
            this.playerNameInput.value = '';
            this.playerNameInput.focus();
        }
    }

    removePlayer(playerId) {
        // Find the player index
        const playerIndex = this.app.playersList.findIndex(player => player.id === playerId);

        if (playerIndex !== -1) {
            // Remove the player
            this.app.playersList.splice(playerIndex, 1);

            // Save state (localStorage + blob sync)
            this.savePlayersState();

            // Update the UI
            this.updatePlayersList();
            this.updatePlayersDisplay();
        }
    }

    updatePlayerActive(playerId, isActive) {
        // Find the player
        const player = this.app.playersList.find(player => player.id === playerId);

        if (player) {
            // Update active status
            player.active = isActive;

            // Save state (localStorage + blob sync)
            this.savePlayersState();

            // Update the players display
            this.updatePlayersDisplay();
        }
    }

    updatePlayerPoints(playerId, points) {
        // Find the player
        const player = this.app.playersList.find(player => player.id === playerId);

        if (player) {
            // Update points
            player.points = parseInt(points) || 0;

            // Save state (localStorage + blob sync)
            this.savePlayersState();

            // Update the players display
            this.updatePlayersDisplay();
        }
    }

    incrementPlayerPoints(playerId) {
        // Find the player
        const player = this.app.playersList.find(player => player.id === playerId);

        if (player) {
            // Increment points
            player.points += 1;

            // Increment team score but don't record history yet
            // (we'll do it with player info below)
            if (parseInt(player.team) === 1) {
                this.app.teams.team1Points += 1;
            } else {
                this.app.teams.team2Points += 1;
            }
            this.app.teams.updateScoreDisplay();
            
            // Record the score change with player name
            this.app.recordScoreChange(parseInt(player.team), true, player.name);

            // Save state (localStorage + blob sync)
            this.savePlayersState();

            // Update the players display and list
            this.updatePlayersDisplay();
            this.updatePlayersList();
        }
    }

    updatePlayersDisplay() {
        // Clear the displays
        this.team1PlayersDisplay.innerHTML = '';
        this.team2PlayersDisplay.innerHTML = '';

        if (this.app.playersList && Array.isArray(this.app.playersList)) {
            // Filter active players by team and sort by name
            const team1Players = this.app.playersList
                .filter(player => player.team === '1' && player.active)
                .sort((a, b) => a.name.toLowerCase().localeCompare(b.name.toLowerCase()));
                
            const team2Players = this.app.playersList
                .filter(player => player.team === '2' && player.active)
                .sort((a, b) => a.name.toLowerCase().localeCompare(b.name.toLowerCase()));

            // Add team 1 players
            team1Players.forEach(player => {
                const playerItem = document.createElement('div');
                playerItem.className = 'player-item-display';
                playerItem.dataset.playerId = player.id;

                const playerName = document.createElement('div');
                playerName.className = 'player-name-display';
                playerName.textContent = player.name;

                const playerPoints = document.createElement('div');
                playerPoints.className = 'player-points-display';
                playerPoints.textContent = player.points;

                playerItem.appendChild(playerName);
                playerItem.appendChild(playerPoints);

                // Add click event to increment points
                playerItem.addEventListener('click', () => {
                    this.incrementPlayerPoints(player.id);
                });

                this.team1PlayersDisplay.appendChild(playerItem);
            });

            // Add team 2 players
            team2Players.forEach(player => {
                const playerItem = document.createElement('div');
                playerItem.className = 'player-item-display';
                playerItem.dataset.playerId = player.id;

                const playerName = document.createElement('div');
                playerName.className = 'player-name-display';
                playerName.textContent = player.name;

                const playerPoints = document.createElement('div');
                playerPoints.className = 'player-points-display';
                playerPoints.textContent = player.points;

                playerItem.appendChild(playerName);
                playerItem.appendChild(playerPoints);

                // Add click event to increment points
                playerItem.addEventListener('click', () => {
                    this.incrementPlayerPoints(player.id);
                });

                this.team2PlayersDisplay.appendChild(playerItem);
            });
        }
    }

    updatePlayerTeam(playerId, teamNumber) {
        // Find the player
        const player = this.app.playersList.find(player => player.id === playerId);

        if (player) {
            // Update team
            player.team = teamNumber;

            // Save state (localStorage + blob sync)
            this.savePlayersState();

            // Update the players display
            this.updatePlayersDisplay();
        }
    }

    exportPlayers() {
        try {
            // Convert players list to JSON string
            const playersData = JSON.stringify(this.app.playersList, null, 2);

            // Copy to clipboard
            navigator.clipboard.writeText(playersData)
                .then(() => {
                    alert('Players data copied to clipboard!');
                })
                .catch(err => {
                    console.error('Failed to copy to clipboard:', err);
                    // Fallback method
                    this.fallbackCopyToClipboard(playersData);
                });
        } catch (error) {
            console.error('Error exporting players:', error);
            alert('Error exporting players data');
        }
    }

    fallbackCopyToClipboard(text) {
        // Create temporary textarea
        const textArea = document.createElement('textarea');
        textArea.value = text;
        textArea.style.position = 'fixed';
        document.body.appendChild(textArea);
        textArea.focus();
        textArea.select();

        try {
            // Execute copy command
            const successful = document.execCommand('copy');
            if (successful) {
                alert('Players data copied to clipboard!');
            } else {
                alert('Unable to copy to clipboard');
            }
        } catch (err) {
            console.error('Fallback clipboard copy failed:', err);
            alert('Unable to copy to clipboard');
        }

        document.body.removeChild(textArea);
    }

    loadDefaultPlayers() {
        try {
            // Show loading message
            const loadingMessage = confirm("Load default players? This will replace your current player list.");
            if (!loadingMessage) {
                return; // User cancelled
            }

            // Fetch the default players from JSON file
            fetch('/Scoreboard/api/default-players')
                .then(response => {
                    if (!response.ok) {
                        throw new Error(`HTTP error! Status: ${response.status}`);
                    }
                    return response.json();
                })
                .then(defaultPlayers => {
                    // Process the imported players
                    this.processImportedPlayers(defaultPlayers, `${defaultPlayers.length} default players loaded successfully!`);
                })
                .catch(error => {
                    console.error('Error loading default players:', error);
                    alert(`Error loading default players: ${error.message}`);
                });
        }
        catch (error) {
            console.error('Error loading default players:', error);
            alert(`Error loading default players: ${error.message}`);
        }
    }

    processImportedPlayers(parsedData, successMessage = 'Players imported successfully!') {
        // Validate the data structure
        if (!Array.isArray(parsedData)) {
            throw new Error('Invalid data format. Expected an array.');
        }

        // Check if each item has required properties
        parsedData.forEach(player => {
            if (!player.hasOwnProperty('name') ||
                !player.hasOwnProperty('team') ||
                !player.hasOwnProperty('active')) {
                throw new Error('Invalid player data format');
            }
            
            // Ensure points property exists
            if (!player.hasOwnProperty('points')) {
                player.points = 0;
            }
        });

        // Add unique IDs if missing
        parsedData.forEach(player => {
            if (!player.id) {
                player.id = Date.now() + Math.floor(Math.random() * 1000);
            }
        });

        // Replace the current players list
        this.app.playersList = parsedData;

        // Save state (localStorage + blob sync)
        this.savePlayersState();

        // Update the UI
        this.updatePlayersList();
        this.updatePlayersDisplay();
    }

    importPlayers() {
        try {
            // Prompt user to paste the JSON data
            const playersData = prompt('Paste players data here:');

            if (!playersData) {
                return; // User cancelled
            }

            // Parse the JSON data
            const parsedData = JSON.parse(playersData);

            // Confirm before replacing
            if (confirm(`Import ${parsedData.length} players? This will replace your current player list.`)) {
                this.processImportedPlayers(parsedData);
            }
        } catch (error) {
            console.error('Error importing players:', error);
            alert(`Error importing players: ${error.message}`);
        }
    }

    updatePlayersList() {
        // Clear the list
        this.playersList.innerHTML = '';

        // Get team names for display
        const team1Name = this.app.teams.team1NameElement.textContent;
        const team2Name = this.app.teams.team2NameElement.textContent;

        // Sort players by team, name, and active status
        const sortedPlayers = [...this.app.playersList].sort((a, b) => {
            // First sort by team
            if (a.team !== b.team) {
                return parseInt(a.team) - parseInt(b.team);
            }
            
            // Then sort by active status (active first)
            if (a.active !== b.active) {
                return b.active ? 1 : -1;
            }
            
            // Finally sort by name (case insensitive)
            return a.name.toLowerCase().localeCompare(b.name.toLowerCase());
        });

        // Add each player to the table
        if (sortedPlayers && Array.isArray(sortedPlayers)) {
            sortedPlayers.forEach(player => {
                const row = document.createElement('tr');

                // Name cell
                const nameCell = document.createElement('td');
                nameCell.textContent = player.name;
                row.appendChild(nameCell);

                // Team cell
                const teamCell = document.createElement('td');
                const teamSelect = document.createElement('select');
                teamSelect.className = 'team-select';

                const team1Option = document.createElement('option');
                team1Option.value = '1';
                team1Option.textContent = team1Name;

                const team2Option = document.createElement('option');
                team2Option.value = '2';
                team2Option.textContent = team2Name;

                teamSelect.appendChild(team1Option);
                teamSelect.appendChild(team2Option);
                teamSelect.value = player.team;

                teamSelect.addEventListener('change', (e) => {
                    this.updatePlayerTeam(player.id, e.target.value);
                });

                teamCell.appendChild(teamSelect);
                row.appendChild(teamCell);

                // Active cell
                const activeCell = document.createElement('td');
                const activeCheckbox = document.createElement('input');
                activeCheckbox.type = 'checkbox';
                activeCheckbox.className = 'player-active';
                activeCheckbox.checked = player.active;

                activeCheckbox.addEventListener('change', (e) => {
                    this.updatePlayerActive(player.id, e.target.checked);
                });

                activeCell.appendChild(activeCheckbox);
                row.appendChild(activeCell);

                // Points cell with increment/decrement controls
                const pointsCell = document.createElement('td');
                pointsCell.className = 'points-cell';
                
                // Create decrement button
                const decrementBtn = document.createElement('button');
                decrementBtn.className = 'points-btn decrement-btn';
                decrementBtn.textContent = '-';
                decrementBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    if (player.points > 0) {
                        player.points--;
                        pointsDisplay.textContent = player.points;
                        
                        // Update team score
                        if (parseInt(player.team) === 1) {
                            this.app.teams.team1Points -= 1;
                        } else {
                            this.app.teams.team2Points -= 1;
                        }
                        this.app.teams.updateScoreDisplay();
                        
                        // Record the score change with player name
                        this.app.recordScoreChange(parseInt(player.team), false, player.name);

                        // Save state (localStorage + blob sync)
                        this.savePlayersState();
                        // Update the players display
                        this.updatePlayersDisplay();
                    }
                });
                
                // Create points display
                const pointsDisplay = document.createElement('span');
                pointsDisplay.className = 'points-display';
                pointsDisplay.textContent = player.points;
                
                // Create increment button
                const incrementBtn = document.createElement('button');
                incrementBtn.className = 'points-btn increment-btn';
                incrementBtn.textContent = '+';
                incrementBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    player.points++;
                    pointsDisplay.textContent = player.points;
                    
                    // Update team score
                    if (parseInt(player.team) === 1) {
                        this.app.teams.team1Points += 1;
                    } else {
                        this.app.teams.team2Points += 1;
                    }
                    this.app.teams.updateScoreDisplay();
                    
                    // Record the score change with player name
                    this.app.recordScoreChange(parseInt(player.team), true, player.name);

                    // Save state (localStorage + blob sync)
                    this.savePlayersState();
                    // Update the players display
                    this.updatePlayersDisplay();
                });
                
                // Add all elements to the cell
                pointsCell.appendChild(decrementBtn);
                pointsCell.appendChild(pointsDisplay);
                pointsCell.appendChild(incrementBtn);
                row.appendChild(pointsCell);

                // Action cell
                const actionCell = document.createElement('td');
                const removeBtn = document.createElement('button');
                removeBtn.className = 'remove-player';
                removeBtn.textContent = 'X';

                removeBtn.addEventListener('click', (e) => {
                    e.stopPropagation(); // Prevent event from bubbling up
                    this.removePlayer(player.id);
                });

                actionCell.appendChild(removeBtn);
                row.appendChild(actionCell);

                // Add the row to the table
                this.playersList.appendChild(row);
            });
        }
    }
}

class UI {
    constructor(app) {
        this.app = app;

        // UI elements
        this.fullscreenBtn = document.getElementById('fullscreen-btn');
        this.scoreHistoryElement = document.getElementById('score-history');

        // Set up event listeners
        this.setupEventListeners();
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
}

class WeeklySetup {
    constructor(app) {
        this.app = app;

        this.weeklyBtn = document.getElementById('weekly-btn');
        this.weeklyPanel = document.getElementById('weekly-panel');
        this.dateInput = document.getElementById('weekly-date-input');
        this.rosterContainer = document.getElementById('weekly-roster');
        this.randomizeBtn = document.getElementById('randomize-teams-btn');
        this.publishBtn = document.getElementById('publish-weekly-btn');
        this.applyBtn = document.getElementById('apply-teams-btn');

        this.available = new Set();
        this.teams = { white: [], black: [] };
        this.forced = [];

        this.dateInput.value = this.getDefaultDate();
        this.setupEventListeners();
    }

    setupEventListeners() {
        this.weeklyBtn.addEventListener('click', () => this.toggle());
        this.randomizeBtn.addEventListener('click', () => this.randomizeTeams());
        this.publishBtn.addEventListener('click', () => this.publish());
        this.applyBtn.addEventListener('click', () => this.applyToScoreboard());
        this.dateInput.addEventListener('change', () => this.loadWeekConfig());

        document.addEventListener('click', (e) => {
            if (!this.weeklyPanel.contains(e.target) &&
                e.target !== this.weeklyBtn &&
                !this.weeklyBtn.contains(e.target)) {
                this.weeklyPanel.classList.remove('active');
            }
        });
    }

    toggle() {
        this.weeklyPanel.classList.toggle('active');
        document.getElementById('settings-panel').classList.remove('active');
        document.getElementById('players-panel').classList.remove('active');
        if (this.weeklyPanel.classList.contains('active')) {
            this.render();
        }
    }

    getDefaultDate() {
        const today = new Date();
        const day = today.getDay();
        const offset = day === 5 ? 0 : (5 - day + 7) % 7;
        const target = new Date(today);
        target.setDate(today.getDate() + offset);
        return target.toISOString().split('T')[0];
    }

    async loadWeekConfig() {
        const date = this.dateInput.value;
        if (!date) return;

        if (this.app.blobSync.isConnected) {
            const config = await this.app.blobSync.download(`week/${date}.json`);
            if (config) {
                this.available = new Set(config.available || []);
                this.teams = config.teams || { white: [], black: [] };
                this.forced = config.forced || [];
                this.render();
                return;
            }
        }

        // No saved config - default all players available
        this.available = new Set(this.app.playersList.map(p => p.id));
        this.teams = { white: [], black: [] };
        this.forced = [];
        this.render();
    }

    render() {
        this.rosterContainer.innerHTML = '';

        const players = [...this.app.playersList].sort((a, b) =>
            a.name.toLowerCase().localeCompare(b.name.toLowerCase()));

        players.forEach(player => {
            const row = document.createElement('div');
            row.className = 'weekly-player-row';

            const isAvail = this.available.has(player.id);
            const forcedEntry = this.forced.find(f => f.playerId === player.id);
            const team = this.teams.white.includes(player.id) ? 'White' :
                         this.teams.black.includes(player.id) ? 'Black' : '';

            const cb = document.createElement('input');
            cb.type = 'checkbox';
            cb.checked = isAvail;
            cb.className = 'weekly-checkbox';
            cb.addEventListener('change', () => {
                if (cb.checked) {
                    this.available.add(player.id);
                } else {
                    this.available.delete(player.id);
                    this.teams.white = this.teams.white.filter(id => id !== player.id);
                    this.teams.black = this.teams.black.filter(id => id !== player.id);
                    this.forced = this.forced.filter(f => f.playerId !== player.id);
                }
                this.render();
            });

            const name = document.createElement('span');
            name.className = 'weekly-player-name';
            name.textContent = player.name;
            if (!isAvail) name.style.opacity = '0.4';

            const pin = document.createElement('select');
            pin.className = 'weekly-pin';
            pin.disabled = !isAvail;
            [{ v: '', t: '\u2014' }, { v: 'white', t: 'Pin W' }, { v: 'black', t: 'Pin B' }].forEach(o => {
                const opt = document.createElement('option');
                opt.value = o.v;
                opt.textContent = o.t;
                if (forcedEntry && forcedEntry.team === o.v) opt.selected = true;
                pin.appendChild(opt);
            });
            pin.addEventListener('change', () => {
                this.forced = this.forced.filter(f => f.playerId !== player.id);
                if (pin.value) {
                    this.forced.push({ playerId: player.id, team: pin.value });
                }
            });

            const badge = document.createElement('span');
            badge.className = 'weekly-badge';
            if (team) {
                badge.textContent = team;
                badge.classList.add(team === 'White' ? 'badge-white' : 'badge-black');
            }

            row.appendChild(cb);
            row.appendChild(name);
            row.appendChild(pin);
            row.appendChild(badge);
            this.rosterContainer.appendChild(row);
        });
    }

    randomizeTeams() {
        let pool = [...this.available];
        this.teams = { white: [], black: [] };

        // Apply forced placements first
        this.forced.forEach(f => {
            if (this.available.has(f.playerId)) {
                this.teams[f.team].push(f.playerId);
                pool = pool.filter(id => id !== f.playerId);
            }
        });

        // Fisher-Yates shuffle
        for (let i = pool.length - 1; i > 0; i--) {
            const j = Math.floor(Math.random() * (i + 1));
            [pool[i], pool[j]] = [pool[j], pool[i]];
        }

        // Distribute evenly
        pool.forEach(id => {
            if (this.teams.white.length <= this.teams.black.length) {
                this.teams.white.push(id);
            } else {
                this.teams.black.push(id);
            }
        });

        this.render();
    }

    async publish() {
        if (!this.app.blobSync.isConnected) {
            alert('Join a group first.');
            return;
        }

        const date = this.dateInput.value;
        if (!date) { alert('Select a date.'); return; }

        const config = {
            date,
            available: [...this.available],
            teams: this.teams,
            forced: this.forced,
            status: 'published'
        };

        const ok = await this.app.blobSync.upload(`week/${date}.json`, config);
        alert(ok ? 'Weekly setup published!' : 'Failed to publish.');
    }

    async applyToScoreboard() {
        if (this.teams.white.length === 0 && this.teams.black.length === 0) {
            alert('Randomize or set up teams first.');
            return;
        }

        this.app.playersList.forEach(player => {
            if (this.teams.white.includes(player.id)) {
                player.team = '1';
                player.active = true;
                player.points = 0;
            } else if (this.teams.black.includes(player.id)) {
                player.team = '2';
                player.active = true;
                player.points = 0;
            } else {
                player.active = false;
                player.points = 0;
            }
        });

        this.app.players.savePlayersState();
        this.app.players.updatePlayersList();
        this.app.players.updatePlayersDisplay();
        this.app.teams.reset();
        this.app.resetScoreHistory();
        this.app.clearScoreHistory();

        this.weeklyPanel.classList.remove('active');
    }
}

// Initialize the app when the DOM is fully loaded
document.addEventListener('DOMContentLoaded', () => {
    window.scoreboardApp = new ScoreboardApp();
});
