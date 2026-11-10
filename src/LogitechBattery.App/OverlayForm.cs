using System.Drawing.Drawing2D;
using System.Drawing.Text;
using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>The panel that opens on a left-click of the tray icon: status, last charge, estimate and the battery graph.</summary>
internal sealed class OverlayForm : Form
{
    /// <summary>All sizes are logical pixels at 96 DPI; <see cref="Scaled"/> and <see cref="PixelFont"/> apply the monitor's DPI.</summary>
    private static class PanelLayout
    {
        public const int Width = 360;
        public const int Height = 260;
        public const int Margin = 12;
        public const int ContentWidth = Width - 2 * Margin;

        public const int StatusTop = 10;
        public const int StatusHeight = 24;
        public const int EstimateOffsetY = 3; // the smaller estimate text sits lower to share the status baseline
        public const int LastChargeTop = 36;
        public const int LastChargeHeight = 18;

        public const int ToggleTop = 62;
        public const int ToggleWidth = 48;
        public const int ToggleHeight = 24;
        public const int ToggleGap = 6;

        public const int PlotLeft = 44; // room for the percent labels
        public const int PlotTop = 100;
        public const int PlotRightMargin = 14;
        public const int PlotBottomMargin = 34; // room for the time labels

        public const int PercentLabelWidth = 32;
        public const int PercentLabelHeight = 16;
        public const int PercentLabelGap = 4;

        public const int TickLength = 4;
        public const int TickLabelTop = 6;
        public const int TickLabelWidth = 40;
        public const int TickLabelHeight = 16;

        public const float SolidLineWidth = 2;
        public const float DashedLineWidth = 1.5f;
        public const float SinglePointRadius = 3;

        public const float StatusFontSize = 15;
        public const float DetailFontSize = 12;
        public const float AxisFontSize = 11;
    }

    private sealed record TimeRangeOption(string Label, TimeSpan Window);

    private static readonly Color Background = ColorTranslator.FromHtml("#202020");
    private static readonly Color BorderColor = ColorTranslator.FromHtml("#3A3A3A");
    private static readonly Color PrimaryText = Color.White;
    private static readonly Color SecondaryText = ColorTranslator.FromHtml("#9A9A9A");
    private static readonly Color GridColor = Color.FromArgb(60, SecondaryText);
    private static readonly Color LineColor = ColorTranslator.FromHtml("#4CC2FF");
    private static readonly Color SelectedToggle = ColorTranslator.FromHtml("#3A3A3A");

    private static readonly TimeRangeOption[] TimeRanges =
    [
        new("24 h", HistoryChart.Day),
        new("7 d", HistoryChart.Week),
    ];

    private static readonly int[] PercentGridLines = [0, 50, 100];

    private readonly BatteryHistory? _history;
    private IReadOnlyList<HistoryRecord> _records = [];
    private BatteryStatus? _status;
    private string _statusLine = "";
    private string _estimateText = "";
    private string? _lastChargeText;
    private bool _systemDrawsBorder;
    private TimeRangeOption _selectedRange = TimeRanges[0]; // every open starts with the 24 h view
    private HistoryChart _chart = HistoryChart.Build([], DateTimeOffset.UtcNow, HistoryChart.Day);

    public OverlayForm(BatteryHistory? history)
    {
        _history = history;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None; // sizes are scaled by hand
        BackColor = Background;
        // Created on the monitor under the cursor, so it gets that monitor's DPI; OnLoad then sizes and places it.
        Location = TargetScreen().WorkingArea.Location;

        ReloadHistory();
    }

    private float DpiScale => DeviceDpi / 96f;

    public void SetStatus(BatteryStatus? status, string statusLine, double? drainPercentPerDay)
    {
        _status = status;
        _statusLine = statusLine;
        _estimateText = _history is null ? "" : RemainingTime.EstimateText(status, drainPercentPerDay); // the simulator shows the status line only
        UpdateLastChargeText();
        Invalidate();
    }

    public void ReloadHistory()
    {
        // ReadSince returns nothing when the file is missing or locked.
        _records = _history?.ReadSince(DateTimeOffset.UtcNow - BatteryHistory.Retention) ?? [];
        RebuildChart();
        UpdateLastChargeText();
        Invalidate();
    }

    private void RebuildChart() => _chart = HistoryChart.Build(_records, DateTimeOffset.UtcNow, _selectedRange.Window);

    private void UpdateLastChargeText()
    {
        var now = DateTimeOffset.UtcNow;
        _lastChargeText = _history is null
            ? null
            : ChargeHistory.Describe(ChargeHistory.LastCharge(_records, now), _status, now, TimeZoneInfo.Local);
    }

    private static Screen TargetScreen() => Screen.FromPoint(Cursor.Position);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _systemDrawsBorder = WindowCorners.TryUseRoundedCorners(Handle, BorderColor);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ClientSize = new Size((int)(PanelLayout.Width * DpiScale), (int)(PanelLayout.Height * DpiScale));
        Location = OverlayPlacement.Location(TargetScreen().WorkingArea, Taskbar.Find(), Size, DpiScale);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate(); // without focus there is no focus loss to close on
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
            Close();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (RangeAt(e.Location) is not { } clicked || clicked == _selectedRange)
            return;

        _selectedRange = clicked;
        RebuildChart();
        Invalidate();
    }

    private TimeRangeOption? RangeAt(Point location)
    {
        for (var index = 0; index < TimeRanges.Length; index++)
        {
            if (ToggleRect(index).Contains(location))
                return TimeRanges[index];
        }
        return null;
    }

    private RectangleF ToggleRect(int index) =>
        Scaled(PanelLayout.Margin + index * (PanelLayout.ToggleWidth + PanelLayout.ToggleGap), PanelLayout.ToggleTop, PanelLayout.ToggleWidth, PanelLayout.ToggleHeight);

    private RectangleF PlotRect => Scaled(
        PanelLayout.PlotLeft,
        PanelLayout.PlotTop,
        PanelLayout.Width - PanelLayout.PlotLeft - PanelLayout.PlotRightMargin,
        PanelLayout.Height - PanelLayout.PlotTop - PanelLayout.PlotBottomMargin);

    /// <summary>A box of logical size whose corner lies at a pixel position moved by logical offsets.</summary>
    private RectangleF BoxAt(PointF origin, float offsetX, float offsetY, float width, float height) =>
        new(origin.X + offsetX * DpiScale, origin.Y + offsetY * DpiScale, width * DpiScale, height * DpiScale);

    private RectangleF Scaled(float x, float y, float width, float height) =>
        new(x * DpiScale, y * DpiScale, width * DpiScale, height * DpiScale);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        if (!_systemDrawsBorder)
        {
            using var border = new Pen(BorderColor);
            graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        DrawHeader(graphics);
        DrawToggle(graphics);
        DrawGraph(graphics, DateTimeOffset.UtcNow);
    }

    private void DrawHeader(Graphics graphics)
    {
        var statusLine = Scaled(PanelLayout.Margin, PanelLayout.StatusTop, PanelLayout.ContentWidth, PanelLayout.StatusHeight);
        using var statusFont = PixelFont(PanelLayout.StatusFontSize, FontStyle.Bold);
        using var detailFont = PixelFont(PanelLayout.DetailFontSize);
        DrawText(graphics, _statusLine, statusFont, PrimaryText, statusLine, StringAlignment.Near);
        DrawText(graphics, _estimateText, detailFont, SecondaryText, statusLine with { Y = statusLine.Y + PanelLayout.EstimateOffsetY * DpiScale }, StringAlignment.Far);

        if (_lastChargeText is null)
            return;
        var lastChargeLine = Scaled(PanelLayout.Margin, PanelLayout.LastChargeTop, PanelLayout.ContentWidth, PanelLayout.LastChargeHeight);
        DrawText(graphics, _lastChargeText, detailFont, SecondaryText, lastChargeLine, StringAlignment.Near);
    }

    private void DrawToggle(Graphics graphics)
    {
        using var font = PixelFont(PanelLayout.DetailFontSize);
        using var selectedBrush = new SolidBrush(SelectedToggle);
        for (var index = 0; index < TimeRanges.Length; index++)
        {
            var isSelected = TimeRanges[index] == _selectedRange;
            var textColor = isSelected ? PrimaryText : SecondaryText;
            if (isSelected)
                graphics.FillRectangle(selectedBrush, ToggleRect(index));
            DrawText(graphics, TimeRanges[index].Label, font, textColor, ToggleRect(index), StringAlignment.Center, StringAlignment.Center);
        }
    }

    private void DrawGraph(Graphics graphics, DateTimeOffset now)
    {
        var plot = PlotRect;
        using var font = PixelFont(PanelLayout.AxisFontSize);
        DrawAxes(graphics, plot, font, now);

        if (EmptyMessage() is { } message)
        {
            DrawText(graphics, message, font, SecondaryText, plot, StringAlignment.Center, StringAlignment.Center);
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
        if (_history is null)
            return "No history in simulator mode";
        return _chart.Points.Count == 0 ? "No history yet" : null;
    }

    private void DrawBatteryLine(Graphics graphics, RectangleF plot, DateTimeOffset now)
    {
        // Segments are drawn one by one, so round caps (not a line join) hide the notches at the bends.
        using var solidPen = new Pen(LineColor, PanelLayout.SolidLineWidth * DpiScale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var dashedPen = new Pen(LineColor, PanelLayout.DashedLineWidth * DpiScale) { DashStyle = DashStyle.Dash };
        foreach (var join in _chart.Joins)
            graphics.DrawLine(join.IsSolid ? solidPen : dashedPen, ToPixel(join.From, plot, now), ToPixel(join.To, plot, now));
    }

    private void DrawSinglePoint(Graphics graphics, PointF center)
    {
        var radius = PanelLayout.SinglePointRadius * DpiScale;
        using var brush = new SolidBrush(LineColor);
        graphics.FillEllipse(brush, center.X - radius, center.Y - radius, 2 * radius, 2 * radius);
    }

    private PointF ToPixel(ChartPoint point, RectangleF plot, DateTimeOffset now) => new(
        TimeToX(point.Time, plot, now),
        PercentToY(point.Percent, plot));

    private float TimeToX(DateTimeOffset time, RectangleF plot, DateTimeOffset now) =>
        plot.Left + (float)HistoryChart.WindowFraction(time, now, _selectedRange.Window) * plot.Width;

    private static float PercentToY(int percent, RectangleF plot) =>
        plot.Bottom - (float)HistoryChart.HeightFraction(percent) * plot.Height;

    private void DrawAxes(Graphics graphics, RectangleF plot, Font font, DateTimeOffset now)
    {
        using var gridPen = new Pen(GridColor, 1);
        DrawPercentGrid(graphics, plot, font, gridPen);
        DrawTimeTicks(graphics, plot, font, gridPen, now);
    }

    private void DrawPercentGrid(Graphics graphics, RectangleF plot, Font font, Pen gridPen)
    {
        foreach (var percent in PercentGridLines)
        {
            var y = PercentToY(percent, plot);
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);

            var label = BoxAt(
                new PointF(plot.Left, y),
                offsetX: -(PanelLayout.PercentLabelWidth + PanelLayout.PercentLabelGap),
                offsetY: -PanelLayout.PercentLabelHeight / 2f,
                PanelLayout.PercentLabelWidth,
                PanelLayout.PercentLabelHeight);
            DrawText(graphics, percent.ToString(), font, SecondaryText, label, StringAlignment.Far, StringAlignment.Center);
        }
    }

    private void DrawTimeTicks(Graphics graphics, RectangleF plot, Font font, Pen gridPen, DateTimeOffset now)
    {
        var ticks = _selectedRange.Window == HistoryChart.Day
            ? HistoryChart.HourTicks(now, TimeZoneInfo.Local)
            : HistoryChart.DayTicks(now, TimeZoneInfo.Local);
        foreach (var tick in ticks)
        {
            var x = TimeToX(tick.Time, plot, now);
            graphics.DrawLine(gridPen, x, plot.Bottom, x, plot.Bottom + PanelLayout.TickLength * DpiScale);

            var label = BoxAt(
                new PointF(x, plot.Bottom),
                offsetX: -PanelLayout.TickLabelWidth / 2f,
                offsetY: PanelLayout.TickLabelTop,
                PanelLayout.TickLabelWidth,
                PanelLayout.TickLabelHeight);
            DrawText(graphics, tick.Label, font, SecondaryText, label, StringAlignment.Center);
        }
    }

    // Pixel-sized fonts follow the monitor DPI through DpiScale, not the system DPI.
    private Font PixelFont(float logicalPixels, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI", logicalPixels * DpiScale, style, GraphicsUnit.Pixel);

    private static void DrawText(
        Graphics graphics, string text, Font font, Color color, RectangleF area,
        StringAlignment horizontal, StringAlignment vertical = StringAlignment.Near)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = horizontal,
            LineAlignment = vertical,
            Trimming = StringTrimming.EllipsisCharacter,
        };
        graphics.DrawString(text, font, brush, area, format);
    }
}
