using Android.App;
using Android.Content;
using Android.OS;
using System.Net.Http;
using System.Text.Json;

namespace TopJournal;

/// <summary>Информация о доступном обновлении из GitHub Releases.</summary>
public class UpdateInfo
{
    public string Tag = "";
    public string Version = "";
    public string Date = "";
    public List<string> Notes = new();
    public string ApkUrl = "";
}

/// <summary>
/// Самообновление приложения: проверка GitHub Releases → модалка →
/// скачивание через DownloadManager → запуск установщика.
/// Проверка выполняется один раз за запуск, без каких-либо фоновых сервисов.
/// </summary>
public static class Updater
{
    public const string Repo = "ERambler/Journal-Top-Academy";
    const string Prefs = "topjournal";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Возвращает описание обновления или null (нет/та же версия/пропущена).</summary>
    public static async Task<UpdateInfo?> CheckAsync(Context ctx)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd("TopJournal-Updater/1.0"); // GitHub требует UA
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            string ver = tag.TrimStart('v', 'V');
            string cur = ctx.PackageManager!.GetPackageInfo(ctx.PackageName!, 0).VersionName ?? "";
            if (VersionCompare(ver, cur) <= 0) return null;
            if (GetSkipped(ctx) == tag) return null;

            string? url = null;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
                if ((a.GetProperty("name").GetString() ?? "").EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                    url = a.GetProperty("browser_download_url").GetString();
            if (url is null) return null;

            string date = "";
            if (root.TryGetProperty("published_at", out var pa) && pa.GetString() is { } ds &&
                DateTime.TryParse(ds, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d))
                date = d.ToString("dd.MM.yyyy");

            var notes = new List<string>();
            if (root.TryGetProperty("body", out var body) && body.GetString() is { } b)
                foreach (var line in b.Split('\n'))
                {
                    var t = line.Trim().TrimStart('-', '*', '•', ' ');
                    if (t.Length > 0) notes.Add(t);
                }

            return new UpdateInfo
            {
                Tag = tag, Version = ver, Date = date,
                Notes = notes.Take(8).ToList(), ApkUrl = url,
            };
        }
        catch { return null; }
    }

    public static void Skip(Context ctx, string tag) =>
        ctx.GetSharedPreferences(Prefs, FileCreationMode.Private)!.Edit()!
            .PutString("skip_version", tag).Apply();

    static string? GetSkipped(Context ctx) =>
        ctx.GetSharedPreferences(Prefs, FileCreationMode.Private)!.GetString("skip_version", null);

    public static int VersionCompare(string a, string b)
    {
        static int[] P(string s) =>
            s.Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
        var pa = P(a);
        var pb = P(b);
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0;
            int y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    /// <summary>Скачивает APK; по завершении открывает системный установщик.</summary>
    public static void DownloadAndInstall(Context ctx, UpdateInfo u, Action<string> toast)
    {
        var req = new DownloadManager.Request(Android.Net.Uri.Parse(u.ApkUrl));
        req.SetTitle($"Журнал ТОП {u.Version}");
        req.SetDescription("Загрузка обновления…");
        req.SetMimeType("application/vnd.android.package-archive");
        req.SetNotificationVisibility(Android.App.DownloadVisibility.VisibleNotifyCompleted);
        var dm = (DownloadManager)ctx.GetSystemService(Context.DownloadService)!;
        var id = dm.Enqueue(req);
        toast($"Скачиваю версию {u.Version} — файл появится в шторке");

        var receiver = new InstallOnDownload(dm, id);
        var filter = new IntentFilter(DownloadManager.ActionDownloadComplete);
        if ((int)Build.VERSION.SdkInt >= 33)
            ctx.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
        else
            ctx.RegisterReceiver(receiver, filter);
        _keepAlive = receiver; // не дать GC собрать до прихода broadcast
    }

    static BroadcastReceiver? _keepAlive;

    class InstallOnDownload : BroadcastReceiver
    {
        readonly DownloadManager _dm;
        readonly long _id;

        public InstallOnDownload(DownloadManager dm, long id)
        {
            _dm = dm;
            _id = id;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            try { context!.UnregisterReceiver(this); } catch { }
            if (intent?.Action != DownloadManager.ActionDownloadComplete) return;

            var query = new DownloadManager.Query();
            query.SetFilterById(_id);
            using var cursor = _dm.InvokeQuery(query);
            if (cursor is null || !cursor.MoveToFirst()) return;
            int statusCol = cursor.GetColumnIndex(DownloadManager.ColumnStatus);
            if (statusCol < 0 || cursor.GetInt(statusCol) != (int)DownloadStatus.Successful) return;

            var uri = _dm.GetUriForDownloadedFile(_id);
            if (uri is null) return;

            var install = new Intent(Intent.ActionView);
            install.SetDataAndType(uri, "application/vnd.android.package-archive");
            install.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
            try { context!.StartActivity(install); }
            catch
            {
                // нет разрешения «установка из этого источника» — пользователь включит сам
                Toast.MakeText(context, "Откройте шторку и разрешите установку", ToastLength.Long)?.Show();
            }
        }
    }
}
