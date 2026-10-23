using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace XingHui;

public sealed record ProductSearchRule(string Field, string Value, bool OrPrevious = false)
{
    public static readonly Dictionary<string, string> Fields = new()
    {
        ["Supplier"] = "供应商", ["Name"] = "名称", ["Code"] = "编号", ["Color"] = "色号", ["Specification"] = "规格",
        ["StockDisplay"] = "库存（件）", ["PiecesPerCase"] = "片/件", ["TotalPieces"] = "总片数", ["Status"] = "状态",
        ["Minimum"] = "预警值", ["LastInboundPrice"] = "上次入库价", ["LastOutboundPrice"] = "上次出库价", ["Notes"] = "备注", ["Updated"] = "更新时间"
    };
    public bool Matches(Product product) => Fields.ContainsKey(Field) &&
        (Convert.ToString(typeof(Product).GetProperty(Field)?.GetValue(product), System.Globalization.CultureInfo.CurrentCulture) ?? "").Contains(Value.Trim(), StringComparison.OrdinalIgnoreCase);
    // Evaluate in display order: each connector combines the preceding result with this row.
    public static bool Evaluate(IReadOnlyList<ProductSearchRule> rules, Product product)
    {
        if (rules.Count == 0) return true;
        bool result = rules[0].Matches(product);
        foreach (var rule in rules.Skip(1)) result = rule.OrPrevious ? result | rule.Matches(product) : result & rule.Matches(product);
        return result;
    }
    public static string Describe(IReadOnlyList<ProductSearchRule> rules)
    {
        if (rules.Count == 0) return "";
        var text = rules[0].Description;
        foreach (var rule in rules.Skip(1)) text = "（" + text + (rule.OrPrevious ? " 或 " : " 与 ") + rule.Description + "）";
        return text;
    }
    public string Description => $"{Fields.GetValueOrDefault(Field, Field)}包含“{Value}”";
}

public partial class Dashboard
{
    private string? supplierFilter, specificationFilter;
    private List<ProductSearchRule> searchRules = [];
    private bool anySearchRule;
    private bool MatchesProductFilters(Product p) =>
        (supplierFilter is null || string.Equals(p.Supplier, supplierFilter, StringComparison.OrdinalIgnoreCase)) &&
        (specificationFilter is null || string.Equals(p.Specification, specificationFilter, StringComparison.OrdinalIgnoreCase)) &&
        ProductSearchRule.Evaluate(searchRules, p);
    private void InitializeProductFilters()
    {
        foreach (var member in new[] { "Supplier", "Specification" })
        {
            var column = ProductsGrid.Columns.Single(c => ColumnMember(c) == member);
            var title = member == "Supplier" ? "供应商" : "规格";
            column.CanUserSort = false;
            var style = new Style(typeof(DataGridColumnHeader), (Style)FindResource(typeof(DataGridColumnHeader)));
            style.Setters.Add(new Setter(ContentControl.ContentTemplateProperty, null));
            column.HeaderStyle = style;
            var button = new Button { Content = title + " ▾", Background = Brushes.Transparent, Foreground = Brushes.Teal, Padding = new Thickness(0), Margin = new Thickness(0), ToolTip = "点击选择筛选条件" };
            column.Header = button;
            button.Click += (_, _) =>
            {
                var menu = new ContextMenu { MaxHeight = 420 };
                var selected = member == "Supplier" ? supplierFilter : specificationFilter;
                void Option(string text, string? value)
                {
                    var item = new MenuItem { Header = text, IsCheckable = true, IsChecked = selected == value };
                    item.Click += (_, _) =>
                    {
                        RememberView();
                        if (member == "Supplier") supplierFilter = value; else specificationFilter = value;
                        productPage = 0; Refresh();
                    };
                    menu.Items.Add(item);
                }
                Option("全部" + title, null);
                // Values come from the complete inventory, so an active filter never hides another option.
                foreach (var value in store.Data.Products.Select(p => member == "Supplier" ? p.Supplier : p.Specification).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                    Option(value == "" ? "（未填写）" : value, value);
                menu.PlacementTarget = button; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
            };
        }
    }
    private void UpdateFilterSummary()
    {
        var labels = new List<string>();
        if (Search.Text.Trim() != "") labels.Add($"搜索包含“{Search.Text.Trim()}”");
        if (LowOnly.IsChecked == true) labels.Add("仅预警");
        if (supplierFilter is not null) labels.Add("供应商：" + (supplierFilter == "" ? "（未填写）" : supplierFilter));
        if (specificationFilter is not null) labels.Add("规格：" + (specificationFilter == "" ? "（未填写）" : specificationFilter));
        if (searchRules.Count > 0) labels.Add("高级搜索：（" + ProductSearchRule.Describe(searchRules) + "）");
        FilterSummary.Text = labels.Count == 0 ? "当前筛选：无（全部库存）" : "当前筛选：" + string.Join("；", labels);
        foreach (var c in ProductsGrid.Columns)
            if (c.Header is Button button)
            {
                var supplier = ColumnMember(c) == "Supplier";
                var value = supplier ? supplierFilter : specificationFilter;
                button.Content = (supplier ? "供应商" : "规格") + (value is null ? " ▾" : " ● ▾");
            }
    }
    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        RememberView(); restoringView = true;
        try
        {
            supplierFilter = specificationFilter = null; searchRules.Clear(); anySearchRule = false;
            Search.Text = ""; LowOnly.IsChecked = false; productPage = 0; Refresh();
        }
        finally { restoringView = false; }
    }
    private void AdvancedSearch_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AdvancedProductSearchWindow(searchRules, anySearchRule) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        RememberView(); searchRules = dialog.Rules; anySearchRule = dialog.AnyRule; productPage = 0; Refresh();
    }
}

public sealed class AdvancedProductSearchWindow : FormWindow
{
    public List<ProductSearchRule> Rules { get; private set; } = [];
    public bool AnyRule { get; private set; }
    public AdvancedProductSearchWindow(IEnumerable<ProductSearchRule> existing, bool any) : base("高级搜索", 690)
    {
        Heading("高级搜索", "按列进行模糊匹配，不区分英文大小写。这里的结果仍需满足外部的供应商、规格、搜索及预警条件。");
        Body.Children.Add(new TextBlock { Text = "从第二条起选择与／或，按从上到下顺序结合前面的结果（筛选摘要显示括号）。", TextWrapping = TextWrapping.Wrap });
        var rows = new StackPanel(); Body.Children.Add(rows);
        var inputs = new List<(ComboBox Field, TextBox Value, FrameworkElement Row, ComboBox Join)>();
        void UpdateJoins()
        {
            for (int i = 0; i < inputs.Count; i++) inputs[i].Join.Visibility = i == 0 ? Visibility.Hidden : Visibility.Visible;
        }
        void Add(ProductSearchRule? rule = null)
        {
            var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var field = new ComboBox { ItemsSource = ProductSearchRule.Fields, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = rule?.Field ?? "Name", Margin = new Thickness(0, 0, 10, 0) };
            var value = new TextBox { Text = rule?.Value ?? "", MaxLength = 1000, ToolTip = "输入要包含的文字；数字也按文字模糊匹配" };
            var remove = new Button { Content = "删除条件", Margin = new Thickness(10, 0, 0, 0) };
            var join = new ComboBox { ItemsSource = new[] { "与", "或" }, SelectedIndex = (rule?.OrPrevious ?? any) ? 1 : 0, Margin = new Thickness(0, 0, 8, 0) };
            row.Children.Add(join); Grid.SetColumn(field, 1);
            Grid.SetColumn(value, 2); Grid.SetColumn(remove, 3); row.Children.Add(field); row.Children.Add(value); row.Children.Add(remove);
            inputs.Add((field, value, row, join)); rows.Children.Add(row); UpdateJoins();
            remove.Click += (_, _) => { inputs.RemoveAll(x => x.Row == row); rows.Children.Remove(row); UpdateJoins(); };
        }
        foreach (var rule in existing) Add(rule);
        if (inputs.Count == 0) { Add(); Add(); }
        var add = new Button { Content = "＋ 添加条件", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        add.Click += (_, _) => Add(); Body.Children.Add(add);
        Submit("应用高级搜索", () =>
        {
            Rules = inputs.Where(x => !string.IsNullOrWhiteSpace(x.Value.Text)).Select(x => new ProductSearchRule((string)x.Field.SelectedValue, x.Value.Text.Trim(), x.Join.SelectedIndex == 1)).ToList();
            AnyRule = false; DialogResult = true;
        });
    }
}

public sealed class SuppliersWindow : FormWindow
{
    public SuppliersWindow(InventoryStore store) : base("管理供应商", 540)
    {
        Heading("供应商选项", "删除只移除下拉选项，不更改已有商品和历史记录中的供应商。");
        var list = new ListBox { Height = 230, ItemsSource = store.Data.Suppliers?.ToList() ?? [] }; Body.Children.Add(list);
        var value = Field("新供应商", new TextBox { MaxLength = 1000 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var add = new Button { Content = "添加" }; var remove = new Button { Content = "删除选中供应商" };
        add.Click += (_, _) => { try { store.AddSupplier(value.Text); list.ItemsSource = store.Data.Suppliers!.ToList(); value.Text = ""; Error.Text = ""; } catch (Exception ex) { Error.Text = ex.Message; } };
        remove.Click += (_, _) =>
        {
            try
            {
                if (list.SelectedItem is not string selected) throw new InvalidOperationException("请选择供应商。");
                if (MessageBox.Show(this, $"删除供应商选项“{selected}”？已有商品不受影响。", "删除选项", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                store.RemoveSupplier(selected); list.ItemsSource = store.Data.Suppliers!.ToList();
            }
            catch (Exception ex) { Error.Text = ex.Message; }
        };
        buttons.Children.Add(add); buttons.Children.Add(remove); Body.Children.Add(buttons);
        Submit("完成", () => DialogResult = true);
    }
}
