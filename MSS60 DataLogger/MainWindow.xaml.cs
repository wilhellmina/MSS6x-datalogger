using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MSS60_DataLogger.Controls;
using MSS60_DataLogger.Diagnostics;
using MSS60_DataLogger.Logging;
using MSS60_DataLogger.Settings;

namespace MSS60_DataLogger;

public partial class MainWindow : Window
{
    /// <summary>MSS60.prg が置かれているフォルダー。</summary>
    private const string EcuPath = @"C:\EDIABAS\Ecu";

    // グラフの表示時間(秒)。短すぎると読み取れず、長すぎるとサンプル数が膨らみ描画が重くなるため範囲を制限する。
    private const int MinWindowSeconds = 5;
    private const int MaxWindowSeconds = 3600;

    private readonly ObservableCollection<MeasurementRow> _rows = [];
    private readonly List<MeasurementDefinition> _selection = [];
    private readonly List<SelectableMeasurement> _selectable = [];
    private readonly List<MeasurementGroup> _groups = [];

    private EcuSampler? _sampler;
    private CsvSampleWriter? _logWriter;
    private bool _suppressSelectionUpdates;
    private string? _currentVin;

    // 続けてチェックを付け外ししたときに毎回つなぎ直さないよう、少し待ってからまとめて反映する。
    private readonly DispatcherTimer _restartTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool _restartInProgress;

    // サンプリングレート表示用
    private DateTime _rateWindowStart = DateTime.Now;
    private int _rateWindowCount;

    public MainWindow()
    {
        InitializeComponent();

        ValueGrid.ItemsSource = _rows;
        RefreshPortList();

        _restartTimer.Tick += async (_, _) =>
        {
            _restartTimer.Stop();
            await RestartSamplerAsync();
        };

        BuildCategoryTree();
    }

    private bool IsConnected => _sampler is not null;

    private bool IsRecording => _logWriter is not null;

    /// <summary>カタログ全項目をカテゴリごとにまとめ、前回終了時(無ければ既定)の項目にチェックを入れる。</summary>
    private void BuildCategoryTree()
    {
        List<string> savedSelection = AppSettingsStore.Load().SelectedMeasurementArgs;
        HashSet<string> defaults = savedSelection.Count > 0 ? [.. savedSelection] : [.. MeasurementCatalog.DefaultSelection];

        _selectable.AddRange(MeasurementCatalog.All.Select(d => new SelectableMeasurement(d, OnMeasurementToggled)));

        foreach (string category in MeasurementCategories.DisplayOrder)
        {
            List<SelectableMeasurement> items = [.. _selectable.Where(s => s.Definition.Category == category)];
            if (items.Count == 0)
            {
                continue;
            }

            var group = new MeasurementGroup(category, items);
            _groups.Add(group);
        }

        CategoryList.ItemsSource = _groups;

        // 既定の項目にチェックを入れる(この間は再構築を 1 回にまとめる)
        _suppressSelectionUpdates = true;
        foreach (SelectableMeasurement item in _selectable.Where(s => defaults.Contains(s.Definition.Arg)))
        {
            item.IsSelected = true;
        }

        _suppressSelectionUpdates = false;

        _groups[0].IsExpanded = true; // 「基本」だけ最初から開いておく
        RebuildSelection();
    }

    #region 接続

    private void OnRefreshPortsClick(object sender, RoutedEventArgs e) => RefreshPortList();

    private void RefreshPortList()
    {
        string? previous = ComPortCombo.SelectedItem as string;
        string[] ports = SerialPort.GetPortNames().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();

        ComPortCombo.ItemsSource = ports;
        ComPortCombo.SelectedItem = previous is not null && ports.Contains(previous)
            ? previous
            : ports.FirstOrDefault();
    }

    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (IsConnected)
        {
            Disconnect("切断しました。");
            return;
        }

        if (ComPortCombo.SelectedItem is not string comPort)
        {
            SetStatus("COM ポートを選択してください。");
            return;
        }

        if (_selection.Count == 0)
        {
            SetStatus("記録する項目を選択してください。");
            return;
        }

        if (!Directory.Exists(EcuPath))
        {
            SetStatus($"SGBD フォルダーが見つかりません: {EcuPath}");
            return;
        }

        StartSampler(comPort);
        UpdateConnectionUi();
    }

    private void StartSampler(string comPort)
    {
        var sampler = new EcuSampler(comPort, EcuPath, [.. _selection]);
        sampler.SampleReceived += OnSampleReceived;
        sampler.IdentityReceived += identity => Dispatcher.Invoke(() =>
        {
            _currentVin = identity.Vin;
            IdentityText.Text = $"VIN: {identity.Vin}  SW: {identity.SoftwareVersion}";
        });
        sampler.StatusChanged += message => Dispatcher.Invoke(() => SetStatus(message));
        sampler.Faulted += message => Dispatcher.Invoke(() => Disconnect(message));

        _sampler = sampler;
        _rateWindowStart = DateTime.Now;
        _rateWindowCount = 0;

        _currentVin = null;
        IdentityText.Text = "VIN: —  SW: —"; // 前回接続分が残らないよう、取得できるまではプレースホルダーに戻す
        Chart.Clear();
        sampler.Start();
    }

    /// <summary>
    /// 接続したまま項目構成だけを切り替える。
    /// 古いサンプラーが COM ポートを解放しきる前に開き直すと接続に失敗するため、Dispose の完了を待つ。
    /// </summary>
    private async Task RestartSamplerAsync()
    {
        if (_restartInProgress)
        {
            _restartTimer.Start(); // 進行中なら終わったあとにやり直す
            return;
        }

        if (ComPortCombo.SelectedItem is not string comPort)
        {
            return;
        }

        _restartInProgress = true;
        try
        {
            EcuSampler? old = _sampler;
            _sampler = null;

            if (old is not null)
            {
                old.DetachEvents(); // 停止時の「切断しました」で新しい状態表示を上書きさせない
                SetStatus("項目の変更を反映しています…");
                await Task.Run(old.Dispose);
            }

            if (_selection.Count == 0)
            {
                UpdateConnectionUi();
                SetStatus("項目が選択されていないため切断しました。");
                return;
            }

            StartSampler(comPort);
            UpdateConnectionUi();
        }
        finally
        {
            _restartInProgress = false;
        }
    }

    private void Disconnect(string status)
    {
        _restartTimer.Stop();
        StopRecording();
        DisposeSamplerInBackground();

        IdentityText.Text = "VIN: —  SW: —";
        UpdateConnectionUi();
        SetStatus(status);
        RateText.Text = "— Hz";
    }

    private void DisposeSamplerInBackground()
    {
        EcuSampler? sampler = _sampler;
        _sampler = null;

        if (sampler is not null)
        {
            // Dispose はワーカースレッドの終了待ちを含むため、UI を止めないよう別スレッドで行う。
            Task.Run(sampler.Dispose);
        }
    }

    private void UpdateConnectionUi()
    {
        ConnectButton.Content = IsConnected ? "切断" : "接続";
        ComPortCombo.IsEnabled = !IsConnected;
        RefreshPortsButton.IsEnabled = !IsConnected;
        RecordButton.IsEnabled = IsConnected && _selection.Count > 0;
    }

    private void SetStatus(string message) => StatusText.Text = message;

    #endregion

    #region 測定値の受信

    private void OnSampleReceived(SampleSnapshot sample)
    {
        // ワーカースレッドから呼ばれるので UI スレッドへ渡す。
        // 取りこぼしより表示落ちを優先して BeginInvoke(非同期)にする。
        Dispatcher.BeginInvoke(() =>
        {
            for (int i = 0; i < _rows.Count && i < sample.Values.Length; i++)
            {
                _rows[i].Value = sample.Values[i];
            }

            Chart.AddSample(sample);
            UpdateRate();
        });

        // CSV への書き込みはワーカースレッド側で行い、UI の遅延に影響されないようにする。
        CsvSampleWriter? writer = _logWriter;
        if (writer is not null)
        {
            writer.Write(sample);
            int rowCount = writer.RowCount;
            Dispatcher.BeginInvoke(() => RowCountText.Text = $"{rowCount:N0} 行");
        }
    }

    private void UpdateRate()
    {
        _rateWindowCount++;
        TimeSpan elapsed = DateTime.Now - _rateWindowStart;
        if (elapsed < TimeSpan.FromSeconds(1))
        {
            return;
        }

        RateText.Text = $"{_rateWindowCount / elapsed.TotalSeconds:0.0} Hz";
        _rateWindowStart = DateTime.Now;
        _rateWindowCount = 0;
    }

    #endregion

    #region 項目選択

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchBox.Text.Trim().ToLowerInvariant();
        foreach (MeasurementGroup group in _groups)
        {
            group.ApplyFilter(query);
        }
    }

    /// <summary>チェックが変わったときの処理。上限超過と記録中の変更を弾く。</summary>
    private void OnMeasurementToggled(SelectableMeasurement item)
    {
        if (_suppressSelectionUpdates)
        {
            return;
        }

        if (IsRecording)
        {
            // CSV の列は記録開始時に確定しているため、途中で項目を変えられない。
            _suppressSelectionUpdates = true;
            item.SetSelectedSilently(!item.IsSelected);
            _suppressSelectionUpdates = false;

            SetStatus("記録中は項目を変更できません。いったん記録を停止してください。");
            return;
        }

        if (item.IsSelected && _selectable.Count(s => s.IsSelected) > MeasurementCatalog.MaxSelectableCount)
        {
            _suppressSelectionUpdates = true;
            item.SetSelectedSilently(false);
            _suppressSelectionUpdates = false;

            SetStatus($"同時に記録できるのは {MeasurementCatalog.MaxSelectableCount} 項目までです。");
            return;
        }

        RebuildSelection();

        // 接続中に項目が変わったら、新しい構成で読み直す(連続操作はまとめて 1 回)。
        if (IsConnected)
        {
            _restartTimer.Stop();
            _restartTimer.Start();
        }
    }

    /// <summary>チェック状態から、表示・グラフ・ログ対象の一覧を作り直す。</summary>
    private void RebuildSelection()
    {
        _selection.Clear();
        _selection.AddRange(_selectable.Where(s => s.IsSelected).Select(s => s.Definition));

        Chart.ResetSeries(_selection);

        _rows.Clear();
        for (int i = 0; i < _selection.Count; i++)
        {
            // リストの色見本はグラフの線と同じ色にする。
            _rows.Add(new MeasurementRow(_selection[i], Chart.Series[i].Brush));
        }

        SeriesToggleList.ItemsSource = null;
        SeriesToggleList.ItemsSource = Chart.Series;

        foreach (MeasurementGroup group in _groups)
        {
            group.RefreshHeader();
        }

        SelectionCountText.Text = $"{_selection.Count} / {MeasurementCatalog.MaxSelectableCount}";

        // 次回起動時にも同じ項目を復元できるよう、選択のたびに保存しておく。
        AppSettingsStore.Save(new AppSettings { SelectedMeasurementArgs = [.. _selection.Select(d => d.Arg)] });
    }

    #endregion

    #region グラフ操作

    private void OnSeriesVisibilityChanged(object sender, RoutedEventArgs e)
    {
        // CheckBox の IsChecked が ChartSeries.IsVisible に書き戻された後に再描画する。
        Chart.InvalidateVisual();
    }

    /// <summary>表示時間(秒)の入力欄には数字以外を打てないようにする。</summary>
    private void OnWindowSecondsPreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    private void OnWindowSecondsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyWindowSeconds();
            Keyboard.ClearFocus();
        }
    }

    private void OnWindowSecondsLostFocus(object sender, RoutedEventArgs e) => ApplyWindowSeconds();

    private void ApplyWindowSeconds()
    {
        if (!int.TryParse(WindowSecondsBox.Text, out int seconds) || seconds <= 0)
        {
            seconds = (int)Chart.Window.TotalSeconds;
        }

        seconds = Math.Clamp(seconds, MinWindowSeconds, MaxWindowSeconds);
        WindowSecondsBox.Text = seconds.ToString();
        Chart.Window = TimeSpan.FromSeconds(seconds);
        Chart.InvalidateVisual();
    }

    #endregion

    #region 記録

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        if (_logWriter is null)
        {
            StartRecording();
        }
        else
        {
            StopRecording();
        }
    }

    private void StartRecording()
    {
        string logDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
        try
        {
            _logWriter = CsvSampleWriter.Create(logDirectory, _selection, _currentVin);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"ログファイルを作成できませんでした: {ex.Message}");
            return;
        }

        RecordButton.Content = "■ 記録停止";
        RecordButton.Foreground = (System.Windows.Media.Brush)FindResource("RecordingBrush");
        LogFileText.Text = _logWriter.FilePath;
        RowCountText.Text = "0 行";
    }

    private void StopRecording()
    {
        if (_logWriter is null)
        {
            return;
        }

        string path = _logWriter.FilePath;
        int rows = _logWriter.RowCount;
        _logWriter.Dispose();
        _logWriter = null;

        RecordButton.Content = "● 記録開始";
        RecordButton.Foreground = (System.Windows.Media.Brush)FindResource("ForegroundBrush");
        LogFileText.Text = $"保存しました: {path}";
        RowCountText.Text = $"{rows:N0} 行";
    }

    #endregion

    protected override void OnClosed(EventArgs e)
    {
        StopRecording();
        _sampler?.Dispose();
        base.OnClosed(e);
    }
}
