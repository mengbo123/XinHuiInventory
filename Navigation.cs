using System.Windows;
using System.Windows.Controls;

namespace XingHui;

public static class Navigation
{
    public static void AddBack(Window window, Panel header)
    {
        var back = new Button { Content = "← 返回", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12), ToolTip = "返回上一个窗口；未提交的表单不保存" };
        back.Click += (_, _) => window.Close(); header.Children.Insert(0, back);
    }
}
