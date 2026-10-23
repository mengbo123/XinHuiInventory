using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace XingHui;

public sealed class PagedTable : DockPanel
{
    private readonly DataGrid grid = new() { RowHeight = 36, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow };
    private readonly TextBlock caption = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
    private readonly Button previous = new() { Content = "上一页" };
    private readonly Button next = new() { Content = "下一页" };
    private List<object> rows = [];
    private int page;
    private int pageSize;
    private readonly TextBox size = new() { Width = 56, Height = 36, TextAlignment = TextAlignment.Center, ToolTip = "每页条数（1～1000），输入后按回车" };
    private readonly TextBox jump = new() { Width = 52, Height = 36, TextAlignment = TextAlignment.Center, ToolTip = "目标页码，输入后按回车" };
    private readonly TextBlock pageTotal = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 12, 0) };
    private readonly HashSet<object> selected = [];
    private bool refreshing;
    private readonly TextBlock selectedCaption = new() { VerticalAlignment = VerticalAlignment.Center };
    public object? SelectedItem => grid.SelectedItem;
    public IReadOnlyList<object> SelectedItems => rows.Where(selected.Contains).ToList();
    public PagedTable(int pageSize, params (string Label, string Property)[] columns)
    {
        this.pageSize = pageSize;
        var footer = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        size.Text = pageSize.ToString(); jump.Text = "1";
        NumericInput.Attach(size, pageSize); NumericInput.Attach(jump, 1);
        UiInputs.OnEnter(size, () => { if (int.TryParse(size.Text, out var n) && n >= 1 && n <= 1000) { this.pageSize = n; page = 0; Refresh(); } else { MessageBox.Show("每页条数请输入 1～1000 的整数。"); size.Text = this.pageSize.ToString(); } });
        UiInputs.OnEnter(jump, () => { int pages = Math.Max(1, (rows.Count + this.pageSize - 1) / this.pageSize); if (int.TryParse(jump.Text, out var n) && n >= 1 && n <= pages) { page = n - 1; Refresh(); } else { MessageBox.Show($"页码请输入 1～{pages}。"); jump.Text = (page + 1).ToString(); } });
        var paging = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        paging.Children.Add(previous);
        paging.Children.Add(new TextBlock { Text = "第", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) }); paging.Children.Add(jump); paging.Children.Add(pageTotal); paging.Children.Add(caption);
        paging.Children.Add(new TextBlock { Text = "每页", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }); paging.Children.Add(size);
        paging.Children.Add(new TextBlock { Text = "条", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 12, 0) }); paging.Children.Add(next); footer.Children.Add(paging);
        SetDock(footer, Dock.Bottom); Children.Add(footer);
        var selectPage = new Button { Content = "本页全选 / 取消" };
        selectPage.Click += (_, _) => { if (grid.SelectedItems.Count == grid.Items.Count) grid.UnselectAll(); else grid.SelectAll(); };
        var clear = new Button { Content = "清除选择" }; clear.Click += (_, _) => { selected.Clear(); grid.UnselectAll(); UpdateSelection(); };
        footer.Children.Add(selectPage); footer.Children.Add(clear); footer.Children.Add(selectedCaption);
        grid.SelectionChanged += (_, e) => { if (refreshing) return; foreach (var item in e.RemovedItems) selected.Remove(item); foreach (var item in e.AddedItems) selected.Add(item); UpdateSelection(); };
        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        check.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsSelected") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1), Mode = BindingMode.TwoWay });
        grid.Columns.Add(new DataGridTemplateColumn { Header = "选择", Width = 55, CellTemplate = new DataTemplate { VisualTree = check } });
        foreach (var (label, property) in columns) grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(property) { StringFormat = property == "Time" ? "yyyy-MM-dd HH:mm:ss" : null, TargetNullValue = property is "MoneyAmount" or "UnitPrice" or "ExpectedAmount" ? "未记录" : "" }, Width = DataGridLength.Auto });
        Children.Add(grid); previous.Click += (_, _) => { page--; Refresh(); }; next.Click += (_, _) => { page++; Refresh(); };
    }
    public void FilterHeader(string property, string label, IReadOnlyList<string> values, Action<string> changed)
    {
        var column = grid.Columns.OfType<DataGridTextColumn>().Single(c => (c.Binding as Binding)?.Path.Path == property);
        column.CanUserSort = false;
        var button = new Button { Content = label + " ▾", Background = Brushes.Transparent, Foreground = Brushes.Teal, Padding = new Thickness(4), Margin = new Thickness(0), ToolTip = "点击筛选；选择“" + values[0] + "”取消此列筛选" };
        string selected = values[0];
        button.Click += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var value in values)
            {
                var item = new MenuItem { Header = value, IsCheckable = true, IsChecked = value == selected };
                item.Click += (_, args) => { args.Handled = true; selected = value; button.Content = selected == values[0] ? label + " ▾" : label + "：" + value + " ▾"; button.FontWeight = selected == values[0] ? FontWeights.Normal : FontWeights.Bold; changed(value); };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        column.Header = button;
    }
    public void HideColumns(params string[] properties)
    {
        foreach (var column in grid.Columns.OfType<DataGridTextColumn>())
            if (properties.Contains((column.Binding as Binding)?.Path.Path)) column.Visibility = Visibility.Collapsed;
    }
    public void SetRows(IEnumerable<object> values) { rows = values.ToList(); selected.Clear(); page = 0; Refresh(); }
    private void UpdateSelection() => selectedCaption.Text = $"已选 {selected.Count} 条（跨页保留）";
    private void Refresh()
    {
        int pages = Math.Max(1, (rows.Count + pageSize - 1) / pageSize); page = Math.Clamp(page, 0, pages - 1);
        refreshing = true;
        grid.ItemsSource = rows.Skip(page * pageSize).Take(pageSize).ToList();
        foreach (var item in grid.Items) if (selected.Contains(item)) grid.SelectedItems.Add(item);
        refreshing = false; UpdateSelection();
        size.Text = pageSize.ToString(); jump.Text = (page + 1).ToString(); pageTotal.Text = $"/ {pages} 页";
        caption.Text = $"共 {rows.Count} 条";
        previous.IsEnabled = page > 0; next.IsEnabled = page + 1 < pages;
    }
}

public sealed class ExportWindow : FormWindow
{
    public DateTime? From { get; private set; }
    public DateTime? To { get; private set; }
    public ExportWindow(bool logsOnly = false) : base("导出选项", 560)
    {
        Heading(logsOnly ? "导出操作记录" : "导出选中商品", logsOnly ? "只导出所选日期范围内的日志，包含结束日期当天。" : "仅导出勾选的商品及图片（包括跨页勾选）；可附带这些商品指定日期范围内的日志。");
        var include = new CheckBox { Content = "附带操作记录", IsChecked = logsOnly, IsEnabled = !logsOnly, Margin = new Thickness(0, 8, 0, 12) };
        Body.Children.Add(include);
        var start = Field("日志起始日期", new DatePicker { SelectedDate = DateTime.Today.AddDays(-30), IsEnabled = logsOnly });
        var end = Field("日志结束日期（含当天）", new DatePicker { SelectedDate = DateTime.Today, IsEnabled = logsOnly });
        include.Checked += (_, _) => start.IsEnabled = end.IsEnabled = true;
        include.Unchecked += (_, _) => start.IsEnabled = end.IsEnabled = false;
        Submit("选择导出文件", () =>
        {
            if (include.IsChecked == true)
            {
                if (start.SelectedDate is not { } a || end.SelectedDate is not { } b || a.Date > b.Date) throw new InvalidOperationException("请选择有效的起止日期。");
                From = a.Date; To = b.Date;
            }
            DialogResult = true;
        });
    }
}

public sealed class HistoryWindow : Window
{
    public HistoryWindow(InventoryStore store, Product? product = null)
    {
        Title = product is null ? "全部操作记录" : "商品详情 · " + product.Name;
        Width = 1240; 
        Height = 760; 
        MinWidth = 950; 
        MinHeight = 600; 
        WindowStartupLocation = WindowStartupLocation.CenterOwner;      //开始window时，窗口在屏幕中心显示
        //DockPanel是WPF中的一种布局控件，它允许子元素在指定的位置（顶部、底部、左侧、右侧或中心）进行布局。
        var panel = new DockPanel { Margin = new Thickness(22) };      
        var header = new StackPanel(); //StackPanel是WPF中的一种布局控件，它允许子元素在垂直方向上排列。
        DockPanel.SetDock(header, Dock.Top); 
        panel.Children.Add(header); 
        Navigation.AddBack(this, header);
        header.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.Bold });
        if (product is not null)
        {
            var details = new WrapPanel();
            foreach (var item in new[] { ("供应商", product.Supplier), ("名称", product.Name), ("编号", product.Code), ("色号", product.Color), ("规格", product.Specification), ("单位", product.Unit), ("片/件", product.PiecesPerCase.ToString()) })
                details.Children.Add(new TextBlock { Text = item.Item1 + "：" + (string.IsNullOrEmpty(item.Item2) ? "未填写" : item.Item2), Margin = new Thickness(0, 4, 24, 4), MaxWidth = 360, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(57, 73, 80)) });
            header.Children.Add(new Border { Child = details, Background = Brushes.WhiteSmoke, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 8, 0, 4) });
        }
        var filter = new MovementFilters(); //MovementFilters是自定义的控件，用于筛选Movement对象。时间什么的筛选都在里面

        var lifetime = new TextBlock { Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap }; 
        if (product is not null) header.Children.Add(lifetime);
        var summary = new TextBlock { Margin = new Thickness(0, 12, 0, 12),
                                      FontSize = 18,     
                                      FontWeight = FontWeights.SemiBold,
                                      TextWrapping = TextWrapping.Wrap  // 文字自动换行
                                     }; 
        header.Children.Insert(product is null ? 2 : 3, filter);
        summary.Margin = new Thickness(12, 6, 0, 6); summary.FontSize = 13;
        summary.VerticalAlignment = VerticalAlignment.Center;
        summary.Foreground = new SolidColorBrush(Color.FromRgb(25, 83, 73));
        
        header.Children.Add(new TextBlock { Text = "合计按当前筛选的有效记录计；已撤销记录及撤销流水不重复计数。", TextWrapping = TextWrapping.Wrap });
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 10) };
        var undo = new Button { Content = "撤销选中出入库" }; 
        var export = new Button { Content = "按日期导出筛选日志" }; 
        actions.Children.Add(undo); 
        actions.Children.Add(export);
        actions.Children.Add(summary); 
        header.Children.Add(actions);
        var table = new PagedTable(100, ("时间", "Time"), ("状态", "RecordStatus"), ("供应商", "Supplier"), ("编号", "Code"), ("名称", "Name"), ("色号", "Color"), ("规格", "Specification"), ("操作", "Action"), ("件数变动", "Change"), ("片数变动", "ChangedPieces"), ("单位", "Unit"), ("片/件", "PiecesPerCase"), ("结存", "Balance"), ("件单价", "UnitPrice"), ("计价件数", "CaseCount"), ("散片数", "LoosePieceCount"), ("片单价", "PieceUnitPrice"), ("应计金额", "ExpectedAmount"), ("实际成交金额", "MoneyAmount"), ("来源/去处", "Counterparty"), ("说明", "Note")); panel.Children.Add(table);
        // Product details above show current values; each history row retains the values at that time.
        List<Movement> visible = [];
        if (product is not null)
        {
            void Adjust(bool incoming)
            {
                try
                {
                    var current = store.Data.Products.Single(p => p.Id == product.Id);
                    var dialog = new StockWindow(current, incoming) { Owner = this };
                    if (dialog.ShowDialog() != true) return;
                    store.AdjustStock(current.Id, dialog.Amount, incoming, dialog.Note, dialog.Counterparty, dialog.MoneyAmount, dialog.UnitPrice, dialog.Pieces, dialog.Pricing);
                    Refresh();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
            var inButton = new Button { Content = "入库" }; var outButton = new Button { Content = "出库" };
            inButton.Click += (_, _) => Adjust(true); outButton.Click += (_, _) => Adjust(false);
            actions.Children.Insert(0, outButton); actions.Children.Insert(0, inButton);
        }
        void Refresh()
        {
            try
            {
                visible = filter.Apply(product is null ? store.Data.Movements : store.History(product));
                summary.Text = $"筛选结果 {visible.Count} 条   ·   入库 {Reporting.Inbound(visible):0.##} 件   ·   出库 {Reporting.Outbound(visible):0.##} 件";
                if (product is not null)
                {
                    var current = store.Data.Products.FirstOrDefault(p => p.Id == product.Id);
                    var history = store.History(product).Where(Reporting.Active).ToList();
                    lifetime.Text = $"全部历史累计入库：{history.Where(m => m.Action is "入库" or "新增").Sum(m => m.ChangedPieces):0.########} 片    累计出库：{-history.Where(m => m.Action == "出库").Sum(m => m.ChangedPieces):0.########} 片";
                    lifetime.Text += current is null ? "    商品已删除" : $"\n当前库存：{current.StockDisplay}，合计 {current.TotalPieces:0.########} 片；每件 {current.PiecesPerCase} 片。" + (current.Quantity < 0 ? $" 超售 {Math.Abs(current.TotalPieces):0.########} 片，请入库补货。" : "");
                }
                table.SetRows(visible);
            }
            catch (Exception ex) { visible = []; table.SetRows(visible); summary.Text = "请调整筛选条件：\n" + ex.Message; }
        }
        undo.Click += (_, _) => { var selected = table.SelectedItems.OfType<Movement>().ToList(); if (selected.Count == 0) { MessageBox.Show(this, "请勾选一条或多条出入库记录（可跨页选择）。"); return; } if (new UndoWindow(store, selected) { Owner = this }.ShowDialog() == true) Refresh(); };
        export.Click += (_, _) =>
        {
            try
            {
                var options = new ExportWindow(true) { Owner = this }; if (options.ShowDialog() != true) return;
                var file = new SaveFileDialog { Filter = "Excel 工作簿|*.xlsx", FileName = "操作记录-" + DateTime.Today.ToString("yyyyMMdd") + ".xlsx" };
                if (file.ShowDialog(this) == true) { ExcelService.Export(file.FileName, new InventoryData { Movements = visible }, options.From, options.To, true); MessageBox.Show(this, "日志已导出。", "完成"); }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败"); }
        };
        filter.Attach(table); filter.Applied += Refresh; Content = panel; Refresh();
    }
}

public sealed record FinanceBucket(string Label, decimal Income, decimal Expense);

public sealed class FinanceChart : FrameworkElement
{
    public List<FinanceBucket> Buckets { get; set; } = [];
    private decimal? axisMaximum;
    public decimal? AxisMaximum
    {
        get => axisMaximum;
        set
        {
            if (value.HasValue && (value <= 0 || value > 1000000000000000000000000m)) throw new ArgumentOutOfRangeException(nameof(value), "纵轴上限必须大于 0 且不超过 10²⁴ 元。");
            axisMaximum = value; InvalidateVisual();
        }
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
        void Text(string value, double x, double y, Brush color, double size = 11) => dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
        Text("收入（绿） / 支出（橙） · 元", 14, 6, Brushes.SlateGray, 13);
        if (Buckets.Count == 0) return;
        decimal maximum = AxisMaximum ?? Math.Max(1, Buckets.Max(b => Math.Max(b.Income, b.Expense)));
        if (Buckets.Any(b => b.Income > maximum || b.Expense > maximum)) Text("▲ 超出纵轴上限；准确金额见右侧统计及下方明细", 14, 27, Brushes.DarkOrange, 11);
        double left = 95, bottom = ActualHeight - 34, height = Math.Max(20, ActualHeight - 90), step = (ActualWidth - left - 20) / Buckets.Count;
        for (int line = 0; line <= 4; line++)
        {
            var y = bottom - height * line / 4;
            dc.DrawLine(new Pen(Brushes.LightGray, 0.5), new Point(left, y), new Point(ActualWidth - 10, y));
            if (line != 4 || !AxisMaximum.HasValue) Text((maximum * line / 4).ToString("0.##"), 4, y - 8, Brushes.SlateGray);
        }
        for (int i = 0; i < Buckets.Count; i++)
        {
            var b = Buckets[i]; double x = left + step * i + 8; double width = Math.Min(27, (step - 22) / 2);
            double ih = (double)(Math.Min(b.Income, maximum) / maximum) * height, eh = (double)(Math.Min(b.Expense, maximum) / maximum) * height;
            dc.DrawRectangle(Brushes.Teal, null, new Rect(x, bottom - ih, width, ih));
            dc.DrawRectangle(Brushes.DarkOrange, null, new Rect(x + width + 5, bottom - eh, width, eh));
            if (b.Income > maximum) Text("▲", x, bottom - height - 16, Brushes.Teal);
            if (b.Expense > maximum) Text("▲", x + width + 5, bottom - height - 16, Brushes.DarkOrange);
            Text(b.Label, x, bottom + 7, Brushes.SlateGray);
        }
    }
}

public sealed class FinanceWindow : Window
{
    public FinanceWindow(InventoryStore store)
    {
        Title = "收支明细";
        Width = 1180; 
        Height = 850; 
        MinWidth = 850; 
        MinHeight = 650; 
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(22) };
        var header = new StackPanel(); 
        DockPanel.SetDock(header, Dock.Top); 
        panel.Children.Add(header); 
        Navigation.AddBack(this, header);
        header.Children.Add(new TextBlock { Text = "收支明细", FontSize = 25, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock { Text = "入库记支出，出库记收入；金额以当次手工确认数为准，不等同于利润。旧记录无金额时不补算。", Margin = new Thickness(0, 10, 0, 12), Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap });
        var filters = new WrapPanel { };
        var mode = new ComboBox { Width = 95, Height = 38, ItemsSource = new[] { "按月", "按年" }, SelectedIndex = 0 };
        var from = new TextBox { Width = 120, Height = 38, Text = DateTime.Today.ToString("yyyy-01"), Margin = new Thickness(10, 0, 10, 0) };
        var to = new TextBox { Width = 120, Height = 38, Text = DateTime.Today.ToString("yyyy-MM"), Margin = new Thickness(10, 0, 10, 0) };


        filters.Children.Add(mode);
        filters.Children.Add(from); 
        filters.Children.Add(new TextBlock { Text = "至", VerticalAlignment = VerticalAlignment.Center }); 
        filters.Children.Add(to); 
 
        var movementFilter = new MovementFilters(false) { Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        filters.Children.Add(movementFilter); header.Children.Add(filters);
        var summary = new TextBlock { Margin = new Thickness(0, 8, 0, 8), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(25, 83, 73)), TextWrapping = TextWrapping.Wrap };
        var chart = new FinanceChart { Height = 220 };
        var chartSurface = new Grid { Height = 220 }; chartSurface.Children.Add(chart);
        var chartScroll = new ScrollViewer { Content = chartSurface, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Height = 240 };
        var chartPanel = new StackPanel();
        var axisControls = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var automaticAxis = new CheckBox { Content = "纵轴自动", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var axisInput = new TextBox { Text = "1", Width = 85, Height = 24, FontSize = 11, Padding = new Thickness(2, 1, 2, 1), Margin = new Thickness(4, 44, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, Background = Brushes.White, BorderBrush = Brushes.LightGray, ToolTip = "直接修改纵轴最高刻度，按回车生效（元）；Esc 取消修改" };
        chartSurface.Children.Add(axisInput);
        var axisError = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        axisControls.Children.Add(automaticAxis);

        axisControls.Children.Add(new TextBlock { Text = "取消自动后，可直接编辑纵轴最高刻度并按回车", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.SlateGray, FontSize = 12, Margin = new Thickness(8, 0, 0, 0) });
        void ApplyAxis()
        {
            try
            {
                if (automaticAxis.IsChecked == true) chart.AxisMaximum = null;
                else if (decimal.TryParse(axisInput.Text, out var maximum) && maximum > 0) chart.AxisMaximum = maximum;
                else throw new InvalidOperationException("请输入大于 0 的纵轴上限；尚未应用本次输入。");
                if (chart.AxisMaximum.HasValue) axisInput.Text = chart.AxisMaximum.Value.ToString(CultureInfo.CurrentCulture);
                axisError.Text = "";
            }
            catch (Exception ex) { axisError.Text = ex.Message; }
        }
        automaticAxis.Checked += (_, _) => { axisInput.Visibility = Visibility.Collapsed; ApplyAxis(); };
        automaticAxis.Unchecked += (_, _) => { axisInput.Visibility = Visibility.Visible; axisInput.Text = Math.Max(1, chart.Buckets.Select(b => Math.Max(b.Income, b.Expense)).DefaultIfEmpty(1).Max()).ToString(CultureInfo.CurrentCulture); ApplyAxis(); axisInput.Focus(); axisInput.SelectAll(); };
        UiInputs.OnEnter(axisInput, ApplyAxis);
        axisInput.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) { axisInput.Text = (chart.AxisMaximum ?? 1).ToString(CultureInfo.CurrentCulture); axisError.Text = ""; e.Handled = true; }
        };
        chartPanel.Children.Add(axisControls); chartPanel.Children.Add(axisError); chartPanel.Children.Add(chartScroll);
        var overview = new Grid { Margin = new Thickness(0, 12, 0, 18) };
        overview.ColumnDefinitions.Add(new ColumnDefinition()); overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(285) });
        overview.Children.Add(chartPanel);
        var summaryCard = new Border { Child = summary, Padding = new Thickness(16, 12, 12, 12), Margin = new Thickness(16, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(229, 241, 237)), CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(summaryCard, 1); overview.Children.Add(summaryCard); header.Children.Add(overview);
        void FitChart() { chart.Width = Math.Max(Math.Max(300, chartScroll.ViewportWidth), 100 + Math.Max(1, chart.Buckets.Count) * 95); chartSurface.Width = chart.Width; }
        chartScroll.SizeChanged += (_, _) => FitChart();
        var undo = new Button { Content = "撤销选中出入库", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) }; header.Children.Add(undo);
        var table = new PagedTable(100, ("时间", "Time"), ("供应商", "Supplier"), ("商品", "Name"), ("状态", "RecordStatus"), ("编号", "Code"), ("色号", "Color"), ("操作", "Action"), ("件数变动", "Change"), ("片数变动", "ChangedPieces"), ("来源/去处", "Counterparty"), ("件单价", "UnitPrice"), ("计价件数", "CaseCount"), ("散片数", "LoosePieceCount"), ("片单价", "PieceUnitPrice"), ("应计金额", "ExpectedAmount"), ("实际成交金额", "MoneyAmount"), ("收入", "Income"), ("支出", "Expense"), ("说明", "Note")); panel.Children.Add(table);
        void Refresh()
        {
            try
            {
                bool monthly = mode.SelectedIndex == 0; string format = monthly ? "yyyy-MM" : "yyyy";
                if (!DateTime.TryParseExact(from.Text.Trim() + (monthly ? "-01" : "-01-01"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) || !DateTime.TryParseExact(to.Text.Trim() + (monthly ? "-01" : "-01-01"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || start > end || end.Year >= 9999) throw new InvalidOperationException("请输入有效区间，格式为 " + format);
                int count = monthly ? (end.Year - start.Year) * 12 + end.Month - start.Month + 1 : end.Year - start.Year + 1;
                if (count > 120) throw new InvalidOperationException("一次最多查看 120 个月或年，请缩小区间。");
                var exclusive = monthly ? end.AddMonths(1) : end.AddYears(1);
                var rows = movementFilter.Apply(store.Data.Movements.Where(m => m.Time >= start && m.Time < exclusive && (m.Action is "入库" or "出库" or "新增" or "撤销")));
                decimal income = rows.Sum(m => m.Income), expense = rows.Sum(m => m.Expense);
                summary.Text = $"筛选结果：共 {rows.Count} 条\n累计入库：{Reporting.Inbound(rows):0.##} 件\n累计出库：{Reporting.Outbound(rows):0.##} 件\n收入 {income:N2} 元 / 支出 {expense:N2} 元\n收支差额：{income - expense:N2} 元\n未记录金额：{rows.Count(m => Reporting.Active(m) && !m.MoneyAmount.HasValue)} 条";
                var groups = rows.GroupBy(m => m.Time.ToString(format)).ToDictionary(g => g.Key, g => new FinanceBucket(g.Key, g.Sum(m => m.Income), g.Sum(m => m.Expense)));
                chart.Buckets = Enumerable.Range(0, count).Select(i => (monthly ? start.AddMonths(i) : start.AddYears(i)).ToString(format)).Select(key => groups.GetValueOrDefault(key, new FinanceBucket(key, 0, 0))).ToList();
                FitChart(); chart.InvalidateVisual(); table.SetRows(rows);
            }
            catch (Exception ex) { table.SetRows([]); chart.Buckets = []; chart.InvalidateVisual(); summary.Text = "请调整区间：\n" + ex.Message; }
        }
        mode.SelectionChanged += (_, _) => { from.Text = mode.SelectedIndex == 0 ? DateTime.Today.ToString("yyyy-01") : DateTime.Today.ToString("yyyy"); to.Text = mode.SelectedIndex == 0 ? DateTime.Today.ToString("yyyy-MM") : DateTime.Today.ToString("yyyy"); Refresh(); };
        undo.Click += (_, _) => { var selected = table.SelectedItems.OfType<Movement>().ToList(); if (selected.Count == 0) { MessageBox.Show(this, "请勾选一条或多条出入库记录（可跨页选择）。"); return; } if (new UndoWindow(store, selected) { Owner = this }.ShowDialog() == true) Refresh(); };
        movementFilter.Attach(table); movementFilter.Applied += Refresh;
        UiInputs.WatchSearch(from, Refresh); UiInputs.WatchSearch(to, Refresh); Content = panel; Refresh();
    }
}
