using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Scoreboard.Services;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace SharedTools.Scoreboard;

/// <summary>
/// The photo page (/Scoreboard/photo): friends pick their name, crop a photo, optionally run it
/// through an AI style, and save it as their scoreboard avatar.
///   GET  /api/photo/config   which styles are available to this phone, tries left today
///   POST /api/photo/stylize  cropped photo + style -> styled photo (OpenAI image edit; key stays here)
///   POST /api/photo/save     cropped (maybe styled) photo -> players/{id}.png + roster imageUrl
///   GET  /api/photo/usage    who used it and how much (X-Admin-Key)
///   POST /api/photo/block    block or unblock a phone or IP (X-Admin-Key)
/// </summary>
public static class PhotoBooth
{
    public sealed record Style(string Id, string Label, string Prompt);

    // Each prompt is appended to Identity, so every style keeps the person recognizable.
    public static readonly Style[] Styles =
    [
        new("baller", "Basketball star", "pro basketball player game-day portrait: sleeveless jersey in plain colors, holding a basketball, bright arena lights and a blurred crowd behind them, sports photography look."),
        new("retro", "Old-school baller", "1970s basketball legend portrait: terry-cloth headband, wristbands, retro jersey with stripes, warm faded film photo colors, old wooden gym behind them."),
        new("card", "Trading card", "vintage 1980s basketball trading card portrait painting, airbrushed look, retro colors, gym background. No real team names or logos."),
        new("bobblehead", "Bobblehead", "collectible sports bobblehead doll of them: oversized glossy painted head on a small body in a basketball uniform, on a little round base, clean studio backdrop."),
        new("goth", "Goth", "dramatic goth portrait: black clothes, silver jewelry, dark eyeliner, moody purple and black lighting, gothic cathedral window behind them. Fun and stylish, not scary."),
        new("nerd", "Nerd", "affectionate nerd caricature: thick-rimmed glasses, sweater vest, pocket protector, bookshelf background, cartoon illustration style."),
        new("8bit", "8-bit", "8-bit pixel art sprite portrait, NES-style limited palette, big chunky pixels, plain solid background."),
        new("comic", "Comic book", "bold comic book cover art, heavy ink lines, halftone dots, dramatic colors, action burst background."),
        new("claymation", "Claymation", "stop-motion claymation figure, sculpted plasticine look with fingerprints, soft studio lighting, simple backdrop."),
    ];

    private const string Identity =
        "Restyle the person in this photo. Keep them recognizable to their friends: same face shape, hairstyle, " +
        "skin tone, facial hair, glasses and expression. Square head-and-shoulders portrait with the face centered " +
        "and filling most of the frame, so it reads well as a small round avatar. No text, no logos, no watermarks. Style: ";

    private const int MaxImageChars = 8 * 1024 * 1024;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/Scoreboard/api/photo");
        api.MapGet("/config", Config);
        api.MapPost("/stylize", Stylize).DisableAntiforgery();
        api.MapPost("/save", Save).DisableAntiforgery();
        api.MapGet("/usage", Usage);
        api.MapPost("/block", Block).DisableAntiforgery();
        api.MapPost("/reset", Reset).DisableAntiforgery();
        api.MapPost("/events", Events).DisableAntiforgery();
        api.MapGet("/archive", ArchiveList);
        api.MapGet("/archive/file", ArchiveFile);
    }

    public sealed record EventItem(string? Type, string? Detail, string? PlayerId, int? Ago);
    public sealed record EventsRequest(string? Device, string? Ua, List<EventItem>? Events);

    // Page beacon: visits, step changes and clicks, batched by the page. Anonymous, so everything is clamped.
    private static async Task<IResult> Events(EventsRequest req, HttpContext ctx, PhotoUsage usage)
    {
        var device = Clean(req.Device);
        var ip = ClientIp(ctx);
        var ua = Trim(req.Ua, 160);
        var now = DateTimeOffset.UtcNow;
        var list = (req.Events ?? []).Take(50)
            .Where(e => e.Type is "visit" or "nav" or "click")
            .Select(e => new PhotoEvent(now.AddMilliseconds(-Math.Clamp(e.Ago ?? 0, 0, 3_600_000)), e.Type!,
                Trim(e.Detail, 60) ?? "", device, ip, Trim(e.PlayerId, 20), e.Type == "visit" ? ua : null))
            .ToList();
        if (list.Count > 0) await usage.AddEventsAsync(list);
        return Results.NoContent();
    }

    private static string? Trim(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, max)];

    // Every photo kept in blob: saved ones, AI tries and the source photos behind them.
    private static async Task<IResult> ArchiveList(HttpContext ctx, IOptions<ScoreboardOptions> options, ScoreboardStore store, string? player)
    {
        if (!IsAdmin(ctx, options.Value)) return Problem(401, "Wrong or missing admin key.");
        var names = (await RosterAsync(store))?["players"]?.AsArray()
            .Where(p => p is not null).ToDictionary(p => p!["id"]?.ToString() ?? "", p => p!["name"]?.GetValue<string>() ?? "?")
            ?? [];
        var all = await store.ListAsync(string.IsNullOrEmpty(player) ? "photo-archive/" : $"photo-archive/{Clean(player)}/");
        var items = all.Take(400).Select(i =>
        {
            var parts = i.Path.Split('/');
            var file = parts[^1];
            var id = parts.Length > 2 ? parts[1] : "";
            return new
            {
                path = i.Path,
                playerId = id,
                player = names.GetValueOrDefault(id),
                kind = file.StartsWith("source-") ? "source" : file.Contains("-try") ? "try" : "saved",
                style = file.StartsWith("source-") ? null : file.Split('-').Skip(2).FirstOrDefault()?.Replace(".jpg", ""),
                size = i.Size,
                ts = i.Modified
            };
        });
        return Results.Json(new { total = all.Count, items }, Web);
    }

    // Images are fetched with the admin key header and shown from a blob URL, so the key never sits in a URL.
    private static async Task<IResult> ArchiveFile(HttpContext ctx, IOptions<ScoreboardOptions> options, ScoreboardStore store, string? path)
    {
        if (!IsAdmin(ctx, options.Value)) return Problem(401, "Wrong or missing admin key.");
        if (path is null || !path.StartsWith("photo-archive/") || path.Contains("..")) return Problem(400, "Bad path.");
        var data = await store.ReadAsync(path);
        return data is null ? Problem(404, "Not found.") : Results.File(data.ToArray(), "image/jpeg");
    }

    // The source photo and the AI result are kept even if the person never saves them. Never blocks the response.
    private static async Task ArchiveTryAsync(ScoreboardStore store, ILogger logger, long playerId, byte[] sourcePng, byte[] resultJpg, string style)
    {
        try
        {
            var hash = Convert.ToHexString(SHA1.HashData(sourcePng))[..10].ToLowerInvariant();
            using (var src = Image.Load(sourcePng))
            using (var jpg = new MemoryStream())
            {
                await src.SaveAsJpegAsync(jpg, new JpegEncoder { Quality = 85 });
                await store.WriteAsync($"photo-archive/{playerId}/source-{hash}.jpg", new BinaryData(jpg.ToArray()));
            }
            await store.WriteAsync($"photo-archive/{playerId}/{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{style}-try.jpg", new BinaryData(resultJpg));
        }
        catch (Exception e) { logger.LogWarning(e, "Couldn't archive the AI try for player {Player}", playerId); }
    }

    public sealed record StylizeRequest(long PlayerId, string? Style, string? Image, string? Device, string? Code);
    public sealed record SaveRequest(long PlayerId, string? Style, string? Image, string? Device);
    public sealed record BlockRequest(string? Key, bool Blocked);

    private static async Task<IResult> Config(HttpContext ctx, IOptions<ScoreboardOptions> options, PhotoUsage usage, string? device, string? code)
    {
        var o = options.Value;
        var reason = AiUnavailable(o, code);
        var dev = Clean(device);
        var (byDevice, _, total) = await usage.TodayAsync(dev, ClientIp(ctx));
        return Results.Json(new
        {
            ai = reason is null,
            reason,
            styles = Styles.Select(s => new { s.Id, s.Label }),
            remaining = Math.Max(0, Math.Min(o.PhotoDailyLimitPerDevice - byDevice, o.PhotoDailyLimitTotal - total))
        });
    }

    private static string? AiUnavailable(ScoreboardOptions o, string? code)
    {
        if (string.IsNullOrEmpty(o.OpenAIKey)) return "AI styles aren't set up yet.";
        if (!string.IsNullOrEmpty(o.PhotoLinkCode) && !FixedEquals(code, o.PhotoLinkCode))
            return "AI styles need the link from the group chat.";
        return null;
    }

    private static async Task<IResult> Stylize(StylizeRequest req, HttpContext ctx, IOptions<ScoreboardOptions> options,
        PhotoUsage usage, ScoreboardStore store, ILogger<PhotoUsageLog> logger)
    {
        var o = options.Value;
        var device = Clean(req.Device);
        var ip = ClientIp(ctx);
        var style = Styles.FirstOrDefault(s => s.Id == req.Style);
        var player = await PlayerNameAsync(store, req.PlayerId);

        PhotoUsageEntry Entry(string action, bool ok, string? error, long ms = 0) =>
            new(DateTimeOffset.UtcNow, action, device, ip, req.PlayerId.ToString(), player, req.Style, ok, error, (int)ms);

        if (AiUnavailable(o, req.Code) is { } why) return Problem(403, why);
        if (style is null) return Problem(400, "Unknown style.");
        if (player is null) return Problem(400, "Pick your name first.");
        if (await usage.IsBlockedAsync(device, ip))
        {
            await usage.AddAsync(Entry("blocked", false, "blocked"));
            return Problem(429, "AI styles are switched off for this phone. Ask the commissioner.");
        }

        var (byDevice, byIp, total) = await usage.TodayAsync(device, ip);
        if (byDevice >= o.PhotoDailyLimitPerDevice || byIp >= 2 * o.PhotoDailyLimitPerDevice || total >= o.PhotoDailyLimitTotal)
        {
            await usage.AddAsync(Entry("limited", false, "daily limit"));
            return Problem(429, total >= o.PhotoDailyLimitTotal
                ? "The group used up today's AI styles. Try again tomorrow."
                : "That's all the AI styles for this phone today. Try again tomorrow.");
        }

        byte[] png;
        try
        {
            using var img = LoadSquare(req.Image);
            img.Mutate(x => x.Resize(1024, 1024));
            using var ms = new MemoryStream();
            await img.SaveAsPngAsync(ms);
            png = ms.ToArray();
        }
        catch (Exception e) when (e is FormatException or UnknownImageFormatException or InvalidImageContentException or ArgumentException)
        {
            return Problem(400, "Couldn't read that photo.");
        }

        var clock = Stopwatch.StartNew();
        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(o.OpenAIImageModel), "model" },
                { new StringContent(Identity + style.Prompt), "prompt" },
                { new StringContent("1024x1024"), "size" },
                { new StringContent(o.OpenAIImageQuality), "quality" },
                { new StringContent("1"), "n" }
            };
            var file = new ByteArrayContent(png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "image", "photo.png");

            using var call = new HttpRequestMessage(HttpMethod.Post, $"{o.OpenAIBaseUrl.TrimEnd('/')}/images/edits") { Content = form };
            call.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.OpenAIKey);
            using var res = await Http.SendAsync(call, ctx.RequestAborted);
            var body = await res.Content.ReadAsStringAsync(ctx.RequestAborted);

            if (!res.IsSuccessStatusCode)
            {
                var (code, message) = OpenAIError(body);
                logger.LogWarning("OpenAI image edit failed: {Status} {Code} {Message}", (int)res.StatusCode, code, message);
                await usage.AddAsync(Entry("stylize", false, $"{(int)res.StatusCode} {code ?? message}", clock.ElapsedMilliseconds));
                return Problem(502, code == "moderation_blocked"
                    ? "The AI wouldn't style that photo. Try another photo, or keep the original."
                    : "The AI couldn't style it this time. Try again, or keep the original.");
            }

            var b64 = JsonNode.Parse(body)?["data"]?[0]?["b64_json"]?.GetValue<string>()
                ?? throw new InvalidOperationException("No image in the response");
            using var styled = Image.Load(Convert.FromBase64String(b64));
            styled.Mutate(x => x.Resize(768, 768));
            using var jpg = new MemoryStream();
            await styled.SaveAsJpegAsync(jpg, new JpegEncoder { Quality = 90 });

            await usage.AddAsync(Entry("stylize", true, null, clock.ElapsedMilliseconds));
            await ArchiveTryAsync(store, logger, req.PlayerId, png, jpg.ToArray(), style.Id);
            var after = await usage.TodayAsync(device, ip);
            return Results.Json(new
            {
                image = "data:image/jpeg;base64," + Convert.ToBase64String(jpg.ToArray()),
                remaining = Math.Max(0, Math.Min(o.PhotoDailyLimitPerDevice - after.ByDevice, o.PhotoDailyLimitTotal - after.Total))
            });
        }
        catch (Exception e) when (e is not OperationCanceledException || !ctx.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(e, "OpenAI image edit failed");
            await usage.AddAsync(Entry("stylize", false, e.GetType().Name, clock.ElapsedMilliseconds));
            return Problem(502, "The AI couldn't style it this time. Try again, or keep the original.");
        }
    }

    private static async Task<IResult> Save(SaveRequest req, HttpContext ctx, PhotoUsage usage, ScoreboardStore store)
    {
        var player = await PlayerNameAsync(store, req.PlayerId);
        if (player is null) return Problem(400, "Pick your name first.");

        Image img;
        try { img = LoadSquare(req.Image); }
        catch (Exception e) when (e is FormatException or UnknownImageFormatException or InvalidImageContentException or ArgumentException)
        {
            return Problem(400, "Couldn't read that photo.");
        }

        using (img)
        {
            var style = Styles.FirstOrDefault(s => s.Id == req.Style)?.Id ?? "original";
            var stamp = DateTimeOffset.UtcNow;

            // Keep a bigger copy of every saved photo, in case we want them later
            using (var archive = img.Clone(x => x.Resize(512, 512)))
            using (var jpg = new MemoryStream())
            {
                await archive.SaveAsJpegAsync(jpg, new JpegEncoder { Quality = 90 });
                await store.WriteAsync($"photo-archive/{req.PlayerId}/{stamp:yyyyMMdd-HHmmss}-{style}.jpg", new BinaryData(jpg.ToArray()));
            }

            img.Mutate(x => x.Resize(ScoreboardApi.AvatarSize, ScoreboardApi.AvatarSize));
            using var png = new MemoryStream();
            await img.SaveAsPngAsync(png);
            await store.WriteAsync($"players/{req.PlayerId}.png", new BinaryData(png.ToArray()));

            var imageUrl = $"/Scoreboard/api/players/{req.PlayerId}/image?v={stamp.ToUnixTimeSeconds()}";
            await SetRosterImageAsync(store, req.PlayerId, imageUrl);
            await usage.AddAsync(new(stamp, "save", Clean(req.Device), ClientIp(ctx), req.PlayerId.ToString(), player, style, true, null, 0));
            return Results.Json(new { imageUrl });
        }
    }

    private static async Task<IResult> Usage(HttpContext ctx, IOptions<ScoreboardOptions> options, PhotoUsage usage, string? month)
    {
        if (!IsAdmin(ctx, options.Value)) return Problem(401, "Wrong or missing admin key.");
        return Results.Json(await usage.ReportAsync(month), Web);
    }

    private static async Task<IResult> Block(BlockRequest req, HttpContext ctx, IOptions<ScoreboardOptions> options, PhotoUsage usage)
    {
        if (!IsAdmin(ctx, options.Value)) return Problem(401, "Wrong or missing admin key.");
        if (string.IsNullOrWhiteSpace(req.Key)) return Problem(400, "Nothing to block.");
        await usage.SetBlockedAsync(req.Key.Trim(), req.Blocked);
        return Results.NoContent();
    }

    // Give a phone its full daily allowance back (usage page).
    private static async Task<IResult> Reset(BlockRequest req, HttpContext ctx, IOptions<ScoreboardOptions> options, PhotoUsage usage)
    {
        if (!IsAdmin(ctx, options.Value)) return Problem(401, "Wrong or missing admin key.");
        if (string.IsNullOrWhiteSpace(req.Key)) return Problem(400, "Which phone?");
        await usage.AddAsync(new(DateTimeOffset.UtcNow, "reset", Clean(req.Key), ClientIp(ctx), null, null, null, true, null, 0));
        return Results.NoContent();
    }

    // ---- helpers ----

    private static bool IsAdmin(HttpContext ctx, ScoreboardOptions o) =>
        !string.IsNullOrEmpty(o.PhotoAdminKey) && FixedEquals(ctx.Request.Headers["X-Admin-Key"].ToString(), o.PhotoAdminKey);

    private static bool FixedEquals(string? a, string b) =>
        a is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static IResult Problem(int status, string error) => Results.Json(new { error }, statusCode: status);

    // A data URL or bare base64 image, center-cropped to a square (the page already crops it).
    private static Image LoadSquare(string? data)
    {
        if (string.IsNullOrEmpty(data) || data.Length > MaxImageChars) throw new ArgumentException("No image");
        var comma = data.IndexOf(',');
        var img = Image.Load(Convert.FromBase64String(comma >= 0 ? data[(comma + 1)..] : data));
        img.Mutate(x => x.AutoOrient());
        var size = Math.Min(img.Width, img.Height);
        if (img.Width != img.Height)
            img.Mutate(x => x.Crop(new Rectangle((img.Width - size) / 2, (img.Height - size) / 2, size, size)));
        return img;
    }

    private static (string? Code, string? Message) OpenAIError(string body)
    {
        try
        {
            var err = JsonNode.Parse(body)?["error"];
            return (err?["code"]?.GetValue<string>(), err?["message"]?.GetValue<string>());
        }
        catch (JsonException) { return (null, body.Length > 200 ? body[..200] : body); }
    }

    // The proxy in front of the module puts the phone's address in X-Forwarded-For.
    private static string ClientIp(HttpContext ctx)
    {
        var forwarded = ctx.Request.Headers["X-Forwarded-For"].ToString();
        var ip = forwarded.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return ip ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
    }

    private static string Clean(string? device) =>
        string.IsNullOrWhiteSpace(device) ? "unknown" : new string(device.Where(c => char.IsLetterOrDigit(c) || c == '-').Take(64).ToArray());

    private static bool SameId(JsonNode? id, long playerId) =>
        id is JsonValue v && (v.TryGetValue<long>(out var n) ? n == playerId : v.TryGetValue<string>(out var s) && s == playerId.ToString());

    private static async Task<JsonNode?> RosterAsync(ScoreboardStore store)
    {
        var data = await store.ReadAsync("roster.json");
        return data is null ? null : JsonNode.Parse(data.ToString());
    }

    private static async Task<string?> PlayerNameAsync(ScoreboardStore store, long playerId)
    {
        var players = (await RosterAsync(store))?["players"]?.AsArray();
        var p = players?.FirstOrDefault(p => SameId(p?["id"], playerId));
        return p is null ? null : p["name"]?.GetValue<string>() ?? "?";
    }

    private static async Task SetRosterImageAsync(ScoreboardStore store, long playerId, string imageUrl)
    {
        var roster = await RosterAsync(store);
        var p = roster?["players"]?.AsArray().FirstOrDefault(p => SameId(p?["id"], playerId));
        if (roster is null || p is null) return;
        p["imageUrl"] = imageUrl;
        await store.WriteAsync("roster.json", new BinaryData(roster.ToJsonString()));
    }
}

/// <summary>Logger category for the photo page.</summary>
public sealed class PhotoUsageLog;

public sealed record PhotoEvent(DateTimeOffset Ts, string Type, string Detail, string Device, string Ip, string? PlayerId, string? Ua);

public sealed record PhotoUsageEntry(DateTimeOffset Ts, string Action, string Device, string Ip,
    string? PlayerId, string? Player, string? Style, bool Ok, string? Error, int Ms);

/// <summary>
/// Every AI style attempt and photo save, one JSON file per month (photo-usage/yyyy-MM.json),
/// plus a block list (photo-blocked.json) of phone ids and IPs. One server, so a lock is enough.
/// </summary>
public sealed class PhotoUsage(ScoreboardStore store)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<PhotoUsageEntry>? _entries;
    private string? _month;
    private HashSet<string>? _blocked;
    private List<PhotoEvent>? _events;
    private const int MaxEventsPerMonth = 20000;

    private static string MonthOf(DateTimeOffset t) => t.ToString("yyyy-MM");

    private async Task LoadAsync()
    {
        var month = MonthOf(DateTimeOffset.UtcNow);
        if (_entries is null || _month != month)
        {
            _entries = await ReadMonthAsync(month);
            _events = await ReadEventsAsync(month);
            _month = month;
        }
        if (_blocked is null)
        {
            var b = await store.ReadAsync("photo-blocked.json");
            _blocked = b is null ? [] : JsonSerializer.Deserialize<HashSet<string>>(b, Web) ?? [];
        }
    }

    private async Task<List<PhotoUsageEntry>> ReadMonthAsync(string month)
    {
        var data = await store.ReadAsync($"photo-usage/{month}.json");
        return data is null ? [] : JsonSerializer.Deserialize<List<PhotoUsageEntry>>(data, Web) ?? [];
    }

    private async Task<List<PhotoEvent>> ReadEventsAsync(string month)
    {
        var data = await store.ReadAsync($"photo-events/{month}.json");
        return data is null ? [] : JsonSerializer.Deserialize<List<PhotoEvent>>(data, Web) ?? [];
    }

    public Task AddEventsAsync(List<PhotoEvent> events) => Locked(async () =>
    {
        if (_events!.Count >= MaxEventsPerMonth) return false;
        _events.AddRange(events);
        await store.WriteAsync($"photo-events/{_month}.json", new BinaryData(JsonSerializer.SerializeToUtf8Bytes(_events, Web)));
        return true;
    });

    private async Task<T> Locked<T>(Func<Task<T>> work)
    {
        await _lock.WaitAsync();
        try { await LoadAsync(); return await work(); }
        finally { _lock.Release(); }
    }

    /// <summary>AI style calls today (UTC) by this phone, by this IP, and by everyone.</summary>
    public Task<(int ByDevice, int ByIp, int Total)> TodayAsync(string device, string ip) => Locked(() =>
    {
        var calls = CountedToday(_entries!);
        return Task.FromResult((calls.Count(e => e.Device == device), calls.Count(e => e.Ip == ip), calls.Count));
    });

    // Today's AI calls that count toward the limits: a "reset" entry (from the usage page)
    // clears that phone's earlier calls, so the log keeps everything that happened.
    private static List<PhotoUsageEntry> CountedToday(List<PhotoUsageEntry> entries)
    {
        var today = DateTimeOffset.UtcNow.Date;
        var todays = entries.Where(e => e.Ts.UtcDateTime.Date == today).ToList();
        var resets = todays.Where(e => e.Action == "reset").GroupBy(e => e.Device).ToDictionary(g => g.Key, g => g.Max(e => e.Ts));
        return todays.Where(e => e.Action == "stylize" && !(resets.TryGetValue(e.Device, out var r) && e.Ts <= r)).ToList();
    }

    public Task<bool> IsBlockedAsync(string device, string ip) =>
        Locked(() => Task.FromResult(_blocked!.Contains(device) || _blocked.Contains(ip)));

    public Task AddAsync(PhotoUsageEntry entry) => Locked(async () =>
    {
        _entries!.Add(entry);
        await store.WriteAsync($"photo-usage/{_month}.json", new BinaryData(JsonSerializer.SerializeToUtf8Bytes(_entries, Web)));
        return true;
    });

    public Task SetBlockedAsync(string key, bool blocked) => Locked(async () =>
    {
        if (blocked) _blocked!.Add(key); else _blocked!.Remove(key);
        await store.WriteAsync("photo-blocked.json", new BinaryData(JsonSerializer.SerializeToUtf8Bytes(_blocked, Web)));
        return true;
    });

    /// <summary>Per-phone totals plus the latest entries, for the usage page.</summary>
    public Task<object> ReportAsync(string? month) => Locked<object>(async () =>
    {
        var m = month is { Length: 7 } ? month : _month!;
        var entries = m == _month ? _entries! : await ReadMonthAsync(m);
        var today = DateTimeOffset.UtcNow.Date;
        var counted = CountedToday(entries);
        var devices = entries.Where(e => e.Action != "reset").GroupBy(e => e.Device).Select(g => new
        {
            device = g.Key,
            ips = g.Select(e => e.Ip).Distinct().ToArray(),
            players = g.Where(e => e.Player is not null).Select(e => e.Player).Distinct().ToArray(),
            styles = g.Count(e => e.Action == "stylize"),
            stylesToday = g.Count(e => e.Action == "stylize" && e.Ts.UtcDateTime.Date == today),
            countedToday = counted.Count(e => e.Device == g.Key),
            failed = g.Count(e => e.Action == "stylize" && !e.Ok),
            refused = g.Count(e => e.Action is "limited" or "blocked"),
            saves = g.Count(e => e.Action == "save"),
            last = g.Max(e => e.Ts),
            blocked = _blocked!.Contains(g.Key)
        }).OrderByDescending(d => d.last).ToArray();
        var evs = m == _month ? _events! : await ReadEventsAsync(m);
        var visits = evs.Where(e => e.Type == "visit").ToList();
        var events = new
        {
            visits = visits.Count,
            visitsToday = visits.Count(e => e.Ts.UtcDateTime.Date == today),
            visitors = visits.Select(e => e.Device).Distinct().Count(),
            steps = evs.Where(e => e.Type == "nav").GroupBy(e => e.Detail)
                .Select(g => new { step = g.Key, count = g.Count(), phones = g.Select(e => e.Device).Distinct().Count() }),
            clicks = evs.Where(e => e.Type == "click").GroupBy(e => e.Detail)
                .Select(g => new { what = g.Key, count = g.Count(), phones = g.Select(e => e.Device).Distinct().Count() })
                .OrderByDescending(c => c.count).Take(40),
            recent = evs.AsEnumerable().Reverse().Take(150)
        };
        return new
        {
            events,
            month = m,
            styles = entries.Count(e => e.Action == "stylize"),
            stylesToday = entries.Count(e => e.Action == "stylize" && e.Ts.UtcDateTime.Date == today),
            saves = entries.Count(e => e.Action == "save"),
            devices,
            blocked = _blocked!.ToArray(),
            recent = entries.AsEnumerable().Reverse().Take(100)
        };
    });
}
