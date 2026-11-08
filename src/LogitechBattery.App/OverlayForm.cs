using System.Drawing.Drawing2D;
using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>The panel that opens on a left-click of the tray icon: status, last charge, estimate and the battery graph.</summary>
internal sealed class OverlayForm : Form
{
    private const int LogicalWidth = 360;
    private const int LogicalHeight = 260;
    private static readonly TimeSpan LastChargeLookBack = TimeSpan.FromDays(365);

    private static readonly Color Background = ColorTranslator.FromHtml("#202020");
    private static readonly Color BorderColor = ColorTranslator.FromHtml("#3A3A3A");
    private static readonly Color PrimaryText = Color.White;
    private static readonly Color SecondaryText = ColorTranslator.FromHtml("#9A9A9A");
    private static readonly Color GridColor = Color.FromArgb(60, 0x9A, 0x9A, 0x9A);
    private static readonly Color LineColor = ColorTranslator.FromHtml("#4CC2FF");
    private static readonly Color SelectedToggle = ColorTranslator.FromHtml("#3A3A3A");

    private static readonly (string Label, TimeSpan Window)[] Windows = [("24 h", HistoryChart.Day), ("7 d", HistoryChart.Week)];

    private readonly BatteryHistory? _history;
    private IReadOnlyList<HistoryRecord> _records = [];
    private BatteryStatus? _status;
    private string _statusLine = "";
    private string _estimateText = "";
    private string? _lastChargeText;
    private TimeSpan _window = HistoryChart.Day; // every open starts with the 24 h view
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
        _estimateText = _history is null ? "" : EstimateText(status, drainPercentPerDay); // the simulator shows the status line only
        UpdateLastChargeText();
        Invalidate();
    }

    public void ReloadHistory()
    {
        // ReadSince returns nothing when the file is missing or locked.
        _records = _history?.ReadSince(DateTimeOffset.UtcNow - LastChargeLookBack) ?? [];
        RebuildChart();
        UpdateLastChargeText();
        Invalidate();
    }

    private static string EstimateText(BatteryStatus? status, double? drainPercentPerDay)
    {
        if (status is null)
            return "";
        if (RemainingTime.RemainingText(status, drainPercentPerDay) is { } text)
            return text;
        var isDischarging = status is { Connection: ConnectionState.Connected, Charging: ChargingState.Discharging, Percent: not null };
        return isDischarging ? "not enough data yet" : "";
    }

    private void RebuildChart()
    {
        var now = DateTimeOffset.UtcNow;
        _chart = HistoryChart.Build(_records, now, _window);
    }

    private void UpdateLastChargeText() =>
        _lastChargeText = _history is null ? null : ChargeHistory.Describe(ChargeHistory.LastCharge(_records, DateTimeOffset.UtcNow), _status, DateTimeOffset.UtcNow, TimeZoneInfo.Local);

    private static Screen TargetScreen() => Screen.FromPoint(Cursor.Position);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ClientSize = new Size((int)(LogicalWidth * DpiScale), (int)(LogicalHeight * DpiScale));
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
        for (var i = 0; i < Windows.Length; i++)
        {
            if (!ToggleRect(i).Contains(e.Location) || Windows[i].Window == _window)
                continue;
            _window = Windows[i].Window;
            RebuildChart();
            Invalidate();
        }
    }

    private RectangleF ToggleRect(int index) => Scaled(12 + index * 54, 62, 48, 24);

    private RectangleF PlotRect => Scaled(44, 100, LogicalWidth - 44 - 14, LogicalHeight - 100 - 34);

    private RectangleF Scaled(float x, float y, float width, float height) =>
        new(x * DpiScale, y * DpiScale, width * DpiScale, height * DpiScale);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var border = new Pen(BorderColor);
        g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

        DrawHeader(g);
        DrawToggle(g);
        DrawGraph(g);
    }

    private void DrawHeader(Graphics g)
    {
        var line = Scaled(12, 10, LogicalWidth - 24, 24);
        using var statusFont = PixelFont(15, FontStyle.Bold);
        using var smallFont = PixelFont(12);
        DrawText(g, _statusLine, statusFont, PrimaryText, line, StringAlignment.Near);
        DrawText(g, _estimateText, smallFont, SecondaryText, line with { Y = line.Y + 3 * DpiScale }, StringAlignment.Far);
        if (_lastChargeText is not null)
            DrawText(g, _lastChargeText, smallFont, SecondaryText, Scaled(12, 36, LogicalWidth - 24, 18), StringAlignment.Near);
    }

    private void DrawToggle(Graphics g)
    {
        using var font = PixelFont(12);
        using var selectedBrush = new SolidBrush(SelectedToggle);
        for (var i = 0; i < Windows.Length; i++)
        {
            var isSelected = Windows[i].Window == _window;
            if (isSelected)
                g.FillRectangle(selectedBrush, ToggleRect(i));
            DrawText(g, Windows[i].Label, font, isSelected ? PrimaryText : SecondaryText, ToggleRect(i), StringAlignment.Center, StringAlignment.Center);
        }
    }

    private void DrawGraph(Graphics g)
    {
        var plot = PlotRect;
        using var font = PixelFont(11);
        var now = DateTimeOffset.UtcNow;
        DrawAxes(g, plot, font, now);

        var emptyText = _history is null ? "No history in simulator mode" : _chart.Points.Count == 0 ? "No history yet" : null;
        if (emptyText is not null)
        {
            DrawText(g, emptyText, font, SecondaryText, plot, StringAlignment.Center, StringAlignment.Center);
            return;
        }

        PointF ToPixel(ChartPoint point) => new(
            plot.Left + (float)((point.Time - (now - _window)) / _window) * plot.Width,
            plot.Bottom - point.Percent / 100f * plot.Height);

        using var solidPen = new Pen(LineColor, 2 * DpiScale) { LineJoin = LineJoin.Round };
        using var dashedPen = new Pen(LineColor, 1.5f * DpiScale) { DashStyle = DashStyle.Dash };
        g.SetClip(plot);
        foreach (var join in _chart.Joins)
            g.DrawLine(join.IsSolid ? solidPen : dashedPen, ToPixel(join.From), ToPixel(join.To));

        if (_chart.Points.Count == 1)
        {
            var dot = ToPixel(_chart.Points[0]);
            var radius = 3 * DpiScale;
            using var brush = new SolidBrush(LineColor);
            g.FillEllipse(brush, dot.X - radius, dot.Y - radius, 2 * radius, 2 * radius);
        }
        g.ResetClip();
    }

    private void DrawAxes(Graphics g, RectangleF plot, Font font, DateTimeOffset now)
    {
        using var gridPen = new Pen(GridColor, 1);
        foreach (var percent in new[] { 0, 50, 100 })
        {
            var y = plot.Bottom - percent / 100f * plot.Height;
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var label = new RectangleF(plot.Left - 36 * DpiScale, y - 8 * DpiScale, 32 * DpiScale, 16 * DpiScale);
            DrawText(g, percent.ToString(), font, SecondaryText, label, StringAlignment.Far, StringAlignment.Center);
        }

        var ticks = _window == HistoryChart.Day
            ? HistoryChart.HourTicks(now, TimeZoneInfo.Local)
            : HistoryChart.DayTicks(now, TimeZoneInfo.Local);
        foreach (var tick in ticks)
        {
            var x = plot.Left + (float)((tick.Time - (now - _window)) / _window) * plot.Width;
            g.DrawLine(gridPen, x, plot.Bottom, x, plot.Bottom + 4 * DpiScale);
            var label = new RectangleF(x - 20 * DpiScale, plot.Bottom + 6 * DpiScale, 40 * DpiScale, 16 * DpiScale);
            DrawText(g, tick.Label, font, SecondaryText, label, StringAlignment.Center);
        }
    }

    // Pixel-sized fonts follow the monitor DPI through DpiScale, not the system DPI.
    private Font PixelFont(float logicalPixels, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI", logicalPixels * DpiScale, style, GraphicsUnit.Pixel);

    private static void DrawText(
        Graphics g, string text, Font font, Color color, RectangleF area,
        StringAlignment horizontal, StringAlignment vertical = StringAlignment.Near)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = horizontal,
            LineAlignment = vertical,
            Trimming = StringTrimming.EllipsisCharacter,
        };
        g.DrawString(text, font, brush, area, format);
    }
}
