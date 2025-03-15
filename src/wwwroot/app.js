
class ScoreboardApp {
    constructor() {
        // Initialize components
        this.timer = new Timer(this);
        this.teams = new Teams(this);
        this.players = new Players(this);
        this.settings = new Settings(this);
        this.ui = new UI(this);

        // Global state
        this.periodScores = [];
        this.wakeLock = null;
        this.team1Players = [];
        this.team2Players = [];

        // Initialize app
        this.loadSavedSettings();
    }

    // Load saved settings from localStorage
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

        // Screen wake lock
        if (localStorage.getItem('keepScreenOn') === 'true') {
            this.settings.keepScreenOnCheckbox.checked = true;
            this.requestWakeLock();
        }
        
        // Load saved players
        if (localStorage.getItem('team1Players')) {
            this.team1Players = JSON.parse(localStorage.getItem('team1Players'));
            this.players.updatePlayersList(1);
        }
        
        if (localStorage.getItem('team2Players')) {
            this.team2Players = JSON.parse(localStorage.getItem('team2Players'));
            this.players.updatePlayersList(2);
        }
    }


    // Capture score at end of timer
    captureScore() {
        this.periodScores.push(`${this.teams.team1Points} - ${this.teams.team2Points}`);
        this.ui.updateScoreHistory();
    }

    // Reset score history
    resetScoreHistory() {
        this.periodScores = [];
        this.ui.updateScoreHistory();
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
        if (teamNumber === 1) {
            this.team1Points++;
        } else {
            this.team2Points++;
        }
        this.updateScoreDisplay();
    }

    decrementScore(teamNumber) {
        if (teamNumber === 1 && this.team1Points > 0) {
            this.team1Points--;
        } else if (teamNumber === 2 && this.team2Points > 0) {
            this.team2Points--;
        }
        this.updateScoreDisplay();
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
    }

    toggle() {
        this.settingsPanel.classList.toggle('active');
    }
}

class Players {
    constructor(app) {
        this.app = app;
        
        // Elements
        this.playersBtn = document.getElementById('players-btn');
        this.playersPanel = document.getElementById('players-panel');
        this.team1PlayersList = document.getElementById('team1-players-list');
        this.team2PlayersList = document.getElementById('team2-players-list');
        this.team1PlayerInput = document.getElementById('team1-player-input');
        this.team2PlayerInput = document.getElementById('team2-player-input');
        this.team1AddPlayerBtn = document.getElementById('team1-add-player-btn');
        this.team2AddPlayerBtn = document.getElementById('team2-add-player-btn');
        this.team1PlayersTitle = document.getElementById('team1-players-title');
        this.team2PlayersTitle = document.getElementById('team2-players-title');
        
        // Initialize
        this.setupEventListeners();
        this.updateTeamTitles();
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
        
        // Add player buttons
        this.team1AddPlayerBtn.addEventListener('click', () => this.addPlayer(1));
        this.team2AddPlayerBtn.addEventListener('click', () => this.addPlayer(2));
        
        // Add player on Enter key
        this.team1PlayerInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                this.addPlayer(1);
            }
        });
        
        this.team2PlayerInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                this.addPlayer(2);
            }
        });
        
        // Update team titles when team names change
        this.app.teams.team1NameElement.addEventListener('input', () => this.updateTeamTitles());
        this.app.teams.team2NameElement.addEventListener('input', () => this.updateTeamTitles());
    }
    
    toggle() {
        this.playersPanel.classList.toggle('active');
        // Close settings panel if open
        document.getElementById('settings-panel').classList.remove('active');
    }
    
    addPlayer(teamNumber) {
        const input = teamNumber === 1 ? this.team1PlayerInput : this.team2PlayerInput;
        const playerName = input.value.trim();
        
        if (playerName) {
            if (teamNumber === 1) {
                this.app.team1Players.push(playerName);
                localStorage.setItem('team1Players', JSON.stringify(this.app.team1Players));
            } else {
                this.app.team2Players.push(playerName);
                localStorage.setItem('team2Players', JSON.stringify(this.app.team2Players));
            }
            
            this.updatePlayersList(teamNumber);
            input.value = '';
            input.focus();
        }
    }
    
    removePlayer(teamNumber, index) {
        if (teamNumber === 1) {
            this.app.team1Players.splice(index, 1);
            localStorage.setItem('team1Players', JSON.stringify(this.app.team1Players));
        } else {
            this.app.team2Players.splice(index, 1);
            localStorage.setItem('team2Players', JSON.stringify(this.app.team2Players));
        }
        
        this.updatePlayersList(teamNumber);
    }
    
    updatePlayersList(teamNumber) {
        const list = teamNumber === 1 ? this.team1PlayersList : this.team2PlayersList;
        const players = teamNumber === 1 ? this.app.team1Players : this.app.team2Players;
        
        list.innerHTML = '';
        
        players.forEach((player, index) => {
            const li = document.createElement('li');
            li.className = 'player-item';
            
            const playerName = document.createElement('span');
            playerName.textContent = player;
            
            const removeBtn = document.createElement('button');
            removeBtn.className = 'remove-player';
            removeBtn.textContent = 'X';
            removeBtn.addEventListener('click', () => this.removePlayer(teamNumber, index));
            
            li.appendChild(playerName);
            li.appendChild(removeBtn);
            list.appendChild(li);
        });
    }
    
    updateTeamTitles() {
        this.team1PlayersTitle.textContent = `${this.app.teams.team1NameElement.textContent} Players`;
        this.team2PlayersTitle.textContent = `${this.app.teams.team2NameElement.textContent} Players`;
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
        this.app.periodScores.forEach((score) => {
            const periodElement = document.createElement('span');
            periodElement.className = 'period-score';
            periodElement.textContent = score;
            periodElement.contentEditable = 'true';
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

// Initialize the app when the DOM is fully loaded
document.addEventListener('DOMContentLoaded', () => {
    window.scoreboardApp = new ScoreboardApp();
});
