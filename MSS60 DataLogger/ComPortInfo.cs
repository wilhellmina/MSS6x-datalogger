namespace MSS60_DataLogger;

/// <summary>
/// COM ポートのプルダウンに表示する 1 件分。<see cref="DisplayName"/> にはポート番号に加えて
/// (分かれば)製造元名を添える。例: "COM3 (FTDI)"。デバイスマネージャーを開かなくても
/// どのケーブルがどのポートに繋がっているか判別できるようにするため。
/// </summary>
/// <param name="PortName">実際に接続に使う名前(例: "COM3")。</param>
/// <param name="DisplayName">プルダウンに表示する文字列。</param>
public sealed record ComPortInfo(string PortName, string DisplayName)
{
    public override string ToString() => DisplayName;
}
