using System.Globalization;

namespace MSS60_DataLogger;

/// <summary>
/// 測定値の画面表示用フォーマット。値の大きさに応じて小数点以下の桁数を変える
/// (回転数のような大きい値は整数、ラムダのような小さい値は小数第3位まで)。
/// 数値リストとグラフのカーソル表示で共通して使う。
/// </summary>
public static class MeasurementFormat
{
    public static string Format(double value)
    {
        double magnitude = Math.Abs(value);
        string format = magnitude switch
        {
            >= 1000 => "0",
            >= 100 => "0.#",
            >= 10 => "0.##",
            _ => "0.###",
        };

        return value.ToString(format, CultureInfo.InvariantCulture);
    }
}
