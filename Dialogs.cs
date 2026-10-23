using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace XingHui;

public sealed class ProductImageConverter : IValueConverter
{
    private static ImageSource? placeholder;
    public static ImageSource Decode(string? data)
    {
        if (!string.IsNullOrEmpty(data))
        {
            using var stream = new MemoryStream(System.Convert.FromBase64String(data));
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 700; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            return bitmap;
        }
        if (placeholder is not null) return placeholder;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(227, 230, 233)), null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
        group.Children.Add(new GeometryDrawing(Brushes.Silver, null, Geometry.Parse("M 15,77 L 40,43 58,61 70,49 87,77 Z")));
        group.Children.Add(new GeometryDrawing(Brushes.Silver, null, new EllipseGeometry(new Point(70, 28), 9, 9)));
        placeholder = new DrawingImage(group); placeholder.Freeze(); return placeholder;
    }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { try { return Decode(value as string); } catch { return Decode(null); } }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    public static string LoadPhoto(string path)
    {
        if (new FileInfo(path).Length > 20_000_000) throw new InvalidDataException("请选择小于 20 MB 的图片。");
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 700; bitmap.UriSource = new Uri(path); bitmap.EndInit();
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 }; encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output);
        if (output.Length > 2_900_000) throw new InvalidDataException("图片过大，请选择较小的图片。");
        return System.Convert.ToBase64String(output.ToArray());
    }
}

public class FormWindow : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(28) };
    protected readonly TextBlock Error = new() { 
        Foreground = Brushes.Firebrick, 
        TextWrapping = TextWrapping.Wrap, 
        Margin = new Thickness(0, 10, 0, 10) };
    public FormWindow(string title, double width = 480)
    {
        Loaded += (_, _) => { if (Owner is not null) Navigation.AddBack(this, Body); };
        Title = title; 
        Width = width; 
        SizeToContent = SizeToContent.Height; 
        MaxHeight = Math.Max(500, SystemParameters.WorkArea.Height - 60); 
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    protected void Heading(string title, string subtitle)
    {
        Body.Children.Add(new TextBlock { Text = title, FontSize = 25, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(25, 53, 56)) });
        Body.Children.Add(new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 10, 0, 18) });
    }
    protected T Field<T>(string label, T control, Panel? parent = null) where T : Control
    {
        parent ??= Body;
        bool required = label.Contains('*');
        parent.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 5), Foreground = required ? Brushes.Firebrick : Brushes.Black, FontWeight = required ? FontWeights.SemiBold : FontWeights.Normal });
        if (required) { control.BorderBrush = Brushes.IndianRed; control.BorderThickness = new Thickness(1.5); control.Background = new SolidColorBrush(Color.FromRgb(255, 249, 246)); }
        parent.Children.Add(control); return control;
    }
    protected void Submit(string caption, Action action, bool clickOnly = false)
    {
        Body.Children.Add(Error);
        var button = new Button { Content = caption, IsDefault = !clickOnly, Focusable = !clickOnly, IsTabStop = !clickOnly, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0) };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { Error.Text = ex.Message; } };
        Body.Children.Add(button);
    }
}

public sealed class LoginWindow : FormWindow
{
    public LoginWindow(InventoryStore store) : base("兴辉库存 · 账户")
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var registering = !store.HasAccount;
        Heading(registering ? "欢迎使用兴辉库存" : "欢迎回来", registering ? "首次使用，请创建本机管理账户。密码至少 8 位，请妥善保管。" : "输入账户与密码，继续管理你的库存。 ");
        var user = Field("账户名", new TextBox { MaxLength = 40, Text = registering ? "" : store.UserName });
        var password = Field("密码", new PasswordInput { MaxLength = 128 });
        if (!registering && store.TryGetRememberedPassword(out var rememberedUser, out var rememberedPassword))
        {
            user.Text = rememberedUser;
            password.Password = rememberedPassword;
        }
        var confirm = registering ? Field("确认密码", new PasswordInput { MaxLength = 128 }) : null;
        
        var question = registering ? Field("忘记密码时的密保问题 *", new TextBox { MaxLength = 200 }) : null;
        var answer = registering ? Field("密保答案 *", new PasswordBox { MaxLength = 200 }) : null;
        var confirmAnswer = registering ? Field("确认密保答案 *", new PasswordBox { MaxLength = 200 }) : null;
        var remember = new CheckBox
        {
            Content = "记住密码，下次自动填入", IsChecked = store.RemembersPassword,
            Margin = new Thickness(0, 14, 0, 0),
            ToolTip = "仅自动填入账户和密码，确认登录后才进入。可在软件设置中清除。"
        };
        remember.Unchecked += (_, _) =>
        {
            try { store.ClearRememberedPassword(); }
            catch (Exception ex) { Error.Text = "清除记住的密码失败：" + ex.Message; remember.IsChecked = true; }
        };
        Body.Children.Add(remember);
        int failed = 0; 
        DateTime retry = DateTime.MinValue;
        Submit(registering ? "创建账户并开始使用" : "登 录", () =>
        {
            if (DateTime.Now < retry) throw new InvalidOperationException("尝试次数较多，请 30 秒后重试。");
            if (registering)
            {
                if (password.Password != confirm!.Password) throw new InvalidDataException("两次输入的密码不一致。");
                if (answer!.Password.Trim() != confirmAnswer!.Password.Trim()) throw new InvalidDataException("两次密保答案不一致。");
                store.Register(user.Text, password.Password, question!.Text, answer.Password);
            }
            else if (!store.Login(user.Text, password.Password))
            {
                if (++failed >= 5) { retry = DateTime.Now.AddSeconds(30); failed = 0; }
                throw new InvalidDataException("账户名或密码不正确。");
            }
            try { store.SetRememberedPassword(user.Text, password.Password, remember.IsChecked == true); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "登录成功，但未能保存记住密码的设置：" + ex.Message + "\n下次可能需要手动登录。", "记住密码", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            DialogResult = true;
        });

        if (!registering)
        {
            var forgot = new Button { Content = "忘记密码？", Margin = new Thickness(0, 14, 0, 0), Background = Brushes.SlateGray };
            forgot.Click += (_, _) => new RecoveryWindow(store) { Owner = this }.ShowDialog(); Body.Children.Add(forgot);
        }
        Loaded += (_, _) => { if (registering) user.Focus(); else password.Focus(); };
    }
}

public sealed class ProductWindow : FormWindow
{
    public Product Result { get; private set; }
    public string InitialSource { get; private set; } = "初始库存";
    public decimal? InitialMoney { get; private set; }
    public ProductWindow(Product? product, InventoryStore store) : base(product is null ? "新增商品" : "编辑商品", 760)
    {
        Result = product?.Copy() ?? new Product { Quantity = 0, LastInboundPrice = 0, LastOutboundPrice = 0 };
        Heading(product is null ? "新增商品" : "商品资料", product is null ? "同一编号可以有不同色号和规格。带 * 为必填项，图片会自动压缩保存。" : "所有资料均可修改；保存保留商品标识和历史流水。库存修改记为调整，不计入收支。单位为整包装名称，数量仍按整包装与散片换算。");
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition()); 
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { Margin = new Thickness(0, 0, 18, 0) }; 
        var right = new StackPanel(); Grid.SetColumn(right, 1); 
        columns.Children.Add(left); 
        columns.Children.Add(right); 
        Body.Children.Add(columns);
        TextBox Input(string label, string value, Panel panel) => Field(label, new TextBox { Text = value, MaxLength = 1000 }, panel);
        var name = Input("名称 *", Result.Name, left); 
        var code = Input("编号 *（保留前导零）", Result.Code, right);
        var color = Input("色号 *", Result.Color, left); 
        var spec = Field("规格 / 尺码 *", new ComboBox { MinHeight = 36 }, right);
        void ReloadSpecs(string selected)
        {
            spec.ItemsSource = store.Data.Specifications.Concat(product is null || string.IsNullOrWhiteSpace(product.Specification) ? Array.Empty<string>() : new[] { product.Specification }).Distinct().ToList();
            spec.SelectedItem = selected;
            if (spec.SelectedIndex < 0 && spec.Items.Count > 0) spec.SelectedIndex = 0;
        }
        ReloadSpecs(Result.Specification);
        var manage = new Button { Content = "添加 / 删除规格", Margin = new Thickness(0, 6, 0, 0) };
        manage.Click += (_, _) => { var selected = spec.SelectedItem as string ?? ""; new SpecificationsWindow(store) { Owner = this }.ShowDialog(); ReloadSpecs(selected); };
        right.Children.Add(manage);
        var quantity = Input(product is null ? "初始库存（固定为 0，请保存后入库）" : "当前库存（包装数量）", Result.Quantity.ToString(CultureInfo.InvariantCulture), left); quantity.IsReadOnly = product is null; NumericInput.Attach(quantity);
        var unit = Input("包装单位", product?.Unit ?? "件", right); unit.IsReadOnly = false;
        var pieces = Input("一件几片 *", Result.PiecesPerCase.ToString(), left);
        var minimum = Input("预警库存 *（等于此值也提醒）", Result.Minimum.ToString(CultureInfo.InvariantCulture), left); 
        //var location = Input("库位", Result.Location, left); 
        var supplier = new ComboBox { MinHeight = 38, BorderBrush = Brushes.IndianRed, BorderThickness = new Thickness(1.5), Background = new SolidColorBrush(Color.FromRgb(255, 249, 246)) };
        void ReloadSuppliers(string selected)
        {
            supplier.ItemsSource = (store.Data.Suppliers ?? []).Concat(product is null ? Array.Empty<string>() : new[] { product.Supplier }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            supplier.SelectedItem = selected;
        }
        ReloadSuppliers(Result.Supplier);
        var manageSupplier = new Button { Content = "管理", Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(6, 0, 0, 0), ToolTip = "添加 / 删除供应商选项" };
        manageSupplier.Click += (_, _) => { var selected = supplier.SelectedItem as string ?? ""; new SuppliersWindow(store) { Owner = this }.ShowDialog(); ReloadSuppliers(selected); };
        manage.Content = "管理"; manage.ToolTip = "添加 / 删除规格选项"; manage.Padding = new Thickness(8, 5, 8, 5);
        NumericInput.Attach(minimum); NumericInput.Attach(pieces, 1);
        var inPrice = Input("上次入库价（自动记录）", (Result.LastInboundPrice ?? 0).ToString(), right); inPrice.IsReadOnly = product is null; NumericInput.Attach(inPrice);
        var outPrice = Input("上次出库价（自动记录）", (Result.LastOutboundPrice ?? 0).ToString(), right); outPrice.IsReadOnly = product is null; NumericInput.Attach(outPrice);
        // Use shared grid rows so labels and inputs on both sides line up.
        left.Children.Clear(); right.Children.Clear(); columns.Children.Clear(); columns.ColumnDefinitions.Clear();
        columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition());
        var fields = new (string Label, Control Input)[] { ("供应商 *", supplier), ("名称 *", name), ("编号 *", code), ("色号 *", color), ("规格 / 尺码 *", spec), (product is null ? "初始库存（固定 0）" : "当前库存", quantity), ("一件几片 *", pieces), ("预警库存 *", minimum), ("上次入库价", inPrice), ("上次出库价", outPrice), ("单位", unit) };
        for (int index = 0; index < fields.Length; index++)
        {
            if (index % 2 == 0) columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var (label, input) = fields[index];
            bool locked = false;
            var cell = new Grid { Margin = new Thickness(index % 2 == 0 ? 0 : 12, 0, index % 2 == 0 ? 12 : 0, 12) };
            cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) }); cell.ColumnDefinitions.Add(new ColumnDefinition());
            cell.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = locked ? Brushes.Gray : label.Contains('*') ? Brushes.Firebrick : Brushes.Black, FontWeight = label.Contains('*') ? FontWeights.SemiBold : FontWeights.Normal });
            input.Margin = new Thickness(0); input.MinHeight = 38;
            if (input == supplier || input == spec)
            {
                var inline = new Grid(); inline.ColumnDefinitions.Add(new ColumnDefinition()); inline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                inline.Children.Add(input);
                var manageButton = input == supplier ? manageSupplier : manage;
                manageButton.IsEnabled = true; manageButton.Margin = new Thickness(6, 0, 0, 0);
                Grid.SetColumn(manageButton, 1); inline.Children.Add(manageButton); Grid.SetColumn(inline, 1); cell.Children.Add(inline);
            }
            else { Grid.SetColumn(input, 1); cell.Children.Add(input); }
            Grid.SetRow(cell, index / 2); Grid.SetColumn(cell, index % 2); columns.Children.Add(cell);
        }
        var notes = Field("备注", new TextBox { Text = Result.Notes, MaxLength = 4000, AcceptsReturn = true, Height = 64, TextWrapping = TextWrapping.Wrap });

        var pictureRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 15, 0, 0) };
        var photo = new Image { Width = 92, Height = 92, Source = ProductImageConverter.Decode(Result.Image), Margin = new Thickness(0, 0, 16, 0) };
        var upload = new Button { Content = "上传图片", VerticalAlignment = VerticalAlignment.Center };
        var clear = new Button { Content = "恢复灰色图片", VerticalAlignment = VerticalAlignment.Center, Background = Brushes.SlateGray };
        upload.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "商品图片|*.jpg;*.jpeg;*.png;*.bmp" };
            if (dialog.ShowDialog(this) != true) return;
            try { var data = ProductImageConverter.LoadPhoto(dialog.FileName); photo.Source = ProductImageConverter.Decode(data); Result.Image = data; Error.Text = ""; } catch (Exception ex) { Error.Text = "图片无法读取：" + ex.Message; }
        };
        clear.Click += (_, _) => { Result.Image = ""; photo.Source = ProductImageConverter.Decode(null); };
        pictureRow.Children.Add(photo); pictureRow.Children.Add(upload); pictureRow.Children.Add(clear); 
        Body.Children.Add(pictureRow);
        Submit("保存商品", () =>
        {
            decimal Number(TextBox box, string label) => decimal.TryParse(box.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : throw new InvalidDataException(label + "必须为数字。");
            if (!int.TryParse(pieces.Text, out var count) || count < 1) throw new InvalidDataException("每件片数必须是正整数。");
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidDataException("名称不能为空。");
            if (string.IsNullOrWhiteSpace(color.Text)) { color.Focus(); throw new InvalidDataException("请填写色号。"); }
            if (string.IsNullOrWhiteSpace(supplier.SelectedItem as string)) { supplier.Focus(); throw new InvalidDataException("请填写供应商。"); }
            InitialMoney = 0; InitialSource = "初始库存为 0";
            var enteredQuantity = product is null ? 0 : Number(quantity, "当前库存");
            // Preserve exact loose pieces when neither packaging nor the displayed quantity was changed.
            var totalPieces = product is not null && count == product.PiecesPerCase && enteredQuantity == product.Quantity ? product.TotalPieces : enteredQuantity * count;
            Result.PiecesPerCase = count;
            Result.Name = name.Text.Trim(); 
            Result.Code = code.Text.Trim(); 
            Result.Color = color.Text.Trim(); 
            Result.Specification = InventoryStore.NormalizeSpecification(spec.SelectedItem as string ?? ""); 
            Result.Unit = unit.Text.Trim();
            if (Result.Unit == "") throw new InvalidDataException("请填写包装单位名称。");
            Result.LastInboundPrice = product is null ? 0 : Number(inPrice, "上次入库价");
            Result.LastOutboundPrice = product is null ? 0 : Number(outPrice, "上次出库价");
            InventoryStore.ValidatePrice(Result.LastInboundPrice.Value); InventoryStore.ValidatePrice(Result.LastOutboundPrice.Value); 
            Result.SetStockPieces(totalPieces); 
            Result.Minimum = Number(minimum, "预警库存"); 
            //Result.Location = location.Text.Trim(); 
            Result.Supplier = ((string)supplier.SelectedItem).Trim();
            Result.Notes = notes.Text.Trim(); Result.Updated = DateTime.Now;
            InventoryStore.Validate([Result]); DialogResult = true;
        });
    }
}

public sealed class StockWindow : FormWindow
{
    public decimal Amount { get; private set; }
    public decimal Pieces { get; private set; }
    public StockPricing? Pricing { get; private set; }
    public string Counterparty { get; private set; } = "";
    public decimal MoneyAmount { get; private set; }
    public decimal UnitPrice { get; private set; }
    public string Note { get; private set; } = "";
    public StockWindow(Product p, bool incoming) : base(incoming ? "商品入库" : "商品出库", 680)
    {
        // Enter completes the current field; stock changes require clicking the save button.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (!e.IsRepeat && Keyboard.FocusedElement is TextBox input)
                input.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        };
        Heading(incoming ? "登记入库" : "登记出库", $"{p.Name} · {p.Code} · {p.Color} · {p.Specification}\n当前库存：{p.StockDisplay} / {p.TotalPieces:0.########} 片，每{p.Unit} {p.PiecesPerCase} 片");
        var supplier = string.IsNullOrWhiteSpace(p.Supplier) ? "未填写供应商" : p.Supplier.Trim();
        if (incoming)
            Body.Children.Add(new TextBlock { Text = $"入库供应商：{supplier}", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var cases = NumericInput.Create();
        var pieces = NumericInput.Create();
        var pricing = new MoneyEditor(cases, pieces, p.PiecesPerCase, incoming ? p.LastInboundPrice : p.LastOutboundPrice, incoming, p.Unit);
        Body.Children.Add(pricing);
        TextBox? counterparty = incoming ? null : Field("客户 / 出库去向 *", new TextBox { MaxLength = 1000 });
        var note = Field("说明 / 单据号（选填）", new TextBox { MaxLength = 1000, AcceptsReturn = true, Height = 68, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var conversion = new TextBlock { Margin = new Thickness(0, 10, 0, 0), Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap };
        Body.Children.Add(conversion);
        decimal ReadPieces()
        {
            if (!decimal.TryParse(cases.Text, out var c) || c < 0 || c > 1000000000 || c != decimal.Truncate(c) || !decimal.TryParse(pieces.Text, out var n) || n < 0 || n > 1000000000000000 || n != decimal.Truncate(n)) throw new InvalidDataException("件数、片数请输入非负整数。可只填其中一项，另一项默认为 0。");
            return c * p.PiecesPerCase + n;
        }
        void ConvertAmount()
        {
            try { var total = ReadPieces(); conversion.Text = $"合计 {total:0} 片，折合 {Product.FormatStock(total, p.PiecesPerCase)}。金额按件和散片分别计价再求和。"; }
            catch (Exception ex) { conversion.Text = ex.Message; }
        }
        cases.TextChanged += (_, _) => ConvertAmount(); pieces.TextChanged += (_, _) => ConvertAmount(); ConvertAmount();
        Body.Children.Add(new TextBlock
        {
            Text = "回车确认当前输入并移至下一项；请点击下方按钮完成登记。",
            Foreground = Brushes.SlateGray, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        Submit("确认并保存", () =>
        {
            var total = ReadPieces(); var n = total / p.PiecesPerCase;
            if (n <= 0 || n > 1000000000) throw new InvalidDataException("合计数量必须大于 0 且不超过十亿件。");
            if (!incoming && string.IsNullOrWhiteSpace(counterparty?.Text)) throw new InvalidDataException("请填写客户或出库去向，方便后续核对。");
            pricing.Validate();
            Pricing = pricing.Pricing; Pieces = total; Amount = n; Note = note.Text.Trim(); Counterparty = incoming ? supplier : counterparty!.Text.Trim(); MoneyAmount = pricing.Actual; UnitPrice = pricing.UnitPrice; DialogResult = true;
        }, clickOnly: true);
    }
}

public sealed class PasswordWindow : FormWindow
{
    public PasswordWindow(InventoryStore store) : base("修改密码")
    {
        Heading("修改账户密码", "新密码至少 8 位。");
        var old = Field("原密码", new PasswordBox()); 
        var next = Field("新密码", new PasswordBox { MaxLength = 128 }); 
        var confirm = Field("确认新密码", new PasswordBox { MaxLength = 128 });
        Submit("保存新密码", () => { 
            if (next.Password != confirm.Password) throw new InvalidDataException("两次输入的新密码不一致。");
            store.ChangePassword(old.Password, next.Password); DialogResult = true; });
    }
}

public sealed class ImportWindow : FormWindow
{
    public ImportWindow(List<Product> products, int existing) : base("确认导入库存", 900)
    {
        var final = products.GroupBy(InventoryStore.ImportKey, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToList();
        Heading("按名称＋编号＋色号新增 / 覆盖", $"文件共 {products.Count} 行，按三项匹配合并后 {final.Count} 个商品；系统当前有 {existing} 个商品。文件内三项相同的记录以最后一行为准。");
        var table = new PagedTable(20, ("供应商", "Supplier"), ("名称", "Name"), ("编号", "Code"), ("色号", "Color"), ("规格", "Specification"), ("库存（件）", "StockDisplay"), ("片/件", "PiecesPerCase"), ("自动补齐说明", "ImportNotice"));
        table.Height = 330; table.SetRows(final); Body.Children.Add(table);
        if (products.Any(p => p.ImportNotice != ""))
            Body.Children.Add(new TextBlock { Text = $"有 {products.Count(p => p.ImportNotice != "")} 行已自动补齐，请核对“自动补齐说明”列。名称默认为“未导入名称”，缺失编号生成独立编号，规格必须为宽*长格式，库存/预警/单价为 0，每件片数为 1。请按实际包装修正后再出入库。", Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        Body.Children.Add(new TextBlock { Text = "名称、编号、色号三项均相同才覆盖原行资料、库存和图片；任一项不同则新增。比较时忽略各项首尾空格及英文大小写，编号保留前导零。供应商、规格不参与匹配，三项相同但规格不同也会覆盖。未出现在文件中的商品保留。历史日志不重复导入，也不记为收入或支出。没有自动备份，需要留存请先取消并手动导出。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 14, 0, 0) });
        Submit("确认按三项匹配导入", () => DialogResult = true);
    }
}

public sealed class ImportErrorsWindow : FormWindow
{
    public ImportErrorsWindow(string report) : base("Excel 导入检查", 850)
    {
        Heading("请调整 Excel 后重新导入", "本次未导入任何商品。可以复制以下问题清单，按工作表、行号和列名逐项修改。");
        Body.Children.Add(new TextBox { Text = report, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = 350, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Foreground = Brushes.Firebrick });
        var template = new Button { Content = "另存一份正确的空白模板", Margin = new Thickness(0, 12, 0, 12) };
        template.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "Excel 工作簿|*.xlsx", FileName = "库存导入模板.xlsx" };
            if (dialog.ShowDialog(this) != true) return;
            try { ExcelService.Export(dialog.FileName, new InventoryData()); MessageBox.Show(this, "模板已保存。请保留第一行表头，从第二行填写商品。", "模板已保存"); }
            catch (Exception ex) { Error.Text = ex.Message; }
        };
        Body.Children.Add(template); Submit("返回修改文件", Close);
    }
}
