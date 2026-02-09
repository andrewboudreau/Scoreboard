using System.Text.Json;
using Microsoft.Playwright.NUnit;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Scoreboard.Tests.Playwright;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class ScoreboardTests : PageTest
{
    private ScoreboardPage _scoreboard = null!;

    // Set the base URL via environment variable SCOREBOARD_BASE_URL, default to localhost:5000
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("SCOREBOARD_BASE_URL") ?? "http://localhost:5227";

    [SetUp]
    public async Task SetUp()
    {
        _scoreboard = new ScoreboardPage(Page, BaseUrl);
        // Clear localStorage before each test
        await Page.GotoAsync($"{BaseUrl}/Scoreboard/");
        await Page.EvaluateAsync("() => localStorage.clear()");
        await _scoreboard.GotoAsync();
    }

    [Test]
    public async Task PageLoads_ScoreboardRendersWithDefaults()
    {
        // Scores should be 0-0
        Assert.That(await _scoreboard.GetTeam1Score(), Is.EqualTo("0"));
        Assert.That(await _scoreboard.GetTeam2Score(), Is.EqualTo("0"));

        // Timer should show a time
        var timer = await _scoreboard.GetTimerText();
        Assert.That(timer, Does.Match(@"^\d{2}:\d{2}$"));
    }

    [Test]
    public async Task ScoreByMainButton_ClickTeam1_Increments()
    {
        await _scoreboard.ClickTeam1Score();

        Assert.That(await _scoreboard.GetTeam1Score(), Is.EqualTo("1"));
        Assert.That(await _scoreboard.GetTeam2Score(), Is.EqualTo("0"));
    }

    [Test]
    public async Task ScoreByPlayer_AddPlayerAndClick_IncrementsTeamAndPlayerPoints()
    {
        await _scoreboard.AddPlayer("TestPlayer");

        // Wait for the player to appear in the display
        await _scoreboard.Team1PlayersDisplay.Locator(".player-item-display").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible });

        // Click the player in the team display to score
        await _scoreboard.ClickTeam1PlayerDisplay(0);

        Assert.That(await _scoreboard.GetTeam1Score(), Is.EqualTo("1"));
        Assert.That(await _scoreboard.GetTeam1PlayerPoints(0), Is.EqualTo("1"));
    }

    [Test]
    public async Task DecrementScore_UsesSettingsButton()
    {
        // First increment to 1
        await _scoreboard.ClickTeam1Score();
        Assert.That(await _scoreboard.GetTeam1Score(), Is.EqualTo("1"));

        // Decrement via settings
        await _scoreboard.DecrementTeam1();

        Assert.That(await _scoreboard.GetTeam1Score(), Is.EqualTo("0"));
    }

    [Test]
    public async Task ResetScores_BothTeamsReturnToZero()
    {
        // Score some points
        await _scoreboard.ClickTeam1Score();
        await _scoreboard.ClickTeam1Score();
        await _scoreboard.ClickTeam2Score();

        Assert.That(await _scoreboard.GetTeam1Score(), Is.EqualTo("2"));
        Assert.That(await _scoreboard.GetTeam2Score(), Is.EqualTo("1"));

        // Reset
        await _scoreboard.ResetScores();

        Assert.That(await _scoreboard.GetTeam1Score(), Is.EqualTo("0"));
        Assert.That(await _scoreboard.GetTeam2Score(), Is.EqualTo("0"));
    }

    [Test]
    public async Task TimerStartStop_ClickStartsThenStops()
    {
        var initialTime = await _scoreboard.GetTimerText();

        // Start timer
        await _scoreboard.ClickTimerDisplay();

        // Wait a bit for the timer to tick
        await Page.WaitForTimeoutAsync(1500);

        // Stop timer
        await _scoreboard.ClickTimerDisplay();

        var afterTime = await _scoreboard.GetTimerText();

        // Timer should have changed from initial value
        Assert.That(afterTime, Is.Not.EqualTo(initialTime));
    }

    [Test]
    public async Task TimerReset_DoubleClickResetsTimer()
    {
        // Set timer to a known value first
        await _scoreboard.SetTimerDuration(5);
        var expectedTime = "05:00";

        // Start and let it tick
        await _scoreboard.ClickTimerDisplay();
        await Page.WaitForTimeoutAsync(1500);
        await _scoreboard.ClickTimerDisplay();

        // Double-click to reset
        await _scoreboard.DoubleClickTimerDisplay();

        var resetTime = await _scoreboard.GetTimerText();
        Assert.That(resetTime, Is.EqualTo(expectedTime));
    }

    [Test]
    public async Task SetTimerDuration_ChangesTimerDisplay()
    {
        await _scoreboard.SetTimerDuration(7);

        var timerText = await _scoreboard.GetTimerText();
        Assert.That(timerText, Is.EqualTo("07:00"));
    }

    [Test]
    public async Task PeriodCapture_SimulateTimerEnd_CreatesHistoryEntry()
    {
        // Score some points
        await _scoreboard.ClickTeam1Score();
        await _scoreboard.ClickTeam1Score();
        await _scoreboard.ClickTeam2Score();

        // Simulate timer end
        await _scoreboard.SimulateTimerEnd();

        // Check period score appeared
        var count = await _scoreboard.GetPeriodScoreCount();
        Assert.That(count, Is.EqualTo(1));

        var scores = await _scoreboard.GetPeriodScores();
        Assert.That(scores[0], Is.EqualTo("2 - 1"));
    }

    [Test]
    public async Task PeriodScoreCorrection_ClickUpdatesToCurrentScores()
    {
        // Score and capture period
        await _scoreboard.ClickTeam1Score();
        await _scoreboard.ClickTeam1Score();
        await _scoreboard.SimulateTimerEnd();

        // Change current scores
        await _scoreboard.ClickTeam1Score();
        await _scoreboard.ClickTeam2Score();
        await _scoreboard.ClickTeam2Score();

        // Now current is 3-2, period was "2 - 0"
        // Click the period score to correct it
        await _scoreboard.ClickPeriodScore(0);

        var scores = await _scoreboard.GetPeriodScores();
        Assert.That(scores[0], Is.EqualTo("3 - 2"));
    }

    [Test]
    public async Task PlayerManagement_AddAndRemovePlayer()
    {
        await _scoreboard.AddPlayer("Alice");

        // Verify player is in the list
        var count = await _scoreboard.GetPlayerRowCount();
        Assert.That(count, Is.EqualTo(1));

        // Remove the player
        await _scoreboard.RemovePlayer(0);

        count = await _scoreboard.GetPlayerRowCount();
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task PlayerTeamAssignment_ChangeMovesPlayer()
    {
        await _scoreboard.AddPlayer("Bob");

        // Initially on team 1 — should appear in team 1 display
        await _scoreboard.Team1PlayersDisplay.Locator(".player-item-display").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible });

        var team1Count = await _scoreboard.Team1PlayersDisplay.Locator(".player-item-display").CountAsync();
        Assert.That(team1Count, Is.EqualTo(1));

        // Change to team 2
        await _scoreboard.ChangePlayerTeam(0, "2");

        // Wait for re-render
        await _scoreboard.Team2PlayersDisplay.Locator(".player-item-display").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible });

        team1Count = await _scoreboard.Team1PlayersDisplay.Locator(".player-item-display").CountAsync();
        var team2Count = await _scoreboard.Team2PlayersDisplay.Locator(".player-item-display").CountAsync();

        Assert.That(team1Count, Is.EqualTo(0));
        Assert.That(team2Count, Is.EqualTo(1));
    }

    [Test]
    public async Task SettingsPanel_OpensAndCloses()
    {
        // Should be hidden initially
        await Expect(_scoreboard.SettingsPanel).Not.ToHaveClassAsync("active");

        // Open settings
        await _scoreboard.OpenSettings();
        await Expect(_scoreboard.SettingsPanel).ToHaveClassAsync("side-panel active");

        // Click outside to close (click on the scoreboard body)
        await Page.Locator("body").ClickAsync(new() { Position = new Position { X = 10, Y = 10 } });
        await Expect(_scoreboard.SettingsPanel).Not.ToHaveClassAsync("active");
    }

    [Test]
    public async Task PlayersPanel_OpensOnClick()
    {
        await Expect(_scoreboard.PlayersPanel).Not.ToHaveClassAsync("active");

        await _scoreboard.OpenPlayers();
        await Expect(_scoreboard.PlayersPanel).ToHaveClassAsync("side-panel active");
    }

    [Test]
    public async Task ScoreFontSize_SliderChangesSize()
    {
        await _scoreboard.OpenSettings();

        // Get initial font size
        var initial = await _scoreboard.GetScoreFontSize();

        // Move slider to a different value
        await _scoreboard.ScoreFontSizeSlider.FillAsync("12");
        // Trigger input event
        await _scoreboard.ScoreFontSizeSlider.DispatchEventAsync("input");

        var updated = await _scoreboard.GetScoreFontSize();
        Assert.That(updated, Is.EqualTo("12rem"));
    }

    [Test]
    public async Task FullWorkflow_PlayersScoreTimerCaptureReset()
    {
        // Add two players
        await _scoreboard.AddPlayer("Alice");
        await _scoreboard.AddPlayer("Bob");

        // Move Bob to team 2
        await _scoreboard.ChangePlayerTeam(1, "2");

        // Wait for re-render
        await _scoreboard.Team2PlayersDisplay.Locator(".player-item-display").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible });

        // Score via player click (Alice on team 1)
        await _scoreboard.ClickTeam1PlayerDisplay(0);
        await _scoreboard.ClickTeam1PlayerDisplay(0);

        // Score via team 2 main button
        await _scoreboard.ClickTeam2Score();

        Assert.That(await _scoreboard.GetTeam1Score(), Is.EqualTo("2"));
        Assert.That(await _scoreboard.GetTeam2Score(), Is.EqualTo("1"));

        // Simulate timer end — captures period
        await _scoreboard.SimulateTimerEnd();

        var periodCount = await _scoreboard.GetPeriodScoreCount();
        Assert.That(periodCount, Is.EqualTo(1));

        var periodScores = await _scoreboard.GetPeriodScores();
        Assert.That(periodScores[0], Is.EqualTo("2 - 1"));

        // Reset scores for next period
        await _scoreboard.ResetScores();
        Assert.That(await _scoreboard.GetTeam1Score(), Is.EqualTo("0"));
        Assert.That(await _scoreboard.GetTeam2Score(), Is.EqualTo("0"));

        // Score more and capture second period
        await _scoreboard.ClickTeam1Score();
        await _scoreboard.ClickTeam2Score();
        await _scoreboard.ClickTeam2Score();

        await _scoreboard.SimulateTimerEnd();

        periodCount = await _scoreboard.GetPeriodScoreCount();
        Assert.That(periodCount, Is.EqualTo(2));

        periodScores = await _scoreboard.GetPeriodScores();
        Assert.That(periodScores[1], Is.EqualTo("1 - 2"));
    }

    [Test, Explicit("Slow E2E simulation — run manually to regenerate fixture")]
    public async Task FullGameSimulation_FourQuarters_TenPlayers()
    {
        // --- 1. Create a fresh group so this test is fully self-contained ---
        // Auto-accept all dialogs (prompt for group name, alert with admin code)
        var groupName = $"E2E Sim {DateTime.UtcNow:yyyy-MM-dd HH:mm}";
        Page.Dialog += async (_, dialog) =>
        {
            if (dialog.Type == "prompt")
                await dialog.AcceptAsync(groupName);
            else
                await dialog.AcceptAsync();
        };

        await _scoreboard.OpenSettings();
        await Page.Locator("#create-group-btn").ClickAsync();

        // Wait for group connection to establish
        await Page.WaitForFunctionAsync("() => window.scoreboardApp.blobSync.isConnected",
            new PageWaitForFunctionOptions { Timeout = 15000 });
        TestContext.WriteLine($"Group created: {groupName}");

        // Close settings
        await _scoreboard.SettingsBtn.ClickAsync();

        // --- 2. Add 10 players ---
        string[] team1Names = ["Adams", "Baker", "Clark", "Davis", "Evans"];
        string[] team2Names = ["Foster", "Garcia", "Harris", "Irving", "Jones"];

        foreach (var name in team1Names.Concat(team2Names))
            await _scoreboard.AddPlayer(name);

        // Move players 5-9 to team 2
        // All on team 1 sorted alphabetically:
        // 0=Adams, 1=Baker, 2=Clark, 3=Davis, 4=Evans, 5=Foster, 6=Garcia, 7=Harris, 8=Irving, 9=Jones
        for (int i = 5; i <= 9; i++)
            await _scoreboard.ChangePlayerTeam(i, "2");

        // Wait for team 2 display to show 5 players
        await _scoreboard.Team2PlayersDisplay.Locator(".player-item-display").Nth(4)
            .WaitForAsync(new() { State = WaitForSelectorState.Visible });

        // Close players panel before scoring
        await _scoreboard.PlayersBtn.ClickAsync();

        // Set timer to 5 minutes
        await _scoreboard.SetTimerDuration(5);

        // Close settings panel
        await _scoreboard.SettingsBtn.ClickAsync();

        // --- 3. Play 4 quarters ---
        // Q1: T1+12, T2+10 → 12-10
        // Q2: T1+8,  T2+11 → 20-21
        // Q3: T1+14, T2+9  → 34-30
        // Q4: T1+10, T2+12 → 44-42
        int[][] quarterScoring =
        [
            [12, 10],
            [8, 11],
            [14, 9],
            [10, 12]
        ];

        int cumulativeT1 = 0, cumulativeT2 = 0;
        int t1PlayerIdx = 0, t2PlayerIdx = 0;

        for (int q = 0; q < 4; q++)
        {
            int t1Pts = quarterScoring[q][0];
            int t2Pts = quarterScoring[q][1];

            // Interleave scoring: alternate team1 and team2 clicks
            int t1Done = 0, t2Done = 0;
            while (t1Done < t1Pts || t2Done < t2Pts)
            {
                if (t1Done < t1Pts)
                {
                    await _scoreboard.ClickTeam1PlayerDisplay(t1PlayerIdx % 5);
                    t1PlayerIdx++;
                    t1Done++;
                    cumulativeT1++;
                }

                if (t2Done < t2Pts)
                {
                    await _scoreboard.ClickTeam2PlayerDisplay(t2PlayerIdx % 5);
                    t2PlayerIdx++;
                    t2Done++;
                    cumulativeT2++;
                }
            }

            // Verify cumulative scores
            Assert.That(await _scoreboard.GetTeam1Score(), Is.EqualTo(cumulativeT1.ToString()));
            Assert.That(await _scoreboard.GetTeam2Score(), Is.EqualTo(cumulativeT2.ToString()));

            // Simulate timer end to capture period
            await _scoreboard.SimulateTimerEnd();

            // Verify period count and score
            Assert.That(await _scoreboard.GetPeriodScoreCount(), Is.EqualTo(q + 1));
            var periodScores = await _scoreboard.GetPeriodScores();
            Assert.That(periodScores[q], Is.EqualTo($"{cumulativeT1} - {cumulativeT2}"));

            TestContext.WriteLine($"Q{q + 1} done: {cumulativeT1} - {cumulativeT2}");
        }

        // Final score assertions
        Assert.That(cumulativeT1, Is.EqualTo(44));
        Assert.That(cumulativeT2, Is.EqualTo(42));

        // --- 4. Extract game state and save fixture ---
        var gameStateJson = await _scoreboard.GetGameStateJson();
        Assert.That(gameStateJson, Is.Not.Null.And.Not.Empty);

        var parsed = JsonSerializer.Deserialize<JsonElement>(gameStateJson);
        var prettyJson = JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true });

        var fixtureDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "Fixtures");
        Directory.CreateDirectory(fixtureDir);
        var fixturePath = Path.Combine(fixtureDir, "full-game-state.json");
        await File.WriteAllTextAsync(fixturePath, prettyJson);

        TestContext.WriteLine($"Fixture saved to: {fixturePath}");
        TestContext.WriteLine($"Events in state: {parsed.GetProperty("events").GetArrayLength()}");

        // --- 5. Force sync to blob storage ---
        await Page.EvaluateAsync(@"async () => {
            const app = window.scoreboardApp;
            const state = app._buildGameState();
            await app.blobSync.upload('games/' + app.currentGameId + '.json', state);
            await app.blobSync.upload('active-game.json', { gameId: app.currentGameId });
            await app.blobSync.upload('roster.json', { players: app.playersList });
        }");
        TestContext.WriteLine("Game synced to blob storage");

        // --- 6. Verify game appears on stats page via real blob flow ---
        await Page.GotoAsync($"{BaseUrl}/Scoreboard/stats");
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        // Wait for game cards to load from blob (may take a moment)
        var gameCard = Page.Locator(".game-card").First;
        await gameCard.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        // Verify card shows correct final score
        await Expect(gameCard).ToContainTextAsync("44");
        await Expect(gameCard).ToContainTextAsync("42");

        // Click into detail view
        await gameCard.ClickAsync();
        await Page.Locator("#detail-view").WaitForAsync(new() { State = WaitForSelectorState.Visible });

        // Verify detail view
        var finalScore = Page.Locator(".final-score");
        await Expect(finalScore).ToContainTextAsync("44");
        await Expect(finalScore).ToContainTextAsync("42");
        Assert.That(await Page.Locator(".periods-table tbody tr").CountAsync(), Is.EqualTo(4));
        await Expect(Page.Locator(".player-stats-table")).ToContainTextAsync("Adams");
        await Expect(Page.Locator(".player-stats-table")).ToContainTextAsync("Foster");
        await Expect(Page.Locator(".scoring-timeline")).ToBeVisibleAsync();
        await Expect(Page.Locator("#score-canvas")).ToBeVisibleAsync();

        TestContext.WriteLine("Stats page verified via blob storage");
    }

    [Test]
    public async Task StatsPage_ShowsGameDetailCorrectly()
    {
        // Load the fixture JSON
        var fixturePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "full-game-state.json");
        Assert.That(File.Exists(fixturePath), Is.True, $"Fixture file not found at {fixturePath}");
        var fixtureJson = await File.ReadAllTextAsync(fixturePath);

        // Navigate to stats page
        await Page.GotoAsync($"{BaseUrl}/Scoreboard/stats");
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        // Inject the fixture data and render the detail view.
        // The stats page uses `let games` at script scope, which isn't accessible
        // from Page.EvaluateAsync (uses new Function). Inject via <script> tag instead.
        await Page.EvaluateAsync("(json) => { window.__fixture = json; }", fixtureJson);
        await Page.EvaluateAsync(@"() => {
            document.getElementById('loading').style.display = 'none';
            document.getElementById('connect').style.display = 'none';
            const s = document.createElement('script');
            s.textContent = 'games = [JSON.parse(window.__fixture)]; showDetail(0);';
            document.head.appendChild(s);
        }");

        // Wait for detail view to be visible
        await Page.Locator("#detail-view").WaitForAsync(new() { State = WaitForSelectorState.Visible });

        // Assert: Final score section exists
        var finalScore = Page.Locator(".final-score");
        await Expect(finalScore).ToBeVisibleAsync();

        // Verify team names and scores in the final score section
        var scoreRow = finalScore.Locator(".score-row");
        await Expect(scoreRow).ToContainTextAsync("BLACK");
        await Expect(scoreRow).ToContainTextAsync("WHITE");
        await Expect(scoreRow).ToContainTextAsync("44");
        await Expect(scoreRow).ToContainTextAsync("42");

        // Assert: 4 period rows in the periods table
        var periodRows = Page.Locator(".periods-table tbody tr");
        Assert.That(await periodRows.CountAsync(), Is.EqualTo(4));

        // Verify period labels
        await Expect(periodRows.Nth(0)).ToContainTextAsync("P1");
        await Expect(periodRows.Nth(3)).ToContainTextAsync("P4");

        // Assert: Player stats table has entries for both teams
        var playerStatsTable = Page.Locator(".player-stats-table");
        await Expect(playerStatsTable).ToBeVisibleAsync();

        // Should have team headers for BLACK and WHITE
        await Expect(playerStatsTable).ToContainTextAsync("BLACK");
        await Expect(playerStatsTable).ToContainTextAsync("WHITE");

        // Should contain player names from both teams
        await Expect(playerStatsTable).ToContainTextAsync("Adams");
        await Expect(playerStatsTable).ToContainTextAsync("Foster");

        // Assert: Scoring timeline exists and has events
        var timeline = Page.Locator(".scoring-timeline");
        await Expect(timeline).ToBeVisibleAsync();
        var timelineItems = timeline.Locator("li");
        // Should have many events (86 scores + 4 period ends = 90)
        Assert.That(await timelineItems.CountAsync(), Is.GreaterThanOrEqualTo(10));

        // Assert: Score graph canvas exists (need >= 2 score events)
        var canvas = Page.Locator("#score-canvas");
        await Expect(canvas).ToBeVisibleAsync();
    }
}
