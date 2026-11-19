using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TopJournal;

/// <summary>
/// Клиент неофициального API MyStat (msapi.top-academy.ru/api/v2).
/// Особенность: WAF требует заголовок Referer (см. API.md в topacademy-api).
/// </summary>
public sealed class MystatApi
{
    public const string BaseUrl = "https://msapi.top-academy.ru/api/v2/";
    public const string AppKey = "6a56a5df2667e65aab73ce76d1dd737f7d1faef9c52e8b8c55ac75f565d8e8a6";

    // JSON — через Source Generator (AppJson), совместимо с PublishTrimmed

    readonly HttpClient _http;
    Tokens? _tokens;

    public string? SavedLogin { get; set; }
    public string? SavedPassword { get; set; }

    public MystatApi(string? login = null, string? password = null, Tokens? tokens = null)
    {
        SavedLogin = login;
        SavedPassword = password;
        _tokens = tokens;
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Referrer = new Uri("https://journalka.top-academy.ru/");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    // ------------------------------------------------------------------ auth

    public async Task<Tokens> LoginAsync(string username, string password)
    {
        var body = new { username, password, application_key = AppKey };
        var resp = await _http.PostAsJsonAsync("auth/login", body);
        resp.EnsureSuccessStatusCode();
        var tokens = JsonSerializer.Deserialize<Tokens>(
            await resp.Content.ReadAsStringAsync(), AppJson.Default.Tokens)
                     ?? throw new InvalidOperationException("пустой ответ auth/login");
        _tokens = tokens;
        SavedLogin = username;
        SavedPassword = password;
        return tokens;
    }

    public async Task<bool> RefreshAsync(string? refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken)) return false;
        var resp = await _http.PostAsJsonAsync("auth/refresh", new { refresh_token = refreshToken });
        if (!resp.IsSuccessStatusCode) return false;
        var tokens = JsonSerializer.Deserialize<Tokens>(
            await resp.Content.ReadAsStringAsync(), AppJson.Default.Tokens);
        if (tokens is null) return false;
        _tokens = tokens;
        return true;
    }

    /// <summary>Токены живут ~6 ч: сначала пробуем refresh, при провале — полный логин.</summary>
    public async Task EnsureAuthAsync(string? refreshToken = null)
    {
        if (_tokens != null && await PingAsync()) return;
        if (await RefreshAsync(refreshToken) && await PingAsync()) return;
        if (SavedLogin is null || SavedPassword is null)
            throw new InvalidOperationException("нет учётных данных");
        await LoginAsync(SavedLogin, SavedPassword);
    }

    async Task<bool> PingAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, "settings/user-info");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens!.AccessToken);
            using var resp = await _http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ------------------------------------------------------------------ data

    async Task<T?> GetAsync<T>(string ep, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, string? query = null)
        where T : class
    {
        if (_tokens is null) throw new InvalidOperationException("не авторизован");
        var url = query is null ? ep : $"{ep}?{query}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens.AccessToken);
        using var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize(body, typeInfo);
    }

    public Task<List<VisitDto>?> GetVisitsAsync() =>
        GetAsync("progress/operations/student-visits", AppJson.Default.ListVisitDto);

    public Task<List<HomeworkDto>?> GetHomeworkAsync(int? status = null) =>
        GetAsync("homework/operations/list", AppJson.Default.ListHomeworkDto,
            status is null ? null : $"status={status}");

    public async Task<Dictionary<int, int>> GetCountersAsync()
    {
        var rows = await GetAsync("count/homework", AppJson.Default.ListCounterDto) ?? new List<CounterDto>();
        return rows.ToDictionary(r => r.CounterType, r => r.Counter);
    }

    public Task<List<ScheduleDto>?> GetScheduleRangeAsync(DateTime start, DateTime end) =>
        GetAsync("schedule/operations/get-by-date-range", AppJson.Default.ListScheduleDto,
            $"date_start={start:yyyy-MM-dd}&date_end={end:yyyy-MM-dd}");

    public Task<UserInfo?> GetUserInfoAsync() =>
        GetAsync("settings/user-info", AppJson.Default.UserInfo);

    public Tokens? CurrentTokens => _tokens;
}
