namespace MSS60_DataLogger.Localization;

/// <summary>アプリ内で選べる「言語」(実際には標準語と大阪弁の2バリエーション)。</summary>
public enum UiLanguage
{
    Standard,
    Osaka,
}

/// <summary>現在有効な <see cref="IUiText"/> を保持し、切り替えを一箇所に集約する。
/// UI 側は表示のたびに <see cref="Current"/> を読みに行く(値をキャッシュしない)ことで、
/// 切り替え後は次の描画から自然に新しい言語になる。</summary>
public static class UiText
{
    public static IUiText Current { get; private set; } = StandardUiText.Instance;

    public static UiLanguage CurrentLanguage { get; private set; } = UiLanguage.Standard;

    /// <summary>言語が切り替わったときに発火する。画面側はこれを購読して、
    /// 動的に設定しているテキスト(ボタンの文言など)を再適用する。</summary>
    public static event Action? Changed;

    public static void SetLanguage(UiLanguage language)
    {
        if (CurrentLanguage == language)
        {
            return;
        }

        CurrentLanguage = language;
        Current = language == UiLanguage.Osaka ? OsakaUiText.Instance : StandardUiText.Instance;
        Changed?.Invoke();
    }
}
