using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace StromForbrug.Windows;

/// <summary>One bar: its time, one value per series (stacked bottom-up) and whether to draw it pale.</summary>
public sealed record ChartBar(DateTime Start, double[] Values, bool Muted = false)
{
    public double Sum => Values.Where(v => v > 0).Sum();
}

/// <summary>
/// A stacked bar chart over time that fills its client area: hourly bars with a line at each
/// midnight, or daily bars with a line before each Monday. Gridlines fall on round numbers and
/// hovering a bar shows the text from <see cref="TooltipText"/>.
/// </summary>
public sealed class BarChartControl : Control
{
    private static readonly CultureInfo Da = CultureInfo.GetCultureInfo("da-DK");

    private readonly ToolTip _tip = new() { InitialDelay = 0, ReshowDelay = 0, AutoPopDelay = 30000, UseAnimation = false, UseFading = false };
    private IReadOnlyList<ChartBar> _bars = [];
    private int _hover = -1;
    private Rectangle _plot;

    public BarChartControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 8.5F);
    }

    /// <summary>Series names and colours, bottom of the stack first.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public (string Label, Color Color)[] Series { get; set; } = [("Værdi", Color.SeaGreen)];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Daily { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Unit { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string ValueFormat { get; set; } = "0.00";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? MutedLabel { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set; } = "Ingen data.";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<int, string>? TooltipText { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<ChartBar> Bars
    {
        get => _bars;
        set
        {
            _bars = value;
            _hover = -1;
            _tip.Hide(this);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var inkBrush = new SolidBrush(Color.FromArgb(82, 81, 78));
        using var mutedBrush = new SolidBrush(Color.FromArgb(137, 135, 129));
        using var gridPen = new Pen(Color.FromArgb(225, 224, 217));
        using var axisPen = new Pen(Color.FromArgb(195, 194, 183));

        DrawLegend(g, inkBrush, mutedBrush);

        _plot = new Rectangle(52, 34, Math.Max(10, Width - 52 - 14), Math.Max(10, Height - 34 - 44));

        if (_bars.Count == 0)
        {
            TextRenderer.DrawText(g, EmptyText, Font, _plot, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        // Scale: positive parts stack up from zero, negative ones (a negative spot price) down.
        var high = _bars.Max(b => b.Values.Where(v => v > 0).Sum());
        var low = _bars.Min(b => b.Values.Where(v => v < 0).Sum());
        var step = NiceStep(Math.Max(high - low, 1e-6) / 5);
        var yMax = Math.Max(step, Math.Ceiling(high * 1.04 / step) * step);
        var yMin = Math.Floor(low / step) * step;
        // As many decimals as the step needs (2.5 → "0.#"), never rounding a tick.
        static bool IsWhole(double x) => Math.Abs(x - Math.Round(x)) < 1e-9;
        var tickFormat = IsWhole(step) ? "0" : IsWhole(step * 10) ? "0.#" : "0.##";
        float Y(double v) => (float)(_plot.Top + (yMax - v) / (yMax - yMin) * _plot.Height);

        for (var v = yMin; v <= yMax + step / 1000; v += step)
        {
            var y = Y(v);
            g.DrawLine(Math.Abs(v) < 1e-9 ? axisPen : gridPen, _plot.Left, y, _plot.Right, y);
            var text = v.ToString(tickFormat, Da);
            var size = g.MeasureString(text, Font);
            g.DrawString(text, Font, mutedBrush, _plot.Left - 6 - size.Width, y - size.Height / 2);
        }

        var slot = (float)_plot.Width / _bars.Count;
        var gap = Daily ? slot * 0.25f : Math.Min(2f, slot * 0.15f);

        // Separators: midnight (hourly) or Monday (daily).
        for (var i = 1; i < _bars.Count; i++)
        {
            var newDay = Daily ? _bars[i].Start.DayOfWeek == DayOfWeek.Monday : _bars[i].Start.Date != _bars[i - 1].Start.Date;
            if (newDay)
                g.DrawLine(axisPen, _plot.Left + i * slot, _plot.Top, _plot.Left + i * slot, _plot.Bottom);
        }

        if (_hover >= 0 && _hover < _bars.Count)
        {
            using var hoverBrush = new SolidBrush(Color.FromArgb(18, 0, 0, 0));
            g.FillRectangle(hoverBrush, _plot.Left + _hover * slot, _plot.Top, slot, _plot.Height);
        }

        for (var i = 0; i < _bars.Count; i++)
        {
            var bar = _bars[i];
            var x = _plot.Left + i * slot + gap / 2;
            var w = Math.Max(1f, slot - gap);
            double up = 0, down = 0;
            for (var p = 0; p < bar.Values.Length && p < Series.Length; p++)
            {
                var value = bar.Values[p];
                if (value == 0)
                    continue;
                var color = Series[p].Color;
                if (bar.Muted && i != _hover)
                    color = Color.FromArgb(110, color);
                using var brush = new SolidBrush(color);
                if (value > 0)
                {
                    g.FillRectangle(brush, x, Y(up + value), w, Y(up) - Y(up + value));
                    up += value;
                }
                else
                {
                    g.FillRectangle(brush, x, Y(down), w, Y(down + value) - Y(down));
                    down += value;
                }
            }
        }

        // ▲ on the highest bar.
        if (_bars.Count > 1)
        {
            var peak = Enumerable.Range(0, _bars.Count).MaxBy(i => _bars[i].Sum);
            var text = "▲ " + _bars[peak].Sum.ToString(ValueFormat, Da);
            var size = g.MeasureString(text, Font);
            var cx = _plot.Left + (peak + 0.5f) * slot;
            g.DrawString(text, Font, inkBrush, Math.Clamp(cx - size.Width / 2, _plot.Left, _plot.Right - size.Width), Y(_bars[peak].Sum) - size.Height - 1);
        }

        g.DrawLine(axisPen, _plot.Left, _plot.Bottom, _plot.Right, _plot.Bottom);
        DrawXAxis(g, slot, inkBrush, mutedBrush);
    }

    private void DrawLegend(Graphics g, Brush ink, Brush muted)
    {
        float x = 8;
        if (Series.Length > 1 || MutedLabel is not null)
        {
            var items = Series.Length > 1 ? Series.Select(s => (s.Label, s.Color)).ToList() : [];
            if (MutedLabel is not null && _bars.Any(b => b.Muted))
                items.Add((MutedLabel, Color.FromArgb(110, Series[0].Color)));
            foreach (var (label, color) in items)
            {
                using var b = new SolidBrush(color);
                g.FillRectangle(b, x, 10, 10, 10);
                g.DrawString(label, Font, ink, x + 13, 7);
                x += 13 + g.MeasureString(label, Font).Width + 12;
            }
        }
        var unitSize = g.MeasureString(Unit, Font);
        g.DrawString(Unit, Font, muted, Width - 14 - unitSize.Width, 7);
    }

    private void DrawXAxis(Graphics g, float slot, Brush ink, Brush muted)
    {
        using var bold = new Font(Font, FontStyle.Bold);
        var top = _plot.Bottom + 3;

        if (Daily)
        {
            // Every day when there is room, otherwise Mondays only; the month at its first bar.
            var every = slot >= 18;
            for (var i = 0; i < _bars.Count; i++)
            {
                var d = _bars[i].Start;
                if (every || d.DayOfWeek == DayOfWeek.Monday)
                    Centered(d.Day.ToString(Da), i, top, muted);
                if (i == 0 || d.Day == 1)
                    g.DrawString(d.ToString("MMMM", Da), bold, ink, _plot.Left + i * slot, top + 17);
            }
            return;
        }

        // Hour labels at a spacing that fits: every 1, 2, 3, 6 or 12 hours.
        var stepHours = new[] { 1, 2, 3, 6, 12, 24 }.First(h => h * slot >= 22 || h == 24);
        float lastDateEnd = float.MinValue;
        for (var i = 0; i < _bars.Count; i++)
        {
            var d = _bars[i].Start;
            if (d.Hour % stepHours == 0 && stepHours < 24)
                Centered(d.ToString("HH", Da), i, top, muted);
            if (i == 0 || d.Date != _bars[i - 1].Start.Date)
            {
                var label = d.ToString(slot * 24 >= 80 ? "ddd d. MMM" : "d/M", Da);
                var x = _plot.Left + i * slot + 2;
                if (x > lastDateEnd + 4)
                {
                    g.DrawString(label, bold, ink, x, top + 17);
                    lastDateEnd = x + g.MeasureString(label, bold).Width;
                }
            }
        }

        void Centered(string text, int i, float y, Brush brush)
        {
            var size = g.MeasureString(text, Font);
            g.DrawString(text, Font, brush, _plot.Left + (i + 0.5f) * slot - size.Width / 2, y);
        }
    }

    private static double NiceStep(double raw)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var n = raw / magnitude;
        return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10) * magnitude;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = -1;
        if (_bars.Count > 0 && _plot.Contains(e.Location))
            index = Math.Clamp((int)((e.X - _plot.Left) / ((float)_plot.Width / _bars.Count)), 0, _bars.Count - 1);

        if (index == _hover)
            return;
        _hover = index;
        Invalidate();

        if (index < 0 || TooltipText is null)
            _tip.Hide(this);
        else
            _tip.Show(TooltipText(index), this, e.X + 16, e.Y + 12);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        _tip.Hide(this);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _tip.Dispose();
        base.Dispose(disposing);
    }
}
