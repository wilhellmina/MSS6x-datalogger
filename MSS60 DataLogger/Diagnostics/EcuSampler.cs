using System.Globalization;
using EdiabasLib;

namespace MSS60_DataLogger.Diagnostics;

/// <summary>
/// バックグラウンドスレッドで DME に接続し、測定値ブロックを繰り返し読み出す。
/// EDIABAS の呼び出しは同期ブロッキングなので、専用スレッドで回して結果をイベントで通知する。
/// </summary>
public sealed class EcuSampler : IDisposable
{
    private const string JobName = "STATUS_MESSWERTBLOCK_LESEN";
    private const string IdentityJobName = "AIF_LESEN";
    private const string SgbdName = "MSS60";

    private readonly string _comPort;
    private readonly string _ecuPath;
    private readonly IReadOnlyList<MeasurementDefinition> _selection;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _worker;

    public EcuSampler(string comPort, string ecuPath, IReadOnlyList<MeasurementDefinition> selection)
    {
        if (selection.Count is 0 or > MeasurementCatalog.MaxSelectableCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selection),
                $"測定値は 1〜{MeasurementCatalog.MaxSelectableCount} 項目で指定してください。");
        }

        _comPort = comPort;
        _ecuPath = ecuPath;
        _selection = selection;
    }

    /// <summary>1 サンプル読み出すたびに発火する。呼び出しはワーカースレッド上。</summary>
    public event Action<SampleSnapshot>? SampleReceived;

    /// <summary>接続直後、VIN・SW バージョンが読み取れたときに 1 回だけ発火する。呼び出しはワーカースレッド上。</summary>
    public event Action<EcuIdentity>? IdentityReceived;

    /// <summary>状態メッセージ(接続中・エラーなど)。呼び出しはワーカースレッド上。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>復帰不能なエラーで停止したときに発火する。呼び出しはワーカースレッド上。</summary>
    public event Action<string>? Faulted;

    public void Start()
    {
        if (_worker is not null)
        {
            throw new InvalidOperationException("すでに開始しています。");
        }

        _worker = new Thread(Run)
        {
            Name = "EcuSampler",
            IsBackground = true,
        };
        _worker.Start();
    }

    /// <summary>
    /// イベント購読を外す。停止処理中の通知で画面が乱れないよう、Dispose の前に呼ぶ。
    /// </summary>
    public void DetachEvents()
    {
        SampleReceived = null;
        IdentityReceived = null;
        StatusChanged = null;
        Faulted = null;
    }

    /// <summary>ワーカースレッドの終了(= COM ポートの解放)まで待ってから戻る。</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _worker?.Join(TimeSpan.FromSeconds(5));
        _cts.Dispose();
    }

    private void Run()
    {
        CancellationToken token = _cts.Token;
        string argList = string.Join(';', _selection.Select(d => d.Arg));

        EdiabasNet? ediabas = null;
        try
        {
            StatusChanged?.Invoke($"{_comPort} に接続しています…");

            ediabas = new EdiabasNet();
            var obdInterface = new EdInterfaceObd { ComPort = _comPort };
            ediabas.EdInterfaceClass = obdInterface;
            ediabas.SetConfigProperty("EcuPath", _ecuPath);
            ediabas.SetConfigProperty("IfhTrace", "0");
            ediabas.SetConfigProperty("ApiTrace", "0");

            // 拡張子の解決は ResolveSgbdFile に任せる(SgbdFileName への直接代入では .prg が付かない)。
            ediabas.ResolveSgbdFile(SgbdName);

            // 測定値の連続読み出しを始める前に、VIN と SW バージョンを 1 回だけ読んでおく。
            // 読めなくても致命的ではないので、失敗しても測定は続行する。
            if (TryReadIdentity(ediabas) is { } identity)
            {
                IdentityReceived?.Invoke(identity);
            }

            // MODE=JA は「ブロック消去 → 定義 → 読み出し」で 3 往復、MODE=NEIN は読み出しのみの 1 往復。
            // 最初の 1 回だけ JA でブロックを定義し、以降は NEIN で読むことでサンプリングレートを稼ぐ。
            bool blockDefined = false;
            int consecutiveFailures = 0;

            while (!token.IsCancellationRequested)
            {
                ediabas.ArgString = (blockDefined ? "NEIN;" : "JA;") + argList;

                try
                {
                    ediabas.ExecuteJob(JobName);
                }
                catch (EdiabasNet.EdiabasNetException ex)
                {
                    if (++consecutiveFailures >= 3)
                    {
                        Faulted?.Invoke($"通信に失敗しました: {ex.Message}");
                        return;
                    }

                    StatusChanged?.Invoke($"再試行しています… ({ex.Message})");
                    blockDefined = false;
                    Thread.Sleep(200);
                    continue;
                }

                if (!TryReadResultSet(ediabas, out var resultSet))
                {
                    blockDefined = false;
                    Thread.Sleep(100);
                    continue;
                }

                if (GetJobStatus(resultSet) is { } jobStatus && jobStatus != "OKAY")
                {
                    // ブロックが ECU 側で失われた場合など。JA で定義し直す。
                    if (++consecutiveFailures >= 5)
                    {
                        Faulted?.Invoke($"ジョブが繰り返し失敗しました (JOB_STATUS={jobStatus})。");
                        return;
                    }

                    blockDefined = false;
                    continue;
                }

                if (!blockDefined)
                {
                    blockDefined = true;
                    StatusChanged?.Invoke("接続しました。");
                }

                consecutiveFailures = 0;
                SampleReceived?.Invoke(new SampleSnapshot(DateTime.Now, ExtractValues(resultSet)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Faulted?.Invoke($"接続に失敗しました: {ex.Message}");
        }
        finally
        {
            ediabas?.Dispose();
            StatusChanged?.Invoke("切断しました。");
        }
    }

    /// <summary>
    /// AIF_LESEN(ユーザー情報フィールド)から VIN とプログラム番号を読み取る。
    /// AIF_FG_NR_LANG が 17 桁の完全な車台番号、AIF_PROG_NR の末尾 4 桁が SW バージョン表記(例: "240E")。
    /// </summary>
    private static EcuIdentity? TryReadIdentity(EdiabasNet ediabas)
    {
        try
        {
            ediabas.ArgString = string.Empty;
            ediabas.ExecuteJob(IdentityJobName);

            if (!TryReadResultSet(ediabas, out var resultSet))
            {
                return null;
            }

            string? vin = resultSet.TryGetValue("AIF_FG_NR_LANG", out EdiabasNet.ResultData? vinData)
                ? vinData.OpData as string
                : null;
            string? progNr = resultSet.TryGetValue("AIF_PROG_NR", out EdiabasNet.ResultData? progData)
                ? progData.OpData as string
                : null;

            if (string.IsNullOrWhiteSpace(vin) && string.IsNullOrWhiteSpace(progNr))
            {
                return null;
            }

            string swVersion = !string.IsNullOrWhiteSpace(progNr) && progNr.Length >= 4
                ? progNr[^4..]
                : progNr ?? "?";

            return new EcuIdentity(vin ?? "?", swVersion);
        }
        catch (EdiabasNet.EdiabasNetException)
        {
            return null; // VIN が読めなくても測定自体は続行する
        }
    }

    /// <summary>実データが入っている ResultSet(先頭はジョブのメタ情報)を取り出す。</summary>
    private static bool TryReadResultSet(
        EdiabasNet ediabas,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dictionary<string, EdiabasNet.ResultData>? resultSet)
    {
        List<Dictionary<string, EdiabasNet.ResultData>> sets = ediabas.ResultSets;
        resultSet = sets.Count >= 2 ? sets[1] : null;
        return resultSet is not null;
    }

    private static string? GetJobStatus(Dictionary<string, EdiabasNet.ResultData> resultSet) =>
        resultSet.TryGetValue("JOB_STATUS", out EdiabasNet.ResultData? data) ? data.OpData as string : null;

    private double?[] ExtractValues(Dictionary<string, EdiabasNet.ResultData> resultSet)
    {
        var values = new double?[_selection.Count];
        for (int i = 0; i < _selection.Count; i++)
        {
            values[i] = resultSet.TryGetValue(_selection[i].ResultName, out EdiabasNet.ResultData? data)
                ? ToDouble(data.OpData)
                : null;
        }

        return values;
    }

    private static double? ToDouble(object? opData) => opData switch
    {
        null => null,
        double d => d,
        long l => l,
        int i => i,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
        _ => null,
    };
}
