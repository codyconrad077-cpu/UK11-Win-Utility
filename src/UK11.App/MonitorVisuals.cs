using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace UK11.App;

public sealed class UsageGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(UsageGauge), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    protected override void OnRender(DrawingContext drawing)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2); var radius = Math.Max(1, Math.Min(ActualWidth, ActualHeight) / 2 - 6);
        drawing.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(48, 53, 63)), 3), center, radius, radius);
        if (double.IsNaN(Value) || Value <= 0) return;
        var angle = Math.Clamp(Value, 0, 99.99) / 100 * Math.PI * 2;
        var start = new Point(center.X, center.Y - radius); var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var geometry = new StreamGeometry(); using (var c = geometry.Open()) { c.BeginFigure(start, false, false); c.ArcTo(end, new(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true, false); }
        drawing.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(38, 139, 255)), 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
    }
}
public sealed class TemperatureChart : FrameworkElement
{
    public IReadOnlyList<double?> Values { get; set; } = [];
    public void Update(IReadOnlyList<double?> values) { Values = values; InvalidateVisual(); }
    protected override void OnRender(DrawingContext d)
    {
        var left = 42d; var top = 12d; var width = Math.Max(1, ActualWidth - 52); var height = Math.Max(1, ActualHeight - 42);
        var ink = new SolidColorBrush(Color.FromRgb(107, 116, 131));
        void Text(string text, double x, double y) => d.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, y));
        for (var v = 0; v <= 100; v += 20) { var y = top + height * (1 - v / 110d); d.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(39, 44, 52)), 1) { DashStyle = DashStyles.Dot }, new(left, y), new(left + width, y)); Text(v + "°C", 2, y - 7); }
        Text("2 min", left, top + height + 10); Text("now", left + width - 22, top + height + 10);
        var groups = new List<List<Point>>(); var points = new List<Point>();
        for (var i = 0; i < Values.Count; i++)
        {
            if (Values[i] is not double v) { if (points.Count > 0) groups.Add(points); points = []; continue; }
            points.Add(new(left + (60 - Values.Count + i) * width / 59, top + height * (1 - Math.Clamp(v, 0, 110) / 110)));
        }
        if (points.Count > 0) groups.Add(points);
        foreach (var group in groups.Where(g => g.Count > 1))
        {
            var line = new StreamGeometry(); using (var c = line.Open()) { c.BeginFigure(group[0], false, false); c.PolyLineTo(group.Skip(1).ToArray(), true, false); }
            var fill = new StreamGeometry(); using (var c = fill.Open()) { c.BeginFigure(new(group[0].X, top + height), true, true); c.PolyLineTo(group.ToArray(), true, false); c.LineTo(new(group[^1].X, top + height), true, false); }
            d.DrawGeometry(new LinearGradientBrush(Color.FromArgb(90, 28, 123, 255), Color.FromArgb(0, 28, 123, 255), 90), null, fill);
            d.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(48, 148, 255)), 2), line);
        }
    }
}
