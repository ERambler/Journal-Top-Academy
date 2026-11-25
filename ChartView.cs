using Android.Content;
using Android.Graphics;
using Android.Views;

namespace TopJournal;

/// <summary>Линейный график: серии линий, точки- scatter, диапазон-зона. Всё на Canvas.</summary>
public class LineChartView : View
{
    public class Series
    {
        public List<(double X, double Y)> Points = new();
        public int ColorArgb;
        public float StrokeWidth = 4f;
        public bool Dashed;
    }

    public List<Series> Lines { get; } = new();
    public List<(double X, double Y, int ColorArgb)> Dots { get; } = new();
    public (double YMin, double YMax) YRange { get; set; } = (1, 5);
    public string? XFirstLabel, XLastLabel;
    public string? YLabelFormat; // например "0.0"

    readonly Paint _line = new() { AntiAlias = true };
    readonly Paint _dot = new() { AntiAlias = true };
    readonly Paint _text = new() { AntiAlias = true, TextSize = 26 };
    readonly Paint _grid = new() { AntiAlias = true };
    readonly Paint _zone = new() { AntiAlias = true };

    int _dimArgb;

    public LineChartView(Context ctx, int dimArgb) : base(ctx)
    {
        _dimArgb = dimArgb;
        _grid.Color = new Color(dimArgb);
        _grid.StrokeWidth = 2f;
        _grid.SetStyle(Paint.Style.Stroke);
        _text.Color = new Color(dimArgb);
        _zone.SetStyle(Paint.Style.Fill);
    }

    protected override void OnMeasure(int widthSpec, int heightSpec)
    {
        int w = MeasureSpec.GetSize(widthSpec);
        int h = MeasureSpec.GetSize(heightSpec);
        if (w <= 0) w = 1000;
        if (h <= 0) h = 620;
        SetMeasuredDimension(w, h);
    }

    protected override void OnDraw(Canvas? c)
    {
        if (c is null) return;
        float padL = 90, padR = 60, padT = 40, padB = 70;
        float w = Width - padL - padR, h = Height - padT - padB;

        double xMin = double.MaxValue, xMax = double.MinValue;
        foreach (var s in Lines)
            foreach (var p in s.Points)
            {
                xMin = Math.Min(xMin, p.X);
                xMax = Math.Max(xMax, p.X);
            }
        foreach (var d in Dots)
        {
            xMin = Math.Min(xMin, d.X);
            xMax = Math.Max(xMax, d.X);
        }
        if (xMax - xMin < 1e-6) { xMin -= 1; xMax += 1; }

        float Sx(double x) => padL + w * (float)((x - xMin) / (xMax - xMin));
        float Sy(double y) => padT + h * (float)(1 - (y - YRange.YMin) / (YRange.YMax - YRange.YMin));

        // сетка + подписи Y
        _text.TextAlign = Paint.Align.Right;
        for (int i = 0; i <= 4; i++)
        {
            double y = YRange.YMin + (YRange.YMax - YRange.YMin) * i / 4;
            float yy = Sy(y);
            c.DrawLine(padL, yy, Width - padR, yy, _grid);
            c.DrawText(y.ToString(YLabelFormat ?? "0"), padL - 12, yy + 9, _text);
        }

        // подписи X
        _text.TextAlign = Paint.Align.Left;
        if (XFirstLabel is { } f) c.DrawText(f, padL, Height - 20, _text);
        _text.TextAlign = Paint.Align.Right;
        if (XLastLabel is { } l) c.DrawText(l, Width - padR, Height - 20, _text);

        // серии линий
        foreach (var s in Lines)
        {
            if (s.Points.Count < 2) continue;
            _line.Color = new Color(s.ColorArgb);
            _line.StrokeWidth = s.StrokeWidth;
            _line.SetPathEffect(s.Dashed
                ? new DashPathEffect(new float[] { 18, 14 }, 0)
                : null!);
            var path = new Android.Graphics.Path();
            path.MoveTo(Sx(s.Points[0].X), Sy(s.Points[0].Y));
            for (int i = 1; i < s.Points.Count; i++)
                path.LineTo(Sx(s.Points[i].X), Sy(s.Points[i].Y));
            c.DrawPath(path, _line);
        }

        // точки
        foreach (var d in Dots)
        {
            _dot.Color = new Color(d.ColorArgb);
            c.DrawCircle(Sx(d.X), Sy(d.Y), 9f, _dot);
        }
    }
}

/// <summary>Столбчатая диаграмма (вертикальные бары с подписями).</summary>
public class BarChartView : View
{
    public List<(string Label, double Value, int ColorArgb, string? Caption)> Bars { get; } = new();
    public double MaxValue { get; set; } = double.NaN; // auto если NaN
    public string ValueFormat { get; set; } = "0";

    readonly Paint _bar = new() { AntiAlias = true };
    readonly Paint _text = new() { AntiAlias = true, TextSize = 28 };
    readonly Paint _val = new() { AntiAlias = true, TextSize = 30 };
    readonly Paint _grid = new() { AntiAlias = true };

    public BarChartView(Context ctx, int dimArgb, int textArgb) : base(ctx)
    {
        _grid.Color = new Color(dimArgb);
        _grid.StrokeWidth = 2f;
        _text.Color = new Color(dimArgb);
        _val.Color = new Color(textArgb);
        _val.SetTypeface(Typeface.DefaultBold);
    }

    protected override void OnMeasure(int widthSpec, int heightSpec)
    {
        int w = MeasureSpec.GetSize(widthSpec);
        int h = MeasureSpec.GetSize(heightSpec);
        if (w <= 0) w = 1000;
        if (h <= 0) h = 620;
        SetMeasuredDimension(w, h);
    }

    protected override void OnDraw(Canvas? c)
    {
        if (c is null || Bars.Count == 0) return;
        float padT = 50, padB = 60;
        float w = Width, h = Height - padT - padB;
        float slot = w / Bars.Count;
        float barW = Math.Min(slot * 0.6f, 160);

        double max = double.IsNaN(MaxValue)
            ? Bars.Max(b => b.Value) * 1.15 + 1e-6
            : MaxValue;

        // базовая линия
        c.DrawLine(0, Height - padB, w, Height - padB, _grid);

        for (int i = 0; i < Bars.Count; i++)
        {
            var (label, value, color, caption) = Bars[i];
            float bh = (float)(h * value / max);
            float left = slot * i + (slot - barW) / 2;
            _bar.Color = new Color(color);
            c.DrawRoundRect(left, Height - padB - bh, left + barW, Height - padB, 10, 10, _bar);

            _text.TextAlign = Paint.Align.Center;
            c.DrawText(label, slot * i + slot / 2, Height - 18, _text);
            if (caption is { } cap)
            {
                _val.TextAlign = Paint.Align.Center;
                c.DrawText(cap, slot * i + slot / 2, Height - padB - bh - 14, _val);
            }
        }
    }
}
