using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

namespace TopJournal;

/// <summary>Виджет рабочего стола: сводка + расписание (сегодня/завтра) + ДЗ.</summary>
[BroadcastReceiver(Label = "Журнал ТОП", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_info")]
public class JournalWidget : AppWidgetProvider
{
    public override void OnUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds)
        => Render(context, appWidgetManager, appWidgetIds);

    /// <summary>Перерисовать все экземпляры виджета (вызывается после синхронизации).</summary>
    public static void RenderAll(Context context)
    {
        var mgr = AppWidgetManager.GetInstance(context);
        var ids = mgr.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(typeof(JournalWidget))));
        Render(context, mgr, ids);
    }

    static void Render(Context context, AppWidgetManager mgr, int[] ids)
    {
        var snap = Store.LoadSnapshot(context);

        foreach (var id in ids)
        {
            var views = new RemoteViews(context.PackageName, Resource.Layout.widget_journal);

            if (snap is null)
            {
                views.SetTextViewText(Resource.Id.wName, "Журнал ТОП");
                views.SetTextViewText(Resource.Id.wTime, "нет данных");
                views.SetTextViewText(Resource.Id.wAvg, "—");
                views.SetTextViewText(Resource.Id.wAttend, "откройте приложение");
                views.SetTextViewText(Resource.Id.wHw, "");
                views.SetTextViewText(Resource.Id.wSched1, "");
                views.SetTextViewText(Resource.Id.wSched2, "");
                views.SetTextViewText(Resource.Id.wHw1, "");
                views.SetTextViewText(Resource.Id.wHw2, "");
            }
            else
            {
                views.SetTextViewText(Resource.Id.wName, $"{snap.FullName} · {snap.GroupName}");
                views.SetTextViewText(Resource.Id.wTime, snap.SavedAt);
                views.SetTextViewText(Resource.Id.wAvg, snap.Average.ToString("0.0"));
                views.SetTextViewText(Resource.Id.wAttend,
                    $"посещаемость {snap.AttendancePct:0}% · {snap.Visits.Count} занятий");
                views.SetTextViewText(Resource.Id.wHw, HwSummary(snap));
                SetScheduleLines(views, snap);
                SetHomeworkLines(views, snap);
            }

            ApplyFontSizes(views, context);

            var intent = new Intent(context, typeof(MainActivity));
            var pi = PendingIntent.GetActivity(context, 1, intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            views.SetOnClickPendingIntent(Resource.Id.widgetRoot, pi);

            mgr.UpdateAppWidget(id, views);
        }
    }

    /// <summary>Размер шрифта виджета из настроек: малый −2sp · средний база · большой +2sp.</summary>
    static void ApplyFontSizes(RemoteViews views, Context context)
    {
        float k = Store.GetWidgetFont(context) switch { 0 => -2, 2 => +2, _ => 0 };
        void Sz(int viewId, float baseSp) =>
            views.SetTextViewTextSize(viewId, (int)Android.Util.ComplexUnitType.Sp, baseSp + k);

        Sz(Resource.Id.wName, 13);
        Sz(Resource.Id.wTime, 10);
        Sz(Resource.Id.wAvg, 24);
        Sz(Resource.Id.wAttend, 11);
        Sz(Resource.Id.wHw, 11);
        Sz(Resource.Id.wSchedTitle, 10);
        Sz(Resource.Id.wSched1, 10);
        Sz(Resource.Id.wSched2, 10);
        Sz(Resource.Id.wHwTitle, 10);
        Sz(Resource.Id.wHw1, 10);
        Sz(Resource.Id.wHw2, 10);
    }

    static string HwSummary(Snapshot snap)
    {
        int C(int t) => snap.Counters.TryGetValue(t, out var v) ? v : 0;
        return $"ДЗ: ⚠{C(0)} просрочено · ⏳{C(3)} текущих · ✓{C(1)} проверено";
    }

    /// <summary>
    /// Расписание на сегодня (только непрошедшие уроки) и завтра; максимум 2 строки.
    /// </summary>
    static void SetScheduleLines(RemoteViews views, Snapshot snap)
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        var now = DateTime.Now.TimeOfDay;

        static TimeSpan T(string s) =>
            TimeSpan.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : TimeSpan.Zero;

        var lessons = new List<(DateTime Date, ScheduleDto L)>();
        foreach (var l in snap.Schedule)
        {
            if (!DateTime.TryParse(l.Date, out var d)) continue;
            if (d.Date == today && T(l.FinishedAt) > now) lessons.Add((d, l));   // сегодня, не закончился
            else if (d.Date == tomorrow) lessons.Add((d, l));                     // завтра целиком
        }

        var lines = lessons
            .OrderBy(x => x.Date)
            .ThenBy(x => T(x.L.StartedAt))
            .Take(2)
            .ToList();

        var ids = new[] { Resource.Id.wSched1, Resource.Id.wSched2 };
        if (lines.Count == 0)
        {
            views.SetTextViewText(ids[0], "занятий сегодня и завтра нет");
            views.SetTextViewText(ids[1], "");
            return;
        }

        for (int i = 0; i < ids.Length; i++)
        {
            if (i >= lines.Count) { views.SetTextViewText(ids[i], ""); continue; }
            var (date, l) = lines[i];
            string when = date.Date == today ? "сегодня" : "завтра";
            views.SetTextViewText(ids[i],
                $"{when} {l.StartedAt} {Short(l.SubjectName, 18)} · {Short(l.RoomName, 10)}");
        }
    }

    /// <summary>
    /// Строки ДЗ: просроченные (красным) + ближайшие 7 дней.
    /// Формат: «дата · Предмет — срок» / тема курсивом.
    /// </summary>
    static void SetHomeworkLines(RemoteViews views, Snapshot snap)
    {
        var today = DateTime.Today;
        var lines = snap.Homework
            .Where(h => h.Status == 0 ||
                        (h.Status == 3 && DateTime.TryParse(h.CompletionTime, out var d) &&
                         d.Date >= today && d.Date <= today.AddDays(7)))
            .OrderBy(h => h.Status == 0 ? 0 : 1)
            .ThenBy(h => h.CompletionTime)
            .Take(2)
            .ToList();

        var ids = new[] { Resource.Id.wHw1, Resource.Id.wHw2 };
        if (lines.Count == 0)
        {
            views.SetTextViewText(ids[0], "✓ ДЗ на неделю чисто — всё сдано");
            views.SetTextViewText(ids[1], "");
            return;
        }

        for (int i = 0; i < ids.Length; i++)
        {
            if (i >= lines.Count) { views.SetTextViewText(ids[i], ""); continue; }
            var h = lines[i];
            var due = DateTime.TryParse(h.CompletionTime, out var d) ? d.Date : today;
            bool overdue = h.Status == 0;
            string deadline = overdue
                ? (today > due ? $"просрочено {(today - due).Days} дн." : "просрочено")
                : (due == today ? "сегодня!" : $"до {due:dd.MM}");

            var datePart = $"{due:dd.MM} · {Short(h.NameSpec, 18)}";
            var line1 = $"{datePart} — {deadline}";
            var theme = Short(h.Theme, 42);
            var ss = new Android.Text.SpannableString($"{line1}\n{theme}");
            var main = new Android.Graphics.Color(overdue ? unchecked((int)0xFFF26D6D) : unchecked((int)0xFFC7CFE4));
            ss.SetSpan(new Android.Text.Style.ForegroundColorSpan(main), 0, datePart.Length,
                Android.Text.SpanTypes.InclusiveExclusive);
            ss.SetSpan(new Android.Text.Style.ForegroundColorSpan(
                    new Android.Graphics.Color(overdue ? unchecked((int)0xFFF26D6D) : unchecked((int)0xFF8E9AB8))),
                datePart.Length + 3, line1.Length, Android.Text.SpanTypes.InclusiveExclusive);
            ss.SetSpan(new Android.Text.Style.StyleSpan(Android.Graphics.TypefaceStyle.Italic),
                line1.Length + 1, ss.Length(), Android.Text.SpanTypes.InclusiveExclusive);
            ss.SetSpan(new Android.Text.Style.ForegroundColorSpan(new Android.Graphics.Color(unchecked((int)0xFF9AA6C6))),
                line1.Length + 1, ss.Length(), Android.Text.SpanTypes.InclusiveExclusive);
            views.SetTextViewText(ids[i], ss);
        }
    }

    static string Short(string s, int max) =>
        string.IsNullOrWhiteSpace(s) ? "—" : (s.Length <= max ? s : s[..(max - 1)] + "…");
}
