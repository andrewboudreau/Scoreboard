using Microsoft.Playwright;

namespace Scoreboard.Tests.Playwright;

/// <summary>
/// Page object model for the scoreboard main page.
/// Encapsulates element selectors and common actions.
/// </summary>
public class ScoreboardPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public ScoreboardPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    // Navigation
    public async Task GotoAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/Scoreboard/");
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    // Score elements
    public ILocator Team1Score => _page.Locator("#score-team1");
    public ILocator Team2Score => _page.Locator("#score-team2");
    public ILocator Team1Name => _page.Locator(".team:nth-of-type(1) .team-name");
    public ILocator Team2Name => _page.Locator(".team:nth-of-type(2) .team-name");

    // Timer
    public ILocator TimerDisplay => _page.Locator("#timer");

    // Score history
    public ILocator ScoreHistory => _page.Locator("#score-history");
    public ILocator PeriodScores => _page.Locator("#score-history .period-score");

    // Control buttons
    public ILocator SettingsBtn => _page.Locator("#settings-btn");
    public ILocator PlayersBtn => _page.Locator("#players-btn");
    public ILocator FullscreenBtn => _page.Locator("#fullscreen-btn");

    // Settings panel
    public ILocator SettingsPanel => _page.Locator("#settings-panel");
    public ILocator TimerMinutesInput => _page.Locator("#timer-minutes");
    public ILocator SetTimerBtn => _page.Locator("#set-timer-btn");
    public ILocator ResetScoresBtn => _page.Locator("#reset-scores-btn");
    public ILocator ResetHistoryBtn => _page.Locator("#reset-history-btn");
    public ILocator SimulateTimerEndBtn => _page.Locator("#simulate-timer-end-btn");
    public ILocator Team1DecrementBtn => _page.Locator("#team1-decrement");
    public ILocator Team2DecrementBtn => _page.Locator("#team2-decrement");
    public ILocator ScoreFontSizeSlider => _page.Locator("#score-font-size");

    // Players panel
    public ILocator PlayersPanel => _page.Locator("#players-panel");
    public ILocator PlayerNameInput => _page.Locator("#player-name-input");
    public ILocator AddPlayerBtn => _page.Locator("#add-player-btn");
    public ILocator PlayersList => _page.Locator("#players-list");
    public ILocator Team1PlayersDisplay => _page.Locator("#team1-players-display");
    public ILocator Team2PlayersDisplay => _page.Locator("#team2-players-display");

    // Actions
    public async Task ClickTeam1Score()
    {
        await Team1Score.ClickAsync();
    }

    public async Task ClickTeam2Score()
    {
        await Team2Score.ClickAsync();
    }

    public async Task<string> GetTeam1Score()
    {
        return await Team1Score.TextContentAsync() ?? "0";
    }

    public async Task<string> GetTeam2Score()
    {
        return await Team2Score.TextContentAsync() ?? "0";
    }

    public async Task<string> GetTimerText()
    {
        return await TimerDisplay.TextContentAsync() ?? "";
    }

    public async Task ClickTimerDisplay()
    {
        await TimerDisplay.ClickAsync();
    }

    public async Task DoubleClickTimerDisplay()
    {
        await TimerDisplay.DblClickAsync();
    }

    public async Task OpenSettings()
    {
        await SettingsBtn.ClickAsync();
        await SettingsPanel.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task OpenPlayers()
    {
        await PlayersBtn.ClickAsync();
        await PlayersPanel.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task SimulateTimerEnd()
    {
        // Open settings if not already open
        if (!await SettingsPanel.IsVisibleAsync())
        {
            await OpenSettings();
        }
        await SimulateTimerEndBtn.ClickAsync();
    }

    public async Task ResetScores()
    {
        if (!await SettingsPanel.IsVisibleAsync())
        {
            await OpenSettings();
        }
        await ResetScoresBtn.ClickAsync();
    }

    public async Task DecrementTeam1()
    {
        if (!await SettingsPanel.IsVisibleAsync())
        {
            await OpenSettings();
        }
        await Team1DecrementBtn.ClickAsync();
    }

    public async Task SetTimerDuration(int minutes)
    {
        if (!await SettingsPanel.IsVisibleAsync())
        {
            await OpenSettings();
        }
        await TimerMinutesInput.FillAsync(minutes.ToString());
        await SetTimerBtn.ClickAsync();
    }

    public async Task AddPlayer(string name)
    {
        if (!await PlayersPanel.IsVisibleAsync())
        {
            await OpenPlayers();
        }
        await PlayerNameInput.FillAsync(name);
        await AddPlayerBtn.ClickAsync();
    }

    public async Task<int> GetPeriodScoreCount()
    {
        return await PeriodScores.CountAsync();
    }

    public async Task<string[]> GetPeriodScores()
    {
        var count = await PeriodScores.CountAsync();
        var scores = new string[count];
        for (int i = 0; i < count; i++)
        {
            scores[i] = await PeriodScores.Nth(i).TextContentAsync() ?? "";
        }
        return scores;
    }

    public async Task ClickPeriodScore(int index)
    {
        await PeriodScores.Nth(index).ClickAsync();
    }

    public async Task<int> GetPlayerRowCount()
    {
        return await PlayersList.Locator("tr").CountAsync();
    }

    public async Task RemovePlayer(int rowIndex)
    {
        await PlayersList.Locator("tr").Nth(rowIndex).Locator(".remove-player").ClickAsync();
    }

    public async Task ChangePlayerTeam(int rowIndex, string teamValue)
    {
        await PlayersList.Locator("tr").Nth(rowIndex).Locator(".team-select").SelectOptionAsync(teamValue);
    }

    public async Task ClickTeam1PlayerDisplay(int index)
    {
        await Team1PlayersDisplay.Locator(".player-item-display").Nth(index).ClickAsync();
    }

    public async Task<string> GetTeam1PlayerPoints(int index)
    {
        return await Team1PlayersDisplay.Locator(".player-item-display").Nth(index)
            .Locator(".player-points-display").TextContentAsync() ?? "0";
    }

    public async Task<string> GetScoreFontSize()
    {
        return await _page.Locator(".score").First.EvaluateAsync<string>("el => el.style.fontSize") ?? "";
    }
}
