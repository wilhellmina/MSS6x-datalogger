using System.IO;
using System.Text.Json;

namespace MSS60_DataLogger.Settings;

/// <summary>
/// アプリ設定を実行ファイルと同じフォルダの settings.json に保存・読み込みする。
/// 配布先でどこに展開しても、その場に設定が残る(インストーラー不要のポータブル運用を想定)。
/// 開発中に dotnet clean 等で bin フォルダごと消すと、この設定も一緒に消える点には注意。
/// </summary>
public static class AppSettingsStore
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "settings.json");

    /// <summary>
    /// 保存済み設定を読み込む。ファイルが無い・壊れている場合は既定値(空)を返す。
    /// 呼び出し側で読み込み失敗を気にしなくていいよう、例外は投げない。
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 読めなくても既定値にフォールバックするだけで、アプリの起動は妨げない。
        }

        return new AppSettings();
    }

    /// <summary>設定を保存する。書き込みに失敗しても(読み取り専用フォルダ等)アプリの動作は継続する。</summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
