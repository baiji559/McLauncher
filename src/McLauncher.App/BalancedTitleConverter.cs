using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace McLauncher.App;

/// <summary>Balances a two-line display title without changing its underlying name or identifier.</summary>
public sealed class BalancedTitleConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var text = values.ElementAtOrDefault(0) as string ?? "";
        if (values.ElementAtOrDefault(1) is not double width || !double.IsFinite(width) || width <= 0
            || values.ElementAtOrDefault(2) is not TextBlock label || text.Contains('\n')) return text;

        width -= label.Padding.Left + label.Padding.Right;
        if (width <= 0) return text;
        var typeface = new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(label).PixelsPerDip;
        double Measure(string value) => new FormattedText(value, culture, label.FlowDirection,
            typeface, label.FontSize, Brushes.White, pixelsPerDip).WidthIncludingTrailingWhitespace;
        if (Measure(text) <= width) return text;

        var boundaries = StringInfo.ParseCombiningCharacters(text).Skip(1).ToArray();
        string? ChooseBreak(bool wordsOnly)
        {
            var bestScore = double.MaxValue;
            string? best = null;
            foreach (var index in boundaries)
            {
                if (wordsOnly && !IsWordBoundary(text, index)) continue;
                var left = text[..index].TrimEnd();
                var right = text[index..].TrimStart();
                if (left.Length == 0 || right.Length == 0) continue;
                var leftWidth = Measure(left);
                var rightWidth = Measure(right);
                if (leftWidth > width || rightWidth > width) continue;
                var score = Math.Abs(leftWidth - rightWidth);
                if (score >= bestScore) continue;
                bestScore = score;
                best = left + "\n" + right;
            }
            return best;
        }

        // Prefer complete words; fall back to grapheme boundaries for long unspaced names.
        return ChooseBreak(true) ?? ChooseBreak(false) ?? text;
    }

    private static bool IsWordBoundary(string text, int index) =>
        char.IsWhiteSpace(text[index - 1]) || char.IsWhiteSpace(text[index])
        || "-_/·".Contains(text[index - 1]) || text[index] is '[' or '('
        || text[index - 1] is >= '\u2E80' and <= '\u9FFF';

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
