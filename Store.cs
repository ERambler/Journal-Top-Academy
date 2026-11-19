using System.Text.Json;
using Android.Content;

namespace TopJournal;

/// <summary>
/// Учётные данные (SharedPreferences) + снимок данных (файл snapshot.json)
/// и дифф для уведомлений. Логика ключей совпадает с mystat_client.py.
/// </summary>
public static class Store
{
    const string PrefsName = "topjournal";
    const string SnapshotFile = "snapshot.json";
    // JSON без рефлексии (Source Generator) — переживает PublishTrimmed

    // ---------------------------------------------------------------- creds

    public static void SaveCreds(Context ctx, string login, string password)
    {
        ctx.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.Edit()!
            .PutString("login", login)
            .PutString("password", password)
            .Apply();
    }

    public static (string? Login, string? Password) GetCreds(Context ctx)
    {
        var p = ctx.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
        return (p.GetString("login", null), p.GetString("password", null));
    }

    public static bool HasCreds(Context ctx) => GetCreds(ctx).Login is not null;

    public static void SaveTokens(Context ctx, Tokens t)
    {
        ctx.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.Edit()!
            .PutString("access", t.AccessToken)
            .PutString("refresh", t.RefreshToken)
            .Apply();
    }

    public static string? GetRefreshToken(Context ctx) =>
        ctx.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.GetString("refresh", null);

    // ------------------------------------------------- настройки виджета

    /// <summary>Размер шрифта виджета: 0 малый · 1 средний (умолчание) · 2 большой.</summary>
    public static int GetWidgetFont(Context ctx) =>
        ctx.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.GetInt("widget_font", 1);

    public static void SetWidgetFont(Context ctx, int value) =>
        ctx.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.Edit()!
            .PutInt("widget_font", value).Apply();

    // ------------------------------------------------------------- snapshot

    static string SnapshotPath(Context ctx) =>
        Path.Combine(ctx.FilesDir!.AbsolutePath, SnapshotFile);

    public static void SaveSnapshot(Context ctx, Snapshot snap)
    {
        snap.SavedAt = DateTime.Now.ToString("dd.MM HH:mm");
        File.WriteAllText(SnapshotPath(ctx), JsonSerializer.Serialize(snap, AppJson.Default.Snapshot));
    }

    public static Snapshot? LoadSnapshot(Context ctx)
    {
        try
        {
            var path = SnapshotPath(ctx);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize(File.ReadAllText(path), AppJson.Default.Snapshot);
        }
        catch { return null; }
    }

    // ----------------------------------------------------------------- diff

    /// <summary>Идемпотентные ключи оценок: дата | №пары | предмет | тип работы.</summary>
    static Dictionary<string, string> MarkKeys(Snapshot? snap)
    {
        var dict = new Dictionary<string, string>();
        if (snap is null) return dict;
        foreach (var v in snap.Visits)
            foreach (var (mark, field) in v.Marks)
                dict[$"{v.DateVisit}|{v.LessonNumber}|{v.SpecId}|{field}"] =
                    $"Новая оценка: {mark} — {v.SpecName} ({field}, {FmtDate(v.DateVisit)})";
        return dict;
    }

    /// <summary>Ключи ДЗ: id → (статус, оценка).</summary>
    static Dictionary<int, (int Status, int? Mark)> HwKeys(Snapshot? snap)
    {
        var dict = new Dictionary<int, (int, int?)>();
        if (snap is null) return dict;
        foreach (var h in snap.Homework)
            dict[h.Id] = (h.Status, h.Stud?.Mark);
        return dict;
    }

    /// <summary>Возвращает тексты событий «что нового» между двумя снимками.</summary>
    public static List<string> Diff(Snapshot? old, Snapshot @new)
    {
        var events = new List<string>();

        var oldMarks = MarkKeys(old);
        foreach (var kv in MarkKeys(@new))
            if (!oldMarks.ContainsKey(kv.Key))
                events.Add(kv.Value);

        var oldHw = HwKeys(old);
        foreach (var kv in HwKeys(@new))
        {
            if (!oldHw.TryGetValue(kv.Key, out var prev))
            {
                var added = @new.Homework.First(h => h.Id == kv.Key);
                if (added.Status is 0 or 3)
                    events.Add($"Новое ДЗ: {added.NameSpec} — «{Trim(added.Theme, 40)}»");
            }
            else if (kv.Value.Status == 1 && (prev.Status != 1 || prev.Mark != kv.Value.Mark))
            {
                var h = @new.Homework.First(x => x.Id == kv.Key);
                var mark = h.Stud?.Mark is int m ? $" — оценка {m}" : "";
                events.Add($"ДЗ проверено: {h.NameSpec}{mark}");
            }
        }
        return events;
    }

    static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    static string FmtDate(string iso) =>
        DateTime.TryParse(iso, out var d) ? d.ToString("dd.MM") : iso;
}
