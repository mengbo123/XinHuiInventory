using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace XingHui;

// Standard OOXML workbook; no Excel installation or third-party runtime is required.
public static class ExcelService
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    // These URLs identify XML vocabularies; they are never fetched from the network.
    public static readonly string[] Headers = ["图片", "供应商", "名称", "编号", "色号", "规格", "库存（件）", "片/件", "总片数", "状态", "预警值", "上次入库价", "上次出库价", "备注", "更新时间", "商品标识", "单位", "库位"];
    public static void Export(string path, InventoryData data, DateTime? from = null, DateTime? to = null, bool logsOnly = false)
    {
        if (from.HasValue != to.HasValue || (from.HasValue && from.Value.Date > to!.Value.Date) || (logsOnly && !from.HasValue)) throw new InvalidDataException("请选择有效的日志起止日期。");
        var names = logsOnly ? new[] { "操作记录" } : from.HasValue ? new[] { "库存", "图片数据", "操作记录" } : new[] { "库存", "图片数据" };
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
                XNamespace pr = "http://schemas.openxmlformats.org/package/2006/relationships";
                Write(zip, "[Content_Types].xml", new XElement(ct + "Types",
                    new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    new XElement(ct + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                    Enumerable.Range(1, names.Length).Select(i => new XElement(ct + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))).ToString());
                Write(zip, "_rels/.rels", new XElement(pr + "Relationships", new XElement(pr + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", Rel.NamespaceName + "/officeDocument"), new XAttribute("Target", "xl/workbook.xml"))).ToString());
                Write(zip, "xl/workbook.xml", new XElement(Ns + "workbook", new XAttribute(XNamespace.Xmlns + "r", Rel), new XElement(Ns + "sheets", names.Select((name, i) => new XElement(Ns + "sheet", new XAttribute("name", name), new XAttribute("sheetId", i + 1), new XAttribute(Rel + "id", "rId" + (i + 1)), name == "图片数据" ? new XAttribute("state", "hidden") : null)))).ToString());
                Write(zip, "xl/_rels/workbook.xml.rels", new XElement(pr + "Relationships", Enumerable.Range(1, names.Length).Select(i => new XElement(pr + "Relationship", new XAttribute("Id", "rId" + i), new XAttribute("Type", Rel.NamespaceName + "/worksheet"), new XAttribute("Target", $"worksheets/sheet{i}.xml")))).ToString());
                if (!logsOnly)
                {
                    var rows = new List<object[]> { Headers.Cast<object>().ToArray() };
                    rows.AddRange(data.Products.Select(p => new object[] { p.Image == "" ? "默认灰色图片" : "图片随文件保存", p.Supplier, p.Name, p.Code, p.Color, p.Specification, p.StockDisplay, p.PiecesPerCase, p.TotalPieces, p.Status, p.Minimum, p.LastInboundPrice.HasValue ? (object)p.LastInboundPrice.Value : "", p.LastOutboundPrice.HasValue ? (object)p.LastOutboundPrice.Value : "", p.Notes, p.Updated.ToString("yyyy-MM-dd HH:mm:ss"), p.Id, p.Unit, p.Location }));
                    WriteSheet(zip, 1, rows, true, 16);
                    var images = new List<object[]> { new object[] { "商品标识", "分段序号", "图片数据（请勿修改）" } };
                    foreach (var p in data.Products)
                        for (int offset = 0; offset < p.Image.Length; offset += 30000)
                            images.Add([p.Id, offset / 30000, p.Image.Substring(offset, Math.Min(30000, p.Image.Length - offset))]);
                    WriteSheet(zip, 2, images, false);
                }
                if (from.HasValue)
                {
                    var logs = new List<object[]> { new object[] { "时间", "编号", "名称", "操作", "变动数量", "结存", "说明", "商品标识", "色号", "规格", "片/件", "折合片数", "单位", "来源/去处", "实际成交金额", "收入", "支出", "供应商", "本次单价", "应计金额", "状态", "流水标识", "被撤销流水", "计价件数", "散片数", "片单价", "片价计算方式" } };
                    logs.AddRange(data.Movements.Where(m => m.Time.Date >= from.Value.Date && m.Time.Date <= to!.Value.Date).Select(m => new object[] { m.Time.ToString("yyyy-MM-dd HH:mm:ss"), m.Code, m.Name, m.Action, m.Change, m.Balance, m.Note, m.ProductId, m.Color, m.Specification, m.PiecesPerCase, m.ChangedPieces, m.Unit, m.Counterparty, m.MoneyAmount.HasValue ? (object)m.MoneyAmount.Value : "未记录", m.Income, m.Expense, m.Supplier, m.UnitPrice.HasValue ? (object)m.UnitPrice.Value : "未记录", m.ExpectedAmount.HasValue ? (object)m.ExpectedAmount.Value : "未记录", m.RecordStatus, m.Id, m.ReversesId, m.CaseCount.HasValue ? (object)m.CaseCount.Value : "未记录", m.LoosePieceCount.HasValue ? (object)m.LoosePieceCount.Value : "未记录", m.PieceUnitPrice.HasValue ? (object)m.PieceUnitPrice.Value : "未记录", m.PieceUnitPrice.HasValue ? (m.PiecePriceAutomatic ? "自动换算" : "手动填写") : "未记录" }));
                    WriteSheet(zip, logsOnly ? 1 : 3, logs, true);
                }
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new System.Text.UTF8Encoding(false));
        writer.Write(content);
    }
    private static string Column(int index)
    {
        string result = "";
        for (index++; index > 0; index = (index - 1) / 26) result = (char)('A' + (index - 1) % 26) + result;
        return result;
    }
    private static void WriteSheet(ZipArchive zip, int number, List<object[]> rows, bool filter, int hiddenFrom = 0)
    {
        var root = new XElement(Ns + "worksheet",
            new XElement(Ns + "sheetViews", new XElement(Ns + "sheetView", new XAttribute("workbookViewId", 0), new XElement(Ns + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("state", "frozen")))),
            new XElement(Ns + "cols", new XElement(Ns + "col", new XAttribute("min", 1), new XAttribute("max", rows[0].Length), new XAttribute("width", 20), new XAttribute("customWidth", 1))),
            new XElement(Ns + "sheetData", rows.Select((row, r) => new XElement(Ns + "row", new XAttribute("r", r + 1), row.Select((value, c) =>
                value is decimal or int ? new XElement(Ns + "c", new XAttribute("r", Column(c) + (r + 1)), new XElement(Ns + "v", Convert.ToString(value, CultureInfo.InvariantCulture))) :
                new XElement(Ns + "c", new XAttribute("r", Column(c) + (r + 1)), new XAttribute("t", "inlineStr"), new XElement(Ns + "is", new XElement(Ns + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value.ToString() ?? ""))))))));
        if (hiddenFrom > 0)
        {
            var cols = root.Element(Ns + "cols")!;
            cols.Elements().First().SetAttributeValue("max", hiddenFrom - 1);
            cols.Add(new XElement(Ns + "col", new XAttribute("min", hiddenFrom), new XAttribute("max", rows[0].Length), new XAttribute("width", 20), new XAttribute("hidden", 1)));
        }
        if (filter) root.Add(new XElement(Ns + "autoFilter", new XAttribute("ref", $"A1:{Column(rows[0].Length - 1)}{rows.Count}")));
        Write(zip, $"xl/worksheets/sheet{number}.xml", new XDocument(root).ToString());
    }
    private static XDocument ReadXml(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidDataException("Excel 缺少必要文件：" + path);
        if (entry.Length > 150_000_000) throw new InvalidDataException("Excel 工作表过大。");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 150_000_000, XmlResolver = null });
        return XDocument.Load(reader);
    }
    public static List<Product> Import(string path)
    {
        try
        {
            if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("文件格式不支持。请在 Excel 中另存为“Excel 工作簿（.xlsx）”，不能只修改文件后缀。");
            return ImportCore(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or FormatException or OverflowException or IOException)
        {
            throw new ExcelImportException(ex.Message + "\n\n修改方法：\n1. 使用软件“导出 Excel”生成模板，保留“库存”工作表及第一行表头。\n2. 从第 2 行填写数据；缺失的必填字段会自动补默认值，预览中会标明；已填写的数值不要附加“元”等单位。\n3. 片/件填写正整数，如 4；库存可填 2 或 2（+1片），允许负库存；单价、预警值不得为负数。\n4. 公式请复制后选择“粘贴为值”；旧 .xls 或 CSV 文件请另存为 .xlsx。\n5. 修正后保存、关闭 Excel 并重新导入。本次没有修改任何库存。", ex);
        }
    }
    private static List<Product> ImportCore(string path)
    {
        if (new FileInfo(path).Length > 100_000_000) throw new InvalidDataException("请选择小于 100 MB 的 Excel 文件。");
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Sum(e => e.Length) > 250_000_000) throw new InvalidDataException("Excel 解压后过大。");
        var workbook = ReadXml(zip, "xl/workbook.xml");
        var relationships = ReadXml(zip, "xl/_rels/workbook.xml.rels");
        string SheetPath(string name)
        {
            var sheet = workbook.Descendants(Ns + "sheet").FirstOrDefault(e => (string?)e.Attribute("name") == name);
            if (sheet is null) return "";
            var rel = relationships.Root!.Elements().FirstOrDefault(e => (string?)e.Attribute("Id") == (string?)sheet.Attribute(Rel + "id"));
            var target = (string?)rel?.Attribute("Target") ?? throw new InvalidDataException("工作表链接无效。");
            if ((string?)rel?.Attribute("TargetMode") == "External") throw new InvalidDataException("不支持外部工作表链接。");
            // URI resolution normalizes a ZIP entry path only; it never performs HTTP I/O.
            var resolved = new Uri(new Uri("http://local/xl/workbook.xml"), target);
            if (resolved.Host != "local" || resolved.Scheme != "http") throw new InvalidDataException("工作表链接必须指向文件内部。");
            return resolved.AbsolutePath.TrimStart('/');
        }
        var shared = zip.GetEntry("xl/sharedStrings.xml") is null ? [] : ReadXml(zip, "xl/sharedStrings.xml").Descendants(Ns + "si").Select(e => string.Concat(e.Descendants(Ns + "t").Select(t => t.Value))).ToArray();
        List<Dictionary<int, string>> ReadRows(string sheetPath, string sheetName)
        {
            return ReadXml(zip, sheetPath).Descendants(Ns + "row").Select((row, ordinal) =>
            {
                var cells = new Dictionary<int, string> { [-1] = ((int?)row.Attribute("r") ?? ordinal + 1).ToString(CultureInfo.InvariantCulture) };
                foreach (var c in row.Elements(Ns + "c"))
                {
                    if (c.Element(Ns + "f") is not null) throw new InvalidDataException($"工作表“{sheetName}”，单元格 {(string?)c.Attribute("r")}：包含公式，请复制该单元格并粘贴为值。");
                    int column = 0;
                    foreach (var ch in (string?)c.Attribute("r") ?? "") { if (!char.IsAsciiLetterUpper(ch)) break; column = column * 26 + ch - 'A' + 1; }
                    if (column == 0) throw new InvalidDataException("单元格地址无效。");
                    string value = (string?)c.Element(Ns + "v") ?? "";
                    var type = (string?)c.Attribute("t");
                    if (type == "e") throw new InvalidDataException($"工作表“{sheetName}”，单元格 {(string?)c.Attribute("r")}：Excel 错误值 {value}。请修正公式后粘贴为值，或填写正确内容。");
                    if (type == "inlineStr") value = string.Concat(c.Descendants(Ns + "t").Select(t => t.Value));
                    else if (type == "s") value = int.TryParse(value, out var i) && i >= 0 && i < shared.Length ? shared[i] : throw new InvalidDataException("文本索引无效。");
                    cells[column - 1] = value;
                }
                return cells;
            }).ToList();
        }
        var stockPath = SheetPath("库存");
        if (stockPath == "") throw new InvalidDataException("找不到“库存”工作表，请使用本软件导出的模板。");
        var rows = ReadRows(stockPath, "库存");
        if (rows.Count == 0) throw new InvalidDataException("Excel 没有表头。");
        if (rows[0].GetValueOrDefault(-1) != "1") throw new InvalidDataException("库存：表头必须位于第 1 行，请删除表头前的空行或说明行。");
        var header = rows[0].Where(c => c.Key >= 0).ToDictionary(c => c.Key, c => c.Value);
        int Find(params string[] aliases)
        {
            var matches = header.Where(c => aliases.Contains(c.Value.Trim())).Select(c => c.Key).ToList();
            if (matches.Count > 1) throw new InvalidDataException($"库存，第 1 行：表头“{aliases[0]}”重复，位于 {string.Join("、", matches.Select(Column))} 列。请只保留一列，旧别名也不能同时出现。");
            return matches.Count == 0 ? -1 : matches[0];
        }
        int nameCol = Find("（品牌）名称", "名称"), codeCol = Find("编号"), colorCol = Find("色号"), specCol = Find("规格");
        int quantityCol = Find("库存（件）", "库存数量"), piecesCol = Find("片/件", "一件几片"), minCol = Find("预警值", "预警库存"), priceCol = Find("上次入库价", "单价");
        int outPriceCol = Find("上次出库价");
        int notesCol = Find("备注"), updatedCol = Find("更新时间"), idCol = Find("商品标识"), unitCol = Find("单位"), supplierCol = Find("供应商"), locationCol = Find("库位");
        if (!header.Values.Any(v => Headers.Contains(v.Trim()) || new[] { "（品牌）名称", "库存数量", "一件几片", "预警库存", "单价" }.Contains(v.Trim())))
            throw new InvalidDataException("库存，第 1 行：没有识别到任何库存表头。请使用导出模板，避免将其他表格误当作库存导入。");
        var errors = new List<string>();
        var products = new List<Product>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Where(c => c.Key >= 0).All(c => string.IsNullOrWhiteSpace(c.Value))) continue;
            string position = "库存，第 " + row[-1] + " 行";
            string At(int column) => position + (column >= 0 ? $"，{Column(column)} 列（{header.GetValueOrDefault(column, "未知列")}）" : "");
            try
            {
            string S(int column) => column < 0 ? "" : row.GetValueOrDefault(column, "").Trim();
            var filled = new List<string>();
            string Default(int column, string label, string fallback)
            {
                if (S(column) != "") return S(column);
                filled.Add(label + "→" + fallback); return fallback;
            }
            string name = Default(nameCol, "名称", "未导入名称");
            string code = Default(codeCol, "编号", "未导入编号-" + row[-1] + "-" + Guid.NewGuid().ToString("N")[..8]);
            string spec;
            try { spec = InventoryStore.NormalizeSpecification(S(specCol)); }
            catch (InvalidDataException ex) { throw new InvalidDataException($"{At(specCol)}（规格）：值“{S(specCol)}”格式不正确。{ex.Message}"); }
            string packText = Default(piecesCol, "片/件", "1");
            string stockText = Default(quantityCol, "库存（件）", "0");
            string minimumText = Default(minCol, "预警值", "0");
            string unit = Default(unitCol, "单位", "件");
            if (S(priceCol) == "") filled.Add("上次入库价→0");
            if (S(outPriceCol) == "") filled.Add("上次出库价→0");
            decimal N(int column) => decimal.TryParse(S(column), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : throw new InvalidDataException($"{At(column)}：值“{S(column)}”不是数字。请填写如 0、12.5 的纯数字。");
            int pack = int.TryParse(packText, out var pc) && pc >= 1 && pc <= 1000000 ? pc : throw new InvalidDataException($"{At(piecesCol)}：请填写 1～1000000 的整数，例如 4，不能填 0、小数或‘4片’。");
            decimal stock; decimal? exact = null;
            if (decimal.TryParse(stockText, NumberStyles.Number, CultureInfo.InvariantCulture, out var numericStock)) stock = numericStock;
            else
            {
                var match = System.Text.RegularExpressions.Regex.Match(stockText, @"^(-?\d+)\s*[（(]\s*\+\s*(\d+(?:\.\d+)?)\s*片\s*[）)]$");
                if (!match.Success) throw new InvalidDataException($"{At(quantityCol)}：请填写件数如 2，或“2（+1片）”；不要附加其他单位或说明。");
                exact = decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * pack + decimal.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                stock = exact.Value / pack;
            }
            if (stock < -1000000000 || stock > 1000000000) throw new InvalidDataException($"{At(quantityCol)}：库存件数必须在负十亿到十亿之间，请检查单位或小数点。");
            if (!decimal.TryParse(minimumText, NumberStyles.Number, CultureInfo.InvariantCulture, out var minimum) || minimum < 0 || minimum > 1000000000) throw new InvalidDataException($"{At(minCol)}：预警值应在 0～十亿之间，例如 0 或 10。");
            foreach (var column in new[] { priceCol, outPriceCol })
                if (column >= 0 && S(column) != "") { try { InventoryStore.ValidatePrice(N(column)); } catch (InvalidDataException ex) { throw new InvalidDataException($"{At(column)}：{ex.Message} 请填写如 0 或 12.5。"); } }
            var product = new Product { Name = name, Code = code, Color = S(colorCol), Specification = spec, Unit = unit, Quantity = stock, ExactPieces = exact, Minimum = minimum, ImportNotice = filled.Count == 0 ? "" : position + "：" + string.Join("；", filled), LastInboundPrice = priceCol < 0 || S(priceCol) == "" ? null : N(priceCol), LastOutboundPrice = outPriceCol < 0 || S(outPriceCol) == "" ? null : N(outPriceCol), Location = S(locationCol), Supplier = S(supplierCol), Notes = S(notesCol), Updated = DateTime.TryParse(S(updatedCol), out var dt) ? dt : DateTime.Now, Id = S(idCol) is { Length: > 0 } id ? id : Guid.NewGuid().ToString("N"), PiecesPerCase = pack };
            InventoryStore.UpgradeProduct(product); InventoryStore.Validate([product]); products.Add(product);
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException)
            {
                errors.Add(ex.Message.StartsWith(position) ? ex.Message : position + "：" + ex.Message + " 请按模板填写范围内的数值。");
                if (errors.Count >= 100) break;
            }
        }
        if (errors.Count > 0) throw new InvalidDataException($"发现 {errors.Count} 处问题（每行显示首个问题，最多显示 100 条）：\n" + string.Join("\n", errors));
        var imagePath = SheetPath("图片数据");
        if (imagePath != "")
        {
            var imageRows = ReadRows(imagePath, "图片数据").Skip(1).Where(r => r.GetValueOrDefault(0, "") != "");
            foreach (var imageRow in imageRows) if (!int.TryParse(imageRow.GetValueOrDefault(1, ""), out var segment) || segment < 0) throw new InvalidDataException($"图片数据，第 {imageRow[-1]} 行，B 列：分段序号须为非负整数。请从原导出文件恢复图片数据工作表，不要手工编辑该表。");
            var images = imageRows.GroupBy(r => r.GetValueOrDefault(0, "")).ToDictionary(g => g.Key, g => string.Concat(g.OrderBy(r => int.Parse(r.GetValueOrDefault(1, "0"), CultureInfo.InvariantCulture)).Select(r => r.GetValueOrDefault(2, ""))));
            foreach (var p in products) p.Image = images.GetValueOrDefault(p.Id, "");
        }
        // Validate each row; repeated names are deliberately resolved by ImportProducts.
        foreach (var p in products)
        {
            try { InventoryStore.Validate([p]); if (p.Image != "") ProductImageConverter.Decode(p.Image); }
            catch (Exception ex) { throw new InvalidDataException($"图片数据：商品 {p.Name}（编号 {p.Code}）的图片无效。请从原文件恢复图片数据，或删除其图片分段后导入，再在商品编辑中重新上传。原因：{ex.Message}"); }
        }
        return products;
    }
}

public sealed class ExcelImportException(string message, Exception inner) : Exception(message, inner);
