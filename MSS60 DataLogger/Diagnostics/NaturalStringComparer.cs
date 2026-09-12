using System.Globalization;

namespace MSS60_DataLogger.Diagnostics;

/// <summary>
/// 文字列中の数字部分を数値として比較する自然順ソート。
/// 単純な文字列比較だと「気筒1」「気筒10」「気筒2」の順になってしまうため、
/// 数字の連続部分だけは値として比較し「気筒1」「気筒2」…「気筒10」の順にする。
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    private static readonly CompareInfo JaCompareInfo = CultureInfo.GetCultureInfo("ja-JP").CompareInfo;

    private NaturalStringComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }
        if (x is null)
        {
            return -1;
        }
        if (y is null)
        {
            return 1;
        }

        List<string> tokensX = Tokenize(x);
        List<string> tokensY = Tokenize(y);

        int count = Math.Min(tokensX.Count, tokensY.Count);
        for (int i = 0; i < count; i++)
        {
            string a = tokensX[i];
            string b = tokensY[i];

            if (char.IsDigit(a[0]) && char.IsDigit(b[0]))
            {
                if (long.TryParse(a, out long numA) && long.TryParse(b, out long numB) && numA != numB)
                {
                    return numA.CompareTo(numB);
                }
            }

            int result = JaCompareInfo.Compare(a, b);
            if (result != 0)
            {
                return result;
            }
        }

        return tokensX.Count.CompareTo(tokensY.Count);
    }

    /// <summary>数字の連続とそれ以外の連続とで交互に分割する。</summary>
    private static List<string> Tokenize(string s)
    {
        List<string> tokens = [];
        int i = 0;
        while (i < s.Length)
        {
            int start = i;
            bool isDigit = char.IsDigit(s[i]);
            while (i < s.Length && char.IsDigit(s[i]) == isDigit)
            {
                i++;
            }
            tokens.Add(s[start..i]);
        }
        return tokens;
    }
}
