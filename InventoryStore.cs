using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace XingHui;

public sealed class Product
{
    [System.Text.Json.Serialization.JsonIgnore] public int DisplayIndex { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [System.Text.Json.Serialization.JsonIgnore] public string ImportNotice { get; set; } = "";
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Color { get; set; } = "";
    public string Specification { get; set; } = "";
    public string Unit { get; set; } = "件";
    public int PiecesPerCase { get; set; } = 1;
    [System.Text.Json.Serialization.JsonIgnore] public decimal TotalPieces => ExactPieces ?? Quantity * PiecesPerCase;
    public decimal Quantity { get; set; }
    public decimal? ExactPieces { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string StockDisplay => FormatStock(TotalPieces, PiecesPerCase);
    public static string FormatStock(decimal totalPieces, int pack)
    {
        var cases = decimal.Floor(totalPieces / pack);
        return FormattableString.Invariant($"{cases:0}（+{totalPieces - cases * pack:0.############################}片）");
    }
    public void SetStockPieces(decimal pieces) { ExactPieces = pieces; Quantity = pieces / PiecesPerCase; }
    public decimal Minimum { get; set; } = 0;
    // Legacy JSON compatibility only. New transactions use their own unit price.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public decimal Price { get; set; }
    public decimal? LastInboundPrice { get; set; }
    public decimal? LastOutboundPrice { get; set; }
    public string Location { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Image { get; set; } = "";
    public DateTime Updated { get; set; } = DateTime.Now;
    [System.Text.Json.Serialization.JsonIgnore] public bool Low => Quantity <= Minimum;
    [System.Text.Json.Serialization.JsonIgnore] public string Status => Quantity < 0 ? "超售待补货" : Quantity == 0 ? "缺货" : Low ? "库存不足" : "库存充足";
    public Product Copy() => (Product)MemberwiseClone();
}
public sealed record Movement(DateTime Time, string Code, string Name, string Action, decimal Change, decimal Balance, string Note)
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public bool BlocksPriorUndo { get; init; }
    public bool PriceOverride { get; init; }
    public decimal? EditedInboundPrice { get; init; }
    public decimal? EditedOutboundPrice { get; init; }
    public bool IsCancelled { get; init; }
    public string ReversesId { get; init; } = "";
    public string Supplier { get; init; } = "";
    public decimal? UnitPrice { get; init; }
    public decimal? ExpectedAmount { get; init; }
    public decimal? CaseCount { get; init; }
    public decimal? LoosePieceCount { get; init; }
    public decimal? PieceUnitPrice { get; init; }
    public bool PiecePriceAutomatic { get; init; }
    public string RecordStatus => IsCancelled ? "已撤销" : ReversesId != "" ? "撤销记录" : "有效";
    public string ProductId { get; init; } = "";
    public string Color { get; init; } = "";
    public string Specification { get; init; } = "";
    public int PiecesPerCase { get; init; } = 1;
    public string Unit { get; init; } = "件";
    public string Counterparty { get; init; } = "";
    public decimal? MoneyAmount { get; init; }
    public decimal Income => !IsCancelled && Action == "出库" ? MoneyAmount ?? 0 : 0;
    public decimal Expense => !IsCancelled && (Action is "入库" or "新增") ? MoneyAmount ?? 0 : 0;
    public decimal? ExactChangedPieces { get; init; }
    public decimal ChangedPieces => ExactChangedPieces ?? Change * PiecesPerCase;
    public static Movement For(Product p, string action, decimal change, string note) => new(DateTime.Now, p.Code, p.Name, action, change, p.Quantity, note) { ProductId = p.Id, Supplier = p.Supplier, Color = p.Color, Specification = p.Specification, PiecesPerCase = p.PiecesPerCase, Unit = p.Unit };
}
public sealed class InventoryData
{
    public int Version { get; set; } = 1;
    public List<Product> Products { get; set; } = [];
    public List<string> Specifications { get; set; } = ["800*800", "750*1500"];
    public List<string>? Suppliers { get; set; }
    public List<Movement> Movements { get; set; } = [];
}
public sealed record Account(string User, string Salt, string Hash)
{
    public string Question { get; init; } = "";
    public string AnswerSalt { get; init; } = "";
    public string AnswerHash { get; init; } = "";
}
public sealed record AppSettings(string InventoryPath, string RecoveryPassword)
{
    public string RememberedLogin { get; init; } = "";
}

public sealed class InventoryStore
{
    private readonly string startupDirectory;
    private string LocationPath => Path.Combine(startupDirectory, "storage-location.json");
    public string ConfigDirectory { get; private set; }
    public string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");
    public string DefaultDataPath => Path.Combine(ConfigDirectory, "inventory.json");
    public string DirectoryPath => Path.GetDirectoryName(DataPath)!;
    public string DataPath => Settings.InventoryPath;
    private AppSettings Settings { get; set; }
    private string AccountPath => Path.Combine(ConfigDirectory, "account.json");
    public bool HasAccount => File.Exists(AccountPath);
    public InventoryData Data { get; private set; } = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public InventoryStore(string? directory = null) //构造函数
    {
        startupDirectory = Path.GetFullPath(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XingHuiInventory"));
        ConfigDirectory = startupDirectory;
        if (File.Exists(LocationPath))
        {
            var location = JsonSerializer.Deserialize<string>(File.ReadAllText(LocationPath));
            if (string.IsNullOrWhiteSpace(location) || !Path.IsPathFullyQualified(location))
                throw new InvalidDataException("数据目录位置记录无效：" + LocationPath);
            ConfigDirectory = Path.GetFullPath(location);
            if (!File.Exists(SettingsPath) || !File.Exists(AccountPath))
                throw new IOException("迁移后的账户或配置文件不可访问，请连接对应磁盘并检查目录：" + ConfigDirectory + "。原目录不会自动重新注册账户。");
        }
        else Directory.CreateDirectory(ConfigDirectory);
        Settings = File.Exists(SettingsPath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? throw new InvalidDataException("设置文件损坏。") : new AppSettings(DefaultDataPath, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        if (!Path.IsPathFullyQualified(Settings.InventoryPath) || string.IsNullOrWhiteSpace(Settings.RecoveryPassword)) throw new InvalidDataException("设置中的保存路径或恢复密码无效。");
        if (!File.Exists(SettingsPath)) AtomicWrite(SettingsPath, JsonSerializer.Serialize(Settings, Json));
    }
    public void Load()
    {
        if (!File.Exists(DataPath))
        {
            if (!string.Equals(DataPath, DefaultDataPath, StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException("自定义库存文件不可访问，请检查磁盘或在 settings.json 中修正 InventoryPath。", DataPath);
            return;
        }
        Data = ReadInventoryFile(DataPath);
    }
    private static InventoryData ReadInventoryFile(string path)
    {
        var content = File.ReadAllText(path);
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("Products", out var products) || products.ValueKind != JsonValueKind.Array
            || !document.RootElement.TryGetProperty("Movements", out var movements) || movements.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("请选择库存 JSON 文件，不能选择账户、配置或 Excel 文件。");
        var data = JsonSerializer.Deserialize<InventoryData>(content) ?? throw new InvalidDataException("库存文件为空，请选择有效文件。");
        if (data.Version != 1 || data.Products is null || data.Movements is null) throw new InvalidDataException("库存文件格式不正确。");
        foreach (var p in data.Products) UpgradeProduct(p);
        data.Specifications ??= ["800*800", "750*1500"];
        data.Suppliers ??= data.Products.Select(p => p.Supplier.Trim()).Where(x => x != "").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        Validate(data.Products);
        return data;
    }
    public void OpenExistingDataFile(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("请选择完整的库存文件路径。");
        path = Path.GetFullPath(path);
        // Validate before persisting the new location. Never overwrite the chosen file.
        var data = ReadInventoryFile(path);
        var next = Settings with { InventoryPath = path };
        AtomicWrite(SettingsPath, JsonSerializer.Serialize(next, Json));
        Settings = next;
        Data = data;
    }
    public static void Validate(List<Product> products)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>();
        foreach (var p in products)
        {
            if (string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Code)) throw new InvalidDataException("名称和编号不能为空。");
            if (p.Minimum < 0 || p.Price < 0) throw new InvalidDataException($"{p.Code}：预警值、单价不能为负数。");
            if (p.LastInboundPrice is < 0 or > 1000000000 || p.LastOutboundPrice is < 0 or > 1000000000) throw new InvalidDataException("上次出入库单价无效。");
            if (p.PiecesPerCase < 1 || p.PiecesPerCase > 1000000) throw new InvalidDataException($"{p.Code}：每件片数必须为 1～1000000 的整数。");
            if (p.Quantity < -1000000000 || p.Quantity > 1000000000 || p.Minimum > 1000000000 || p.Price > 1000000000) throw new InvalidDataException($"{p.Code}：数值不能超过十亿。");
            if (p.ExactPieces.HasValue && (Math.Abs(p.ExactPieces.Value) > 1000000000m * p.PiecesPerCase || Math.Abs(p.ExactPieces.Value / p.PiecesPerCase - p.Quantity) > 0.000000000001m)) throw new InvalidDataException("精确片数与库存件数不一致。");
            if (!keys.Add(p.Name.Trim() + "\u001f" + p.Code.Trim() + "\u001f" + p.Color.Trim() + "\u001f" + p.Specification.Trim())) throw new InvalidDataException($"商品 {p.Name} 的相同编号、色号、规格重复了。");
            if (string.IsNullOrWhiteSpace(p.Id) || !ids.Add(p.Id)) throw new InvalidDataException("商品标识重复或为空。");
            if (p.Image.Length > 4000000) throw new InvalidDataException("图片过大，请重新上传。");
        }
    }
    public void Commit(List<Product> products, IEnumerable<Movement> movements)
    {
        Validate(products);
        Save(products, Data.Movements.Concat(movements).ToList());
    }
    private void Save(List<Product> products, List<Movement> movements, List<string>? specifications = null, List<string>? suppliers = null)
    {
        Validate(products);
        var next = new InventoryData { Products = products, Movements = movements, Specifications = specifications ?? Data.Specifications.ToList(), Suppliers = suppliers ?? (Data.Suppliers ?? []).Concat(products.Where(p => !Data.Products.Any(old => old.Id == p.Id && old.Supplier == p.Supplier)).Select(p => p.Supplier.Trim()).Where(x => x != "")).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
        AtomicWrite(DataPath, JsonSerializer.Serialize(next, Json));
        Data = next;
    }
    private void AtomicWrite(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        try
        {
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public void Register(string user, string password, string question, string answer)
    {
        if (HasAccount) throw new InvalidOperationException("已注册，请登录。");
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(answer)) throw new InvalidDataException("注册时必须设置密保问题和答案。");
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(answer.Trim(), salt, 210000, HashAlgorithmName.SHA256, 32);
        SetAccount(user, password, new Account(user.Trim(), "", "") { Question = question.Trim(), AnswerSalt = Convert.ToBase64String(salt), AnswerHash = Convert.ToBase64String(hash) });
    }
    private void SetAccount(string user, string password, Account? initial = null)
    {
        if (user.Trim().Length < 2 || user.Trim().Length > 40) throw new InvalidDataException("账户名需要 2～40 个字符。");
        if (password.Length < 8 || password.Length > 128) throw new InvalidDataException("密码需要 8～128 个字符。");
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32);
        var account = HasAccount ? ReadAccount() : initial ?? new Account(user.Trim(), "", "");
        ClearRememberedPassword();
        AtomicWrite(AccountPath, JsonSerializer.Serialize(account with { User = user.Trim(), Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(hash) }, Json));
    }
    public bool Login(string user, string password)
    {
        var account = JsonSerializer.Deserialize<Account>(File.ReadAllText(AccountPath)) ?? throw new InvalidDataException("账户文件损坏。");
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(account.Salt), 210000, HashAlgorithmName.SHA256, 32);
        return string.Equals(user.Trim(), account.User, StringComparison.OrdinalIgnoreCase) && CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(account.Hash));
    }
    public bool RemembersPassword => !string.IsNullOrEmpty(Settings.RememberedLogin);
    public void SetRememberedPassword(string user, string password, bool remember)
    {
        if (!remember) { ClearRememberedPassword(); return; }
        if (!Login(user, password)) throw new InvalidDataException("账户或密码不正确，无法记住密码。");
        var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { user.Trim(), password }));
        try
        {
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            var next = Settings with { RememberedLogin = Convert.ToBase64String(encrypted) };
            AtomicWrite(SettingsPath, JsonSerializer.Serialize(next, Json));
            Settings = next;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void ClearRememberedPassword()
    {
        if (!RemembersPassword) return;
        var next = Settings with { RememberedLogin = "" };
        AtomicWrite(SettingsPath, JsonSerializer.Serialize(next, Json));
        Settings = next;
    }
    public bool TryGetRememberedPassword(out string user, out string password)
    {
        user = ""; password = "";
        if (!HasAccount || !RemembersPassword) return false;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(Settings.RememberedLogin), null, DataProtectionScope.CurrentUser);
            try
            {
                var credentials = JsonSerializer.Deserialize<string[]>(bytes);
                if (credentials is not { Length: 2 } || !Login(credentials[0], credentials[1])) return false;
                user = credentials[0]; password = credentials[1];
                return true;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false; // A moved, damaged or expired credential falls back to manual login.
        }
    }
    public string UserName => JsonSerializer.Deserialize<Account>(File.ReadAllText(AccountPath))!.User;
    public void ChangePassword(string oldPassword, string newPassword)
    {
        if (!Login(UserName, oldPassword)) throw new InvalidDataException("原密码不正确。");
        SetAccount(UserName, newPassword);
    }
    private Account ReadAccount() => JsonSerializer.Deserialize<Account>(File.ReadAllText(AccountPath)) ?? throw new InvalidDataException("账户文件损坏。");
    public string RecoveryQuestion => ReadAccount().Question;
    public void SetRecoveryQuestion(string password, string question, string answer)
    {
        if (!Login(UserName, password)) throw new InvalidDataException("当前密码不正确。");
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(answer)) throw new InvalidDataException("问题和答案不能为空。");
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(answer.Trim(), salt, 210000, HashAlgorithmName.SHA256, 32);
        AtomicWrite(AccountPath, JsonSerializer.Serialize(ReadAccount() with { Question = question.Trim(), AnswerSalt = Convert.ToBase64String(salt), AnswerHash = Convert.ToBase64String(hash) }, Json));
    }
    public void RecoverPassword(string proof, bool useQuestion, string newPassword)
    {
        var account = ReadAccount();
        bool valid;
        if (useQuestion)
        {
            valid = account.AnswerHash != "" && CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(proof.Trim(), Convert.FromBase64String(account.AnswerSalt), 210000, HashAlgorithmName.SHA256, 32), Convert.FromBase64String(account.AnswerHash));
        }
        else valid = CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(proof.Trim()), System.Text.Encoding.UTF8.GetBytes(Settings.RecoveryPassword));
        if (!valid) throw new InvalidDataException("密保答案或恢复密码不正确。");
        SetAccount(account.User, newPassword);
    }
    public void MoveDataFile(string destination)
    {
        if (!Path.IsPathFullyQualified(destination)) throw new InvalidDataException("请选择完整的文件路径。");
        destination = Path.GetFullPath(destination);
        if (string.Equals(destination, DataPath, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(Path.GetExtension(destination), ".json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("库存文件扩展名必须为 .json。");
        var targetDirectory = Path.GetDirectoryName(destination)!;
        var sameDirectory = string.Equals(targetDirectory, ConfigDirectory, StringComparison.OrdinalIgnoreCase);
        var targetSettings = Path.Combine(targetDirectory, "settings.json");
        var targetAccount = Path.Combine(targetDirectory, "account.json");
        if (new[] { "settings.json", "account.json", "storage-location.json" }.Contains(Path.GetFileName(destination), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("库存文件不能使用账户、配置或位置记录的文件名。");
        if (File.Exists(destination) || (!sameDirectory && (File.Exists(targetSettings) || File.Exists(targetAccount) || File.Exists(Path.Combine(targetDirectory, "storage-location.json")))))
            throw new InvalidDataException("目标目录已有库存、账户或配置文件，请选择一个新的专用文件夹，避免覆盖数据。");
        if (!HasAccount) throw new InvalidOperationException("请先注册账户再迁移数据目录。");
        Directory.CreateDirectory(targetDirectory);
        var created = new List<string>();
        var next = Settings with { InventoryPath = destination };
        void CopyNew(string path, string content)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created.Add(path);
                using var writer = new StreamWriter(stream);
                writer.Write(content);
            }
            if (File.ReadAllText(path) != content) throw new IOException("迁移文件校验失败：" + path);
        }
        try
        {
            CopyNew(destination, JsonSerializer.Serialize(Data, Json));
            if (sameDirectory)
                AtomicWrite(SettingsPath, JsonSerializer.Serialize(next, Json));
            else
            {
                CopyNew(targetAccount, File.ReadAllText(AccountPath));
                CopyNew(targetSettings, JsonSerializer.Serialize(next, Json));
                // Switch the startup pointer only after every managed file has been verified.
                AtomicWrite(LocationPath, JsonSerializer.Serialize(targetDirectory, Json));
            }
        }
        catch
        {
            foreach (var path in created)
                try { File.Delete(path); } catch { /* Keep the original exception; source files remain untouched. */ }
            throw;
        }
        ConfigDirectory = targetDirectory;
        Settings = next;
    }
    public List<Movement> History(Product product) => Data.Movements.Where(m => m.ProductId == product.Id).Reverse().ToList();
    public void AdjustStock(string id, decimal amount, bool incoming, string note, string counterparty, decimal moneyAmount, decimal unitPrice, decimal? exactPieces = null, StockPricing? pricing = null)
    {
        var p = Data.Products.Single(p => p.Id == id).Copy();
        if (amount <= 0 || amount > 1000000000) throw new InvalidDataException("数量必须大于 0 且不超过十亿。");
        if (string.IsNullOrWhiteSpace(counterparty)) throw new InvalidDataException(incoming ? "请填写入库来源。" : "请填写出库去处。");
        if (moneyAmount < 0 || moneyAmount > 1000000000000000000m || decimal.Round(moneyAmount, 2) != moneyAmount) throw new InvalidDataException("金额必须为非负数且最多保留两位小数。");
        ValidatePrice(unitPrice);
        var change = incoming ? amount : -amount;
        if (incoming) p.LastInboundPrice = unitPrice; else p.LastOutboundPrice = unitPrice;
        var pieces = exactPieces ?? amount * p.PiecesPerCase;
        if (pieces <= 0 || pieces > 1000000000m * p.PiecesPerCase || Math.Abs(pieces / p.PiecesPerCase - amount) > 0.000000000001m) throw new InvalidDataException("件数与片数不一致。");
        decimal expected = Expected(amount, unitPrice);
        if (pricing is not null)
        {
            pricing.Validate(p.PiecesPerCase);
            if (pricing.Cases * p.PiecesPerCase + pricing.LoosePieces != pieces) throw new InvalidDataException("计价件数、散片数与库存数量不一致。");
            expected = pricing.Expected(unitPrice, p.PiecesPerCase);
        }
        p.SetStockPieces(p.TotalPieces + (incoming ? pieces : -pieces)); p.Updated = DateTime.Now;
        Commit(Data.Products.Select(x => x.Id == id ? p : x).ToList(), [Movement.For(p, incoming ? "入库" : "出库", change, note) with { Counterparty = counterparty.Trim(), MoneyAmount = moneyAmount, UnitPrice = unitPrice, ExpectedAmount = expected, CaseCount = pricing?.Cases, LoosePieceCount = pricing?.LoosePieces, PieceUnitPrice = pricing?.PiecePrice(unitPrice, p.PiecesPerCase), PiecePriceAutomatic = pricing is not null && pricing.OverridePiecePrice is null, ExactChangedPieces = incoming ? pieces : -pieces }]);
    }
    public void UpdateProduct(Product edited)
    {
        var original = Data.Products.Single(p => p.Id == edited.Id);
        var changed = edited.Copy(); changed.Updated = DateTime.Now;
        changed.Name = changed.Name.Trim(); changed.Code = changed.Code.Trim(); changed.Color = changed.Color.Trim(); changed.Supplier = changed.Supplier.Trim(); changed.Unit = changed.Unit.Trim();
        if (changed.Name == "" || changed.Code == "" || changed.Color == "" || changed.Supplier == "" || changed.Unit == "") throw new InvalidDataException("名称、编号、色号、供应商和单位不能为空。");
        changed.Specification = NormalizeSpecification(changed.Specification);
        ValidatePrice(changed.LastInboundPrice ?? 0); ValidatePrice(changed.LastOutboundPrice ?? 0);
        var stockChanged = changed.TotalPieces != original.TotalPieces || changed.PiecesPerCase != original.PiecesPerCase || changed.Unit != original.Unit;
        var fields = new List<string>();
        foreach (var field in new[] { nameof(Product.Supplier), nameof(Product.Name), nameof(Product.Code), nameof(Product.Color), nameof(Product.Specification), nameof(Product.Unit), nameof(Product.PiecesPerCase), nameof(Product.Quantity), nameof(Product.Minimum), nameof(Product.LastInboundPrice), nameof(Product.LastOutboundPrice), nameof(Product.Notes) })
        {
            var property = typeof(Product).GetProperty(field)!;
            if (!Equals(property.GetValue(original), property.GetValue(changed))) fields.Add($"{field}: {property.GetValue(original)} → {property.GetValue(changed)}");
        }
        if (original.Image != changed.Image) fields.Add("图片已修改");
        if (fields.Count == 0 && !stockChanged) return;
        var log = Movement.For(changed, "编辑", stockChanged ? (changed.TotalPieces - original.TotalPieces) / changed.PiecesPerCase : 0,
            "修改商品资料（不计收支）。" + string.Join("；", fields)) with
        { ExactChangedPieces = changed.TotalPieces - original.TotalPieces, BlocksPriorUndo = stockChanged, PriceOverride = original.LastInboundPrice != changed.LastInboundPrice || original.LastOutboundPrice != changed.LastOutboundPrice, EditedInboundPrice = original.LastInboundPrice != changed.LastInboundPrice ? changed.LastInboundPrice : null, EditedOutboundPrice = original.LastOutboundPrice != changed.LastOutboundPrice ? changed.LastOutboundPrice : null };
        Save(Data.Products.Select(p => p.Id == changed.Id ? changed : p).ToList(), Data.Movements.Append(log).ToList(),
            Data.Specifications.Append(changed.Specification).Distinct().ToList(),
            (Data.Suppliers ?? []).Append(changed.Supplier).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }
    public void UpdateProductDisplay(string id, decimal minimum, string image)
    {
        var current = Data.Products.Single(p => p.Id == id);
        var changed = current.Copy();
        changed.Minimum = minimum; changed.Image = image; changed.Updated = DateTime.Now;
        Commit(Data.Products.Select(p => p.Id == id ? changed : p).ToList(),
            [Movement.For(changed, "编辑", 0, "修改预警库存或商品图片")]);
    }

    public static string ImportKey(Product p)
    {
        var name = p.Name.Trim(); var code = p.Code.Trim(); var color = p.Color.Trim();
        return $"{name.Length}:{name}{code.Length}:{code}{color.Length}:{color}";
    }
    public void ImportProducts(IEnumerable<Product> imported)
    {
        var source = imported.Select(p => p.Copy()).ToList();
        foreach (var item in source) { UpgradeProduct(item); item.Specification = NormalizeSpecification(item.Specification); }
        // Match name + code + color; the last matching workbook row wins.
        var incoming = source.GroupBy(ImportKey, StringComparer.OrdinalIgnoreCase).Select(g => g.Last().Copy()).ToList();
        Validate(incoming);
        var next = Data.Products.Select(p => p.Copy()).ToList();
        var logs = new List<Movement>();
        foreach (var item in incoming)
        {
            var old = next.Where(p => string.Equals(ImportKey(p), ImportKey(item), StringComparison.OrdinalIgnoreCase)).ToList();
            next.RemoveAll(p => old.Any(x => x.Id == p.Id));
            item.Id = old.FirstOrDefault()?.Id ?? Guid.NewGuid().ToString("N");
            item.Name = item.Name.Trim(); item.Updated = DateTime.Now;
            next.Add(item);
            logs.Add(Movement.For(item, "导入", item.Quantity - (old.FirstOrDefault()?.Quantity ?? 0), "按名称＋编号＋色号新增或覆盖；不计为收支"));
            foreach (var removed in old.Skip(1)) logs.Add(Movement.For(removed, "匹配合并", -removed.Quantity, "导入名称、编号、色号均相同的商品，合并重复行并保留原流水") with { Balance = 0 });
        }
        Save(next, Data.Movements.Concat(logs).ToList(),
            Data.Specifications.Concat(source.Select(p => p.Specification)).Distinct().ToList(),
            (Data.Suppliers ?? []).Concat(source.Select(p => p.Supplier.Trim()).Where(x => x != "")).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }
    public static void UpgradeProduct(Product p)
    {
        var prefix = "(" + p.Supplier + ") ";
        if (p.Name.StartsWith(prefix, StringComparison.Ordinal)) p.Name = p.Name[prefix.Length..];
        if (p.LastInboundPrice is null && p.Price != 0) p.LastInboundPrice = p.Price;
        p.Price = 0; p.LastInboundPrice ??= 0; p.LastOutboundPrice ??= 0;
    }
    public static void ValidatePrice(decimal price)
    {
        if (price < 0 || price > 1000000000 || decimal.Round(price, 4) != price) throw new InvalidDataException("单价须为 0～十亿，最多四位小数。");
    }
    public static decimal Expected(decimal quantity, decimal price) => decimal.Round(quantity * price, 2, MidpointRounding.AwayFromZero);
    public void UndoMovement(string movementId, string reason) => UndoMovements([movementId], reason);

    public void UndoMovements(IEnumerable<string> movementIds, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidDataException("请填写撤销原因。");
        var ids = movementIds.ToHashSet();
        if (ids.Count == 0) throw new InvalidDataException("请选择要撤销的出入库记录。");
        var indexes = ids.Select(id => Data.Movements.FindIndex(m => m.Id == id)).OrderDescending().ToList();
        if (indexes.Any(i => i < 0)) throw new InvalidDataException("找不到原记录。");
        // 在副本上从新到旧模拟，任何一笔失败都不会改变内存或文件。
        var products = Data.Products.Select(p => p.Copy()).ToList();
        var logs = Data.Movements.ToList();
        foreach (var index in indexes)
        {
            var original = logs[index];
            if (original.IsCancelled || original.Action is not ("入库" or "出库" or "新增")) throw new InvalidDataException($"{original.Name}：仅有效出入库或初始库存记录可以撤销。");
            var changed = products.FirstOrDefault(p => p.Id == original.ProductId) ?? throw new InvalidDataException("原商品已不存在或旧记录没有商品标识，不能自动撤销。");
            if (Data.Movements.Skip(index + 1).Any(m => m.ProductId == changed.Id && m.BlocksPriorUndo)) throw new InvalidDataException("此记录之后库存、包装或单位已被人工调整，不能再撤销调整前的记录。请通过新的出入库纠正。");
            if (changed.Unit != original.Unit || changed.PiecesPerCase != original.PiecesPerCase) throw new InvalidDataException("商品单位或每件片数已改变，不能自动撤销。");
            if (Data.Movements.Skip(index + 1).Any(m => m.ProductId == original.ProductId && (m.Action is "导入" or "同名合并" or "匹配合并"))) throw new InvalidDataException("此记录之后库存已被导入覆盖，不能自动反向调整。");
            changed.SetStockPieces(changed.TotalPieces - original.ChangedPieces); changed.Updated = DateTime.Now;
            logs[index] = original with { IsCancelled = true };
            var active = logs.Where(m => m.ProductId == changed.Id && !m.IsCancelled && m.ReversesId == "").ToList();
            if (original.Action is "入库" or "新增")
            {
                var baseline = active.LastOrDefault(m => m.EditedInboundPrice.HasValue || (m.Action is "入库" or "新增") && m.UnitPrice.HasValue);
                changed.LastInboundPrice = baseline?.EditedInboundPrice ?? baseline?.UnitPrice ?? 0;
            }
            else
            {
                var baseline = active.LastOrDefault(m => m.EditedOutboundPrice.HasValue || m.Action == "出库" && m.UnitPrice.HasValue);
                changed.LastOutboundPrice = baseline?.EditedOutboundPrice ?? baseline?.UnitPrice ?? 0;
            }
            logs.Add(Movement.For(changed, "撤销", -original.Change, reason.Trim()) with { ReversesId = original.Id, Counterparty = original.Counterparty, UnitPrice = original.UnitPrice, ExpectedAmount = original.ExpectedAmount, MoneyAmount = original.MoneyAmount, ExactChangedPieces = -original.ChangedPieces, CaseCount = original.CaseCount, LoosePieceCount = original.LoosePieceCount, PieceUnitPrice = original.PieceUnitPrice, PiecePriceAutomatic = original.PiecePriceAutomatic });
        }
        Save(products, logs);
    }

    public void ClearAllData() => Save([], []);

    public static string NormalizeSpecification(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^([0-9]{1,5})\s*\*\s*([0-9]{1,5})$");
        if (!match.Success || int.Parse(match.Groups[1].Value) == 0 || int.Parse(match.Groups[2].Value) == 0) throw new InvalidDataException("规格必须填写两个正整数，中间使用英文星号 *，例如 800*800 或 750*1500（不能使用 x、× 或其他文字）。");
        return int.Parse(match.Groups[1].Value) + "*" + int.Parse(match.Groups[2].Value);
    }
    public void AddSupplier(string text)
    {
        var value = text.Trim();
        if (value.Length == 0 || value.Length > 1000) throw new InvalidDataException("供应商名称请输入 1～1000 个字符。");
        var list = Data.Suppliers ?? [];
        if (list.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        Save(Data.Products, Data.Movements, suppliers: list.Append(value).ToList());
    }
    public void RemoveSupplier(string text) => Save(Data.Products, Data.Movements, suppliers: (Data.Suppliers ?? []).Where(x => x != text).ToList());
    public void AddSpecification(string text)
    {
        var value = NormalizeSpecification(text);
        if (Data.Specifications.Contains(value)) return;
        Save(Data.Products, Data.Movements, Data.Specifications.Append(value).ToList());
    }
    public void RemoveSpecification(string text) => Save(Data.Products, Data.Movements, Data.Specifications.Where(s => s != text).ToList());
    public void DeleteProducts(IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet();
        var removed = Data.Products.Where(p => selected.Contains(p.Id)).ToList();
        if (removed.Count == 0) throw new InvalidDataException("请先选择要删除的商品。");
        Commit(Data.Products.Where(p => !selected.Contains(p.Id)).ToList(), removed.Select(p => Movement.For(p, "删除", -p.Quantity, "批量删除商品") with { Balance = 0 }));
    }
}

public sealed record StockPricing(decimal Cases, decimal LoosePieces, decimal? OverridePiecePrice)
{
    public decimal PiecePrice(decimal casePrice, int pack) => OverridePiecePrice ?? casePrice / pack;
    public decimal Expected(decimal casePrice, int pack) => decimal.Round(Cases * casePrice + LoosePieces * PiecePrice(casePrice, pack), 2, MidpointRounding.AwayFromZero);
    public void Validate(int pack)
    {
        if (pack < 1 || Cases < 0 || Cases > 1000000000 || Cases != decimal.Truncate(Cases) || LoosePieces < 0 || LoosePieces > 1000000000000000m || LoosePieces != decimal.Truncate(LoosePieces)) throw new InvalidDataException("计价数量必须为范围内的非负整数。");
        if (OverridePiecePrice.HasValue) InventoryStore.ValidatePrice(OverridePiecePrice.Value);
    }
}
