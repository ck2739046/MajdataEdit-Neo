using System;
using System.Globalization;
using System.Numerics;

namespace MajdataEdit_Neo.Utils;

public static class BpmMeasureCalculator
{
    public readonly struct Fraction
    {
        public readonly BigInteger Num;
        public readonly BigInteger Den;

        public Fraction(BigInteger num, BigInteger den)
        {
            if (den == 0)
                den = 1;
            if (den < 0)
            {
                num = -num;
                den = -den;
            }

            var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(num), den);
            if (gcd > 1)
            {
                num /= gcd;
                den /= gcd;
            }

            Num = num;
            Den = den;
        }

        public static Fraction Zero => new(0, 1);

        public static Fraction operator +(in Fraction left, in Fraction right)
            => new(left.Num * right.Den + right.Num * left.Den, left.Den * right.Den);

        public BigInteger Whole => Num >= 0 ? Num / Den : BigInteger.Zero;

        public bool IsZero => Num.IsZero;
    }

    public static string FormatMeasure(Fraction position)
    {
        var whole = position.Whole;
        var fraction = new Fraction(position.Num - whole * position.Den, position.Den);

        if (fraction.IsZero)
            return $"{whole}.0";

        if (fraction.Den == 2 || fraction.Den == 4)
        {
            var quarter = fraction.Num * 4 / fraction.Den;
            var suffix = quarter == 1 ? "25" : quarter == 2 ? "5" : quarter == 3 ? "75" : null;
            if (suffix != null)
                return $"{whole}.{suffix}";
        }

        return $"{whole} + {fraction.Num}/{fraction.Den}";
    }

    public static Fraction ComputeMeasureAtOffset(string text, int offset)
    {
        var position = Fraction.Zero;
        long currentDiv = 4;
        var limit = Math.Clamp(offset, 0, text.Length);
        var i = 0;

        while (i < limit)
        {
            var ch = text[i];

            if (ch == '|' && i + 1 < text.Length && text[i + 1] == '|')
            {
                var lineEnd = text.IndexOf('\n', i);
                i = lineEnd == -1 ? limit : Math.Min(lineEnd, limit);
                continue;
            }

            if (ch == '{')
            {
                var end = text.IndexOf('}', i);
                if (end != -1 && end < limit)
                {
                    var value = text.AsSpan(i + 1, end - i - 1).Trim();
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var division) && division != 0)
                        currentDiv = division;
                    i = end + 1;
                    continue;
                }
            }

            if (ch == '(')
            {
                var end = text.IndexOf(')', i);
                if (end != -1 && end < limit)
                {
                    var value = text.AsSpan(i + 1, end - i - 1).Trim();
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm) &&
                        !double.IsNaN(bpm) && !double.IsInfinity(bpm))
                    {
                        i = end + 1;
                        continue;
                    }
                }
            }

            if (ch == ',')
                position += new Fraction(1, currentDiv);

            i++;
        }

        return position;
    }
}
