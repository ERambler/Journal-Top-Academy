using Android.Content;
using Android.Graphics;
using Android.Views;

namespace TopJournal;

/// <summary>Радар «Специалист»: 4 оси 0..100, рисуется на Canvas без библиотек.</summary>
public class RadarView : View
{
    readonly string[] _labels;
    readonly double?[] _values;
    readonly int _accentArgb;
    readonly int _dimArgb;

    readonly Paint _grid = new() { AntiAlias = true };
    readonly Paint _axis = new() { AntiAlias = true };
    readonly Paint _fill = new() { AntiAlias = true };
    readonly Paint _stroke = new() { AntiAlias = true };
    readonly Paint _label = new() { AntiAlias = true, TextSize = 30 };
    readonly Paint _value = new() { AntiAlias = true, TextSize = 30 };

    public RadarView(Context ctx, string[] labels, double?[] values, int accentArgb, int dimArgb)
        : base(ctx)
    {
        _labels = labels;
        _values = values;
        _accentArgb = accentArgb;
        _dimArgb = dimArgb;

        _grid.SetStyle(Paint.Style.Stroke);
        _grid.Color = new Color(dimArgb);
        _grid.StrokeWidth = 2f;
        _axis.Color = new Color(dimArgb);
        _axis.StrokeWidth = 2f;
        _fill.Color = new Color(accentArgb & 0x00FFFFFF | 0x33000000); // 20% акцент
        _stroke.Color = new Color(accentArgb);
        _stroke.StrokeWidth = 5f;
        _stroke.SetStyle(Paint.Style.Stroke);
        _label.Color = new Color(dimArgb);
        _value.Color = new Color(accentArgb);
        _value.SetTypeface(Typeface.DefaultBold);
    }

    protected override void OnMeasure(int widthSpec, int heightSpec)
    {
        int side = Math.Min(MeasureSpec.GetSize(widthSpec), MeasureSpec.GetSize(heightSpec));
        if (side <= 0) side = 900;
        SetMeasuredDimension(side, side);
    }

    protected override void OnDraw(Canvas? canvas)
    {
        if (canvas is null) return;
        float cx = Width / 2f, cy = Height / 2f;
        float r = Math.Min(cx, cy) * 0.72f;
        int n = _labels.Length;

        // сетка: кольца 25/50/75/100
        for (int ring = 1; ring <= 4; ring++)
        {
            var path = new Android.Graphics.Path();
            for (int i = 0; i <= n; i++)
            {
                double a = -Math.PI / 2 + 2 * Math.PI * (i % n) / n;
                float x = cx + r * ring / 4 * (float)Math.Cos(a);
                float y = cy + r * ring / 4 * (float)Math.Sin(a);
                if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
            }
            canvas.DrawPath(path, _grid);
        }

        // оси + подписи
        var pts = new float[n][];
        for (int i = 0; i < n; i++)
        {
            double a = -Math.PI / 2 + 2 * Math.PI * i / n;
            float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a);
            canvas.DrawLine(cx, cy, cx + r * dx, cy + r * dy, _axis);

            float lx = cx + (r + 70) * dx, ly = cy + (r + 70) * dy;
            _label.TextAlign = Math.Abs(dx) < 0.3f ? Paint.Align.Center : dx > 0 ? Paint.Align.Left : Paint.Align.Right;
            canvas.DrawText(_labels[i], lx, ly + 10, _label);

            double v = _values[i] ?? 0;
            pts[i] = new[] { cx + r * (float)(v / 100) * dx, cy + r * (float)(v / 100) * dy };
        }

        // полигон значений
        var poly = new Android.Graphics.Path();
        for (int i = 0; i < n; i++)
        {
            if (i == 0) poly.MoveTo(pts[i][0], pts[i][1]);
            else poly.LineTo(pts[i][0], pts[i][1]);
        }
        poly.Close();
        canvas.DrawPath(poly, _fill);
        canvas.DrawPath(poly, _stroke);

        // точки и числа
        _value.TextAlign = Paint.Align.Center;
        for (int i = 0; i < n; i++)
        {
            canvas.DrawCircle(pts[i][0], pts[i][1], 8f, _stroke);
            string val = _values[i] is double vv ? vv.ToString("0") : "—";
            float tx = cx + (pts[i][0] - cx) * 1.25f, ty = cy + (pts[i][1] - cy) * 1.25f;
            canvas.DrawText(val, tx, ty, _value);
        }
    }
}
