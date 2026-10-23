using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;

namespace XingHui;
public partial class Dashboard : Window
{
    private readonly InventoryStore store;
    private int productPage;
    private int ProductPageSize = 20;
    private List<Product> filteredProducts = [];
    public Dashboard(InventoryStore store)
    {
        this.store = store; InitializeComponent(); 
        UserText.Text = store.UserName; 
        UserText.ToolTip = store.UserName; 
        UserInitial.Text = store.UserName[..1];
        UiInputs.OnEnter(ProductPageSizeInput, () => ProductPageSize_Click(ProductPageSizeInput, new RoutedEventArgs()));
        UiInputs.OnEnter(ProductJumpInput, () => ProductJump_Click(ProductJumpInput, new RoutedEventArgs())); 
        InitializeProductFilters();
        Refresh();
    }
    private void Safe(Action action) { 
        try { 
            action(); //无参、返回值的委托类型
        } 
        catch (Exception ex) { 
            MessageBox.Show(this, ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning); 
        } 
    }
    private void Refresh(string? message = null)
    {
        if (ProductsGrid is null) return;
        var scroll = FindScroll(ProductsGrid); 
        var horizontal = scroll?.HorizontalOffset ?? 0; 
        var vertical = scroll?.VerticalOffset ?? 0;
        var all = store.Data.Products; 
        var query = Search.Text.Trim();
        var visible = all.Where(p => MatchesProductFilters(p) && (LowOnly.IsChecked != true || p.Low) && (query == "" || string.Join(" ", p.Name, p.Code, p.Color, p.Specification, p.Location, p.Supplier).Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        var property = typeof(Product).GetProperty(sortMember)!;
        visible = (sortDirection == System.ComponentModel.ListSortDirection.Ascending ? visible.OrderBy(p => property.GetValue(p)) : visible.OrderByDescending(p => property.GetValue(p))).ThenBy(p => p.Id).ToList();
        filteredProducts = visible;
        for (int index = 0; index < visible.Count; index++) visible[index].DisplayIndex = index + 1;
        UpdateFilterSummary();
        int pages = Math.Max(1, (visible.Count + ProductPageSize - 1) / ProductPageSize);
        productPage = Math.Clamp(productPage, 0, pages - 1);
        selectedProductIds.IntersectWith(visible.Select(p => p.Id));
        updatingSelection = true;
        try
        {
            // WPF can retain selection when a new ItemsSource contains the same product instances.
            // Rebuild the visual selection exclusively from the cross-page ID set.
            ProductsGrid.UnselectAll();
            ProductsGrid.ItemsSource = visible.Skip(productPage * ProductPageSize).Take(ProductPageSize).ToList();
            ProductsGrid.UnselectAll();
            foreach (Product p in ProductsGrid.Items)
                if (selectedProductIds.Contains(p.Id)) ProductsGrid.SelectedItems.Add(p);
        }
        finally { updatingSelection = false; }

        UpdateSelectionCaption();
        foreach (var column in ProductsGrid.Columns) column.SortDirection = ColumnMember(column) == sortMember ? sortDirection : null;
        ProductJumpInput.Text = (productPage + 1).ToString(); 
        ProductPageSizeInput.Text = ProductPageSize.ToString();
        BackButton.IsEnabled = viewHistory.Count > 0;
        lastView = CaptureView();
        Dispatcher.BeginInvoke(new Action(() => { var viewer = FindScroll(ProductsGrid); viewer?.ScrollToHorizontalOffset(horizontal); viewer?.ScrollToVerticalOffset(vertical); }));
        ProductPageTotal.Text = $"/ {pages} 页"; PageText.Text = $"共 {visible.Count} 条";
        PreviousPage.IsEnabled = productPage > 0; NextPage.IsEnabled = productPage + 1 < pages;
        SkuText.Text = all.Count.ToString(); QuantityText.Text = all.Sum(p => p.Quantity).ToString("0.##"); LowText.Text = all.Count(p => p.Low).ToString(); ValueText.Text = all.Sum(p => p.TotalPieces).ToString("0.##");
        AlertText.Text = all.Any(p => p.Low) ? $"补货提醒：{all.Count(p => p.Low)} 个商品库存不足，其中 {all.Count(p => p.Quantity <= 0)} 个缺货或超售，负库存请及时入库补货。点击左侧“库存预警”查看。" : "库存状态良好。可为每个商品设置预警库存，达到预警值时会在这里提醒。";
        EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = all.Count == 0 ? "还没有商品，点击“新增商品”开始管理库存。\n也可以导出空模板，填写后导入。" : "没有符合条件的商品，请调整搜索词或筛选条件。";
        StatusText.Text = $"显示 {visible.Count} / {all.Count} 个商品  ·  勾选可批量删除 · 右击商品查看详情并出入库 · 双击可编辑  ·  " + (message ?? "手动导出留存，不再自动备份");
    }
    private Product? Selected()
    {
        if (selectedProductIds.Count == 1 && store.Data.Products.FirstOrDefault(p => selectedProductIds.Contains(p.Id)) is { } product) return product;
        MessageBox.Show(this, "请只选择一个商品再进行此操作。", "选择商品"); return null;
    }
    private void Add_Click(object sender, RoutedEventArgs e) => Edit(null);
    private void Edit_Click(object sender, RoutedEventArgs e) { if (Selected() is { } p) Edit(p); }
    private void Grid_DoubleClick(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(ProductsGrid, source) is DataGridRow && ProductsGrid.SelectedItem is Product p) Edit(p); }
    private void Edit(Product? current) => Safe(() =>
    {
        var dialog = new ProductWindow(current, store) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (current is not null)
        {
            store.UpdateProduct(dialog.Result);
            Refresh("商品资料已保存"); return;
        }
        var p = dialog.Result; var products = store.Data.Products.Where(x => x.Id != p.Id).Append(p).ToList();
        store.Commit(products, [Movement.For(p, current is null ? "新增" : "编辑", current is null ? p.Quantity : 0, current is null ? "初始库存" : "修改商品资料") with { Counterparty = current is null ? dialog.InitialSource : "", MoneyAmount = current is null ? dialog.InitialMoney : null, UnitPrice = current is null ? p.LastInboundPrice : null, ExpectedAmount = current is null && p.LastInboundPrice.HasValue ? InventoryStore.Expected(p.Quantity, p.LastInboundPrice.Value) : null }]); Refresh("商品已保存");
    });
    private void Delete_Click(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var products = store.Data.Products.Where(p => selectedProductIds.Contains(p.Id)).ToList();
        if (products.Count == 0) throw new InvalidOperationException("请勾选需要删除的商品。");
        var preview = string.Join("\n", products.Take(8).Select(p => $"{p.Name}（{p.Code} / {p.Color} / {p.Specification}）"));
        if (MessageBox.Show(this, $"确定删除选中的 {products.Count} 个商品？\n{preview}\n合计库存：{products.Sum(p => p.Quantity):0.##}。历史记录会保留，本次删除没有自动备份；如需留存，请先取消并导出。", "批量删除确认", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        store.DeleteProducts(products.Select(p => p.Id)); Refresh($"已删除 {products.Count} 个商品");
    });
    private void Export_Click(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var selected = store.Data.Products.Where(p => selectedProductIds.Contains(p.Id)).ToList();
        if (selected.Count == 0) throw new InvalidOperationException("请先勾选需要导出的商品，或点击“未全部选中”全选当前结果。未选择商品，无法导出。");
        var ids = selected.Select(p => p.Id).ToHashSet();
        var exportData = new InventoryData { Products = selected, Movements = store.Data.Movements.Where(m => ids.Contains(m.ProductId)).ToList() };
        var options = new ExportWindow { Owner = this }; if (options.ShowDialog() != true) return;
        var dialog = new SaveFileDialog { Filter = "Excel 工作簿|*.xlsx", FileName = "兴辉库存-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".xlsx" };
        if (dialog.ShowDialog(this) != true) return;
        ExcelService.Export(dialog.FileName, exportData, options.From, options.To);
        Refresh($"已导出选中的 {selected.Count} 款商品");
        MessageBox.Show(this, options.From.HasValue ? "选中商品、图片及这些商品指定日期范围内的日志已导出。" : "选中商品和图片已导出，不含日志。", "导出完成");
    });
    private void Import_Click(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var file = new OpenFileDialog { Filter = "Excel 工作簿|*.xlsx" }; if (file.ShowDialog(this) != true) return;
        List<Product> imported;
        try { imported = ExcelService.Import(file.FileName); }
        catch (ExcelImportException ex) { new ImportErrorsWindow(ex.Message) { Owner = this }.ShowDialog(); return; }
        var dialog = new ImportWindow(imported, store.Data.Products.Count) { Owner = this }; 
        if (dialog.ShowDialog() != true) return;
        var newSuppliers = imported.Select(p => p.Supplier.Trim()).Where(x => x != "").Distinct(StringComparer.OrdinalIgnoreCase).Except(store.Data.Suppliers ?? [], StringComparer.OrdinalIgnoreCase).ToList();
        var newSpecs = imported.Select(p => p.Specification).Distinct().Except(store.Data.Specifications).ToList();
        store.ImportProducts(imported);
        MessageBox.Show(this, $"导入完成。供应商选项新增 {newSuppliers.Count} 项：{string.Join("、", newSuppliers)}\n规格选项新增 {newSpecs.Count} 项：{string.Join("、", newSpecs)}", "导入结果");
        Refresh($"已按名称＋编号＋色号新增 / 覆盖 {imported.Select(InventoryStore.ImportKey).Distinct(StringComparer.OrdinalIgnoreCase).Count()} 个商品");
    });
    private void SelectFiltered_Click(object sender, RoutedEventArgs e)
    {
        selectedProductIds.Clear(); selectedProductIds.UnionWith(filteredProducts.Select(p => p.Id)); Refresh();
    }
    private void ProductPageSize_Click(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (!int.TryParse(ProductPageSizeInput.Text, out var size) || size < 1 || size > 1000) throw new InvalidOperationException("每页条数请输入 1～1000。");
        RememberView(); ProductPageSize = size; productPage = 0; Refresh();
    });
    private void ProductJump_Click(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (!int.TryParse(ProductJumpInput.Text, out var page) || page < 1) throw new InvalidOperationException("请输入正整数页码。");
        RememberView(); productPage = page - 1; Refresh(); ProductJumpInput.Text = (productPage + 1).ToString();
    });
    private void PreviousPage_Click(object sender, RoutedEventArgs e) { RememberView(); productPage--; Refresh(); }
    private void NextPage_Click(object sender, RoutedEventArgs e) { RememberView(); productPage++; Refresh(); }
    private void Product_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(ProductsGrid, source) is DataGridRow row)
        {
            selectedProductIds.Clear(); ProductsGrid.UnselectAll(); row.IsSelected = true; UpdateSelectionCaption();
            e.Handled = true;
            if (row.Item is Product product) Dispatcher.BeginInvoke(new Action(() => { new HistoryWindow(store, product) { Owner = this }.ShowDialog(); Refresh(); }));
        }
        else e.Handled = true;
    }
    private void Finance_Click(object sender, RoutedEventArgs e) => Safe(() => { new FinanceWindow(store) { Owner = this }.ShowDialog(); Refresh(); });
    private void History_Click(object sender, RoutedEventArgs e) { new HistoryWindow(store) { Owner = this }.ShowDialog(); Refresh(); }
    private void ProductHistory_Click(object sender, RoutedEventArgs e) { if (Selected() is { } p) { new HistoryWindow(store, p) { Owner = this }.ShowDialog(); Refresh(); } }
    private void Sales_Click(object sender, RoutedEventArgs e) => Safe(() => new SalesWindow(store) { Owner = this }.ShowDialog());
    private void Settings_Click(object sender, RoutedEventArgs e) => Safe(() => { new SettingsWindow(store) { Owner = this }.ShowDialog(); Refresh(); });
    private void SelectAll_Click(object sender, RoutedEventArgs e) { if (ProductsGrid.SelectedItems.Count == ProductsGrid.Items.Count) ProductsGrid.UnselectAll(); else ProductsGrid.SelectAll(); }
    private void Password_Click(object sender, RoutedEventArgs e) => Safe(() => new PasswordWindow(store) { Owner = this }.ShowDialog());
    private void Folder_Click(object sender, RoutedEventArgs e) => Safe(() => Process.Start(new ProcessStartInfo(store.DirectoryPath) { UseShellExecute = true }));
    private void All_Click(object sender, RoutedEventArgs e) => NavigateInventory(false);
    private void Low_Click(object sender, RoutedEventArgs e) => NavigateInventory(true);
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (IsLoaded && !restoringView) { RememberView(); productPage = 0; Refresh(); } }
    private void Filter_Changed(object sender, RoutedEventArgs e) { if (IsLoaded && !restoringView) { RememberView(); productPage = 0; Refresh(); } }
}
