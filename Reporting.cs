using System.Windows;
using System.Windows.Controls;

namespace XingHui;

public static class Reporting
{
    public static bool Active(Movement m) => !m.IsCancelled && m.ReversesId == "";
    public static decimal Inbound(IEnumerable<Movement> rows) => rows.Where(m => Active(m) && (m.Action is "入库" or "新增")).Sum(m => m.Change);
    public static decimal Outbound(IEnumerable<Movement> rows) => -rows.Where(m => Active(m) && m.Action == "出库").Sum(m => m.Change);
    public static List<SalesRow> Sales(InventoryData data, DateTime from, DateTime to)
    {
        var rows = data.Movements.Where(m => Active(m) && m.ProductId != "" && m.Time.Date >= from.Date && m.Time.Date <= to.Date).ToList();
        var result = rows.Where(m => m.Action == "出库").GroupBy(m => m.ProductId).Select(g =>
        {
            var p = data.Products.FirstOrDefault(p => p.Id == g.Key);
            var last = g.Last();
            return new SalesRow { ProductId = g.Key, Name = p?.Name ?? last.Name, Supplier = p?.Supplier ?? last.Supplier, Code = p?.Code ?? last.Code, Color = p?.Color ?? last.Color, Specification = p?.Specification ?? last.Specification, Outbound = -g.Sum(m => m.Change), Inbound = Inbound(rows.Where(m => m.ProductId == g.Key)), Income = g.Sum(m => m.Income), MissingAmount = g.Count(m => !m.MoneyAmount.HasValue) };
        }).Where(r => r.Outbound > 0).OrderByDescending(r => r.Outbound).ThenBy(r => r.Code).ToList();
        foreach (var row in result) row.Rank = 1 + result.Count(r => r.Outbound > row.Outbound);
        return result;
    }
}

public sealed class SalesRow
{
    public string ProductId { get; init; } = "";
    public string Supplier { get; init; } = "";
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";
    public string Color { get; init; } = "";
    public string Specification { get; init; } = "";
    public decimal Inbound { get; init; }
    public decimal Outbound { get; init; }
    public decimal Income { get; init; }
    public int MissingAmount { get; init; }
    public int Rank { get; set; }
    public string Mark => Rank == 1 ? "★ 畅销第一" : Rank <= 3 ? "畅销前三" : "";
}

public sealed class MovementFilters : StackPanel
{
    private readonly DatePicker from = new() { Width = 130,FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly DatePicker to = new() { Width = 130, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox search = new() { Width = 180, ToolTip = "名称、编号、供应商、色号、规格、来源或去处" };
    private string action = "全部操作", status = "全部状态";
    public event Action? Applied;
    public MovementFilters(bool showDates = true)
    {
        Margin = new Thickness(0, 10, 0, 10);
        var controls = new WrapPanel();
        void Date(string label, DatePicker input)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 8) };
            group.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }); group.Children.Add(input); controls.Children.Add(group);
        }
        if (showDates) { Date("开始时间", from); Date("结束时间", to); }
        search.Margin = new Thickness(0, 0, 0, 8); controls.Children.Add(UiInputs.Search(search)); Children.Add(controls);
        from.SelectedDateChanged += (_, _) => Applied?.Invoke(); to.SelectedDateChanged += (_, _) => Applied?.Invoke();
        UiInputs.WatchSearch(search, () => Applied?.Invoke());
    }
    public void Attach(PagedTable table)
    {
        table.FilterHeader("Action", "操作", new[] { "全部操作", "入库", "出库", "初始库存", "编辑", "导入", "同名合并", "匹配合并", "删除", "撤销" }, value => { action = value; Applied?.Invoke(); });
        table.FilterHeader("RecordStatus", "状态", new[] { "全部状态", "有效", "已撤销", "撤销记录" }, value => { status = value; Applied?.Invoke(); });
    }
    public List<Movement> Apply(IEnumerable<Movement> rows)
    {
        if (from.SelectedDate.HasValue && to.SelectedDate.HasValue && from.SelectedDate.Value.Date > to.SelectedDate.Value.Date) throw new InvalidOperationException("开始日期不能晚于结束日期。");
        string query = search.Text.Trim(), type = action, state = status;
        if (type == "初始库存") type = "新增";
        return rows.Where(m => (!from.SelectedDate.HasValue || m.Time.Date >= from.SelectedDate.Value.Date) && (!to.SelectedDate.HasValue || m.Time.Date <= to.SelectedDate.Value.Date)
            && (type == "全部操作" || m.Action == type)
            && (state == "全部状态" || m.RecordStatus == state)
            && (query == "" || string.Join(" ", m.Name, m.Code, m.Supplier, m.Color, m.Specification, m.Counterparty).Contains(query, StringComparison.OrdinalIgnoreCase))).OrderByDescending(m => m.Time).ToList();
    }
}

public sealed class SalesWindow : Window
{
    public SalesWindow(InventoryStore store)
    {
        Title = "畅销分析"; 
        Width = 1100; 
        Height = 720; 
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(22) }; 
        var header = new StackPanel(); 
        DockPanel.SetDock(header, Dock.Top); 
        panel.Children.Add(header); Navigation.AddBack(this, header);
        header.Children.Add(new TextBlock { Text = "畅销分析 · 按净出库件数排名", FontSize = 22, FontWeight = FontWeights.Bold });
        var controls = new WrapPanel { Margin = new Thickness(0, 16, 0, 12) };
        var from = new DatePicker { SelectedDate = DateTime.Today.AddDays(-30), Width = 150, Margin = new Thickness(0, 0, 12, 0) }; 
        var to = new DatePicker { SelectedDate = DateTime.Today, Width = 150, Margin = new Thickness(0, 0, 12, 0) };
        var search = new TextBox { Width = 180, ToolTip = "搜索供应商、商品、编号或规格" }; 
        var onlyTop = new CheckBox { Content = "只看畅销前三", Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center }; 
        var feedback = new TextBlock { Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.SlateGray, Visibility = Visibility.Collapsed };
        controls.Children.Add(from); controls.Children.Add(to); 
        controls.Children.Add(UiInputs.Search(search)); controls.Children.Add(onlyTop); 
        header.Children.Add(controls);
        var note = new TextBlock { Text = "撤销的出库不计入销量，初始库存和导入不计销量；并列销量同排名。排名按区间内全部商品计算，搜索仅筛选结果。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; header.Children.Add(note);
        header.Children.Add(feedback);
        var table = new PagedTable(20, ("排名", "Rank"), ("标记", "Mark"), ("供应商", "Supplier"), ("名称", "Name"), ("编号", "Code"), ("色号", "Color"), ("规格", "Specification"), ("区间入库件数", "Inbound"), ("净出库件数", "Outbound"), ("成交收入", "Income"), ("未记录金额笔数", "MissingAmount")); panel.Children.Add(table);
        void Refresh()
        {
            try
            {
                if (from.SelectedDate is not { } a || to.SelectedDate is not { } b || a > b) throw new InvalidOperationException("请选择有效起止日期。");
                var query = search.Text.Trim(); var rows = Reporting.Sales(store.Data, a, b);
                var visible = rows.Where(r => (onlyTop.IsChecked != true || r.Rank <= 3) && (query == "" || string.Join(" ", r.Name, r.Supplier, r.Code, r.Color, r.Specification).Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
                table.SetRows(visible);
                feedback.Foreground = System.Windows.Media.Brushes.SlateGray;
                feedback.Text = rows.Count == 0 ? "所选时间内暂无有效出库记录，登记出库后可在这里查看销量排名。" : "没有符合当前条件的商品，请调整搜索词或取消“只看畅销前三”。";
                feedback.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex) { table.SetRows([]); feedback.Foreground = System.Windows.Media.Brushes.Firebrick; feedback.Text = "请调整筛选条件：" + ex.Message; feedback.Visibility = Visibility.Visible; }
        }
        UiInputs.WatchSearch(search, Refresh);
        from.SelectedDateChanged += (_, _) => Refresh(); to.SelectedDateChanged += (_, _) => Refresh();
        onlyTop.Checked += (_, _) => Refresh(); onlyTop.Unchecked += (_, _) => Refresh();
        Content = panel; Refresh();
    }
}
