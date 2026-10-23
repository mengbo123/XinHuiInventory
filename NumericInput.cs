using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;

namespace XingHui;

public static class NumericInput
{
    public static TextBox Create(decimal value = 0)
    {
        var box = new TextBox { Text = value.ToString(CultureInfo.CurrentCulture) };
        Attach(box, value); return box;
    }
    public static void Attach(TextBox box, decimal fallback = 0)
    {
        var original = box.Foreground;
        var hint = fallback == 0 ? "默认为0" : $"默认为{fallback}";
        box.ToolTip = string.IsNullOrWhiteSpace(box.ToolTip?.ToString()) ? hint : box.ToolTip + "；" + hint;
        void Paint() => box.Foreground = decimal.TryParse(box.Text, out var n) && n == 0 ? Brushes.Gray : original;
        if (string.IsNullOrWhiteSpace(box.Text)) box.Text = fallback.ToString(CultureInfo.CurrentCulture);
        box.TextChanged += (_, _) => Paint();
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        box.LostKeyboardFocus += (_, _) => { if (string.IsNullOrWhiteSpace(box.Text)) box.Text = fallback.ToString(CultureInfo.CurrentCulture); };
        Paint();
    }
}
