using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;

namespace XingHui;

public static class UiInputs
{
    public static Grid Search(TextBox box, string hint = "搜索")
    {
        var grid = new Grid { Width = box.Width, Height = 38, Margin = box.Margin };
        box.Margin = new Thickness(0); box.Height = 38; box.VerticalContentAlignment = VerticalAlignment.Center;
        var placeholder = new TextBlock { Text = hint, Foreground = Brushes.Gray, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        grid.Children.Add(box); grid.Children.Add(placeholder);
        void Refresh() => placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Refresh(); Refresh(); return grid;
    }
    public static void WatchSearch(TextBox box, Action apply)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) => { timer.Stop(); apply(); };
        box.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        box.Unloaded += (_, _) => timer.Stop();
        OnEnter(box, () => { timer.Stop(); apply(); });
    }
    public static Grid FilterSummary(UIElement filters, TextBlock summary)
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition()); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(285) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var card = new Border { Padding = new Thickness(16, 8, 12, 8), Margin = new Thickness(12, 0, 0, 8), Background = new SolidColorBrush(Color.FromRgb(229, 241, 237)), CornerRadius = new CornerRadius(8), Child = summary };
        summary.FontSize = 15; summary.FontWeight = FontWeights.SemiBold; summary.Foreground = new SolidColorBrush(Color.FromRgb(25, 83, 73)); summary.TextWrapping = TextWrapping.Wrap;
        layout.Children.Add(filters); layout.Children.Add(card); Grid.SetColumn(card, 1);
        layout.SizeChanged += (_, _) =>
        {
            bool narrow = layout.ActualWidth < 860;
            layout.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(285);
            Grid.SetColumn(card, narrow ? 0 : 1); Grid.SetRow(card, narrow ? 1 : 0);
            card.Margin = new Thickness(narrow ? 0 : 12, 0, 0, 8);
        };
        return layout;
    }
    public static void OnEnter(TextBox box, Action apply)
    {
        box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; apply(); } };
    }
}

public sealed class PasswordInput : UserControl
{
    private readonly PasswordBox hidden = new() { MinHeight = 38, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox shown = new() { MinHeight = 38, Visibility = Visibility.Collapsed, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button eye = new() { Width = 42, Padding = new Thickness(8), Margin = new Thickness(6, 0, 0, 0), FontFamily = new FontFamily("Segoe MDL2 Assets"), Content = "\uE890", ToolTip = "显示密码", IsDefault = false };
    private bool visible, syncing;
    public string Password { get => hidden.Password; set => hidden.Password = value; }
    public int MaxLength { get => hidden.MaxLength; set { hidden.MaxLength = value; shown.MaxLength = value; } }
    public PasswordInput()
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(hidden); grid.Children.Add(shown); Grid.SetColumn(eye, 1); grid.Children.Add(eye); Content = grid;
        AutomationProperties.SetName(eye, "显示密码");
        hidden.PasswordChanged += (_, _) => { if (syncing || !visible) return; syncing = true; shown.Text = hidden.Password; syncing = false; };
        shown.TextChanged += (_, _) => { if (syncing || !visible) return; syncing = true; hidden.Password = shown.Text; syncing = false; };
        void SetVisible(bool value)
        {
            syncing = true; visible = value;
            shown.Text = value ? hidden.Password : "";
            shown.Visibility = value ? Visibility.Visible : Visibility.Collapsed; hidden.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            eye.Content = value ? "\uED1A" : "\uE890"; eye.ToolTip = value ? "隐藏密码" : "显示密码"; AutomationProperties.SetName(eye, eye.ToolTip.ToString()); syncing = false;
        }
        eye.Click += (_, _) => { SetVisible(!visible); if (visible) { shown.Focus(); shown.CaretIndex = shown.Text.Length; } else hidden.Focus(); };
        Loaded += (_, _) => { var owner = Window.GetWindow(this); if (owner is not null) { owner.Deactivated += Hide; owner.Closed += Clear; } };
        void Hide(object? sender, EventArgs e) => SetVisible(false);
        void Clear(object? sender, EventArgs e) { SetVisible(false); hidden.Clear(); if (sender is Window owner) { owner.Deactivated -= Hide; owner.Closed -= Clear; } }
        GotKeyboardFocus += (_, e) => { if (ReferenceEquals(e.OriginalSource, this)) { if (visible) shown.Focus(); else hidden.Focus(); } };
        Focusable = true;
    }
}
