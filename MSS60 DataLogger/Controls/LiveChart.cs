using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MSS60_DataLogger.Diagnostics;

namespace MSS60_DataLogger.Controls;

/// <summary>グラフに描く 1 本の線。</summary>
public sealed class ChartSeries
{
    public ChartSeries(string key, string name, string unit, Color color)
    {
        Key = key;
        Name = name;
        Unit = unit;
        Color = color;

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Brush = brush;
    }

    /// <summary>項目を跨いで同じ線を追跡するための識別子(測定値の短縮名)。</summary>
    public string Key { get; }

    public string Name { get; }

    public string Unit { get; }

    public Color Color { get; }

    /// <summary>チェックボックスや凡例のバインド用。</summary>
    public Brush Brush { get; }

    public bool IsVisible { get; set; }

    /// <summary>直近の値(凡例表示用)。</summary>
    public double? LatestValue { get; set; }
}

/// <summary>
/// 直近一定時間の測定値をスクロール表示する折れ線グラフ。
/// 項目ごとに単位が大きく違うため、線はそれぞれ自身の最小〜最大で正規化して描く。
/// </summary>
public sealed class LiveChart : FrameworkElement
{
    private const double LeftMargin = 8;
    private const double RightMargin = 8;
    private const double TopMargin = 8;
    private const double BottomMargin = 20;

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x4F, 0xC3, 0xF7), // 水色
        Color.FromRgb(0xFF, 0x8A, 0x65), // 橙
        Color.FromRgb(0x81, 0xC7, 0x84), // 緑
        Color.FromRgb(0xFF, 0xD5, 0x4F), // 黄
        Color.FromRgb(0xBA, 0x68, 0xC8), // 紫
        Color.FromRgb(0x4D, 0xD0, 0xE1), // シアン
        Color.FromRgb(0xF0, 0x62, 0x92), // 桃
        Color.FromRgb(0xA1, 0x88, 0x7F), // 茶
    ];

    private readonly Queue<(DateTime Time, double?[] Values)> _samples = new();
    private readonly Pen _gridPen = CreateFrozenPen(Color.FromRgb(0x3A, 0x3A, 0x3A), 1);
    private readonly Brush _axisBrush = CreateFrozenBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private readonly Pen _hoverLinePen = CreateFrozenPen(Color.FromRgb(0x80, 0x80, 0x80), 1);
    private readonly Brush _hoverHeaderBrush = CreateFrozenBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private readonly Brush _tooltipBackground = CreateFrozenBrush(Color.FromArgb(235, 0x20, 0x20, 0x20));
    private readonly Pen _tooltipBorderPen = CreateFrozenPen(Color.FromRgb(0x4A, 0x4A, 0x4A), 1);
    private ChartSeries[] _series = [];
    private Point? _hoverPosition;

    /// <summary>横軸に表示する時間の長さ。</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(60);

    public IReadOnlyList<ChartSeries> Series => _series;

    public static Color ColorFor(int index) => Palette[index % Palette.Length];

    public LiveChart()
    {
        Cursor = Cursors.Cross;
    }

    /// <summary>
    /// 表示対象の項目が変わったときに呼ぶ。蓄積済みのデータは破棄される。
    /// 「項目」タブでチェックした測定値は、ここでも既定で表示(チェック済み)にする。
    /// ただし既に手動で非表示にしていた項目は、その状態を保ったままにする。
    /// </summary>
    public void ResetSeries(IReadOnlyList<MeasurementDefinition> selection)
    {
        Dictionary<string, bool> previousVisibility = _series.ToDictionary(s => s.Key, s => s.IsVisible);

        _series = [.. selection.Select((d, i) =>
        {
            var series = new ChartSeries(d.Arg, d.DisplayName, d.Unit, ColorFor(i));
            series.IsVisible = !previousVisibility.TryGetValue(d.Arg, out bool wasVisible) || wasVisible;
            return series;
        })];

        _samples.Clear();
        InvalidateVisual();
    }

    public void AddSample(SampleSnapshot sample)
    {
        if (_series.Length == 0 || sample.Values.Length != _series.Length)
        {
            return;
        }

        _samples.Enqueue((sample.Timestamp, sample.Values));

        DateTime oldest = sample.Timestamp - Window;
        while (_samples.Count > 0 && _samples.Peek().Time < oldest)
        {
            _samples.Dequeue();
        }

        for (int i = 0; i < _series.Length; i++)
        {
            if (sample.Values[i] is { } value)
            {
                _series[i].LatestValue = value;
            }
        }

        InvalidateVisual();
    }

    public void Clear()
    {
        _samples.Clear();
        foreach (ChartSeries series in _series)
        {
            series.LatestValue = null;
        }

        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _hoverPosition = e.GetPosition(this);
        InvalidateVisual();
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hoverPosition = null;
        InvalidateVisual();
        base.OnMouseLeave(e);
    }

    protected override void OnRender(DrawingContext dc)
    {
        // 描画済みの図形がない余白部分でもマウス移動を拾えるよう、全体を透明色で塗って当たり判定を作る。
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var area = new Rect(
            LeftMargin,
            TopMargin,
            Math.Max(0, ActualWidth - LeftMargin - RightMargin),
            Math.Max(0, ActualHeight - TopMargin - BottomMargin));

        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        DrawGrid(dc, area);

        if (_samples.Count < 2)
        {
            DrawCenteredText(dc, area, _series.Length == 0 ? "項目を選択してください" : "データ待機中…");
            return;
        }

        (DateTime Time, double?[] Values)[] samples = [.. _samples];
        DateTime end = samples[^1].Time;
        DateTime start = end - Window;
        double totalSeconds = Window.TotalSeconds;

        for (int i = 0; i < _series.Length; i++)
        {
            if (_series[i].IsVisible)
            {
                DrawSeries(dc, area, samples, i, start, totalSeconds);
            }
        }

        DrawTimeAxis(dc, area);
        DrawHoverInfo(dc, area, samples, start, totalSeconds);
    }

    private void DrawGrid(DrawingContext dc, Rect area)
    {
        dc.DrawRectangle(CreateFrozenBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), null, area);

        for (int i = 1; i < 4; i++)
        {
            double y = area.Top + (area.Height * i / 4.0);
            dc.DrawLine(_gridPen, new Point(area.Left, y), new Point(area.Right, y));
        }

        for (int i = 1; i < 4; i++)
        {
            double x = area.Left + (area.Width * i / 4.0);
            dc.DrawLine(_gridPen, new Point(x, area.Top), new Point(x, area.Bottom));
        }
    }

    private void DrawSeries(
        DrawingContext dc,
        Rect area,
        (DateTime Time, double?[] Values)[] samples,
        int seriesIndex,
        DateTime start,
        double totalSeconds)
    {
        // 単位がばらばらなので、線ごとに自身の値域へ正規化する。
        double min = double.MaxValue;
        double max = double.MinValue;
        foreach ((DateTime _, double?[] values) in samples)
        {
            if (values[seriesIndex] is { } v)
            {
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
        }

        if (min > max)
        {
            return; // 値がまったく取れていない
        }

        if (Math.Abs(max - min) < 1e-9)
        {
            // 値が一定のときは中央に水平線を引く
            min -= 1;
            max += 1;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            bool started = false;
            foreach ((DateTime time, double?[] values) in samples)
            {
                if (values[seriesIndex] is not { } value)
                {
                    started = false; // 欠測は線を切る
                    continue;
                }

                double x = area.Left + (area.Width * (time - start).TotalSeconds / totalSeconds);
                double y = area.Bottom - (area.Height * (value - min) / (max - min));
                var point = new Point(x, y);

                if (started)
                {
                    ctx.LineTo(point, isStroked: true, isSmoothJoin: false);
                }
                else
                {
                    ctx.BeginFigure(point, isFilled: false, isClosed: false);
                    started = true;
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, CreateFrozenPen(_series[seriesIndex].Color, 1.6), geometry);
    }

    private void DrawTimeAxis(DrawingContext dc, Rect area)
    {
        double seconds = Window.TotalSeconds;
        for (int i = 0; i <= 4; i++)
        {
            double x = area.Left + (area.Width * i / 4.0);
            string label = i == 4 ? "現在" : FormatOffset(seconds * (4 - i) / 4);
            FormattedText text = CreateText(label, 11, _axisBrush);

            // 両端のラベルが描画領域からはみ出して欠けないように収める。
            double left = Math.Clamp(x - (text.Width / 2), area.Left, area.Right - text.Width);
            dc.DrawText(text, new Point(left, area.Bottom + 3));
        }
    }

    /// <summary>横軸の目盛り。1 分以上は「分」表記にする。</summary>
    private static string FormatOffset(double seconds) =>
        seconds >= 60 ? $"-{seconds / 60:0.#}分" : $"-{seconds:0}秒";

    /// <summary>
    /// カーソル位置に最も近いサンプルを縦線で示し、そのときの各線の値を吹き出しで表示する。
    /// </summary>
    private void DrawHoverInfo(
        DrawingContext dc,
        Rect area,
        (DateTime Time, double?[] Values)[] samples,
        DateTime start,
        double totalSeconds)
    {
        if (_hoverPosition is not { } pos || !area.Contains(pos))
        {
            return;
        }

        double targetSeconds = totalSeconds * (pos.X - area.Left) / area.Width;
        DateTime targetTime = start.AddSeconds(targetSeconds);

        // samples は時刻順なので、差が縮み続ける間だけ追跡し、増加に転じたら打ち切る(谷を一度だけ通る)。
        int nearestIndex = 0;
        TimeSpan bestDiff = TimeSpan.MaxValue;
        for (int i = 0; i < samples.Length; i++)
        {
            TimeSpan diff = (samples[i].Time - targetTime).Duration();
            if (diff < bestDiff)
            {
                bestDiff = diff;
                nearestIndex = i;
            }
            else if (samples[i].Time > targetTime)
            {
                break;
            }
        }

        (DateTime time, double?[] values) = samples[nearestIndex];
        double snappedX = area.Left + (area.Width * (time - start).TotalSeconds / totalSeconds);

        List<(ChartSeries Series, double Value)> rows = [];
        for (int i = 0; i < _series.Length; i++)
        {
            if (_series[i].IsVisible && values[i] is { } value)
            {
                rows.Add((_series[i], value));
            }
        }

        if (rows.Count == 0)
        {
            return;
        }

        dc.DrawLine(_hoverLinePen, new Point(snappedX, area.Top), new Point(snappedX, area.Bottom));

        double secondsAgo = (samples[^1].Time - time).TotalSeconds;
        string header = secondsAgo < 0.05 ? "現在" : FormatOffset(secondsAgo);
        DrawTooltip(dc, area, new Point(snappedX, pos.Y), header, rows);
    }

    private void DrawTooltip(
        DrawingContext dc,
        Rect area,
        Point anchor,
        string header,
        List<(ChartSeries Series, double Value)> rows)
    {
        const double padding = 8;
        const double lineHeight = 17;
        const double swatchSize = 8;

        FormattedText headerText = CreateText(header, 11, _hoverHeaderBrush);
        List<FormattedText> rowTexts = [.. rows.Select(r => CreateText(
            $"{r.Series.Name}: {MeasurementFormat.Format(r.Value)}"
                + (string.IsNullOrEmpty(r.Series.Unit) ? string.Empty : $" {r.Series.Unit}"),
            11,
            _axisBrush))];

        double contentWidth = Math.Max(headerText.Width, rowTexts.Count == 0 ? 0 : rowTexts.Max(t => t.Width) + swatchSize + 6);
        double boxWidth = contentWidth + (padding * 2);
        double boxHeight = headerText.Height + 4 + (rowTexts.Count * lineHeight) + (padding * 2);

        // 吹き出しがグラフ領域からはみ出さないよう、右に置けなければ左に、上下にもはみ出さない位置へ収める。
        double x = anchor.X + 12;
        if (x + boxWidth > area.Right)
        {
            x = anchor.X - boxWidth - 12;
        }

        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - boxWidth));
        double y = Math.Clamp(anchor.Y - (boxHeight / 2), area.Top, Math.Max(area.Top, area.Bottom - boxHeight));

        var box = new Rect(x, y, boxWidth, boxHeight);
        dc.DrawRoundedRectangle(_tooltipBackground, _tooltipBorderPen, box, 4, 4);

        double textY = y + padding;
        dc.DrawText(headerText, new Point(x + padding, textY));
        textY += headerText.Height + 4;

        foreach ((FormattedText text, (ChartSeries series, double _)) in rowTexts.Zip(rows))
        {
            var swatch = new Rect(x + padding, textY + ((lineHeight - swatchSize) / 2), swatchSize, swatchSize);
            dc.DrawRectangle(series.Brush, null, swatch);
            dc.DrawText(text, new Point(x + padding + swatchSize + 6, textY));
            textY += lineHeight;
        }
    }

    private void DrawCenteredText(DrawingContext dc, Rect area, string message)
    {
        FormattedText text = CreateText(message, 13, _axisBrush);
        dc.DrawText(text, new Point(
            area.Left + ((area.Width - text.Width) / 2),
            area.Top + ((area.Height - text.Height) / 2)));
    }

    private FormattedText CreateText(string value, double size, Brush brush) => new(
        value,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface("Yu Gothic UI"),
        size,
        brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Pen CreateFrozenPen(Color color, double thickness)
    {
        var pen = new Pen(CreateFrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
