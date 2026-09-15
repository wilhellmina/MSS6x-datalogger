namespace MSS60_DataLogger.Settings;

/// <summary>アプリ終了後も保持しておく設定。</summary>
public sealed class AppSettings
{
    /// <summary>前回チェックしていた測定値の短縮名(Arg)一覧。</summary>
    public List<string> SelectedMeasurementArgs { get; set; } = [];

    /// <summary>お気に入りに登録済みの測定値の短縮名(Arg)一覧。</summary>
    public List<string> FavoriteMeasurementArgs { get; set; } = [];

    /// <summary>UI の表示言語(<see cref="Localization.UiLanguage"/> の名前)。未設定は標準語。</summary>
    public string Language { get; set; } = nameof(Localization.UiLanguage.Standard);
}
