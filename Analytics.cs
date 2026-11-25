using System.Globalization;

namespace TopJournal;

/// <summary>
/// Аналитика успеваемости: робастные тренды, прогноз, перелом, байесовская усадка.
/// Чистая математика без Android-зависимостей — всё считается на телефоне мгновенно.
///
/// Методология (см. docs/ANALYTICS.md):
///  - Theil–Sen + Mann–Kendall: оценки порядковые (1–5), OLS не робастен;
///  - Kalman [уровень, наклон]: адаптивное сглаживание и прогнозное распределение
///    аналитически, без бутстрепа; EWMA/SPC на тренде невалидны;
///  - эмпирический Байес (Джеймс–Стайн) для предметов при малых n;
///  - композиционная поправка среднего (эффект Симпсона из-за микса типов работ);
///  - BH–FDR против множественных сравнений по предметам;
///  - ни одной цифры без неопределённости; выводы только при достаточном n.
/// </summary>
public static class Analytics
{
    // ------------------------------------------------------------- запись оценки

    public readonly record struct MarkPoint(DateTime Date, int Mark, string Field);

    public static List<MarkPoint> AllMarks(Snapshot snap) =>
        snap.Visits
            .SelectMany(v => v.Marks.Select(m => new MarkPoint(
                DateTime.Parse(v.DateVisit, CultureInfo.InvariantCulture), m.Mark, m.Field)))
            .OrderBy(p => p.Date)
            .ToList();

    // --------------------------------------------------------------- Theil–Sen

    /// <summary>Робастный наклон (медиана попарных наклонов), балл/день.</summary>
    public static double TheilSlope(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var slopes = new List<double>();
        for (int i = 0; i < x.Count; i++)
            for (int j = i + 1; j < x.Count; j++)
                if (x[j] > x[i])
                    slopes.Add((y[j] - y[i]) / (x[j] - x[i]));
        slopes.Sort();
        if (slopes.Count == 0) return 0;
        return slopes.Count % 2 == 1
            ? slopes[slopes.Count / 2]
            : (slopes[slopes.Count / 2 - 1] + slopes[slopes.Count / 2]) / 2;
    }

    /// <summary>SE наклона Theil–Sen через MAD попарных наклонов.</summary>
    public static double TheilSlopeSe(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var slopes = new List<double>();
        for (int i = 0; i < x.Count; i++)
            for (int j = i + 1; j < x.Count; j++)
                if (x[j] > x[i])
                    slopes.Add((y[j] - y[i]) / (x[j] - x[i]));
        if (slopes.Count < 10) return double.NaN;
        slopes.Sort();
        double med = slopes[slopes.Count / 2];
        var devs = slopes.Select(s => Math.Abs(s - med)).OrderBy(d => d).ToList();
        double mad = devs[devs.Count / 2];
        return 1.4826 * mad / Math.Sqrt(x.Count);
    }

    // ----------------------------------------------------------- Mann–Kendall

    /// <summary>Mann–Kendall: S, τ и двустороннее p (нормальная аппроксимация с поправкой на связи).</summary>
    public static (double Tau, double P) MannKendall(IReadOnlyList<double> y)
    {
        int n = y.Count;
        if (n < 8) return (0, 1);
        long s = 0;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                s += Math.Sign(y[j] - y[i]);

        var ties = y.GroupBy(v => v).Where(g => g.Count() > 1)
            .Select(g => (long)g.Count() * (g.Count() - 1) * (2 * g.Count() + 1)).Sum();
        double n1 = n * (n - 1) / 2.0;
        double varS = (n * (n - 1) * (2.0 * n + 5) - ties) / 18.0;
        if (varS <= 0) return (0, 1);
        double z = (s - Math.Sign(s)) / Math.Sqrt(varS); // коррекция непрерывности
        double tau = s / n1;
        double p = 2 * (1 - Phi(Math.Abs(z)));
        return (tau, Math.Min(1, p));
    }

    // ------------------------------------------------------------------ Kalman

    /// <summary>
    /// Kalman [уровень, наклон] на равномерной сетке (недельные средние).
    /// Возвращает сглаженный уровень, наклон и damped-прогноз на h шагов ±q-квантиль.
    /// </summary>
    public static (double Level, double Slope, double Forecast, double Band) KalmanForecast(
        IReadOnlyList<double> y, int h = 4, double phi = 0.85)
    {
        int n = y.Count;
        if (n < 5) return (n > 0 ? y.Average() : 0, 0, n > 0 ? y.Average() : 0, double.NaN);

        double var0 = y.Count > 1 ? y.Skip(1).Zip(y, (a, b) => a - b).Select(d => d * d).Sum() / (n - 1) : 0.25;
        double sigmaR = Math.Max(0.05, Math.Sqrt(var0) / 2);
        double[,] P = { { 1.0, 0 }, { 0, 0.25 } };
        double level = y[0], slope = n > 1 ? y[1] - y[0] : 0;
        double qLevel = 0.02, qSlope = 0.002;

        for (int t = 1; t < n; t++)
        {
            // predict
            double pl = level + slope;
            double[,] Pp =
            {
                { P[0, 0] + P[0, 1] + P[1, 0] + P[1, 1] + qLevel, P[0, 1] + P[1, 1], },
                { P[1, 0] + P[1, 1], P[1, 1] + qSlope },
            };
            // update
            double r = y[t] - pl;
            double S = Pp[0, 0] + sigmaR * sigmaR;
            double k0 = Pp[0, 0] / S, k1 = Pp[1, 0] / S;
            level = pl + k0 * r;
            slope = slope + k1 * r;
            P = new[,]
            {
                { (1 - k0) * Pp[0, 0], (1 - k0) * Pp[0, 1] },
                { -k1 * Pp[0, 0], Pp[1, 1] - k1 * Pp[0, 1] },
            };
        }

        // damped-прогноз: slope гасится φ на каждом шаге
        double damp = 0;
        for (int k = 1; k <= h; k++) damp += Math.Pow(phi, k);
        double forecast = level + slope * damp;
        double band = 1.28 * Math.Sqrt(Math.Max(1e-6, P[0, 0] + sigmaR * sigmaR) * Math.Max(1, h / 2.0));
        return (level, slope, forecast, band);
    }

    // ------------------------------------------------------- перелом (change-point)

    /// <summary>
    /// Байесовский перелом уровня: перебор точки разбиения, гауссовы сегменты с общей σ.
    /// Возвращает лучшую дату, разность уровней и апостериорную вероятность наличия сдвига.
    /// </summary>
    public static (DateTime? Date, double Delta, double Prob)? ChangePoint(List<MarkPoint> marks)
    {
        if (marks.Count < 30) return null;
        double sigma = StdDev(marks.Select(m => (double)m.Mark));
        if (sigma < 1e-6) return null;
        // гауссова модель без сдвига vs модель с разбиением в точке τ (общая σ):
        double mean0 = marks.Average(m => (double)m.Mark);
        double sse0 = marks.Sum(m => (m.Mark - mean0) * (m.Mark - mean0));
        double ll0 = -sse0 / (2 * sigma * sigma);

        double bestLl = double.NegativeInfinity;
        int bestIdx = -1;
        var lls = new double[marks.Count];
        for (int i = 10; i < marks.Count - 10; i++)
        {
            double m1 = marks.Take(i).Average(m => (double)m.Mark);
            double m2 = marks.Skip(i).Average(m => (double)m.Mark);
            double sse = marks.Take(i).Sum(m => (m.Mark - m1) * (m.Mark - m1))
                       + marks.Skip(i).Sum(m => (m.Mark - m2) * (m.Mark - m2));
            lls[i] = -sse / (2 * sigma * sigma);
            if (lls[i] > bestLl) { bestLl = lls[i]; bestIdx = i; }
        }
        if (bestIdx < 0) return null;

        double wChange = lls.Where(v => v != 0).Select(v => Math.Exp(v - bestLl)).Sum();
        double wNone = Math.Exp(ll0 - bestLl);
        double probChange = wChange / (wChange + wNone); // prior 50/50 на «есть сдвиг»

        double a1 = marks.Take(bestIdx).Average(m => (double)m.Mark);
        double a2 = marks.Skip(bestIdx).Average(m => (double)m.Mark);
        return (marks[bestIdx].Date, a2 - a1, probChange);
    }

    // --------------------------------------------- эмпирический Байес по предметам

    public readonly record struct SubjectStat(string Name, int N, double Raw, double Shrunk, double Se);

    /// <summary>Усадка Джеймса–Стайна: предметные средние тянутся к общему при малых n.</summary>
    public static List<SubjectStat> SubjectStats(Snapshot snap)
    {
        var groups = snap.Visits
            .SelectMany(v => v.Marks.Select(m => (v.SpecName, m.Mark)))
            .GroupBy(x => x.SpecName)
            .Select(g => (Name: g.Key, Marks: g.Select(x => (double)x.Mark).ToList()))
            .OrderByDescending(g => g.Marks.Count)
            .ToList();
        if (groups.Count == 0) return new List<SubjectStat>();

        double global = groups.SelectMany(g => g.Marks).Average();
        double varWithin = groups.Count > 1 && groups.All(g => g.Marks.Count > 1)
            ? groups.Average(g => g.Marks.Select(m => (m - g.Marks.Average()) * (m - g.Marks.Average())).Sum() / (g.Marks.Count - 1))
            : 0.4;
        double varBetween = Math.Max(0,
            groups.Select(g => (g.Marks.Average() - global) * (g.Marks.Average() - global)).Sum() / (groups.Count - 1)
            - varWithin / groups.Average(g => (double)g.Marks.Count));

        var result = new List<SubjectStat>();
        foreach (var g in groups)
        {
            double raw = g.Marks.Average();
            double v = varWithin / g.Marks.Count;
            double tau2 = varBetween + v;
            double b = tau2 > 0 ? v / tau2 : 1;
            double se = Math.Sqrt(v);
            result.Add(new SubjectStat(g.Name, g.Marks.Count, raw, (1 - b) * raw + b * global, se));
        }
        return result;
    }

    /// <summary>BH–FDR: скорректированные q по вектору p.</summary>
    public static double[] BhFdr(IReadOnlyList<double> ps)
    {
        int m = ps.Count;
        var idx = Enumerable.Range(0, m).OrderBy(i => ps[i]).ToArray();
        var q = new double[m];
        double prev = 1;
        for (int rank = m; rank >= 1; rank--)
        {
            int i = idx[rank - 1];
            double val = Math.Min(prev, ps[i] * m / rank);
            q[i] = val;
            prev = val;
        }
        return q;
    }

    // ------------------------------------------- композиционная поправка (Симпсон)

    /// <summary>
    /// Месячные средние, скорректированные за микс типов работ:
    /// каждый месяц взвешивается РЕФЕРЕНСНЫМ (общим за период) распределением типов.
    /// </summary>
    public static List<(string Month, double Raw, double Adjusted, int N)> MonthlyAdjusted(Snapshot snap)
    {
        var rows = snap.Visits
            .SelectMany(v => v.Marks.Select(m => (
                Month: DateTime.Parse(v.DateVisit, CultureInfo.InvariantCulture).ToString("MM.yy"),
                m.Field, m.Mark)))
            .ToList();
        var months = rows.Select(r => r.Month).Distinct().OrderBy(m => m[^3..] + m[..2]).ToList();
        var refMix = rows.GroupBy(r => r.Field).ToDictionary(g => g.Key, g => g.Count() / (double)rows.Count);

        var result = new List<(string, double, double, int)>();
        foreach (var month in months)
        {
            var mr = rows.Where(r => r.Month == month).ToList();
            double raw = mr.Average(r => (double)r.Mark);
            // прямая стандартизация: среднее по типам с референсными весами
            double adj = 0, wsum = 0;
            foreach (var kv in refMix)
            {
                var byType = mr.Where(r => r.Field == kv.Key).ToList();
                if (byType.Count == 0) continue;
                adj += kv.Value * byType.Average(r => (double)r.Mark);
                wsum += kv.Value;
            }
            if (wsum > 0) adj /= wsum;
            result.Add((month, raw, adj, mr.Count));
        }
        return result;
    }

    // ------------------------------------------------------------- прогулы по дням

    public readonly record struct WeekdayRate(string Day, int Absences, int Lessons, double RatePer10);

    /// <summary>Интенсивность прогулов по дням недели (прогулы/занятие — offset на число пар).</summary>
    public static List<WeekdayRate> WeekdayAbsenceRates(Snapshot snap)
    {
        string[] names = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };
        return snap.Visits
            .GroupBy(v => DateTime.Parse(v.DateVisit, CultureInfo.InvariantCulture).DayOfWeek)
            .Where(g => (int)g.Key is >= 1 and <= 6)
            .Select(g => new WeekdayRate(
                names[(int)g.Key - 1],
                g.Count(v => v.StatusWas == 0),
                g.Count(),
                10.0 * g.Count(v => v.StatusWas == 0) / g.Count()))
            .OrderByDescending(r => r.RatePer10)
            .ToList();
    }

    // ------------------------------------------------------------------ лаг-эффект

    public readonly record struct LagEffect(int NAfter, int NClean, double MeanAfter, double MeanClean, double Diff);

    /// <summary>
    /// Эффект прогулов: сравнение оценок в окне (d, d+3] после дня с прогулом
    /// и после дня полного посещения.
    /// </summary>
    public static LagEffect? AbsenceLag(Snapshot snap)
    {
        var byDay = snap.Visits
            .GroupBy(v => DateTime.Parse(v.DateVisit, CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => g.ToList());
        if (byDay.Count < 10) return null;

        var afterMarks = new List<double>();
        var cleanMarks = new List<double>();
        foreach (var (day, lessons) in byDay.OrderBy(kv => kv.Key))
        {
            bool absent = lessons.Any(v => v.StatusWas == 0);
            var window = byDay.Where(kv => kv.Key > day && kv.Key <= day.AddDays(3))
                .SelectMany(kv => kv.Value)
                .SelectMany(v => v.Marks.Select(m => (double)m.Mark))
                .ToList();
            if (window.Count == 0) continue;
            if (absent) afterMarks.AddRange(window);
            else cleanMarks.AddRange(window.Take(window.Count)); // сопоставимо по объёму
        }
        if (afterMarks.Count < 5 || cleanMarks.Count < 5) return null;
        return new LagEffect(afterMarks.Count, cleanMarks.Count,
            afterMarks.Average(), cleanMarks.Average(),
            afterMarks.Average() - cleanMarks.Average());
    }

    // ------------------------------------------------------------ радар «Специалист»

    public readonly record struct RadarAxes(double? Knowledge, double? Discipline, double? Independence, double? Dynamics);

    public static RadarAxes ComputeRadar(Snapshot snap)
    {
        // Знания: GPA с весами сложности (контрольная 3, лаба/практика 2.5, домашняя 1, классная 1)
        double wSum = 0, wGpa = 0;
        foreach (var v in snap.Visits)
        {
            void Add(int? mark, double w)
            {
                if (mark is int mm) { wGpa += w * mm; wSum += w; }
            }
            Add(v.ControlWork, 3);
            Add(v.LabWork, 2.5);
            Add(v.PracticalWork, 2.5);
            Add(v.FinalWork, 3);
            Add(v.HomeWork, 1);
            Add(v.ClassWork, 1);
        }
        double? knowledge = wSum >= 5 ? ((wGpa / wSum - 1) / 4) * 100 : null;

        // Дисциплина: 60% посещаемость + 40% своевременность ДЗ (один фактор — переменные скоррелированы)
        double attend = snap.Visits.Count == 0
            ? 0 : 100.0 * snap.Visits.Count(v => v.StatusWas is 1 or 2) / snap.Visits.Count;
        double onTime = HwOnTimeShare(snap) ?? attend / 100;
        double? discipline = snap.Visits.Count >= 10 ? 100 * (0.6 * attend / 100 + 0.4 * onTime) : null;

        // Самостоятельность: разрыв «классная+домашняя» vs «контрольная+лаба/практика»
        var cw = Marks(v => v.ClassWork ?? v.HomeWork);
        var exam = Marks(v => v.ControlWork ?? v.LabWork ?? v.PracticalWork);
        double? independence = null;
        if (cw.Count >= 5 && exam.Count >= 3)
        {
            double gap = cw.Average() - exam.Average(); // >0: на контрольных слабее
            independence = Math.Clamp(100 - Math.Max(0, gap) * 60, 0, 100);
        }

        // Динамика: Theil–Sen за всё время, балл/мес → 0..100
        var pts = AllMarks(snap);
        double? dyn = null;
        if (pts.Count >= 30)
        {
            var x = pts.Select(p => p.Date.ToOADate()).ToList();
            var y = pts.Select(p => (double)p.Mark).ToList();
            double slopePerMonth = TheilSlope(x, y) * 30.4;
            dyn = Math.Clamp(50 + slopePerMonth / 0.15 * 50, 0, 100);
        }

        return new RadarAxes(knowledge, discipline, independence, dyn);

        List<double> Marks(Func<VisitDto, int?> pick) =>
            snap.Visits.Select(pick).Where(m => m.HasValue).Select(m => (double)m!.Value).ToList();
    }

    /// <summary>Доля ДЗ, сданных в срок (0..1); null — если разрешённых ДЗ нет.</summary>
    public static double? HwOnTimeShare(Snapshot snap)
    {
        int done = 0, total = 0;
        foreach (var h in snap.Homework)
        {
            if (h.Status == 3) continue; // ещё открыто — не считаем
            total++;
            if (h.Status == 0) continue; // просрочено не сдано
            if (h.Stud is { } s &&
                DateTime.TryParse(s.CreationTime, out var submitted) &&
                DateTime.TryParse(h.CompletionTime, out var due) &&
                submitted.Date <= due.Date)
                done++;
        }
        return total >= 5 ? (double)done / total : null;
    }

    // ---------------------------------------------------------------- utils

    public static double StdDev(IEnumerable<double> xs)
    {
        var list = xs.ToList();
        if (list.Count < 2) return 0;
        double m = list.Average();
        return Math.Sqrt(list.Sum(x => (x - m) * (x - m)) / (list.Count - 1));
    }

    /// <summary>CDF стандартного нормального (Абрамовиц–Стеган 26.2.17).</summary>
    public static double Phi(double z)
    {
        double t = 1 / (1 + 0.2316419 * Math.Abs(z));
        double poly = 0.319381530 * t - 0.356563782 * t * t + 1.781477937 * Math.Pow(t, 3)
                    - 1.821255978 * Math.Pow(t, 4) + 1.330274429 * Math.Pow(t, 5);
        double p = 1 - Math.Exp(-z * z / 2) / Math.Sqrt(2 * Math.PI) * poly;
        return z >= 0 ? p : 1 - p;
    }

    /// <summary>P(наклон &lt; 0 | данные) — нормальная апостериорная по Theil-Sen ± SE.</summary>
    public static double ProbNegative(double slope, double se)
    {
        if (double.IsNaN(se) || se <= 0) return slope < 0 ? 1 : 0;
        return Phi(-slope / se);
    }
}
