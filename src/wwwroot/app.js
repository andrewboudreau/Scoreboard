
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
        this.playersList = [];

        // Initialize app
        this.loadSavedSettings();
    }

    // Load saved settings from localStorage
    loadSavedSettings() {
        // Initialize playersList
        this.playersList = [];

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
        
        // Initialize with any existing player points
        setTimeout(() => {
            if (this.app.players) {
                this.app.players.syncTeamScoresWithPlayerPoints();
            }
        }, 100);

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

            // Save to localStorage
            localStorage.setItem('playersList', JSON.stringify(this.app.playersList));

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

            // Save to localStorage
            localStorage.setItem('playersList', JSON.stringify(this.app.playersList));

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

            // Save to localStorage
            localStorage.setItem('playersList', JSON.stringify(this.app.playersList));

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

            // Save to localStorage
            localStorage.setItem('playersList', JSON.stringify(this.app.playersList));

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

            // Increment team score
            this.app.teams.incrementScore(parseInt(player.team));

            // Save to localStorage
            localStorage.setItem('playersList', JSON.stringify(this.app.playersList));

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
            // Filter active players by team
            const team1Players = this.app.playersList.filter(player => player.team === '1' && player.active);
            const team2Players = this.app.playersList.filter(player => player.team === '2' && player.active);

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
                    // Update team scores
                    this.syncTeamScoresWithPlayerPoints();
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
                    // Update team scores
                    this.syncTeamScoresWithPlayerPoints();
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

            // Save to localStorage
            localStorage.setItem('playersList', JSON.stringify(this.app.playersList));

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
            fetch('default-players.json')
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

        // Save to localStorage
        localStorage.setItem('playersList', JSON.stringify(this.app.playersList));

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

    syncTeamScoresWithPlayerPoints() {
        // Calculate total points for each team
        const team1Points = this.app.playersList
            .filter(player => player.team === '1')
            .reduce((sum, player) => sum + (parseInt(player.points) || 0), 0);
            
        const team2Points = this.app.playersList
            .filter(player => player.team === '2')
            .reduce((sum, player) => sum + (parseInt(player.points) || 0), 0);
        
        // Update team scores
        this.app.teams.team1Points = team1Points;
        this.app.teams.team2Points = team2Points;
        this.app.teams.updateScoreDisplay();
    }

    updatePlayersList() {
        // Clear the list
        this.playersList.innerHTML = '';

        // Get team names for display
        const team1Name = this.app.teams.team1NameElement.textContent;
        const team2Name = this.app.teams.team2NameElement.textContent;

        // Add each player to the table
        if (this.app.playersList && Array.isArray(this.app.playersList)) {
            this.app.playersList.forEach(player => {
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

                // Points cell
                const pointsCell = document.createElement('td');
                const pointsInput = document.createElement('input');
                pointsInput.type = 'number';
                pointsInput.className = 'player-points';
                pointsInput.value = player.points;
                pointsInput.min = 0;

                pointsInput.addEventListener('change', (e) => {
                    this.updatePlayerPoints(player.id, e.target.value);
                });

                pointsCell.appendChild(pointsInput);
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
