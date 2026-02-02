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
}
