using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace XingHui;

public partial class Dashboard
{
    private readonly HashSet<string> selectedProductIds = [];
    private bool updatingSelection, restoringView;
    private string sortMember = nameof(Product.Code);
    private ListSortDirection sortDirection = ListSortDirection.Ascending;
    private sealed record ViewPosition(string Query, bool Low, int Page, int Size, string Sort, ListSortDirection Direction, string[] Selected, double Horizontal, double Vertical, string? Supplier, string? Specification, ProductSearchRule[] Rules, bool AnyRule);
    private readonly Stack<ViewPosition> viewHistory = new();
    private ViewPosition? lastView;
    private static string ColumnMember(DataGridColumn column) => !string.IsNullOrEmpty(column.SortMemberPath) ? column.SortMemberPath : (column as DataGridBoundColumn)?.Binding is Binding binding ? binding.Path.Path : "";
    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer scroll) return scroll;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (FindScroll(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
    private ViewPosition CaptureView()
    {
        var scroll = FindScroll(ProductsGrid);
        return new(Search.Text, LowOnly.IsChecked == true, productPage, ProductPageSize, sortMember, sortDirection, selectedProductIds.ToArray(), scroll?.HorizontalOffset ?? 0, scroll?.VerticalOffset ?? 0, supplierFilter, specificationFilter, searchRules.ToArray(), anySearchRule);
    }
    private void RememberView()
    {
        if (restoringView || lastView is null) return;
        var scroll = FindScroll(ProductsGrid);
        viewHistory.Push(lastView with { Selected = selectedProductIds.ToArray(), Horizontal = scroll?.HorizontalOffset ?? 0, Vertical = scroll?.VerticalOffset ?? 0 });
    }
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (!viewHistory.TryPop(out var view)) return;
        restoringView = true;
        try
        {
            supplierFilter = view.Supplier; specificationFilter = view.Specification; searchRules = view.Rules.ToList(); anySearchRule = view.AnyRule;
            Search.Text = view.Query; LowOnly.IsChecked = view.Low; productPage = view.Page; ProductPageSize = view.Size; sortMember = view.Sort; sortDirection = view.Direction;
            selectedProductIds.Clear(); selectedProductIds.UnionWith(view.Selected); Refresh();
            Dispatcher.BeginInvoke(new Action(() => { var scroll = FindScroll(ProductsGrid); scroll?.ScrollToHorizontalOffset(view.Horizontal); scroll?.ScrollToVerticalOffset(view.Vertical); }));
        }
        finally { restoringView = false; }
    }
    private void NavigateInventory(bool low)
    {
        RememberView(); restoringView = true;
        try { supplierFilter = specificationFilter = null; searchRules.Clear(); anySearchRule = false; productPage = 0; Search.Text = ""; LowOnly.IsChecked = low; Refresh(); }
        finally { restoringView = false; }
    }
    private void Products_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true; var member = ColumnMember(e.Column);
        if (typeof(Product).GetProperty(member) is null) return;
        RememberView(); sortDirection = sortMember == member && sortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        sortMember = member; productPage = 0; Refresh();
    }
    private void Products_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingSelection) return;
        foreach (Product p in e.RemovedItems) selectedProductIds.Remove(p.Id);
        foreach (Product p in e.AddedItems) selectedProductIds.Add(p.Id);
        UpdateSelectionCaption();
    }
    private void UpdateSelectionCaption()
    {
        if (SelectionText is not null) SelectionText.Text = $"已选 {selectedProductIds.Count} 款（当前筛选范围内跨页保留）";
        if (ToggleSelection is not null)
        {
            bool allSelected = filteredProducts.Count > 0 && filteredProducts.All(p => selectedProductIds.Contains(p.Id));
            ToggleSelection.Content = allSelected ? "已全部选中" : "未全部选中";
            ToggleSelection.Background = allSelected ? Brushes.Firebrick : Brushes.SlateGray;
            ToggleSelection.Foreground = Brushes.White;
            ToggleSelection.IsEnabled = filteredProducts.Count > 0;
        }
    }
    private void ToggleSelection_Click(object sender, RoutedEventArgs e)
    {
        bool allSelected = filteredProducts.Count > 0 && filteredProducts.All(p => selectedProductIds.Contains(p.Id));
        selectedProductIds.Clear();
        if (!allSelected) selectedProductIds.UnionWith(filteredProducts.Select(p => p.Id));
        Refresh();
    }
    private void SelectEverything_Click(object sender, RoutedEventArgs e) { selectedProductIds.UnionWith(store.Data.Products.Select(p => p.Id)); Refresh(); }
    private void ClearSelection_Click(object sender, RoutedEventArgs e) { selectedProductIds.Clear(); ProductsGrid.UnselectAll(); UpdateSelectionCaption(); }
}
