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

    // Set the base URL via environment variable SCOREBOARD_BASE_URL, default to localhost:5227
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
        var lastGame = Page.Locator(".card", new() { HasText = "Last game" });
        await Expect(lastGame).ToContainTextAsync("44");
        await Expect(lastGame).ToContainTextAsync("Adams");
        await Page.Locator("#tabs button[data-tab=games]").ClickAsync();
        var row = Page.Locator(".game-row").First;
        await Expect(row).ToContainTextAsync("44");
        await Expect(row).ToContainTextAsync("42");
        await Expect(row).ToContainTextAsync("Player stats");

        await Page.Locator("#tabs button[data-tab=players]").ClickAsync();
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

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/stats#games");
        await Expect(Page.Locator(".game-row").First).ToContainTextAsync("Team scores");

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/game?id={game.id}");
        await Expect(Page.Locator(".hero")).ToContainTextAsync("WHITE won by 5");
        await Expect(Page.Locator(".card", new() { HasText = "Who played" })).ToContainTextAsync("Ann");
        await Expect(Page.Locator(".card", new() { HasText = "Box score" })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task StatsPage_MixedGames_PlayerStatsOnlyFromTrackedGames()
    {
        // Fully tracked: every point credited
        _api.AddGame(JsonSerializer.Serialize(new
        {
            id = "game-2026-04-04-aaaa",
            weekDate = "2026-04-04",
            team1 = new { name = "BLACK", score = 20 },
            team2 = new { name = "WHITE", score = 18 },
            periodScores = new[] { "20 - 18" },
            players = new[]
            {
                new { id = 1, name = "Amy", team = "1", active = true, points = 20 },
                new { id = 2, name = "Ben", team = "2", active = true, points = 18 }
            },
            events = new object[] { new { ts = "2026-04-04T18:40:00Z", type = "period_end", period = 1, scores = new[] { 20, 18 } } },
            lastUpdated = "2026-04-04T18:40:00Z"
        }));
        // Team scores typed per quarter, one basket credited, team typed as "DARK"
        _api.AddGame(JsonSerializer.Serialize(new
        {
            id = "game-2026-04-11-bbbb",
            weekDate = "2026-04-11",
            team1 = new { name = "DARK", score = 30 },
            team2 = new { name = "WHITE", score = 35 },
            periodScores = new[] { "30 - 35" },
            players = new[]
            {
                new { id = 1, name = "Amy", team = "1", active = true, points = 0 },
                new { id = 2, name = "Ben", team = "2", active = true, points = 0 }
            },
            events = new object[]
            {
                new { ts = "2026-04-11T18:01:00Z", type = "score", team = 1, playerId = 1, player = "Amy", action = "increment", scores = new[] { 2, 0 } },
                new { ts = "2026-04-11T18:40:00Z", type = "period_end", period = 1, scores = new[] { 30, 35 } }
            },
            lastUpdated = "2026-04-11T18:40:00Z"
        }));

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/stats#overview");
        // Colors are reshuffled weekly, so nothing totals BLACK vs WHITE; sides are named by who played
        var lastGame = Page.Locator(".card", new() { HasText = "Last game" });
        await Expect(lastGame).ToContainTextAsync("Ben");
        await Expect(lastGame).ToContainTextAsync("35");
        await Expect(lastGame).Not.ToContainTextAsync("DARK");
        await Expect(Page.Locator(".card", new() { HasText = "Head to head" })).ToHaveCountAsync(0);
        await Expect(Page.Locator(".card", new() { HasText = "Where games are won" })).ToContainTextAsync("Winner avg");

        // Amy played 2 games, but points only count the tracked one: 20 in 1, 20.0 PPG
        await Page.Locator("#tabs button[data-tab=players]").ClickAsync();
        var amy = Page.Locator("#players-table tr.clickable", new() { HasText = "Amy" });
        await Expect(amy).ToContainTextAsync("20 in 1");
        await Expect(amy).ToContainTextAsync("20.0");

        await Page.Locator("#tabs button[data-tab=games]").ClickAsync();
        await Expect(Page.Locator(".game-row", new() { HasText = "30" })).ToContainTextAsync("some players");

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/game?id=game-2026-04-11-bbbb");
        await Expect(Page.Locator(".hero")).ToContainTextAsync("BLACK");
        await Expect(Page.Locator(".card", new() { HasText = "Who played" })).ToContainTextAsync("Only 2 of 65 points");
    }

    // Four games where Amy + Ben always beat Cal + Dee by 6
    private void SeedLopsidedHistory()
    {
        for (int i = 0; i < 4; i++)
        {
            var date = $"2026-03-{i + 1:00}";
            var game = new
            {
                id = $"game-{date}-000{i}",
                weekDate = date,
                team1 = new { name = "BLACK", score = 30 },
                team2 = new { name = "WHITE", score = 24 },
                periodScores = new[] { "30 - 24" },
                players = new[]
                {
                    new { id = 1, name = "Amy", team = "1", active = true, points = 0 },
                    new { id = 2, name = "Ben", team = "1", active = true, points = 0 },
                    new { id = 3, name = "Cal", team = "2", active = true, points = 0 },
                    new { id = 4, name = "Dee", team = "2", active = true, points = 0 }
                },
                events = new object[] { new { ts = $"{date}T18:40:00Z", type = "period_end", period = 1, scores = new[] { 30, 24 } } },
                lastUpdated = $"{date}T18:40:00Z"
            };
            _api.AddGame(JsonSerializer.Serialize(game));
        }
    }

    private void SeedRoster(string amy, string ben, string cal, string dee)
    {
        var roster = new
        {
            team1Name = "BLACK",
            team2Name = "WHITE",
            players = new[]
            {
                new { id = 1, name = "Amy", team = amy, active = true, points = 0 },
                new { id = 2, name = "Ben", team = ben, active = true, points = 0 },
                new { id = 3, name = "Cal", team = cal, active = true, points = 0 },
                new { id = 4, name = "Dee", team = dee, active = true, points = 0 }
            }
        };
        _api.Docs["roster"] = JsonSerializer.Serialize(roster);
    }

    [Test]
    public async Task StatsInsights_ImpactAndPrediction()
    {
        SeedLopsidedHistory();
        SeedRoster("1", "1", "2", "2");

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/stats#insights");
        var impact = Page.Locator(".card", new() { HasText = "Player impact" });
        await Expect(impact).ToContainTextAsync("all 4 games", new() { IgnoreCase = true });
        // Winners rank above losers
        var names = await impact.Locator(".hbar-name").AllTextContentsAsync();
        Assert.That(names.Take(2), Is.EquivalentTo(new[] { "Amy", "Ben" }));
        await Expect(Page.Locator(".card", new() { HasText = "Best duos" })).ToContainTextAsync("Amy + Ben");

        await Page.Locator("#tabs button[data-tab=overview]").ClickAsync();
        // Sides are named after their top-impact player; Amy and Ben always played together
        await Expect(Page.Locator("#matchup")).ToContainTextAsync(new Regex(@"Predicted: Team (Amy|Ben) by"));

        // Streaks follow players: Amy and Ben won all 4
        var streaks = Page.Locator(".card", new() { HasText = "Win streaks" });
        await Expect(streaks).ToContainTextAsync("Amy");
        await Expect(streaks).ToContainTextAsync("4");
    }

    [Test]
    public async Task StatsPage_RemoveGameFromStatsAndPutItBack()
    {
        SeedLopsidedHistory();
        const string id = "game-2026-03-04-0003";

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/game?id={id}");
        var remove = Page.Locator("#hide-btn");
        await Expect(remove).ToHaveTextAsync("Remove from stats");
        await remove.ClickAsync();
        await Expect(remove).ToHaveTextAsync("Tap again to remove");
        await remove.ClickAsync();
        await Expect(Page.Locator(".removed-banner")).ToBeVisibleAsync();
        Assert.That(_api.Docs["hidden-games"], Does.Contain(id));

        // Left out of stats, listed under History so it can be put back
        await Page.GotoAsync($"{BaseUrl}/Scoreboard/stats#games");
        await Expect(Page.Locator(".game-row")).ToHaveCountAsync(3);
        await Expect(Page.Locator("#removed-games")).ToContainTextAsync("30–24");

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/game?id={id}");
        await Page.Locator("#hide-btn", new() { HasText = "Put back in stats" }).ClickAsync();
        await Expect(Page.Locator(".removed-banner")).ToHaveCountAsync(0);
        Assert.That(_api.Docs["hidden-games"], Does.Not.Contain(id));

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/stats#games");
        await Expect(Page.Locator(".game-row")).ToHaveCountAsync(4);
    }

    [Test]
    public async Task PhotoPage_PickNameCropStyleAndSave()
    {
        _api.Docs["roster"] = """
            {"team1Name":"BLACK","team2Name":"WHITE","players":[
              {"id":1,"name":"Zed","team":"1"},{"id":2,"name":"Amy","team":"2"}]}
            """;
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync($"{BaseUrl}/Scoreboard/photo");

        // Names are sorted and searchable
        await Expect(Page.Locator(".person .name")).ToHaveTextAsync(["Amy", "Zed"]);
        await Page.Locator("#search").FillAsync("ze");
        await Expect(Page.Locator(".person")).ToHaveCountAsync(1);
        await Page.Locator(".person").ClickAsync();
        await Expect(Page.Locator("#me-pick")).ToContainTextAsync("Zed");

        // A wide photo, made in the browser
        var png = await Page.EvaluateAsync<string>("""
            () => { const c = document.createElement('canvas'); c.width = 600; c.height = 300;
                    const x = c.getContext('2d'); x.fillStyle = '#c00'; x.fillRect(0, 0, 300, 300);
                    x.fillStyle = '#00c'; x.fillRect(300, 0, 300, 300);
                    return c.toDataURL('image/png').split(',')[1]; }
            """);
        await Page.Locator("#file").SetInputFilesAsync(new FilePayload
        {
            Name = "me.png", MimeType = "image/png", Buffer = Convert.FromBase64String(png)
        });
        await Expect(Page.Locator("#step-crop")).ToBeVisibleAsync();

        // Zoom in, then drag
        await Page.Locator("#zoom").FillAsync("2");
        var box = (await Page.Locator("#stage").BoundingBoxAsync())!;
        await Page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(box.X + box.Width / 2 + 60, box.Y + box.Height / 2, new() { Steps = 5 });
        await Page.Mouse.UpAsync();
        await Page.Locator("#crop-done").ClickAsync();

        // Styles from the server; the fake hands the photo back
        await Expect(Page.Locator(".style-chip")).ToHaveTextAsync(["Original", "Anime", "8-bit"]);
        await Page.Locator(".style-chip", new() { HasText = "Anime" }).ClickAsync();
        await Expect(Page.Locator(".style-chip.selected")).ToHaveTextAsync("Anime");
        await Expect(Page.Locator("#remaining")).ToContainTextAsync("9 AI tries left");
        var stylize = JsonDocument.Parse(_api.Docs["photo/stylize"]).RootElement;
        Assert.That(stylize.GetProperty("playerId").GetInt64(), Is.EqualTo(1));
        Assert.That(stylize.GetProperty("device").GetString(), Is.Not.Empty);

        await Page.Locator("#save").ClickAsync();
        await Expect(Page.Locator("#done-title")).ToHaveTextAsync("Looking good, Zed!");
        var save = JsonDocument.Parse(_api.Docs["photo/save"]).RootElement;
        Assert.That(save.GetProperty("style").GetString(), Is.EqualTo("anime"));
        Assert.That(save.GetProperty("image").GetString(), Does.StartWith("data:image/jpeg;base64,"));
    }

    [Test]
    public async Task TeamsPanel_BalanceSplitsTheStrongPair()
    {
        SeedLopsidedHistory();
        SeedRoster("1", "1", "2", "2");
        await _scoreboard.GotoAsync();
        await _scoreboard.OpenPlayers();
        await Expect(_scoreboard.PlayerRows).ToHaveCountAsync(4);
        await Expect(Page.Locator("#team-prediction")).ToContainTextAsync("BLACK by");

        await Page.Locator("#balance-btn").ClickAsync();
        await Expect(Page.Locator("#team-prediction")).ToContainTextAsync("dead even");

        var teams = await Page.EvaluateAsync<string>(
            "() => window.scoreboardApp.playersList.slice().sort((a, b) => a.id - b.id).map(p => p.team).join('')");
        // Amy and Ben end up on different teams, two a side
        Assert.That(teams[0], Is.Not.EqualTo(teams[1]));
        Assert.That(teams.Count(c => c == '1'), Is.EqualTo(2));
    }

    // ---- Deep links and the guided tour ----

    private static readonly Regex Editing = new(@"\bediting\b");
    private static readonly Regex PlainUrl = new(@"/Scoreboard/$");

    [Test]
    public async Task DeepLink_Players_OpensTeamsPanel()
    {
        SeedRoster("1", "1", "2", "2");
        await _scoreboard.GotoAsync("?players");

        await Expect(_scoreboard.PlayersPanel).ToHaveClassAsync(Active);
        await Expect(_scoreboard.PlayersPanel).Not.ToHaveClassAsync(Editing);
        await Expect(_scoreboard.PlayerRows).ToHaveCountAsync(4);
        await Expect(_scoreboard.Tour).ToHaveCountAsync(0);
        // The param is stripped so a refresh is a plain load
        await Expect(Page).ToHaveURLAsync(PlainUrl);
    }

    [Test]
    public async Task DeepLink_PlayersEdit_OpensEditMode()
    {
        SeedRoster("1", "1", "2", "2");
        await _scoreboard.GotoAsync("?players=edit");

        await Expect(_scoreboard.PlayersPanel).ToHaveClassAsync(Active);
        await Expect(_scoreboard.PlayersPanel).ToHaveClassAsync(Editing);
        await Expect(_scoreboard.EditRosterBtn).ToHaveTextAsync("Done");
        await Expect(_scoreboard.PlayerRows.Locator(".remove-player")).ToHaveCountAsync(4);
        await Expect(_scoreboard.TeamsHelpBtn).ToBeHiddenAsync();
        await Expect(Page).ToHaveURLAsync(PlainUrl);
    }

    [Test]
    public async Task DeepLink_Tour_ShowsStepsAndSkipCloses()
    {
        SeedRoster("1", "1", "2", "2");
        await _scoreboard.GotoAsync("?players&tour");

        await Expect(_scoreboard.PlayersPanel).ToHaveClassAsync(Active);
        await Expect(_scoreboard.TourCard).ToBeVisibleAsync();
        await Expect(_scoreboard.TourCount).ToHaveTextAsync("1 of 8");
        await Expect(_scoreboard.TourBackBtn).ToBeHiddenAsync();
        await Expect(Page).ToHaveURLAsync(PlainUrl);

        await _scoreboard.TourNextBtn.ClickAsync();
        await Expect(_scoreboard.TourCount).ToHaveTextAsync("2 of 8");
        await Expect(_scoreboard.TourCard).ToContainTextAsync("All out");
        await _scoreboard.TourNextBtn.ClickAsync();
        await Expect(_scoreboard.TourCount).ToHaveTextAsync("3 of 8");
        await Expect(_scoreboard.TourCard).ToContainTextAsync("BLACK / OUT / WHITE");
        await _scoreboard.TourBackBtn.ClickAsync();
        await Expect(_scoreboard.TourCount).ToHaveTextAsync("2 of 8");

        await _scoreboard.TourSkipBtn.ClickAsync();
        await Expect(_scoreboard.Tour).ToHaveCountAsync(0);
        // Skipping leaves you on the teams panel, and a refresh doesn't replay the tour
        await Expect(_scoreboard.PlayersPanel).ToHaveClassAsync(Active);
        await Page.ReloadAsync();
        await Expect(_scoreboard.PlayerRows).ToHaveCountAsync(4);
        await Expect(_scoreboard.Tour).ToHaveCountAsync(0);
    }

    [Test]
    public async Task DeepLink_TourAlone_OpensThePanelToo()
    {
        await _scoreboard.GotoAsync("?tour=1");

        await Expect(_scoreboard.PlayersPanel).ToHaveClassAsync(Active);
        await Expect(_scoreboard.TourCount).ToHaveTextAsync("1 of 8");
        await Expect(Page).ToHaveURLAsync(PlainUrl);
    }

    [Test]
    public async Task Tour_WalkedToTheEnd_ChangesNothing()
    {
        SeedRoster("1", "1", "2", "2");
        var roster = _api.Docs["roster"];
        await _scoreboard.GotoAsync("?players&tour");
        await Expect(_scoreboard.PlayerRows).ToHaveCountAsync(4);

        for (var step = 1; step <= 8; step++)
        {
            await Expect(_scoreboard.TourCount).ToHaveTextAsync($"{step} of 8");
            // A tap on the dimmed page is swallowed: the panel stays open and the tour stays put
            await Page.Mouse.ClickAsync(3, 3);
            await Expect(_scoreboard.PlayersPanel).ToHaveClassAsync(Active);
            if (step == 8) await Expect(_scoreboard.TourNextBtn).ToHaveTextAsync("Done");
            await _scoreboard.TourNextBtn.ClickAsync();
        }

        await Expect(_scoreboard.Tour).ToHaveCountAsync(0);
        await Expect(_scoreboard.PlayersPanel).ToHaveClassAsync(Active);
        await Expect(_scoreboard.PlayersPanel).Not.ToHaveClassAsync(Editing);
        // Nothing was saved and nobody moved
        Assert.That(_api.Docs["roster"], Is.EqualTo(roster));
        var teams = await Page.EvaluateAsync<string>(
            "() => window.scoreboardApp.playersList.slice().sort((a, b) => a.id - b.id).map(p => p.team + (p.active ? '' : 'x')).join('')");
        Assert.That(teams, Is.EqualTo("1122"));
    }

    [Test]
    public async Task Tour_HelpButtonStartsIt_SpotlightFollowsTheRealButton()
    {
        SeedRoster("1", "1", "2", "2");
        await _scoreboard.GotoAsync();
        await _scoreboard.OpenPlayers();
        await _scoreboard.TeamsHelpBtn.ClickAsync();
        await Expect(_scoreboard.TourCount).ToHaveTextAsync("1 of 8");

        // Step 2 points at All out: the spotlight frames it and the card stays clear of it
        await _scoreboard.TourNextBtn.ClickAsync();
        await Expect(_scoreboard.TourCount).ToHaveTextAsync("2 of 8");
        await Page.WaitForFunctionAsync("""
            () => {
                const box = s => document.querySelector(s).getBoundingClientRect();
                const btn = box('#all-out-btn'), spot = box('.tour-spot'), card = box('#tour-card');
                const framed = spot.left <= btn.left && spot.top <= btn.top && spot.right >= btn.right && spot.bottom >= btn.bottom
                    && spot.width < btn.width + 20 && spot.height < btn.height + 20;
                const clear = card.top >= spot.bottom || card.bottom <= spot.top || card.left >= spot.right || card.right <= spot.left;
                const onScreen = card.left >= 0 && card.top >= 0 && card.right <= innerWidth && card.bottom <= innerHeight;
                return framed && clear && onScreen;
            }
            """);

        // Escape closes the tour but not the panel
        await Page.Keyboard.PressAsync("Escape");
        await Expect(_scoreboard.Tour).ToHaveCountAsync(0);
        await Expect(_scoreboard.PlayersPanel).ToHaveClassAsync(Active);
    }

    [Test]
    public async Task Tour_NoPlayersYet_ShowsTheCardWithoutASpotlight()
    {
        await _scoreboard.GotoAsync("?players&tour");
        await _scoreboard.TourNextBtn.ClickAsync();
        await _scoreboard.TourNextBtn.ClickAsync();

        await Expect(_scoreboard.TourCount).ToHaveTextAsync("3 of 8");
        await Expect(_scoreboard.Tour).ToHaveClassAsync(new Regex(@"\btour-no-target\b"));
        await Expect(_scoreboard.TourCard).ToBeVisibleAsync();
        await _scoreboard.TourNextBtn.ClickAsync();
        await Expect(_scoreboard.TourCount).ToHaveTextAsync("4 of 8");
    }

    // Older versions kept adding to one saved game across weeks. Stats split it into nights,
    // skip the tap-everything-back-to-zero wind-down, ignore a stray tap a week later,
    // and leave tiny warm-up games out.
    [Test]
    public async Task StatsPage_OldSavedGames_SplitIntoNights()
    {
        var players = new[]
        {
            new { id = 1, name = "Amy", team = "1", active = true, points = 5 },
            new { id = 2, name = "Ben", team = "2", active = true, points = 8 }
        };
        // Two nights in one document; night two starts by tapping the old scores back to 0
        _api.AddGame(JsonSerializer.Serialize(new
        {
            id = "game-2026-04-02-cccc",
            weekDate = "2026-04-16",
            team1 = new { name = "BLACK", score = 5 },
            team2 = new { name = "WHITE", score = 8 },
            players,
            events = new object[]
            {
                new { ts = "2026-04-02T18:01:00Z", type = "score", team = 1, playerId = 1, player = "Amy", action = "increment", scores = new[] { 12, 0 } },
                new { ts = "2026-04-02T18:02:00Z", type = "score", team = 2, playerId = 2, player = "Ben", action = "increment", scores = new[] { 12, 10 } },
                new { ts = "2026-04-02T18:30:00Z", type = "period_end", period = 1, scores = new[] { 12, 10 } },
                new { ts = "2026-04-09T18:00:00Z", type = "score", team = 1, playerId = 1, player = "Amy", action = "decrement", scores = new[] { 0, 10 } },
                new { ts = "2026-04-09T18:00:01Z", type = "score", team = 2, playerId = 2, player = "Ben", action = "decrement", scores = new[] { 0, 0 } },
                new { ts = "2026-04-09T18:05:00Z", type = "score", team = 1, playerId = 1, player = "Amy", action = "increment", scores = new[] { 5, 0 } },
                new { ts = "2026-04-09T18:06:00Z", type = "score", team = 2, playerId = 2, player = "Ben", action = "increment", scores = new[] { 5, 8 } },
                new { ts = "2026-04-09T18:30:00Z", type = "period_end", period = 1, scores = new[] { 5, 8 } }
            },
            lastUpdated = "2026-04-16T18:00:00Z"
        }));
        // One night, then a single stray tap a week later
        _api.AddGame(JsonSerializer.Serialize(new
        {
            id = "game-2026-03-05-dddd",
            team1 = new { name = "BLACK", score = 20 },
            team2 = new { name = "WHITE", score = 21 },
            players,
            events = new object[]
            {
                new { ts = "2026-03-05T18:01:00Z", type = "score", team = 1, action = "increment", scores = new[] { 21, 0 } },
                new { ts = "2026-03-05T18:02:00Z", type = "score", team = 2, action = "increment", scores = new[] { 21, 21 } },
                new { ts = "2026-03-12T17:00:00Z", type = "score", team = 1, action = "decrement", scores = new[] { 20, 21 } }
            },
            lastUpdated = "2026-03-12T17:00:00Z"
        }));
        // A warm-up
        _api.AddGame(JsonSerializer.Serialize(new
        {
            id = "game-2026-03-05-eeee",
            team1 = new { name = "BLACK", score = 3 },
            team2 = new { name = "WHITE", score = 5 },
            events = new object[] { new { ts = "2026-03-05T17:00:00Z", type = "score", team = 2, action = "increment", scores = new[] { 3, 5 } } },
            lastUpdated = "2026-03-05T17:00:00Z"
        }));

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/stats#games");
        var rows = Page.Locator(".game-row");
        await Expect(rows).ToHaveCountAsync(3);
        await Expect(rows.Nth(0)).ToContainTextAsync("Apr 9");
        await Expect(rows.Nth(0).Locator(".score")).ToHaveTextAsync(new Regex(@"5\s*–\s*8"));
        await Expect(rows.Nth(1)).ToContainTextAsync("Apr 2");
        await Expect(rows.Nth(1).Locator(".score")).ToHaveTextAsync(new Regex(@"12\s*–\s*10"));
        await Expect(rows.Nth(2)).ToContainTextAsync("Mar 5");
        await Expect(rows.Nth(2).Locator(".score")).ToHaveTextAsync(new Regex(@"21\s*–\s*21"));
        await Expect(Page.Locator(".note", new() { HasText = "Not counted" })).ToContainTextAsync("1 short game");

        // The earlier night has its own page, with its own player points
        await Page.GotoAsync($"{BaseUrl}/Scoreboard/game?id=game-2026-04-02-cccc~1");
        await Expect(Page.Locator(".hero")).ToContainTextAsync("12");
        await Expect(Page.Locator("tr", new() { HasText = "Amy" }).First).ToContainTextAsync("12");

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/game?id=game-2026-04-02-cccc");
        await Expect(Page.Locator("tr", new() { HasText = "Ben" }).First).ToContainTextAsync("8");
    }

    // Seen on the iPad: a reset that wasn't logged, then player taps took the scores below zero,
    // then team taps brought them back to 0-0. The night starts at that 0-0.
    // Or just a reset that wasn't logged: the first basket of the night (0-1) still counts.
    [Test]
    public async Task StatsPage_NightAfterNegativeWindDown_StartsAtZero()
    {
        _api.AddGame(JsonSerializer.Serialize(new
        {
            id = "game-2026-08-21-ffff",
            team1 = new { name = "BLACK", score = 5 },
            team2 = new { name = "WHITE", score = 6 },
            players = new[]
            {
                new { id = 1, name = "Amy", team = "1", active = true, points = 0 },
                new { id = 2, name = "Ben", team = "2", active = true, points = 0 }
            },
            events = new object[]
            {
                new { ts = "2026-08-20T23:01:00Z", type = "score", team = 1, playerId = 1, player = "Amy", action = "increment", scores = new[] { 30, 0 } },
                new { ts = "2026-08-20T23:02:00Z", type = "score", team = 2, playerId = 2, player = "Ben", action = "increment", scores = new[] { 30, 32 } },
                new { ts = "2026-08-20T23:30:00Z", type = "period_end", period = 1, scores = new[] { 30, 32 } },
                new { ts = "2026-08-27T23:00:00Z", type = "score", team = 1, playerId = 1, player = "Amy", action = "decrement", scores = new[] { -3, 0 } },
                new { ts = "2026-08-27T23:00:01Z", type = "score", team = 2, playerId = 2, player = "Ben", action = "decrement", scores = new[] { -3, -2 } },
                new { ts = "2026-08-27T23:00:05Z", type = "score", team = 1, action = "increment", scores = new[] { 0, -2 } },
                new { ts = "2026-08-27T23:00:06Z", type = "score", team = 2, action = "increment", scores = new[] { 0, 0 } },
                new { ts = "2026-08-27T23:05:00Z", type = "score", team = 1, playerId = 1, player = "Amy", action = "increment", scores = new[] { 4, 0 } },
                new { ts = "2026-08-27T23:06:00Z", type = "score", team = 2, playerId = 2, player = "Ben", action = "increment", scores = new[] { 4, 6 } },
                new { ts = "2026-08-27T23:30:00Z", type = "period_end", period = 1, scores = new[] { 4, 6 } },
                new { ts = "2026-09-03T23:01:00Z", type = "score", team = 2, playerId = 2, player = "Ben", action = "increment", scores = new[] { 0, 1 } },
                new { ts = "2026-09-03T23:02:00Z", type = "score", team = 1, playerId = 1, player = "Amy", action = "increment", scores = new[] { 5, 1 } },
                new { ts = "2026-09-03T23:03:00Z", type = "score", team = 2, playerId = 2, player = "Ben", action = "increment", scores = new[] { 5, 6 } },
                new { ts = "2026-09-03T23:30:00Z", type = "period_end", period = 1, scores = new[] { 5, 6 } }
            },
            lastUpdated = "2026-09-03T23:30:00Z"
        }));

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/stats#games");
        var rows = Page.Locator(".game-row");
        await Expect(rows).ToHaveCountAsync(3);
        await Expect(rows.Nth(0)).ToContainTextAsync("Sep 3");
        await Expect(rows.Nth(0).Locator(".score")).ToHaveTextAsync(new Regex(@"5\s*–\s*6"));
        await Expect(rows.Nth(1)).ToContainTextAsync("Aug 27");
        await Expect(rows.Nth(1).Locator(".score")).ToHaveTextAsync(new Regex(@"4\s*–\s*6"));
        await Expect(rows.Nth(2)).ToContainTextAsync("Aug 20");
        await Expect(rows.Nth(2).Locator(".score")).ToHaveTextAsync(new Regex(@"30\s*–\s*32"));

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/game?id=game-2026-08-21-ffff~2");
        await Expect(Page.Locator("tr", new() { HasText = "Ben" }).First).ToContainTextAsync("6");
        await Page.GotoAsync($"{BaseUrl}/Scoreboard/game?id=game-2026-08-21-ffff");
        await Expect(Page.Locator("tr", new() { HasText = "Ben" }).First).ToContainTextAsync("6");
    }

    // Each iPad kept its own player list, so one person can have two ids. Same name as a roster
    // player counts as them; anyone else can be linked by hand from their stats page.
    [Test]
    public async Task StatsPlayers_SamePersonFromAnotherIpad_CountsOnce()
    {
        _api.Docs["roster"] = JsonSerializer.Serialize(new
        {
            team1Name = "BLACK",
            team2Name = "WHITE",
            players = new[]
            {
                new { id = 10, name = "Amy", team = "1", active = true },
                new { id = 20, name = "Ben", team = "2", active = true }
            }
        });
        object Game(string id, string date, int amyId, int benId, string benName) => new
        {
            id,
            team1 = new { name = "BLACK", score = 6 },
            team2 = new { name = "WHITE", score = 5 },
            players = new[]
            {
                new { id = amyId, name = "Amy", team = "1", active = true, points = 6 },
                new { id = benId, name = benName, team = "2", active = true, points = 5 }
            },
            events = new object[]
            {
                new { ts = $"{date}T23:01:00Z", type = "score", team = 1, playerId = amyId, player = "Amy", action = "increment", scores = new[] { 6, 0 } },
                new { ts = $"{date}T23:02:00Z", type = "score", team = 2, playerId = benId, player = benName, action = "increment", scores = new[] { 6, 5 } }
            },
            lastUpdated = $"{date}T23:30:00Z"
        };
        _api.AddGame(JsonSerializer.Serialize(Game("game-2026-03-05-aaaa", "2026-03-05", 10, 20, "Ben")));
        _api.AddGame(JsonSerializer.Serialize(Game("game-2026-09-03-bbbb", "2026-09-03", 11, 21, "Benny")));

        await Page.GotoAsync($"{BaseUrl}/Scoreboard/stats#players");
        var table = Page.Locator("#players-table");
        var gp = (string name) => table.Locator("tr.clickable", new() { Has = Page.Locator("td.name", new() { HasTextRegex = new Regex($"^{name}$") }) }).Locator("td").Nth(1);
        await Expect(gp("Amy")).ToHaveTextAsync("2");
        await Expect(gp("Ben")).ToHaveTextAsync("1");
        await Expect(gp("Benny")).ToHaveTextAsync("1");

        await Page.Locator("#tabs button[data-tab=games]").ClickAsync();
        await Expect(Page.Locator("#data-check")).ToContainTextAsync("Benny");

        await Page.Locator("#data-check [data-player]", new() { HasText = "Benny" }).ClickAsync();
        await Page.Locator("#same-as").SelectOptionAsync(new SelectOptionValue { Label = "Ben · 1 game" });
        await Page.Locator("#same-link").ClickAsync();
        await Expect(Page.Locator("#sheet")).ToBeHiddenAsync(); // the page reloads after saving

        Assert.That(_api.Docs["player-links"], Does.Contain("\"21\":\"20\""));
        await Page.Locator("#tabs button[data-tab=players]").ClickAsync();
        await Expect(gp("Ben")).ToHaveTextAsync("2");
        await Expect(table.Locator("td.name", new() { HasTextRegex = new Regex("^Benny$") })).ToHaveCountAsync(0);
    }

    // Whoever scores first on a new night gets a fresh game, without tapping New game
    [Test]
    public async Task StaleGame_FirstScoreOfTheNightStartsNewGame()
    {
        var lastWeek = DateTime.UtcNow.AddDays(-7).ToString("o");
        _api.AddGame(JsonSerializer.Serialize(new
        {
            id = "game-old-night",
            team1 = new { name = "BLACK", score = 30 },
            team2 = new { name = "WHITE", score = 28 },
            players = Array.Empty<object>(),
            events = new object[] { new { ts = lastWeek, type = "score", team = 1, action = "increment", scores = new[] { 30, 28 } } },
            lastUpdated = lastWeek
        }));
        _api.Docs["current-game"] = """{"gameId":"game-old-night"}""";

        await _scoreboard.GotoAsync();
        await Expect(_scoreboard.Team1Score).ToHaveTextAsync("30");
        await Expect(_scoreboard.ScoreHistory).ToContainTextAsync("next score starts a new game");

        // Taking points back doesn't touch last week's game
        await _scoreboard.DecrementTeam1();
        await Expect(_scoreboard.Team1Score).ToHaveTextAsync("30");

        await _scoreboard.ClickTeam1Score();
        await Expect(_scoreboard.Team1Score).ToHaveTextAsync("1");
        await Expect(_scoreboard.Team2Score).ToHaveTextAsync("0");
        await Expect(_scoreboard.ScoreHistory).Not.ToContainTextAsync("new game");

        // The new game is saved and becomes the current game; last week's is untouched
        for (var i = 0; i < 50 && (_api.Docs.GetValueOrDefault("current-game") ?? "").Contains("game-old-night"); i++)
            await Task.Delay(200);
        Assert.That(_api.Docs["current-game"], Does.Not.Contain("game-old-night"));
        Assert.That(_api.Docs["games/game-old-night"], Does.Contain("\"score\":30"));
    }

    // A device's saved game whose id is already on the server, but with scores the server
    // doesn't have, is uploaded as its own game instead of being dropped
    [Test]
    public async Task DeviceData_ConflictingGameIsKeptUnderNewId()
    {
        _api.AddGame("""{"id":"game-2026-04-03-ffff","team1":{"name":"BLACK","score":2},"team2":{"name":"WHITE","score":0},"events":[{"ts":"2026-04-03T01:00:00Z","type":"score","team":1,"scores":[2,0]}]}""");
        await Page.EvaluateAsync("""
            () => {
                localStorage.setItem('currentGameId', 'game-2026-04-03-ffff');
                localStorage.setItem('gameEvents', JSON.stringify([
                    { ts: '2026-04-03T01:00:00Z', type: 'score', team: 1, scores: [2, 0] },
                    { ts: '2026-05-15T01:00:00Z', type: 'score', team: 2, scores: [2, 7] }
                ]));
            }
            """);
        await _scoreboard.GotoAsync();

        string DeviceCopy() => _api.Docs.Keys.FirstOrDefault(k => k.StartsWith("games/game-2026-04-03-ffff-device-")) ?? "";
        for (var i = 0; i < 50 && DeviceCopy() == ""; i++)
            await Task.Delay(200);
        Assert.That(DeviceCopy(), Is.Not.Empty);
        Assert.That(_api.Docs[DeviceCopy()], Does.Contain("2,7"));
        Assert.That(_api.Docs["games/game-2026-04-03-ffff"], Does.Not.Contain("2,7"));
        Assert.That(await Page.EvaluateAsync<string?>("() => localStorage.getItem('gameEvents')"), Is.Null);
        Assert.That(await Page.EvaluateAsync<string?>("() => localStorage.getItem('oldDataBackup')"), Does.Contain("2,7"));
    }
}
