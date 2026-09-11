namespace MSS60_DataLogger.Diagnostics;

/// <summary>
/// AIF_LESEN(ユーザー情報フィールド読み出し)から得た車両の識別情報。
/// </summary>
/// <param name="Vin">車台番号(VIN、17桁)。</param>
/// <param name="SoftwareVersion">書き込まれているプログラム番号の末尾(例: "240E")。</param>
public sealed record EcuIdentity(string Vin, string SoftwareVersion);
