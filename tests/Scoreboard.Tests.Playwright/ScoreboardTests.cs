using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright.NUnit;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Scoreboard.Tests.Playwright;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class ScoreboardTests : PageTest
{
    private ScoreboardPage _scoreboard = null!;
    private FakeApi _api = null!;
    private static readonly Regex Active = new(@"\bactive\b");

    // Set the base URL via environment variable SCOREBOARD_BASE_URL, default to localhost:5000
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("SCOREBOARD_BASE_URL") ?? "http://localhost:5227";

    [SetUp]
    public async Task SetUp()
    {
        _scoreboard = new ScoreboardPage(Page, BaseUrl);
        // Serve the API from memory so tests never touch the real store
        _api = new FakeApi();
        await _api.AttachAsync(Page);
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
        await Expect(_scoreboard.SettingsPanel).Not.ToHaveClassAsync(Active);

        // Open settings
        await _scoreboard.OpenSettings();
        await Expect(_scoreboard.SettingsPanel).ToHaveClassAsync(Active);

        // Click outside to close (click on the scoreboard body)
        await Page.Locator("body").ClickAsync(new() { Position = new Position { X = 10, Y = 10 } });
        await Expect(_scoreboard.SettingsPanel).Not.ToHaveClassAsync(Active);
    }

    [Test]
    public async Task PlayersPanel_OpensOnClick()
    {
        await Expect(_scoreboard.PlayersPanel).Not.ToHaveClassAsync(Active);

        await _scoreboard.OpenPlayers();
        await Expect(_scoreboard.PlayersPanel).ToHaveClassAsync(Active);
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

        var firstGameId = await Page.EvaluateAsync<string>("() => window.scoreboardApp.currentGameId");

        // Resetting scores starts a new game; the old one stays in history
        await _scoreboard.ResetScores();
        Assert.That(await _scoreboard.GetTeam1Score(), Is.EqualTo("0"));
        Assert.That(await _scoreboard.GetTeam2Score(), Is.EqualTo("0"));
        Assert.That(await _scoreboard.GetPeriodScoreCount(), Is.EqualTo(0));

        await _scoreboard.ClickTeam1Score();
        await _scoreboard.ClickTeam2Score();
        await _scoreboard.ClickTeam2Score();
        await _scoreboard.SimulateTimerEnd();

        periodScores = await _scoreboard.GetPeriodScores();
        Assert.That(periodScores, Is.EqualTo(new[] { "1 - 2" }));

        await Page.EvaluateAsync("() => window.scoreboardApp.sync.flush()");
        var secondGameId = await Page.EvaluateAsync<string>("() => window.scoreboardApp.currentGameId");
        Assert.That(secondGameId, Is.Not.EqualTo(firstGameId));
        Assert.That(_api.Docs[$"games/{firstGameId}"], Does.Contain("2 - 1"));
    }

    [Test]
    public async Task QuarterEntry_EndQuarterAtTypedScore()
    {
        await _scoreboard.OpenSettings();
        await Page.Locator("#q-team1").FillAsync("12");
        await Page.Locator("#q-team2").FillAsync("9");
        await Page.Locator("#end-quarter-btn").ClickAsync();

        Assert.That(await _scoreboard.GetTeam1Score(), Is.EqualTo("12"));
        Assert.That(await _scoreboard.GetTeam2Score(), Is.EqualTo("9"));
        Assert.That(await _scoreboard.GetPeriodScores(), Is.EqualTo(new[] { "12 - 9" }));

        // Second quarter typed as cumulative score; Q1 must stay intact
        await Page.Locator("#q-team1").FillAsync("20");
        await Page.Locator("#q-team2").FillAsync("22");
        await Page.Locator("#end-quarter-btn").ClickAsync();
        Assert.That(await _scoreboard.GetPeriodScores(), Is.EqualTo(new[] { "12 - 9", "20 - 22" }));
    }

    [Test]
    public async Task ShareTeams_TextListsBothTeams()
    {
        await _scoreboard.AddPlayer("Alice");
        await _scoreboard.AddPlayer("Bob");
        await Expect(Page.Locator("#share-teams-btn")).ToBeVisibleAsync();

        var text = await Page.EvaluateAsync<string>("() => window.scoreboardApp.players.teamsText()");
        Assert.That(text, Does.StartWith("🏀 Teams for"));
        Assert.That(text, Does.Contain("⚫ BLACK (1)\nAlice"));
        Assert.That(text, Does.Contain("⚪ WHITE (1)\nBob"));
    }

    [Test]
    public async Task Roster_SavedToServer()
    {
        await _scoreboard.AddPlayer("Alice");
        await Page.WaitForFunctionAsync("() => !window.scoreboardApp.sync.busy");
        await Page.EvaluateAsync("() => window.scoreboardApp.sync.flush()");
        Assert.That(_api.Docs.GetValueOrDefault("roster"), Does.Contain("Alice"));

        // A fresh page load reads the roster back from the server, not the device
        await Page.EvaluateAsync("() => localStorage.clear()");
        await _scoreboard.GotoAsync();
        await _scoreboard.OpenPlayers();
        await Expect(_scoreboard.PlayerRows).ToHaveCountAsync(1);
    }

    [Test, Explicit("Slow E2E simulation — run manually to regenerate fixture")]
    public async Task FullGameSimulation_FourQuarters_TenPlayers()
    {
        // --- 1. Add 10 players and split them into teams ---
        string[] team1Names = ["Adams", "Baker", "Clark", "Davis", "Evans"];
        string[] team2Names = ["Foster", "Garcia", "Harris", "Irving", "Jones"];

        foreach (var name in team1Names.Concat(team2Names))
            await _scoreboard.AddPlayer(name);

        // Rows are alphabetical: 0-4 = team1Names, 5-9 = team2Names
        for (int i = 0; i < 10; i++)
            await _scoreboard.ChangePlayerTeam(i, i < 5 ? "1" : "2");

        await Expect(_scoreboard.Team2PlayersDisplay.Locator(".player-item-display")).ToHaveCountAsync(5);
        await Page.Locator("#close-teams-btn").ClickAsync();

        await _scoreboard.SetTimerDuration(5);
        await _scoreboard.SettingsBtn.ClickAsync();

        // --- 2. Play 4 quarters: 12-10, 20-21, 34-30, 44-42 ---
        int[][] quarterScoring = [[12, 10], [8, 11], [14, 9], [10, 12]];
        int cumulativeT1 = 0, cumulativeT2 = 0;
        int t1PlayerIdx = 0, t2PlayerIdx = 0;

        for (int q = 0; q < 4; q++)
        {
            int t1Done = 0, t2Done = 0;
            while (t1Done < quarterScoring[q][0] || t2Done < quarterScoring[q][1])
            {
                if (t1Done < quarterScoring[q][0])
                {
                    await _scoreboard.ClickTeam1PlayerDisplay(t1PlayerIdx++ % 5);
                    t1Done++;
                    cumulativeT1++;
                }
                if (t2Done < quarterScoring[q][1])
                {
                    await _scoreboard.ClickTeam2PlayerDisplay(t2PlayerIdx++ % 5);
                    t2Done++;
                    cumulativeT2++;
                }
            }

            Assert.That(await _scoreboard.GetTeam1Score(), Is.EqualTo(cumulativeT1.ToString()));
            Assert.That(await _scoreboard.GetTeam2Score(), Is.EqualTo(cumulativeT2.ToString()));

            await _scoreboard.SimulateTimerEnd();
            await _scoreboard.SettingsBtn.ClickAsync();

            var periodScores = await _scoreboard.GetPeriodScores();
            Assert.That(periodScores, Has.Length.EqualTo(q + 1));
            Assert.That(periodScores[q], Is.EqualTo($"{cumulativeT1} - {cumulativeT2}"));
        }

        // --- 3. Save the fixture ---
        var gameStateJson = await _scoreboard.GetGameStateJson();
        var parsed = JsonSerializer.Deserialize<JsonElement>(gameStateJson);
        var prettyJson = JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true });
        var fixtureDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "Fixtures");
        Directory.CreateDirectory(fixtureDir);
        await File.WriteAllTextAsync(Path.Combine(fixtureDir, "full-game-state.json"), prettyJson);

        // --- 4. The game went to the server and shows on the game page ---
        await Page.EvaluateAsync("() => window.scoreboardApp.sync.flush()");
        var gameId = parsed.GetProperty("id").GetString();
        Assert.That(_api.Docs.ContainsKey($"games/{gameId}"), Is.True);

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/game?id={gameId}");
        await Expect(Page.Locator(".hero")).ToContainTextAsync("44");
        await Expect(Page.Locator(".hero")).ToContainTextAsync("42");
        await Expect(Page.Locator("text=Box score")).ToBeVisibleAsync();
    }

    [Test]
    public async Task StatsPage_DashboardAndGamePage()
    {
        var fixturePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "full-game-state.json");
        Assert.That(File.Exists(fixturePath), Is.True, $"Fixture file not found at {fixturePath}");
        var fixtureJson = await File.ReadAllTextAsync(fixturePath);
        var gameId = JsonDocument.Parse(fixtureJson).RootElement.GetProperty("id").GetString();
        _api.AddGame(fixtureJson);

        // --- Dashboard ---
        await Page.GotoAsync($"{BaseUrl}/Scoreboard/stats");
        var row = Page.Locator(".game-row").First;
        await Expect(row).ToContainTextAsync("44");
        await Expect(row).ToContainTextAsync("42");
        await Expect(row).ToContainTextAsync("Player stats");

        var players = Page.Locator("#players-table");
        await Expect(players).ToContainTextAsync("Adams");
        await Expect(players).ToContainTextAsync("Foster");

        // Tapping a player opens their game log
        await players.Locator("tr.clickable", new() { HasText = "Adams" }).ClickAsync();
        await Expect(Page.Locator("#sheet")).ToBeVisibleAsync();
        await Expect(Page.Locator("#sheet-body")).ToContainTextAsync("Adams");

        // --- Game page ---
        await Page.GotoAsync($"{BaseUrl}/Scoreboard/game?id={gameId}");
        var hero = Page.Locator(".hero");
        await Expect(hero).ToContainTextAsync("BLACK");
        await Expect(hero).ToContainTextAsync("WHITE");
        await Expect(hero).ToContainTextAsync("44");
        await Expect(hero).ToContainTextAsync("42");
        await Expect(hero).ToContainTextAsync("BLACK won by 2");

        var quarters = Page.Locator(".card", new() { HasText = "Quarters" }).Locator("thead th");
        await Expect(quarters).ToHaveTextAsync(new[] { "", "Q1", "Q2", "Q3", "Q4", "Final" });

        var box = Page.Locator(".card", new() { HasText = "Box score" });
        await Expect(box).ToContainTextAsync("Adams");
        await Expect(box).ToContainTextAsync("Foster");
        await Expect(Page.Locator("svg.chart")).ToBeVisibleAsync();
        await Expect(Page.Locator("details summary")).ToContainTextAsync("Play-by-play");
    }

    [Test]
    public async Task StatsPage_TeamScoresOnlyGame()
    {
        // A game where only the quarter totals were typed in, no player points
        var game = new
        {
            id = "game-2026-03-07-aaaa",
            weekDate = "2026-03-07",
            team1 = new { name = "BLACK", score = 40 },
            team2 = new { name = "WHITE", score = 45 },
            periodScores = new[] { "10 - 12", "21 - 20", "30 - 33", "40 - 45" },
            players = new[]
            {
                new { id = 1, name = "Ann", team = "1", active = true, points = 0 },
                new { id = 2, name = "Ben", team = "2", active = true, points = 0 }
            },
            events = new object[]
            {
                new { ts = "2026-03-07T18:10:00Z", type = "score_set", scores = new[] { 10, 12 } },
                new { ts = "2026-03-07T18:10:00Z", type = "period_end", period = 1, scores = new[] { 10, 12 } },
                new { ts = "2026-03-07T18:20:00Z", type = "score_set", scores = new[] { 21, 20 } },
                new { ts = "2026-03-07T18:20:00Z", type = "period_end", period = 2, scores = new[] { 21, 20 } },
                new { ts = "2026-03-07T18:30:00Z", type = "score_set", scores = new[] { 30, 33 } },
                new { ts = "2026-03-07T18:30:00Z", type = "period_end", period = 3, scores = new[] { 30, 33 } },
                new { ts = "2026-03-07T18:40:00Z", type = "score_set", scores = new[] { 40, 45 } },
                new { ts = "2026-03-07T18:40:00Z", type = "period_end", period = 4, scores = new[] { 40, 45 } }
            },
            lastUpdated = "2026-03-07T18:40:00Z"
        };
        _api.AddGame(JsonSerializer.Serialize(game));

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/stats");
        await Expect(Page.Locator(".game-row").First).ToContainTextAsync("Team scores");

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/game?id={game.id}");
        await Expect(Page.Locator(".hero")).ToContainTextAsync("WHITE won by 5");
        await Expect(Page.Locator(".card", new() { HasText = "Who played" })).ToContainTextAsync("Ann");
        await Expect(Page.Locator(".card", new() { HasText = "Box score" })).ToHaveCountAsync(0);
    }
}
