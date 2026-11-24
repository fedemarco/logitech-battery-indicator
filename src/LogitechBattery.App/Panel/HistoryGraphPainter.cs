using System.Drawing.Drawing2D;
using LogitechBattery.History;

namespace LogitechBattery.Panel;

/// <summary>Draws the battery graph (axes, grid, line) of the panel for one paint; sizes are logical pixels scaled by the DPI given.</summary>
internal sealed class HistoryGraphPainter
{
    private const int PercentLabelWidth = 32;
    private const int PercentLabelHeight = 16;
    private const int PercentLabelGap = 4;

    private const int TickLength = 4;
    private const int TickLabelTop = 6;
    private const int TickLabelWidth = 40;
    private const int TickLabelHeight = 16;

    private const float SolidLineWidth = 2;
    private const float DashedLineWidth = 1.5f;
    private const float SinglePointRadius = 3;

    private const int GridLineAlpha = 60; // faint, so the grid stays behind the data
    private const float GridLineWidth = 1;

    private static readonly Color GridColor = Color.FromArgb(GridLineAlpha, OverlayForm.SecondaryText);
    private static readonly Color LineColor = ColorTranslator.FromHtml("#4CC2FF");

    private static readonly int[] PercentGridLines = [0, 50, 100];

    private readonly float _dpiScale;
    private readonly bool _isSimulator; // the simulator has no history files
    private readonly HistoryChart _chart;
    private readonly TimeSpan _window;

    public HistoryGraphPainter(float dpiScale, bool isSimulator, HistoryChart chart, TimeSpan window)
    {
        _dpiScale = dpiScale;
        _isSimulator = isSimulator;
        _chart = chart;
        _window = window;
    }

    public void Draw(Graphics graphics, RectangleF plot, Font axisFont, DateTimeOffset now)
    {
        DrawAxes(graphics, plot, axisFont, now);

        if (EmptyMessage() is { } message)
        {
            OverlayForm.DrawText(graphics, message, axisFont, OverlayForm.SecondaryText, plot, StringAlignment.Center, StringAlignment.Center);
            return;
        }

        graphics.SetClip(plot);
        DrawBatteryLine(graphics, plot, now);
        if (_chart.Points.Count == 1)
            DrawSinglePoint(graphics, ToPixel(_chart.Points[0], plot, now));
        graphics.ResetClip();
    }

    private string? EmptyMessage()
    {
        if (_isSimulator)
            return "No history in simulator mode";
        return _chart.Points.Count == 0 ? "No history yet" : null;
    }

    private void DrawBatteryLine(Graphics graphics, RectangleF plot, DateTimeOffset now)
    {
        // Segments are drawn one by one, so round caps (not a line join) hide the notches at the bends.
        using var solidPen = new Pen(LineColor, SolidLineWidth * _dpiScale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var dashedPen = new Pen(LineColor, DashedLineWidth * _dpiScale) { DashStyle = DashStyle.Dash };
        foreach (var join in _chart.Joins)
            graphics.DrawLine(join.IsSolid ? solidPen : dashedPen, ToPixel(join.From, plot, now), ToPixel(join.To, plot, now));
    }

    private void DrawSinglePoint(Graphics graphics, PointF center)
    {
        var radius = SinglePointRadius * _dpiScale;
        using var brush = new SolidBrush(LineColor);
        graphics.FillEllipse(brush, center.X - radius, center.Y - radius, 2 * radius, 2 * radius);
    }

    private PointF ToPixel(ChartPoint point, RectangleF plot, DateTimeOffset now) => new(
        TimeToX(point.Time, plot, now),
        PercentToY(point.Percent, plot));

    private float TimeToX(DateTimeOffset time, RectangleF plot, DateTimeOffset now) =>
        plot.Left + (float)HistoryChart.WindowFraction(time, now, _window) * plot.Width;

    private static float PercentToY(int percent, RectangleF plot) =>
        plot.Bottom - (float)HistoryChart.HeightFraction(percent) * plot.Height;

    private void DrawAxes(Graphics graphics, RectangleF plot, Font font, DateTimeOffset now)
    {
        using var gridPen = new Pen(GridColor, GridLineWidth);
        DrawPercentGrid(graphics, plot, font, gridPen);
        DrawTimeTicks(graphics, plot, font, gridPen, now);
    }

    private void DrawPercentGrid(Graphics graphics, RectangleF plot, Font font, Pen gridPen)
    {
        foreach (var percent in PercentGridLines)
        {
            var gridLineY = PercentToY(percent, plot);
            graphics.DrawLine(gridPen, plot.Left, gridLineY, plot.Right, gridLineY);

            var label = BoxAt(
                new PointF(plot.Left, gridLineY),
                offsetX: -(PercentLabelWidth + PercentLabelGap),
                offsetY: -PercentLabelHeight / 2f,
                PercentLabelWidth,
                PercentLabelHeight);
            OverlayForm.DrawText(graphics, percent.ToString(), font, OverlayForm.SecondaryText, label, StringAlignment.Far, StringAlignment.Center);
        }
    }

    private void DrawTimeTicks(Graphics graphics, RectangleF plot, Font font, Pen gridPen, DateTimeOffset now)
    {
        var ticks = _window == HistoryChart.Day
            ? HistoryChart.HourTicks(now, TimeZoneInfo.Local)
            : HistoryChart.DayTicks(now, TimeZoneInfo.Local);
        foreach (var tick in ticks)
        {
            var tickX = TimeToX(tick.Time, plot, now);
            graphics.DrawLine(gridPen, tickX, plot.Bottom, tickX, plot.Bottom + TickLength * _dpiScale);

            var label = BoxAt(
                new PointF(tickX, plot.Bottom),
                offsetX: -TickLabelWidth / 2f,
                offsetY: TickLabelTop,
                TickLabelWidth,
                TickLabelHeight);
            OverlayForm.DrawText(graphics, tick.Label, font, OverlayForm.SecondaryText, label, StringAlignment.Center);
        }
    }

    /// <summary>A box of logical size whose corner lies at a pixel position moved by logical offsets.</summary>
    private RectangleF BoxAt(PointF origin, float offsetX, float offsetY, float width, float height) =>
        new(origin.X + offsetX * _dpiScale, origin.Y + offsetY * _dpiScale, width * _dpiScale, height * _dpiScale);
}
