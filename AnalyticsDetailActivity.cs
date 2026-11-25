using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using System.Globalization;

namespace TopJournal;

/// <summary>
/// Страница-пояснение карточки аналитики: что это, методика, инфографика
/// по реальным данным и развёрнутые выводы. Открывается тапом по карточке.
/// </summary>
[Activity(Label = "Аналитика", Exported = false)]
public class AnalyticsDetailActivity : Activity
{
    int Dp(float v) => (int)(v * Resources!.DisplayMetrics!.Density);
    Color C(int id) => Resources!.GetColor(id, null!);

    protected override void OnCreate(Bundle? b)
    {
        bool night = (Resources!.Configuration.UiMode & Android.Content.Res.UiMode.NightMask)
                     == Android.Content.Res.UiMode.NightYes;
        SetTheme(night ? Resource.Style.AppTheme_Dark : Resource.Style.AppTheme);
        base.OnCreate(b);

        int card = Intent?.GetIntExtra("card", 0) ?? 0;
        var snap = Store.LoadSnapshot(this);

        var scroll = new ScrollView(this);
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        box.SetPadding(Dp(18), Dp(14), Dp(18), Dp(24));
        scroll.AddView(box);
        SetContentView(scroll);

        if (snap is null)
        {
            box.AddView(T("Нет данных", 18, true, Resource.Color.ink));
            return;
        }

        // аналитика — на «эффективных» занятиях (без прогулов в отпуск/больничный)
        var snapF = new Snapshot
        {
            SavedAt = snap.SavedAt, FullName = snap.FullName, GroupName = snap.GroupName,
            Visits = Store.EffectiveVisits(this, snap), Homework = snap.Homework,
            Schedule = snap.Schedule, Counters = snap.Counters,
        };

        switch (card)
        {
            case 0: Dynamics(box, snapF); break;
            case 1: Forecast(box, snapF); break;
            case 2: ChangePoint(box, snapF); break;
            case 3: Radar(box, snapF); break;
            case 4: Subjects(box, snapF); break;
            case 5: Weekdays(box, snapF); break;
            case 6: Lag(box, snapF); break;
        }
    }

    // ------------------------------------------------------------------ ui

    TextView T(string text, float sp, bool bold, int colorId, int padL = 0)
    {
        var tv = new TextView(this) { Text = text, TextSize = sp };
        tv.SetTextColor(C(colorId));
        if (bold) tv.SetTypeface(null, TypefaceStyle.Bold);
        tv.SetPadding(Dp(padL), Dp(4), 0, Dp(2));
        return tv;
    }

    void Header(LinearLayout box, string emoji, string title)
    {
        var h = new TextView(this) { Text = $"{emoji} {title}", TextSize = 22 };
        h.SetTextColor(C(Resource.Color.accent));
        h.SetTypeface(null, TypefaceStyle.Bold);
        h.SetPadding(0, Dp(6), 0, Dp(4));
        box.AddView(h);
    }

    void Section(LinearLayout box, string title)
    {
        var s = new TextView(this) { Text = title, TextSize = 15 };
        s.SetTextColor(C(Resource.Color.ink));
        s.SetTypeface(null, TypefaceStyle.Bold);
        s.SetPadding(0, Dp(16), 0, Dp(4));
        box.AddView(s);
    }

    void Paragraph(LinearLayout box, string text)
    {
        var p = new TextView(this) { Text = text, TextSize = 13 };
        p.SetTextColor(C(Resource.Color.ink));
        p.SetLineSpacing(Dp(2), 1.1f);
        box.AddView(p);
    }

    TextView Conclusion(string text, int colorId)
    {
        var p = new TextView(this) { Text = "•  " + text, TextSize = 13 };
        p.SetTextColor(C(colorId));
        p.SetLineSpacing(Dp(2), 1.1f);
        p.SetPadding(Dp(8), Dp(3), 0, Dp(3));
        return p;
    }

    void AddChart(LinearLayout box, View chart, int heightDp)
    {
        var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(heightDp));
        lp.SetMargins(0, Dp(8), 0, Dp(8));
        chart.LayoutParameters = lp;
        box.AddView(chart);
    }

    // ------------------------------------------------------------- 1. динамика

    void Dynamics(LinearLayout box, Snapshot snap)
    {
        Header(box, "📈", "Динамика успеваемости");
        Section(box, "Что это");
        Paragraph(box,
            "Тренд оценок во времени с защитой от случайного шума. Отвечает на вопрос: «студент учится лучше, хуже или держит уровень?» — " +
            "и главное — статистически ли это значимо, или колебания в пределах случайности.");

        Section(box, "Методика");
        Paragraph(box,
            "• Наклон тренда — Theil–Sen: медиана наклонов между всеми парами точек. Одна случайная «двойка» не ломает линию, в отличие от обычной регрессии.\n" +
            "• Значимость — тест Манна–Кендалла: p — вероятность получить такой тренд чисто случайно. p < 0.05 — тренду можно верить.\n" +
            "• Поправка на микс работ: месяц, где было больше контрольных, не должен выглядеть «провалом» — месячные средние пересчитываются с одинаковыми весами типов работ.\n" +
            "• «Вероятность снижения» — байесовская оценка: насколько данные поддерживают гипотезу об отрицательном наклоне.");

        var pts = Analytics.AllMarks(snap);
        var xs = pts.Select(p => p.Date.ToOADate()).ToList();
        var ys = pts.Select(p => (double)p.Mark).ToList();
        double slope = Analytics.TheilSlope(xs, ys) * 30.4;
        double se = Analytics.TheilSlopeSe(xs, ys) * 30.4;
        var mk = Analytics.MannKendall(ys);
        double pNeg = Analytics.ProbNegative(slope, se);

        if (pts.Count >= 30)
        {
            Section(box, "Инфографика");
            var chart = new LineChartView(this, Argb(C(Resource.Color.inkSecondary)))
            {
                XFirstLabel = pts[0].Date.ToString("dd.MM"),
                XLastLabel = pts[^1].Date.ToString("dd.MM"),
            };
            int inkArgb = Argb(C(Resource.Color.ink));
            int accentArgb = Argb(C(Resource.Color.accent));
            int m2 = Argb(C(Resource.Color.mark2));
            foreach (var p in pts)
                chart.Dots.Add((p.Date.ToOADate(), p.Mark, m2));

            // скользящее среднее (окно 10) — глазу виден уровень
            var roll = new List<(double, double)>();
            for (int i = 9; i < pts.Count; i++)
                roll.Add((pts[i].Date.ToOADate(), ys.Skip(i - 9).Take(10).Average()));
            chart.Lines.Add(new LineChartView.Series { Points = roll, ColorArgb = accentArgb, StrokeWidth = 5f });

            // линия тренда
            double y0 = ys.Take(10).DefaultIfEmpty(3).Average();
            double y1 = ys.Skip(Math.Max(0, ys.Count - 10)).DefaultIfEmpty(3).Average();
            double slopeVis = pts.Count > 1 ? (y1 - y0) / (xs[^1] - xs[0]) : 0;
            double midX = (xs[0] + xs[^1]) / 2, midY = ys.Average();
            chart.Lines.Add(new LineChartView.Series
            {
                Points = { (xs[0], midY - slopeVis * (midX - xs[0])), (xs[^1], midY + slopeVis * (midX - xs[^1])) },
                ColorArgb = inkArgb,
                StrokeWidth = 4f,
                Dashed = true,
            });
            AddChart(box, chart, 300);
        }

        Section(box, "Выводы");
        string verdict = mk.P < 0.05 && slope > 0 ? "устойчивый прогресс"
            : mk.P < 0.05 && slope < 0 ? "устойчивое снижение"
            : "плато: изменения статистически неотличимы от шума";
        int vColor = mk.P >= 0.05 ? Resource.Color.ink
            : slope > 0 ? Resource.Color.mark5 : Resource.Color.mark2;
        box.AddView(Conclusion($"Вердикт: {verdict}", vColor));
        box.AddView(Conclusion($"Скорость: {slope:+0.00;-0.00} балла/мес (±{se:0.00})", Resource.Color.ink));
        box.AddView(Conclusion($"Значимость тренда: p = {mk.P:0.00} (меньше 0.05 — значимо), τ Кендалла = {mk.Tau:+0.00;-0.00}", Resource.Color.ink));
        box.AddView(Conclusion($"Вероятность снижения уровня: {pNeg * 100:0}%", Resource.Color.inkSecondary));

        Section(box, "Месяцы (сырое / с поправкой на микс работ)");
        string[] mn = { "янв", "фев", "мар", "апр", "май", "июн", "июл", "авг", "сен", "окт", "ноя", "дек" };
        foreach (var (month, raw, adj, n) in Analytics.MonthlyAdjusted(snap))
        {
            int mi = int.Parse(month[..2]) - 1;
            box.AddView(T($"{mn[mi]}:  {raw:0.00} → {adj:0.00}  ({n} оценок)", 13, false,
                Math.Abs(raw - adj) > 0.15 ? Resource.Color.accent : Resource.Color.ink, 8));
        }
    }

    // ------------------------------------------------------------- 2. прогноз

    void Forecast(LinearLayout box, Snapshot snap)
    {
        Header(box, "🔮", "Прогноз среднего балла");
        Section(box, "Что это");
        Paragraph(box,
            "Ответ на вопрос «если студент продолжит заниматься в текущем темпе, какой средний балл будет через месяц?» — " +
            "с честным интервалом неопределённости, а не ложной точностью.");

        Section(box, "Методика");
        Paragraph(box,
            "Фильтр Калмана с состоянием [уровень, наклон]: оценка хранит не одно число, а текущий уровень и направление движения, " +
            "и на каждой новой оценке корректирует их с учётом «шумности» данных. Прогноз с демпфированием 0.85 — тренд постепенно затухает, " +
            "поэтому модель не экстраполирует резкие взлёты в бесконечность. Интервал 80% показывает коридор, в который прогноз попадёт в 8 случаях из 10.");

        var pts = Analytics.AllMarks(snap);
        var weekly = pts.GroupBy(p => (int)((p.Date - pts[0].Date).TotalDays / 7))
            .OrderBy(g => g.Key)
            .Select(g => (X: (double)g.Key, Y: g.Average(x => (double)x.Mark)))
            .ToList();
        var (lvl, sl, fc, band) = Analytics.KalmanForecast(weekly.Select(w => w.Y).ToList(), 4);

        if (weekly.Count >= 5)
        {
            Section(box, "Инфографика");
            var chart = new LineChartView(this, Argb(C(Resource.Color.inkSecondary)))
            {
                XFirstLabel = "недели →",
                XLastLabel = "+4 нед (прогноз)",
            };
            int m2 = Argb(C(Resource.Color.mark2));
            foreach (var w in weekly)
                chart.Dots.Add((w.X, w.Y, m2));
            chart.Lines.Add(new LineChartView.Series
            {
                Points = weekly.Select(w => (w.X, lvl + sl * (w.X - weekly[^1].X))).ToList(),
                ColorArgb = Argb(C(Resource.Color.accent)),
                StrokeWidth = 5f,
            });
            // коридор прогноза
            if (!double.IsNaN(band))
                chart.Lines.Add(new LineChartView.Series
                {
                    Points = { (weekly[^1].X, fc - band), (weekly[^1].X + 4, fc - band), (weekly[^1].X + 4, fc + band), (weekly[^1].X, fc + band) },
                    ColorArgb = Argb(C(Resource.Color.ink)),
                    StrokeWidth = 3f,
                    Dashed = true,
                });
            AddChart(box, chart, 300);
        }

        Section(box, "Выводы");
        box.AddView(Conclusion($"Текущий уровень (сглаженный): {lvl:0.00}", Resource.Color.ink));
        box.AddView(Conclusion($"Направление: {sl * 4.33:+0.00;-0.00} балла/мес", Resource.Color.ink));
        string b = double.IsNaN(band) ? "" : $" ± {band:0.00}";
        box.AddView(Conclusion($"Через 4 недели: {Math.Clamp(fc, 1, 5):0.00}{b} (при текущем темпе)", Resource.Color.mark5));
        box.AddView(Conclusion("Прогноз — не приговор: смена привычек меняет траекторию, модель подхватит это за 1–2 недели", Resource.Color.inkSecondary));
    }

    // ------------------------------------------------------------ 3. перелом

    void ChangePoint(LinearLayout box, Snapshot snap)
    {
        Header(box, "⚡", "Перелом уровня");
        Section(box, "Что это");
        Paragraph(box,
            "Поиск момента, когда средний уровень оценок сдвинулся — например, начало проседания или наоборот взлёт. " +
            "«Сдвиг» отличается от шума тем, что до и после даты средние устойчиво различаются.");

        Section(box, "Методика");
        Paragraph(box,
            "Байесовское сравнение двух моделей: «уровень постоянный» против «есть точка разбиения τ, уровни до и после разные». " +
            "Перебираются все возможные τ, выбирается максимум правдоподобия; итог — дата, величина сдвига и апостериорная вероятность того, что сдвиг вообще есть.");

        var pts = Analytics.AllMarks(snap);
        var cp = Analytics.ChangePoint(pts);

        if (cp is { } v && v.Date is { } d && pts.Count >= 30)
        {
            Section(box, "Инфографика");
            var chart = new LineChartView(this, Argb(C(Resource.Color.inkSecondary)))
            {
                XFirstLabel = pts[0].Date.ToString("dd.MM"),
                XLastLabel = pts[^1].Date.ToString("dd.MM"),
            };
            int m2 = Argb(C(Resource.Color.mark2));
            foreach (var p in pts)
                chart.Dots.Add((p.Date.ToOADate(), p.Mark, m2));
            double m1 = pts.TakeWhile(p => p.Date < d).DefaultIfEmpty().Average(p => (double)p.Mark);
            double m2v = pts.SkipWhile(p => p.Date < d).DefaultIfEmpty().Average(p => (double)p.Mark);
            int good = v.Delta >= 0 ? Resource.Color.mark5 : Resource.Color.mark2;
            chart.Lines.Add(new LineChartView.Series
            {
                Points = { (pts[0].Date.ToOADate(), m1), (d.ToOADate(), m1) },
                ColorArgb = Argb(C(good)),
                StrokeWidth = 5f,
            });
            chart.Lines.Add(new LineChartView.Series
            {
                Points = { (d.ToOADate(), m2v), (pts[^1].Date.ToOADate(), m2v) },
                ColorArgb = Argb(C(good)),
                StrokeWidth = 5f,
            });
            AddChart(box, chart, 300);

            Section(box, "Выводы");
            box.AddView(Conclusion($"После {d:dd.MM.yyyy} уровень изменился на {(v.Delta >= 0 ? "+" : "")}{v.Delta:0.00} балла", good));
            box.AddView(Conclusion($"Вероятность, что это реальный сдвиг, а не шум: {v.Prob * 100:0}%", Resource.Color.ink));
            box.AddView(Conclusion($"Средний до: {m1:0.00} · после: {m2v:0.00}", Resource.Color.ink));
        }
        else
        {
            Section(box, "Выводы");
            box.AddView(Conclusion(pts.Count < 30
                ? "Нужно ≥30 оценок для анализа"
                : "Сдвига уровня не обнаружено — успеваемость движется ровно, без переломов", Resource.Color.inkSecondary));
        }
    }

    // --------------------------------------------------------------- 4. радар

    void Radar(LinearLayout box, Snapshot snap)
    {
        Header(box, "🕸", "Профиль специалиста");
        Section(box, "Что это");
        Paragraph(box,
            "Четыре оси «будущего специалиста». Радар намеренно не сворачивается в одну цифру: сильный профиль = равномерный многоугольник, " +
            "а не вытянутый в одну сторону.");

        Section(box, "Оси");
        var axes = Analytics.ComputeRadar(snap);
        Paragraph(box,
            "• Знания — взвешенный средний балл: контрольные и итоговые ×3, лабораторные ×2.5, классные и домашние ×1 (сложное весит больше).\n" +
            "• Дисциплина — 60% посещаемость + 40% своевременность ДЗ (один фактор: они скоррелированы, раздельный учёт задваивал бы вклад).\n" +
            "• Самостоятельность — насколько классная/домашняя работы сильнее контрольных/лаб: маркер «решебник vs понимание».\n" +
            "• Динамика — тренд Theil–Sen за всё время: 50 = плато, выше = рост, ниже = снижение.");

        Section(box, "Инфографика");
        var radar = new RadarView(this,
            new[] { "Знания", "Дисциплина", "Самостоят.", "Динамика" },
            new double?[] { axes.Knowledge, axes.Discipline, axes.Independence, axes.Dynamics },
            Argb(C(Resource.Color.accent)), Argb(C(Resource.Color.inkSecondary)));
        AddChart(box, radar, 340);

        var bars = new BarChartView(this, Argb(C(Resource.Color.inkSecondary)), Argb(C(Resource.Color.ink)));
        var named = new[] { ("Знания", axes.Knowledge), ("Дисципл.", axes.Discipline), ("Самост.", axes.Independence), ("Динамика", axes.Dynamics) };
        foreach (var (label, val) in named)
            bars.Bars.Add((label, val ?? 0, Argb(C(val is null ? Resource.Color.inkSecondary : Resource.Color.accent)),
                val is null ? "—" : val.Value.ToString("0")));
        bars.MaxValue = 100;
        AddChart(box, bars, 240);

        Section(box, "Выводы");
        var have = named.Where(n => n.Item2.HasValue).Select(n => n.Item2!.Value).ToList();
        if (have.Count > 0)
        {
            double geo = Math.Exp(have.Average(v => Math.Log(Math.Max(1, v))));
            box.AddView(Conclusion($"Индекс специалиста (геометрическое среднее осей): {geo:0}/100 — провал по одной оси нельзя «залить» другой", Resource.Color.ink));
            var weakest = named.Where(n => n.Item2.HasValue).OrderBy(n => n.Item2!.Value).First();
            box.AddView(Conclusion($"Самая слабая ось: {weakest.Item1} ({weakest.Item2:0}) — точка роста", Resource.Color.mark3));
        }
        box.AddView(Conclusion("Ось «Самостоятельность» появляется, когда наберётся ≥5 классных/домашних и ≥3 контрольных оценок", Resource.Color.inkSecondary));
    }

    // ------------------------------------------------------------ 5. предметы

    void Subjects(LinearLayout box, Snapshot snap)
    {
        Header(box, "📚", "Предметы");
        Section(box, "Что это");
        Paragraph(box,
            "Рейтинг предметов с честной поправкой на размер выборки: по 5 контрольным нельзя сказать «средний 5.0» — " +
            "такие предметы подтягиваются к общему среднему (байесовская усадка).");

        Section(box, "Методика");
        Paragraph(box,
            "Усадка Джеймса–Стайна: θ̂ = (1−B)·среднее_предмета + B·общее_среднее, где B тем больше, чем меньше оценок. " +
            "± — 95% доверительный интервал. Значки ↗/↘ — только если тренд предмета переживает поправку на множественные сравнения (FDR q<0.1): " +
            "при 10+ предметах без поправки каждый 10-й «тренд» был бы случайным.");

        var stats = Analytics.SubjectStats(snap);
        if (stats.Count > 0)
        {
            Section(box, "Инфографика");
            var bars = new BarChartView(this, Argb(C(Resource.Color.inkSecondary)), Argb(C(Resource.Color.ink)));
            foreach (var s in stats.Take(8))
            {
                int color = s.Shrunk >= 4.5 ? Resource.Color.mark5
                    : s.Shrunk >= 3.5 ? Resource.Color.mark4
                    : s.Shrunk >= 2.5 ? Resource.Color.mark3 : Resource.Color.mark2;
                bars.Bars.Add((ShortName(s.Name), s.Shrunk, Argb(C(color)), s.Shrunk.ToString("0.0")));
            }
            bars.MaxValue = 5;
            AddChart(box, bars, 260);

            Section(box, "Данные");
            foreach (var s in stats)
                box.AddView(T($"{s.Name}: {s.Shrunk:0.00} ± {1.96 * s.Se:0.0}  (сырое {s.Raw:0.00}, n={s.N})", 12, false, Resource.Color.ink, 4));
        }
    }

    static string ShortName(string s)
    {
        var words = s.Split(' ').Where(w => !w.Equals("КБ") && !w.Equals("РПО")).ToArray();
        var name = string.Join(" ", words);
        return name.Length <= 12 ? name : name[..11] + "…";
    }

    // ------------------------------------------------------------ 6. прогулы

    void Weekdays(LinearLayout box, Snapshot snap)
    {
        Header(box, "📅", "Прогулы по дням недели");
        Section(box, "Что это");
        Paragraph(box,
            "Интенсивность прогулов по дням недели, нормированная на число занятий в этот день: «3 прогула в пятницу» при трёх парах — это 100%, " +
            "а при шести — половина. Без нормировки картина врала бы.");

        var rates = Analytics.WeekdayAbsenceRates(snap);
        if (rates.Count > 0)
        {
            Section(box, "Инфографика");
            var bars = new BarChartView(this, Argb(C(Resource.Color.inkSecondary)), Argb(C(Resource.Color.ink)));
            foreach (var r in rates)
                bars.Bars.Add((r.Day, r.RatePer10 * 10,
                    Argb(C(r == rates[0] ? Resource.Color.mark2 : Resource.Color.accent)),
                    $"{r.RatePer10 * 10:0}%"));
            bars.MaxValue = 100;
            AddChart(box, bars, 240);

            Section(box, "Выводы");
            var worst = rates[0];
            box.AddView(Conclusion($"Самый рискованный день: {worst.Day} — {worst.Absences} прогулов из {worst.Lessons} занятий ({worst.RatePer10 * 10:0}%)", Resource.Color.mark2));
            var best = rates[^1];
            box.AddView(Conclusion($"Самый стабильный: {best.Day} — {best.RatePer10 * 10:0}%", Resource.Color.mark5));
            box.AddView(Conclusion("Прогулы в отпуск/больничный (настроенные периоды) здесь не учитываются", Resource.Color.inkSecondary));
        }
    }

    // ---------------------------------------------------------------- 7. лаг

    void Lag(LinearLayout box, Snapshot snap)
    {
        Header(box, "🔗", "Эффект прогулов на оценки");
        Section(box, "Что это");
        Paragraph(box,
            "Проверка связи «прогулял → знания просели»: сравниваются оценки в ближайшие 3 дня после дня с прогулом и после дня полного посещения.");

        Section(box, "Методика");
        Paragraph(box,
            "Для каждого дня занятий берётся окно (день, +3 дня] и среднее оценок в нём; окна после прогулов и после полных дней сравниваются. " +
            "Это наблюдательная связь, не строгая причинность — но для практики достаточно.");

        var lag = Analytics.AbsenceLag(snap);
        if (lag is { } l)
        {
            Section(box, "Инфографика");
            var bars = new BarChartView(this, Argb(C(Resource.Color.inkSecondary)), Argb(C(Resource.Color.ink)));
            bars.Bars.Add(("после\nпрогулов", l.MeanAfter, Argb(C(Resource.Color.mark2)), l.MeanAfter.ToString("0.00")));
            bars.Bars.Add(("после\nполных дней", l.MeanClean, Argb(C(Resource.Color.mark5)), l.MeanClean.ToString("0.00")));
            bars.MaxValue = 5;
            AddChart(box, bars, 240);

            Section(box, "Выводы");
            int c = l.Diff < -0.15 ? Resource.Color.mark2 : Resource.Color.ink;
            box.AddView(Conclusion($"Разница: {(l.Diff >= 0 ? "+" : "")}{l.Diff:0.00} балла (n после прогулов: {l.NAfter}, контрольных дней: {l.NClean})", c));
            if (l.Diff < -0.15)
                box.AddView(Conclusion("Прогулы ощутимо бьют по текущим оценкам — аргумент не прогуливать", Resource.Color.mark2));
            else if (l.Diff > 0.15)
                box.AddView(Conclusion("После прогулов оценки даже выше — студент наверстывает; но материал накапливается", Resource.Color.mark4));
            else
                box.AddView(Conclusion("Заметного краткосрочного эффекта не найдено", Resource.Color.inkSecondary));
        }
        else
            box.AddView(Conclusion("Недостаточно данных: нужно ≥10 дней занятий и ≥5 оценок после прогулов", Resource.Color.inkSecondary));
    }

    static int Argb(Color c) => (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;
}
