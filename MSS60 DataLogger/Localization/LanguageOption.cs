namespace MSS60_DataLogger.Localization;

/// <summary>言語プルダウンに表示する 1 件分。</summary>
/// <param name="Value">実際に切り替える言語。</param>
/// <param name="DisplayName">プルダウンに表示する文字列。</param>
public sealed record LanguageOption(UiLanguage Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}
