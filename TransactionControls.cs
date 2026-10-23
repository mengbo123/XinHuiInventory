using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XingHui;

public sealed class MoneyEditor : StackPanel
{
    private readonly TextBox price = NumericInput.Create();
    private readonly TextBox piecePrice = NumericInput.Create();
    private readonly TextBox expected = NumericInput.Create();
    private readonly TextBox actual = NumericInput.Create();
    private readonly CheckBox automatic = new() { Content = "自动换算散片单价（取消勾选可手动修改）", IsChecked = true };
    private readonly TextBox cases, pieces;
    private readonly int pack;
    private bool edited, updating;
    public decimal UnitPrice => decimal.Parse(price.Text, CultureInfo.CurrentCulture);
    public decimal Actual => decimal.Parse(actual.Text, CultureInfo.CurrentCulture);
    public StockPricing Pricing => new(decimal.Parse(cases.Text), decimal.Parse(pieces.Text), automatic.IsChecked == true ? null : decimal.Parse(piecePrice.Text));
    public MoneyEditor(TextBox cases, TextBox pieces, int pack, decimal? lastPrice, bool incoming, string unit = "件")
    {
        this.cases = cases; this.pieces = pieces; this.pack = pack;
        void Field(string label, UIElement input)
        {
            Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 5), TextWrapping = TextWrapping.Wrap });
            Children.Add(input);
        }
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Cell(string label, UIElement input, int row, int column)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            cell.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 5), TextWrapping = TextWrapping.Wrap });
            cell.Children.Add(input);
            Grid.SetRow(cell, row); Grid.SetColumn(cell, column); grid.Children.Add(cell);
        }
        Cell($"整包装数量 / {unit}（默认 0）", cases, 0, 0);
        Cell("散片数量 / 片（默认 0）", pieces, 0, 2);
        price.Text = (lastPrice ?? 0).ToString(CultureInfo.CurrentCulture);
        Cell(incoming ? $"入库单价 / 元每{unit}" : $"出库单价 / 元每{unit}", price, 1, 0);
        Cell("散片单价 / 元每片", piecePrice, 1, 2);
        Children.Add(grid);
        automatic.Content = "自动换算散片单价";
        automatic.ToolTip = "取消勾选后，可手动填写散片单价。";
        var priceHelp = new StackPanel { Margin = new Thickness(10) };
        priceHelp.Children.Add(automatic);
        priceHelp.Children.Add(new TextBlock
        {
            Text = $"默认片单价 = 件单价 ÷ {pack}（每件 {pack} 片）。取消勾选可单独定价。\n件单价沿用上次记录，可修改；整件与散片分别计价，合计四舍五入到分。",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, FontSize = 12, Margin = new Thickness(0, 6, 0, 0)
        });
        Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = priceHelp });
        expected.IsReadOnly = true; expected.Background = Brushes.WhiteSmoke;
        expected.ToolTip = "整件数量 × 件单价 + 散片数量 × 片单价";
        Field(incoming ? "应支出金额 / 元（自动计算）" : "应收入金额 / 元（自动计算）", expected);
        var settlement = new Grid();
        settlement.ColumnDefinitions.Add(new ColumnDefinition());
        settlement.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        settlement.Children.Add(actual);
        var reset = new Button { Content = "使用应计金额", Margin = new Thickness(10, 0, 0, 0), ToolTip = "重新按应计金额填入，并恢复自动跟随计算。" };
        Grid.SetColumn(reset, 1); settlement.Children.Add(reset);
        Field(incoming ? "实际支出金额 / 元（可修改）" : "实际收入金额 / 元（可修改）", settlement);
        void Refresh()
        {
            if (updating) return;
            updating = true;
            try
            {
                piecePrice.IsReadOnly = automatic.IsChecked == true;
                piecePrice.Background = piecePrice.IsReadOnly ? Brushes.WhiteSmoke : Brushes.White;
                if (automatic.IsChecked == true) piecePrice.Text = decimal.TryParse(price.Text, out var unit) && unit >= 0 && unit <= 1000000000 ? (unit / pack).ToString(CultureInfo.CurrentCulture) : "0";
                try { var values = Pricing; values.Validate(pack); InventoryStore.ValidatePrice(UnitPrice); expected.Text = values.Expected(UnitPrice, pack).ToString("0.00"); }
                catch { expected.Text = "0"; }
                if (!edited) actual.Text = expected.Text;
            }
            finally { updating = false; }
        }
        actual.TextChanged += (_, _) => { if (!updating) edited = true; };
        cases.TextChanged += (_, _) => Refresh(); pieces.TextChanged += (_, _) => Refresh(); price.TextChanged += (_, _) => Refresh(); piecePrice.TextChanged += (_, _) => Refresh();
        automatic.Checked += (_, _) => Refresh(); automatic.Unchecked += (_, _) => { if (decimal.TryParse(piecePrice.Text, out var current)) piecePrice.Text = decimal.Round(current, 4, MidpointRounding.AwayFromZero).ToString(CultureInfo.CurrentCulture); Refresh(); };
        reset.Click += (_, _) => { edited = false; Refresh(); }; Refresh();
    }
    public void Validate()
    {
        if (!decimal.TryParse(price.Text, out var p)) throw new InvalidDataException("请填写件单价。");
        InventoryStore.ValidatePrice(p); Pricing.Validate(pack);
        if (!decimal.TryParse(actual.Text, out var a) || a < 0 || a > 1000000000000000000m || decimal.Round(a, 2) != a) throw new InvalidDataException("实际成交金额必须非负，最多两位小数。");
    }
}

public sealed class SpecificationsWindow : FormWindow
{
    public SpecificationsWindow(InventoryStore store) : base("管理规格", 520)
    {
        Heading("规格 / 尺码", "例如 800*800、750*1500。删除只移除下拉选项，不修改已有商品和历史记录。");
        var list = new ListBox { Height = 230, ItemsSource = store.Data.Specifications.ToList() }; Body.Children.Add(list);
        var value = Field("新规格", new TextBox { MaxLength = 30 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var add = new Button { Content = "添加规格" }; var remove = new Button { Content = "删除选中规格" };
        add.Click += (_, _) => { try { store.AddSpecification(value.Text); list.ItemsSource = store.Data.Specifications.ToList(); Error.Text = ""; } catch (Exception ex) { Error.Text = ex.Message; } };
        remove.Click += (_, _) =>
        {
            try
            {
                if (list.SelectedItem is not string selected) throw new InvalidOperationException("请选择一个规格。");
                if (MessageBox.Show(this, $"从下拉选项中删除 {selected}？已有商品不受影响。", "删除规格", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                store.RemoveSpecification(selected); list.ItemsSource = store.Data.Specifications.ToList();
            }
            catch (Exception ex) { Error.Text = ex.Message; }
        };
        buttons.Children.Add(add); buttons.Children.Add(remove); Body.Children.Add(buttons);
        Submit("完成", () => DialogResult = true);
    }
}

public sealed class UndoWindow : FormWindow
{
    public UndoWindow(InventoryStore store, Movement movement) : this(store, new[] { movement }) { }
    public UndoWindow(InventoryStore store, IReadOnlyList<Movement> movements) : base("批量撤销出入库", 640)
    {
        Heading($"确认撤销 {movements.Count} 条记录", string.Join("\n", movements.Take(8).Select(m => $"{m.Time:yyyy-MM-dd HH:mm} · {m.Name} · {m.Action} · {m.Change:0.##} 件"))
            + $"\n选中入库（含初始）：{Reporting.Inbound(movements):0.##} 件，出库：{Reporting.Outbound(movements):0.##} 件。\n按记录从新到旧撤销，全部成功才保存。库存、原期间收支与销量同步调整，原记录和原因保留。这是纠错，不是退款登记。");
        var reason = Field("撤销原因 *", new TextBox { MaxLength = 1000 });
        Submit("确认批量撤销", () => { store.UndoMovements(movements.Select(m => m.Id), reason.Text); DialogResult = true; });
    }
}
