using System.Drawing.Drawing2D;
using System.Drawing.Text;
using LogitechBattery.Battery;
using LogitechBattery.History;
using LogitechBattery.Tray;

namespace LogitechBattery.Panel;

/// <summary>The panel that opens on a left-click of the tray icon: status, last charge, estimate and the battery graph.</summary>
internal sealed class OverlayForm : Form
{
    /// <summary>All sizes are logical pixels at 96 DPI; <see cref="Scaled"/> and <see cref="PixelFont"/> apply the monitor's DPI.</summary>
    private static class PanelLayout
    {
        public const int Width = 360;
        public const int Height = PlotTop + PlotHeight + PlotBottomMargin;
        public const int Margin = 12;
        public const int ContentWidth = Width - 2 * Margin;

        public const int NameTop = 10;
        public const int NameHeight = 18;

        public const int StatusTop = NameTop + NameHeight;
        public const int StatusHeight = 24;
        public const int EstimateOffsetY = 3; // the smaller estimate text sits lower to share the status baseline
        public const int StatusToLastChargeGap = 26; // from the top of the status line to the top of the last-charge line
        public const int LastChargeTop = StatusTop + StatusToLastChargeGap;
        public const int LastChargeHeight = 18;

        public const int LastChargeToToggleGap = 26;
        public const int ToggleTop = LastChargeTop + LastChargeToToggleGap;
        public const int ToggleWidth = 48;
        public const int ToggleHeight = 24;
        public const int ToggleGap = 6;

        public const int PlotLeft = 44; // room for the percent labels
        public const int ToggleToPlotGap = 38; // from the top of the range buttons to the top of the plot
        public const int PlotTop = ToggleTop + ToggleToPlotGap;
        public const int PlotHeight = 126;
        public const int PlotRightMargin = 14;
        public const int PlotBottomMargin = 34; // room for the time labels

        public const float StatusFontSize = 15;
        public const float DetailFontSize = 12;
        public const float AxisFontSize = 11;

        public const float StandardDpi = 96f; // the DPI at which the layout sizes above are meant
    }

    private sealed record TimeRangeOption(string Label, TimeSpan Window);

    private static readonly Color Background = ColorTranslator.FromHtml("#202020");
    private static readonly Color BorderColor = ColorTranslator.FromHtml("#3A3A3A");
    private static readonly Color PrimaryText = Color.White;
    internal static readonly Color SecondaryText = ColorTranslator.FromHtml("#9A9A9A");
    private static readonly Color SelectedToggle = ColorTranslator.FromHtml("#3A3A3A");

    private static readonly TimeRangeOption[] TimeRanges =
    [
        new("24 h", HistoryChart.Day),
        new("7 d", HistoryChart.Week),
    ];

    private readonly bool _isSimulator; // the simulator has no history files; a real device may have none yet
    private BatteryHistory? _history;
    private IReadOnlyList<HistoryRecord> _records = [];
    private BatteryStatus? _status;
    private string _deviceName = "";
    private string _statusLine = "";
    private string _estimateText = "";
    private string? _lastChargeText;
    private bool _systemDrawsBorder;
    private TimeRangeOption _selectedRange = TimeRanges[0]; // every open starts with the 24 h view
    private HistoryChart _chart = HistoryChart.Build([], DateTimeOffset.UtcNow, HistoryChart.Day);

    public OverlayForm(BatteryHistory? history, bool isSimulator)
    {
        _isSimulator = isSimulator;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None; // sizes are scaled by hand
        BackColor = Background;
        // Created on the monitor under the cursor, so it gets that monitor's DPI; OnLoad then sizes and places it.
        Location = TargetScreen().WorkingArea.Location;

        ShowHistory(history);
    }

    private float DpiScale => DeviceDpi / PanelLayout.StandardDpi;

    public void SetStatus(BatteryStatus? status, string deviceName, string statusLine, double? drainPercentPerDay)
    {
        _status = status;
        _deviceName = deviceName;
        _statusLine = statusLine;
        _estimateText = _isSimulator ? "" : RemainingTime.EstimateText(status, drainPercentPerDay); // the simulator shows the status only
        UpdateLastChargeText();
        Invalidate();
    }

    /// <summary>Shows the history of another device, or reads the same one again.</summary>
    public void ShowHistory(BatteryHistory? history)
    {
        _history = history;
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
        PanelLayout.PlotHeight);

    private RectangleF Scaled(float left, float top, float width, float height) =>
        new(left * DpiScale, top * DpiScale, width * DpiScale, height * DpiScale);

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
        var nameLine = Scaled(PanelLayout.Margin, PanelLayout.NameTop, PanelLayout.ContentWidth, PanelLayout.NameHeight);
        var statusLine = Scaled(PanelLayout.Margin, PanelLayout.StatusTop, PanelLayout.ContentWidth, PanelLayout.StatusHeight);
        using var statusFont = PixelFont(PanelLayout.StatusFontSize, FontStyle.Bold);
        using var detailFont = PixelFont(PanelLayout.DetailFontSize);
        DrawText(graphics, _deviceName, detailFont, SecondaryText, nameLine, StringAlignment.Near);
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
        using var axisFont = PixelFont(PanelLayout.AxisFontSize);
        var painter = new HistoryGraphPainter(DpiScale, _isSimulator, _chart, _selectedRange.Window);
        painter.Draw(graphics, PlotRect, axisFont, now);
    }

    // Pixel-sized fonts follow the monitor DPI through DpiScale, not the system DPI.
    private Font PixelFont(float logicalPixels, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI", logicalPixels * DpiScale, style, GraphicsUnit.Pixel);

    internal static void DrawText(
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
