using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using Orientation = Android.Widget.Orientation;
using System.Globalization;
using Android.Text;
using Android.Text.Style;

namespace TopJournal;

/// <summary>
/// Дневник: Оценки (таблица с закреплённой колонкой) · Расписание · ДЗ (умная сортировка).
/// Тема — системная (светлая/тёмная) через values-night + выбор стиля по UiMode.
/// </summary>
[Activity(Label = "Журнал ТОП", Exported = false, LaunchMode = LaunchMode.SingleTop)]
public class MainActivity : Activity
{
    static readonly string[] DayNames =
        { "воскресенье", "понедельник", "вторник", "среда", "четверг", "пятница", "суббота" };

    const int SubjWidthDp = 108;   // ширина закреплённой колонки предметов
    const int DateWidthDp = 42;    // ширина ячейки даты
    const int RowHeightDp = 40;    // фиксированная высота строки таблицы

    TextView? _name;
    TextView? _group;
    Button? _refresh;
    TextView? _tabGrades;
    TextView? _tabSchedule;
    TextView? _tabHomework;
    TextView? _emptyHint;
    LinearLayout? _gradesRoot;
    LinearLayout? _gradesHeaderRow;
    HorizontalScrollView? _gradesHeaderScroll;
    HorizontalScrollView? _gradesGridScroll;
    TableLayout? _subjectsTable;
    TableLayout? _gradesTable;
    ScrollView? _scheduleScroll;
    LinearLayout? _scheduleList;
    ScrollView? _hwScroll;
    LinearLayout? _hwList;

    int _tab; // 0 оценки · 1 расписание · 2 ДЗ
    bool _syncScroll;
    Snapshot? _snap;

    int Dp(float v) => (int)(v * Resources!.DisplayMetrics!.Density);
    Color C(int colorId) => Resources!.GetColor(colorId, null!);
    static int Argb(Color c) => (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;
    bool IsNight => (Resources!.Configuration.UiMode & UiMode.NightMask) == UiMode.NightYes;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        SetTheme(IsNight ? Resource.Style.AppTheme_Dark : Resource.Style.AppTheme); // до SetContentView
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.main);

        _name = FindViewById<TextView>(Resource.Id.studentName)!;
        _group = FindViewById<TextView>(Resource.Id.studentGroup)!;
        _refresh = FindViewById<Button>(Resource.Id.refreshBtn)!;
        _tabGrades = FindViewById<TextView>(Resource.Id.tabGrades)!;
        _tabSchedule = FindViewById<TextView>(Resource.Id.tabSchedule)!;
        _tabHomework = FindViewById<TextView>(Resource.Id.tabHomework)!;
        _emptyHint = FindViewById<TextView>(Resource.Id.emptyHint)!;
        _gradesRoot = FindViewById<LinearLayout>(Resource.Id.gradesRoot)!;
        _gradesHeaderRow = FindViewById<LinearLayout>(Resource.Id.gradesHeaderRow)!;
        _gradesHeaderScroll = FindViewById<HorizontalScrollView>(Resource.Id.gradesHeaderScroll)!;
        _gradesGridScroll = FindViewById<HorizontalScrollView>(Resource.Id.gradesGridScroll)!;
        _subjectsTable = FindViewById<TableLayout>(Resource.Id.subjectsTable)!;
        _gradesTable = FindViewById<TableLayout>(Resource.Id.gradesTable)!;
        _scheduleScroll = FindViewById<ScrollView>(Resource.Id.scheduleScroll)!;
        _scheduleList = FindViewById<LinearLayout>(Resource.Id.scheduleList)!;
        _hwScroll = FindViewById<ScrollView>(Resource.Id.hwScroll)!;
        _hwList = FindViewById<LinearLayout>(Resource.Id.hwList)!;

        _tabGrades.Click += (_, _) => SwitchTab(0);
        _tabSchedule.Click += (_, _) => SwitchTab(1);
        _tabHomework.Click += (_, _) => SwitchTab(2);
        _refresh.Click += async (_, _) => await RefreshAsync();

        // синхронизация горизонтального скролла: заголовок дат ↔ сетка оценок
        _gradesGridScroll.ScrollChange += (_, e) =>
        {
            if (_syncScroll) return;
            _syncScroll = true;
            _gradesHeaderScroll!.ScrollTo(e.ScrollX, 0);
            _syncScroll = false;
        };
        _gradesHeaderScroll.ScrollChange += (_, e) =>
        {
            if (_syncScroll) return;
            _syncScroll = true;
            _gradesGridScroll!.ScrollTo(e.ScrollX, 0);
            _syncScroll = false;
        };

        // Android 13+: разрешение на уведомления
        if ((int)Build.VERSION.SdkInt >= 33 &&
            CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted)
            RequestPermissions(new[] { Android.Manifest.Permission.PostNotifications }, 1);

        Notifier.EnsureChannels(this);
        SyncService.Schedule(this);

        var snap = Store.LoadSnapshot(this);
        if (snap is null)
        {
            _emptyHint.Visibility = ViewStates.Visible;
            _ = RefreshAsync();
        }
        else Render(snap);
        SwitchTab(0);
    }

    protected override void OnResume()
    {
        base.OnResume();
        var snap = Store.LoadSnapshot(this);
        if (snap is not null) Render(snap);
    }

    // ------------------------------------------------------------- refresh

    async Task RefreshAsync()
    {
        _refresh!.Enabled = false;
        Toast.MakeText(this, "Синхронизация…", ToastLength.Short)?.Show();
        try
        {
            await SyncService.SyncNowAsync(this);
            RunOnUiThread(() =>
            {
                var snap = Store.LoadSnapshot(this);
                if (snap is not null) Render(snap);
                Toast.MakeText(this, "Готово ✓", ToastLength.Short)?.Show();
            });
        }
        catch (Exception ex)
        {
            RunOnUiThread(() =>
                Toast.MakeText(this, "Ошибка: " + ex.Message, ToastLength.Long)?.Show());
        }
        finally
        {
            RunOnUiThread(() => _refresh.Enabled = true);
        }
    }

    // ---------------------------------------------------------------- tabs

    void SwitchTab(int index)
    {
        _tab = index;
        PaintTab(_tabGrades!, index == 0);
        PaintTab(_tabSchedule!, index == 1);
        PaintTab(_tabHomework!, index == 2);
        _emptyHint!.Visibility = _snap is null && index == 0 ? ViewStates.Visible : ViewStates.Gone;
        _gradesRoot!.Visibility = index == 0 ? ViewStates.Visible : ViewStates.Gone;
        _scheduleScroll!.Visibility = index == 1 ? ViewStates.Visible : ViewStates.Gone;
        _hwScroll!.Visibility = index == 2 ? ViewStates.Visible : ViewStates.Gone;
    }

    void PaintTab(TextView tab, bool active)
    {
        tab.SetTextColor(C(active ? Resource.Color.tabActive : Resource.Color.tabIdle));
        tab.SetBackgroundColor(C(active ? Resource.Color.tabActiveBg : Resource.Color.tabBarBg));
    }

    // -------------------------------------------------------------- render

    void Render(Snapshot snap)
    {
        _snap = snap;
        _name!.Text = string.IsNullOrEmpty(snap.FullName) ? "Студент" : snap.FullName;
        _group!.Text = $"{snap.GroupName}  ·  ср. {snap.Average:0.00}  ·  посещ. {snap.AttendancePct:0}%  ·  {snap.SavedAt}";
        BuildGrades(snap);
        BuildSchedule(snap);
        BuildHomework(snap);
    }

    // --------------------------------------------------- вкладка «Оценки»

    void BuildGrades(Snapshot snap)
    {
        _gradesHeaderRow!.RemoveAllViews();
        _subjectsTable!.RemoveAllViews();
        _gradesTable!.RemoveAllViews();

        var dates = snap.Visits.Select(v => v.DateVisit).Distinct().OrderBy(d => d).ToList();
        var subjects = snap.Visits.Select(v => v.SpecName).Distinct().OrderBy(s => s).ToList();

        // шапка дат (скроллится синхронно с сеткой)
        foreach (var d in dates)
        {
            var dt = DateTime.Parse(d, CultureInfo.InvariantCulture);
            var cell = new TextView(this)
            {
                Text = dt.ToString("dd.MM"),
                TextSize = 10,
                Gravity = GravityFlags.Center,
            };
            cell.SetTypeface(null, TypefaceStyle.Bold);
            cell.SetTextColor(C(Resource.Color.inkSecondary));
            cell.Background = CellDrawable(C(Resource.Color.gridHeaderBg));
            cell.LayoutParameters = new LinearLayout.LayoutParams(Dp(DateWidthDp), Dp(32));
            _gradesHeaderRow.AddView(cell);
        }

        for (int i = 0; i < subjects.Count; i++)
        {
            int rowBg = C(i % 2 == 1 ? Resource.Color.rowAlt : Resource.Color.cardBg);
            var subject = subjects[i];

            // закреплённая колонка предмета
            var stv = new TextView(this)
            {
                Text = subject,
                TextSize = 10,
                Gravity = GravityFlags.CenterVertical,
            };
            stv.SetTextColor(C(Resource.Color.ink));
            stv.Ellipsize = TextUtils.TruncateAt.End;
            stv.SetMaxLines(2);
            stv.SetPadding(Dp(8), 0, Dp(4), 0);
            stv.Background = CellDrawable(rowBg);
            var srow = new TableRow(this);
            srow.AddView(stv, new TableRow.LayoutParams(Dp(SubjWidthDp), Dp(RowHeightDp)));
            srow.Clickable = true;
            srow.Click += (_, _) => ShowSubject(subject);
            _subjectsTable.AddView(srow, new TableLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, Dp(RowHeightDp)));

            // строка сетки оценок
            var grow = new TableRow(this);
            grow.Clickable = true;
            grow.Click += (_, _) => ShowSubject(subject);
            foreach (var d in dates)
            {
                var cells = snap.Visits.Where(v => v.SpecName == subject && v.DateVisit == d).ToList();
                string text = "";
                Color color = C(Resource.Color.ink);

                var marks = cells.SelectMany(c => c.Marks.Select(m => m.Mark)).ToList();
                if (marks.Count > 0)
                {
                    text = string.Join("·", marks);
                    color = MarkColor(marks.Min());
                }
                else if (cells.Any(c => c.StatusWas == 0))
                {
                    text = "П"; color = C(Resource.Color.absent);
                }
                else if (cells.Any(c => c.StatusWas == 2))
                {
                    text = "О"; color = C(Resource.Color.late);
                }

                var tv = new TextView(this)
                {
                    Text = text,
                    TextSize = 11,
                    Gravity = GravityFlags.Center,
                };
                tv.SetTextColor(color);
                if (marks.Count > 0 || text.Length > 0) tv.SetTypeface(null, TypefaceStyle.Bold);
                tv.Background = CellDrawable(rowBg);
                grow.AddView(tv, new TableRow.LayoutParams(Dp(DateWidthDp), Dp(RowHeightDp)));
            }
            _gradesTable.AddView(grow, new TableLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, Dp(RowHeightDp)));
        }

        // легенда
        var legend = new TextView(this)
        {
            Text = "П — прогул · О — опоздание · нажмите на строку для деталей",
            TextSize = 10,
        };
        legend.SetTextColor(C(Resource.Color.inkSecondary));
        legend.SetPadding(Dp(8), Dp(10), Dp(8), Dp(6));
        _gradesTable.AddView(legend);

        // к свежим датам
        _gradesGridScroll!.Post(() =>
        {
            _gradesGridScroll.FullScroll(FocusSearchDirection.Right);
            _gradesHeaderScroll!.FullScroll(FocusSearchDirection.Right);
        });
    }

    GradientDrawable CellDrawable(int fillArgb)
    {
        var g = new GradientDrawable();
        g.SetColor(fillArgb);
        g.SetStroke(Dp(1), C(Resource.Color.gridLine));
        return g;
    }

    Color MarkColor(int mark) => mark switch
    {
        5 => C(Resource.Color.mark5),
        4 => C(Resource.Color.mark4),
        3 => C(Resource.Color.mark3),
        _ => C(Resource.Color.mark2),
    };

    /// <summary>Модалка предмета: средний балл + все оценки с темами занятий.</summary>
    void ShowSubject(string subject)
    {
        if (_snap is null) return;
        var visits = _snap.Visits
            .Where(v => v.SpecName == subject)
            .OrderByDescending(v => v.DateVisit)
            .ThenBy(v => v.LessonNumber)
            .ToList();
        var marks = visits.SelectMany(v => v.Marks.Select(m => m.Mark)).ToList();

        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        box.SetPadding(Dp(22), Dp(18), Dp(22), Dp(8));

        var title = new TextView(this) { Text = subject, TextSize = 16 };
        title.SetTextColor(C(Resource.Color.ink));
        title.SetTypeface(null, TypefaceStyle.Bold);
        box.AddView(title);

        var avg = marks.Count > 0
            ? $"средний балл {marks.Average():0.00} · оценок {marks.Count}"
            : "оценок пока нет";
        var sub = new TextView(this) { Text = avg, TextSize = 12 };
        sub.SetTextColor(C(Resource.Color.inkSecondary));
        sub.SetPadding(0, Dp(2), 0, Dp(10));
        box.AddView(sub);

        foreach (var v in visits)
        {
            if (!v.Marks.Any() && v.StatusWas == 1) continue; // пустые «был» не показываем
            var line1 = new TextView(this) { TextSize = 12 };
            var date = DateTime.Parse(v.DateVisit, CultureInfo.InvariantCulture);
            line1.Append(DateChip(date.ToString("dd.MM")));
            line1.Append($"  {(string.IsNullOrWhiteSpace(v.LessonTheme) ? "без темы" : v.LessonTheme)}");
            line1.SetTextColor(C(Resource.Color.ink));
            line1.SetPadding(0, Dp(6), 0, 0);
            box.AddView(line1);

            var line2 = new TextView(this) { TextSize = 11 };
            foreach (var (mark, field) in v.Marks)
            {
                var s = new SpannableString($"{mark} {field}");
                s.SetSpan(new ForegroundColorSpan(MarkColor(mark)), 0, 1, SpanTypes.InclusiveExclusive);
                s.SetSpan(new StyleSpan(TypefaceStyle.Bold), 0, 1, SpanTypes.InclusiveExclusive);
                line2.Append(s);
                line2.Append("   ");
            }
            if (v.StatusWas == 0) line2.Append(Span("П прогул", C(Resource.Color.absent)));
            if (v.StatusWas == 2) line2.Append(Span("О опоздание", C(Resource.Color.late)));
            line2.SetTextColor(C(Resource.Color.inkSecondary));
            line2.SetPadding(Dp(34), Dp(1), 0, 0);
            box.AddView(line2);
        }

        if (visits.Count == 0)
            box.AddView(new TextView(this) { Text = "нет занятий", TextSize = 12 });

        var scroll = new ScrollView(this);
        scroll.AddView(box);
        new AlertDialog.Builder(this)
            .SetView(scroll)
            .SetPositiveButton("Закрыть", (_, _) => { })
            .Show();
    }

    ISpannable Span(string text, Color color)
    {
        var s = new SpannableString(text);
        s.SetSpan(new ForegroundColorSpan(color), 0, text.Length, SpanTypes.InclusiveExclusive);
        s.SetSpan(new StyleSpan(TypefaceStyle.Bold), 0, text.Length, SpanTypes.InclusiveExclusive);
        return s;
    }

    Java.Lang.ICharSequence DateChip(string date)
    {
        var s = new SpannableString(date);
        s.SetSpan(new ForegroundColorSpan(C(Resource.Color.accent)), 0, date.Length, SpanTypes.InclusiveExclusive);
        s.SetSpan(new StyleSpan(TypefaceStyle.Bold), 0, date.Length, SpanTypes.InclusiveExclusive);
        return s;
    }

    // ----------------------------------------------- вкладка «Расписание»

    void BuildSchedule(Snapshot snap)
    {
        var box = _scheduleList!;
        box.RemoveAllViews();

        var days = snap.Schedule
            .GroupBy(s => s.Date)
            .OrderBy(g => g.Key)
            .ToList();
        if (days.Count == 0)
        {
            box.AddView(SimpleLine("Расписание пока пусто"));
            return;
        }

        foreach (var day in days)
        {
            var dt = DateTime.Parse(day.Key, CultureInfo.InvariantCulture);
            bool today = day.Key == DateTime.Today.ToString("yyyy-MM-dd");

            var header = new TextView(this)
            {
                Text = $"{dt:dd.MM} {DayNames[(int)dt.DayOfWeek]}{(today ? "  · сегодня" : "")}",
                TextSize = 13,
                Gravity = GravityFlags.CenterVertical,
            };
            header.SetTextColor(C(today ? Resource.Color.accent : Resource.Color.ink));
            header.SetTypeface(null, TypefaceStyle.Bold);
            header.SetPadding(Dp(10), Dp(12), Dp(10), Dp(6));
            header.Background = Round(C(today ? Resource.Color.todayChipBg : Resource.Color.gridHeaderBg), 10);
            var hlp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            hlp.SetMargins(0, Dp(10), 0, Dp(4));
            header.LayoutParameters = hlp;
            box.AddView(header);

            foreach (var lesson in day.OrderBy(l => l.Lesson))
            {
                var card = new LinearLayout(this) { Orientation = Orientation.Horizontal };
                card.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
                card.Background = Round(C(Resource.Color.cardBg), 14);
                var clp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
                clp.SetMargins(0, 0, 0, Dp(6));
                card.LayoutParameters = clp;

                var num = new TextView(this) { Text = lesson.Lesson.ToString(), TextSize = 16 };
                num.SetTextColor(C(Resource.Color.inkSecondary));
                num.SetTypeface(null, TypefaceStyle.Bold);
                num.SetMinWidth(Dp(20));
                card.AddView(num);

                var mid = new LinearLayout(this) { Orientation = Orientation.Vertical };
                mid.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = Dp(8) };
                var subj = new TextView(this) { Text = lesson.SubjectName, TextSize = 14 };
                subj.SetTextColor(C(Resource.Color.ink));
                subj.SetTypeface(null, TypefaceStyle.Bold);
                mid.AddView(subj);
                var teacher = new TextView(this) { Text = lesson.TeacherName, TextSize = 11 };
                teacher.SetTextColor(C(Resource.Color.inkSecondary));
                mid.AddView(teacher);
                card.AddView(mid);

                var right = new LinearLayout(this) { Orientation = Orientation.Vertical };
                right.SetGravity(GravityFlags.Right);
                var time = new TextView(this) { Text = $"{lesson.StartedAt}–{lesson.FinishedAt}", TextSize = 12 };
                time.SetTextColor(C(Resource.Color.accent));
                right.AddView(time);
                var room = new TextView(this) { Text = lesson.RoomName, TextSize = 11 };
                room.SetTextColor(C(Resource.Color.inkSecondary));
                right.AddView(room);
                card.AddView(right);

                box.AddView(card);
            }
        }
    }

    // ------------------------------------------------------ вкладка «ДЗ»

    void BuildHomework(Snapshot snap)
    {
        var box = _hwList!;
        box.RemoveAllViews();

        int Cnt(int t) => snap.Counters.TryGetValue(t, out var v) ? v : 0;
        var summary = new TextView(this)
        {
            Text = $"Всего {Cnt(4)} · проверено {Cnt(1)} · на проверке {Cnt(2)} · текущих {Cnt(3)} · просрочено {Cnt(0)}",
            TextSize = 12,
        };
        summary.SetTextColor(C(Resource.Color.inkSecondary));
        summary.SetPadding(Dp(4), Dp(6), Dp(4), Dp(10));
        box.AddView(summary);

        var today = DateTime.Today;
        // умный порядок: истекает сегодня → просроченные → предстоящие → на проверке
        var pending = snap.Homework.Where(h => h.Status is 0 or 3).ToList();
        var ordered = pending
            .OrderBy(h => Rank(h, today))
            .ThenBy(h => h.CompletionTime)
            .ToList();
        var onCheck = snap.Homework.Where(h => h.Status == 2)
            .OrderByDescending(h => h.CreationTime).ToList();

        if (ordered.Count == 0 && onCheck.Count == 0)
        {
            box.AddView(SimpleLine("Несданных ДЗ нет — всё чисто ✓"));
            return;
        }

        foreach (var h in ordered)
        {
            var card = HwCard(h, today);
            box.AddView(card);
        }
        foreach (var h in onCheck)
            box.AddView(HwCard(h, today));
    }

    /// <summary>0 — истекает сегодня, 1 — просрочено, 2 — предстоящее, 3 — на проверке.</summary>
    static int Rank(HomeworkDto h, DateTime today)
    {
        var due = DateTime.TryParse(h.CompletionTime, out var d) ? d.Date : today;
        return h.Status switch
        {
            3 when due == today => 0,
            0 => 1,
            3 => 2,
            _ => 3,
        };
    }

    View HwCard(HomeworkDto h, DateTime today)
    {
        var due = DateTime.TryParse(h.CompletionTime, out var d) ? d.Date : today;

        string deadline;
        int bgId, inkId;
        switch (Rank(h, today))
        {
            case 0:
                deadline = "⏰ истекает сегодня";
                bgId = Resource.Color.hwTodayBg; inkId = Resource.Color.hwTodayInk;
                break;
            case 1:
                {
                    int days = Math.Max(1, (today - due).Days);
                    deadline = $"просрочено на {days} дн. (срок {due:dd.MM})";
                    bgId = Resource.Color.hwOverdueBg; inkId = Resource.Color.hwOverdueInk;
                    break;
                }
            case 3:
                deadline = "на проверке";
                bgId = Resource.Color.hwCheckBg; inkId = Resource.Color.hwCheckInk;
                break;
            default:
                deadline = $"сдать до {due:dd.MM}";
                bgId = Resource.Color.cardBg; inkId = Resource.Color.accent;
                break;
        }

        var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
        card.Background = Round(C(bgId), 14);
        var clp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        clp.SetMargins(0, 0, 0, Dp(8));
        card.LayoutParameters = clp;
        card.Clickable = true;
        card.Click += (_, _) => ShowHomework(h);

        var top = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var subject = new TextView(this) { Text = h.NameSpec, TextSize = 14 };
        subject.SetTextColor(C(inkId));
        subject.SetTypeface(null, TypefaceStyle.Bold);
        subject.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
        top.AddView(subject);

        var chip = new TextView(this) { Text = deadline, TextSize = 11 };
        chip.SetTextColor(C(inkId));
        chip.SetPadding(Dp(6), 0, 0, 0);
        top.AddView(chip);
        card.AddView(top);

        var theme = new TextView(this) { Text = h.Theme, TextSize = 13 };
        theme.SetTextColor(C(Resource.Color.ink));
        theme.SetPadding(0, Dp(4), 0, 0);
        card.AddView(theme);

        var teacher = new TextView(this) { Text = h.FioTeach, TextSize = 11 };
        teacher.SetTextColor(C(Resource.Color.inkSecondary));
        teacher.SetPadding(0, Dp(4), 0, 0);
        card.AddView(teacher);

        return card;
    }

    /// <summary>Модалка ДЗ: описание, даты, оценка и открытие файлов (материал/работа ученика).</summary>
    void ShowHomework(HomeworkDto h)
    {
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        box.SetPadding(Dp(22), Dp(18), Dp(22), Dp(8));

        TextView L(string text, float sp, bool bold, int colorId)
        {
            var tv = new TextView(this) { Text = text, TextSize = sp };
            tv.SetTextColor(C(colorId));
            if (bold) tv.SetTypeface(null, TypefaceStyle.Bold);
            return tv;
        }

        box.AddView(L(h.NameSpec, 16, true, Resource.Color.ink));
        var theme = L(h.Theme, 13, false, Resource.Color.ink);
        theme.SetPadding(0, Dp(6), 0, 0);
        box.AddView(theme);

        var due = DateTime.TryParse(h.CompletionTime, out var d) ? d : DateTime.Today;
        string status = h.Status switch
        {
            0 => "❌ просрочено",
            1 => h.Stud?.Mark is int m ? $"✓ проверено — оценка {m}" : "✓ проверено",
            2 => "🕐 на проверке",
            _ => "открыто",
        };
        var dates = L($"выдано {Fmt(h.CreationTime)} · сдать {due:dd.MM} · {status}", 12, false, Resource.Color.inkSecondary);
        dates.SetPadding(0, Dp(6), 0, 0);
        box.AddView(dates);

        box.AddView(L($"Преподаватель: {h.FioTeach}", 12, false, Resource.Color.inkSecondary));

        if (h.Stud is { } stud)
        {
            var markLine = stud.Mark is int m2
                ? $"Оценка за работу: {m2}{(stud.AutoMark ? " (автопроверка)" : "")}"
                : "Сдано (без оценки)";
            var ml = L(markLine, 13, true, Resource.Color.mark5);
            ml.SetPadding(0, Dp(8), 0, 0);
            box.AddView(ml);
            if (!string.IsNullOrEmpty(stud.CreationTime))
                box.AddView(L($"Сдано: {Fmt(stud.CreationTime)}", 12, false, Resource.Color.inkSecondary));
        }

        if (!string.IsNullOrEmpty(h.Comment))
        {
            var cl = L("Комментарий: " + h.Comment, 12, false, Resource.Color.inkSecondary);
            cl.SetPadding(0, Dp(6), 0, 0);
            box.AddView(cl);
        }

        // файлы: материал преподавателя + работа ученика
        var hasMaterial = !string.IsNullOrEmpty(h.FilePath);
        var hasStudentFile = !string.IsNullOrEmpty(h.Stud?.FilePath);
        if (hasMaterial || hasStudentFile)
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row.SetPadding(0, Dp(12), 0, 0);

            if (hasMaterial)
            {
                var b = new Button(this) { Text = "📄 Материал" };
                b.Click += (_, _) => DownloadFile(h.FilePath!, h.NameSpec);
                row.AddView(b, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { RightMargin = Dp(6) });
            }
            if (hasStudentFile)
            {
                var b2 = new Button(this) { Text = "📎 Моя работа" };
                b2.Click += (_, _) => DownloadFile(h.Stud!.FilePath!, h.NameSpec);
                row.AddView(b2, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            }
            box.AddView(row);
        }
        else
        {
            var nf = L("Файл материала отсутствует", 12, false, Resource.Color.inkSecondary);
            nf.SetPadding(0, Dp(10), 0, 0);
            box.AddView(nf);
        }

        var scroll = new ScrollView(this);
        scroll.AddView(box);
        new AlertDialog.Builder(this)
            .SetView(scroll)
            .SetPositiveButton("Закрыть", (_, _) => { })
            .Show();
    }

    static string Fmt(string iso) =>
        DateTime.TryParse(iso, out var d) ? d.ToString("dd.MM.yyyy") : iso;

    /// <summary>Скачивание файла ДЗ через DownloadManager (с Referer для WAF).</summary>
    void DownloadFile(string url, string title)
    {
        try
        {
            var req = new DownloadManager.Request(Android.Net.Uri.Parse(url));
            req.AddRequestHeader("Referer", "https://journalka.top-academy.ru/");
            req.SetTitle("Журнал ТОП: " + title);
            req.SetDescription(url);
            req.SetNotificationVisibility(Android.App.DownloadVisibility.VisibleNotifyCompleted);
            req.SetAllowedOverMetered(true);
            var dm = (DownloadManager)GetSystemService(DownloadService)!;
            dm.Enqueue(req);
            Toast.MakeText(this, "Загрузка началась — файл появится в шторке", ToastLength.Long)?.Show();
        }
        catch (Exception ex)
        {
            Toast.MakeText(this, "Не удалось начать загрузку: " + ex.Message, ToastLength.Long)?.Show();
        }
    }

    // -------------------------------------------------------------- utils

    TextView SimpleLine(string text)
    {
        var tv = new TextView(this) { Text = text, TextSize = 13 };
        tv.SetTextColor(C(Resource.Color.inkSecondary));
        tv.SetPadding(Dp(4), Dp(16), Dp(4), Dp(16));
        return tv;
    }

    GradientDrawable Round(int argb, float radiusDp)
    {
        var g = new GradientDrawable();
        g.SetColor(argb);
        g.SetCornerRadius(Dp(radiusDp));
        return g;
    }
}
