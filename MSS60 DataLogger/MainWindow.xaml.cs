using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
    private readonly ObservableCollection<SelectableMeasurement> _favorites = [];

    private EcuSampler? _sampler;
    private CsvSampleWriter? _logWriter;
    private bool _suppressSelectionUpdates;
    private bool _suppressFavoriteUpdates;
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
        AppSettings settings = AppSettingsStore.Load();
        HashSet<string> defaults = settings.SelectedMeasurementArgs.Count > 0
            ? [.. settings.SelectedMeasurementArgs]
            : [.. MeasurementCatalog.DefaultSelection];
        HashSet<string> favorites = [.. settings.FavoriteMeasurementArgs];

        _selectable.AddRange(MeasurementCatalog.All.Select(d =>
            new SelectableMeasurement(d, OnMeasurementToggled, OnMeasurementFavoriteToggled)));

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
        FavoritesList.ItemsSource = _favorites;

        // 既定の項目にチェックを入れ、お気に入りを復元する(この間は再構築を 1 回にまとめる)
        _suppressSelectionUpdates = true;
        _suppressFavoriteUpdates = true;
        foreach (SelectableMeasurement item in _selectable)
        {
            if (defaults.Contains(item.Definition.Arg))
            {
                item.IsSelected = true;
            }

            if (favorites.Contains(item.Definition.Arg))
            {
                item.IsFavorite = true;
                _favorites.Add(item);
            }
        }

        _suppressSelectionUpdates = false;
        _suppressFavoriteUpdates = false;

        _groups[0].IsExpanded = true; // 「基本」だけ最初から開いておく
        UpdateFavoritesEmptyState();
        RebuildSelection();
    }

    #region 接続

    private void OnRefreshPortsClick(object sender, RoutedEventArgs e) => RefreshPortList();

    private void RefreshPortList()
    {
        string? previousPort = (ComPortCombo.SelectedItem as ComPortInfo)?.PortName;
        IReadOnlyList<ComPortInfo> ports = GetAvailablePorts();

        ComPortCombo.ItemsSource = ports;
        ComPortCombo.SelectedItem = ports.FirstOrDefault(p => p.PortName == previousPort) ?? ports.FirstOrDefault();
    }

    /// <summary>接続可能な COM ポートを、分かれば製造元名付きで列挙する。
    /// WMI(Win32_PnPEntity)の Caption に "(COM3)" のような形で番号が入っているので、
    /// そこから <see cref="SerialPort.GetPortNames"/> の結果と突き合わせる。
    /// WMI が使えない環境でも、ポート番号だけは列挙を続ける。</summary>
    private static IReadOnlyList<ComPortInfo> GetAvailablePorts()
    {
        HashSet<string> remaining = [.. SerialPort.GetPortNames()];
        List<ComPortInfo> ports = [];

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Caption, Manufacturer FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'");
            using ManagementObjectCollection devices = searcher.Get();

            foreach (ManagementBaseObject device in devices)
            {
                string caption = device["Caption"] as string ?? string.Empty;
                Match match = Regex.Match(caption, @"\((COM\d+)\)");
                if (!match.Success || !remaining.Remove(match.Groups[1].Value))
                {
                    continue;
                }

                string portName = match.Groups[1].Value;
                string manufacturer = (device["Manufacturer"] as string)?.Trim() ?? string.Empty;
                string displayName = manufacturer.Length == 0
                    ? portName
                    : $"{portName} ({manufacturer})";

                ports.Add(new ComPortInfo(portName, displayName));
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            // WMI が使えない場合でも、ポート番号だけの一覧にフォールバックする(下の残り分で処理)。
        }

        // WMI 側で製造元が拾えなかった分は、番号だけで追加する。
        foreach (string portName in remaining)
        {
            ports.Add(new ComPortInfo(portName, portName));
        }

        return [.. ports.OrderBy(p => p.PortName, StringComparer.OrdinalIgnoreCase)];
    }

    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (IsConnected)
        {
            Disconnect("切断しました。");
            return;
        }

        if (ComPortCombo.SelectedItem is not ComPortInfo portInfo)
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

        StartSampler(portInfo.PortName);
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

        if (ComPortCombo.SelectedItem is not ComPortInfo portInfo)
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

            StartSampler(portInfo.PortName);
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
            // イベント購読を外してから Dispose する。外さないと、Faulted で Disconnect
            // (このメソッド)を呼んだ直後にワーカースレッドの finally 節が発火する
            // StatusChanged("切断しました。") が届いてしまい、Faulted の本来のメッセージが
            // 画面上で即座に上書きされてしまう。
            sampler.DetachEvents();

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

    /// <summary>カテゴリ見出しのチェックボックス。このカテゴリで選択中の項目をまとめて解除する。
    /// 全選択は同時記録数の上限超過などで意図しない挙動になりやすいため、一斉解除の用途に絞ってある
    /// (選択済みが 1 件も無いときは <see cref="MeasurementGroup.HasAnySelected"/> により操作不可にしている)。</summary>
    private void OnCategoryClearAllClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MeasurementGroup group })
        {
            return;
        }

        if (IsRecording)
        {
            SetStatus("記録中は項目を変更できません。いったん記録を停止してください。");
            return;
        }

        foreach (SelectableMeasurement item in group.AllItems)
        {
            item.IsSelected = false;
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

        SaveSettings();
    }

    /// <summary>選択項目・お気に入りの現在の状態を、次回起動時に復元できるよう保存する。</summary>
    private void SaveSettings() => AppSettingsStore.Save(new AppSettings
    {
        SelectedMeasurementArgs = [.. _selection.Select(d => d.Arg)],
        FavoriteMeasurementArgs = [.. _favorites.Select(f => f.Definition.Arg)],
    });

    #endregion

    #region お気に入り

    /// <summary>各項目の☆ボタン。記録用の選択とは独立にお気に入り登録だけを切り替える。</summary>
    private void OnFavoriteToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SelectableMeasurement item })
        {
            item.IsFavorite = !item.IsFavorite;
        }
    }

    /// <summary>お気に入りの追加・削除に応じて一覧を更新し、保存する。</summary>
    private void OnMeasurementFavoriteToggled(SelectableMeasurement item)
    {
        if (_suppressFavoriteUpdates)
        {
            return;
        }

        if (item.IsFavorite)
        {
            _favorites.Add(item);
        }
        else
        {
            _favorites.Remove(item);
        }

        UpdateFavoritesEmptyState();
        SaveSettings();
    }

    /// <summary>「お気に入り」タブの案内文を、お気に入りが 0 件かどうかで出し分ける。</summary>
    private void UpdateFavoritesEmptyState() =>
        FavoritesEmptyText.Visibility = _favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>「お気に入り」タブの一斉解除ボタン。カテゴリの一斉解除と同じく、お気に入り全体を
    /// 選択状態にする操作は用意しない(上限超過などの意図しない挙動を避けるため)。</summary>
    private void OnFavoritesClearSelectionClick(object sender, RoutedEventArgs e)
    {
        if (IsRecording)
        {
            SetStatus("記録中は項目を変更できません。いったん記録を停止してください。");
            return;
        }

        foreach (SelectableMeasurement item in _favorites.ToArray())
        {
            item.IsSelected = false;
        }
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
