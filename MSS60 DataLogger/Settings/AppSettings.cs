namespace MSS60_DataLogger.Settings;

/// <summary>アプリ終了後も保持しておく設定。</summary>
public sealed class AppSettings
{
    /// <summary>前回チェックしていた測定値の短縮名(Arg)一覧。</summary>
    public List<string> SelectedMeasurementArgs { get; set; } = [];
}
