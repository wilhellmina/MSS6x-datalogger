using System.Globalization;
using System.IO;
using System.Text;
using MSS60_DataLogger.Diagnostics;

namespace MSS60_DataLogger.Logging;

/// <summary>
/// 測定値を CSV に追記する。Excel で開けるよう UTF-8 BOM 付きで書き出す。
/// </summary>
public sealed class CsvSampleWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    private CsvSampleWriter(StreamWriter writer, string filePath)
    {
        _writer = writer;
        FilePath = filePath;
    }

    public string FilePath { get; }

    public int RowCount { get; private set; }

    /// <summary>
    /// ログフォルダーに日時入りのファイルを新規作成する。
    /// VIN が分かっていれば末尾 7 桁(車台番号の個体識別部分)をファイル名に付け足す。
    /// </summary>
    public static CsvSampleWriter Create(string logDirectory, IReadOnlyList<MeasurementDefinition> selection, string? vin = null)
    {
        Directory.CreateDirectory(logDirectory);
        string fileName = $"MSS60_log_{DateTime.Now:yyyyMMdd_HHmmss}{FormatVinSuffix(vin)}.csv";
        string filePath = Path.Combine(logDirectory, fileName);

        var writer = new StreamWriter(filePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // 1 行目: 人が読むヘッダー(項目名と単位)、2 行目: 機械処理用の短縮名
        IEnumerable<string> header = selection.Select(d =>
            string.IsNullOrEmpty(d.Unit) ? d.DisplayName : $"{d.DisplayName} [{d.Unit}]");
        writer.WriteLine("時刻," + string.Join(',', header.Select(Escape)));
        writer.WriteLine("timestamp," + string.Join(',', selection.Select(d => Escape(d.Arg))));
        writer.Flush();

        return new CsvSampleWriter(writer, filePath);
    }

    public void Write(SampleSnapshot sample)
    {
        lock (_lock)
        {
            var line = new StringBuilder();
            line.Append(sample.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            foreach (double? value in sample.Values)
            {
                line.Append(',');
                if (value.HasValue)
                {
                    line.Append(value.Value.ToString("0.####", CultureInfo.InvariantCulture));
                }
            }

            _writer.WriteLine(line);
            RowCount++;

            // 車内で電源が落ちても直前までのデータが残るように、こまめにフラッシュする。
            if (RowCount % 20 == 0)
            {
                _writer.Flush();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.Flush();
            _writer.Dispose();
        }
    }

    /// <summary>VIN の末尾 7 桁(例: "PY30503")をファイル名用に "_PY30503" の形で返す。取得できていなければ空文字。</summary>
    private static string FormatVinSuffix(string? vin)
    {
        if (string.IsNullOrWhiteSpace(vin) || vin.Length < 7)
        {
            return string.Empty;
        }

        string tail = vin[^7..];
        return tail.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? string.Empty : $"_{tail}";
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
