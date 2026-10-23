using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace XingHui;

public sealed class RecoveryWindow : FormWindow
{
    public RecoveryWindow(InventoryStore store) : base("找回密码", 580)
    {
        Heading("重设登录密码", "验证密保答案或配置文件中的恢复密码后，可设置新的登录密码。");
        var question = new RadioButton { Content = "通过密保问题恢复", IsEnabled = store.RecoveryQuestion != "", IsChecked = store.RecoveryQuestion != "", Margin = new Thickness(0, 8, 0, 8) };
        var recovery = new RadioButton { Content = "通过配置文件中的恢复密码恢复", IsChecked = store.RecoveryQuestion == "" };
        Body.Children.Add(question); Body.Children.Add(recovery);
        Body.Children.Add(new TextBlock { Text = "密保问题：" + (store.RecoveryQuestion == "" ? "尚未设置" : store.RecoveryQuestion), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) });
        Body.Children.Add(new TextBlock { Text = "恢复密码位置：" + store.SettingsPath + "\n字段名：RecoveryPassword。持有此文件的人可以重设密码，请妥善保管。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray });
        var proof = Field("密保答案 / 恢复密码", new PasswordBox());
        var password = Field("新登录密码（8～128 位）", new PasswordBox { MaxLength = 128 });
        var confirm = Field("确认新密码", new PasswordBox { MaxLength = 128 });
        int failures = 0; DateTime retry = DateTime.MinValue;
        Submit("验证并重设", () =>
        {
            if (DateTime.Now < retry) throw new InvalidOperationException("请 30 秒后重试。");
            if (password.Password != confirm.Password) throw new InvalidOperationException("两次输入的新密码不一致。");
            try { store.RecoverPassword(proof.Password, question.IsChecked == true, password.Password); }
            catch { if (++failures >= 5) { retry = DateTime.Now.AddSeconds(30); failures = 0; } throw; }
            MessageBox.Show(this, "密码已重设，请使用新密码登录。", "恢复完成"); DialogResult = true;
        });
    }
}

public sealed class SecurityQuestionWindow : FormWindow
{
    public SecurityQuestionWindow(InventoryStore store) : base("设置密保问题", 560)
    {
        Heading("密保问题", "答案区分大小写，忽略首尾空格。"); //以加盐哈希保存密码，SaltedHashPassword
        var password = Field("当前登录密码", new PasswordBox());
        var question = Field("问题", new TextBox { Text = store.RecoveryQuestion, MaxLength = 200 });
        var answer = Field("答案", new PasswordBox { MaxLength = 200 });
        var confirm = Field("再次输入答案", new PasswordBox { MaxLength = 200 });
        Submit("保存密保", () =>
        {
            if (answer.Password.Trim() != confirm.Password.Trim()) throw new InvalidOperationException("两次输入的答案不一致。");
            store.SetRecoveryQuestion(password.Password, question.Text, answer.Password); DialogResult = true;
        });
    }
}

public sealed class SettingsWindow : FormWindow
{
    public SettingsWindow(InventoryStore store) : base("软件设置", 720)
    {
        // 设置窗口的标题--头部
        Heading("数据与账户设置", "库存、图片和操作历史保存在同一个 JSON 文件中。更换位置后立即生效。");     
        var path = Field("当前库存文件", new TextBox { Text = store.DataPath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap });
        // 正文部分添加一个文本块
        Body.Children.Add(new TextBlock { Text = "默认路径：" + store.DefaultDataPath + "\n更换目录会一起迁移库存、图片、操作记录、账户密码、密保及恢复密码配置。重启后从新目录读取；原文件保留，不再继续更新。请选择专用空文件夹。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        // 正文部分再添加一个按钮
        string AccountLocationText() => "当前账户与配置目录：" + store.ConfigDirectory + "\nsettings.json 中的 RecoveryPassword 是恢复密码，请妥善保管。";
        var accountLocation = new TextBlock { Text = AccountLocationText(), TextWrapping = TextWrapping.Wrap };
        var choose = new Button { Content = "迁移全部数据到新目录…", HorizontalAlignment = HorizontalAlignment.Left };
        choose.Click += (_, _) =>
        {
            // 点击时，跳出保存文件的窗口弹出保存文件对话框，让用户选择新的库存保存位置
            var dialog = new OpenFolderDialog { Title = "选择用于保存库存、账户与配置的新目录" };
            if (dialog.ShowDialog(this) != true) return;
            try { 
                store.MoveDataFile(Path.Combine(dialog.FolderName, Path.GetFileName(store.DataPath))); 
                path.Text = store.DataPath; accountLocation.Text = AccountLocationText(); Error.Text = "整套数据已迁移，重启后将从新目录读取账户、配置和库存。原文件保留。"; 
            } 
            catch (Exception ex) 
            { Error.Text = ex.Message; }
        };
        Body.Children.Add(choose);
        var specs = new Button { Content = "管理规格 / 尺码", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 24, 0, 0) };
        specs.Click += (_, _) => new SpecificationsWindow(store) { Owner = this }.ShowDialog(); Body.Children.Add(specs);
        var security = new Button { Content = "设置 / 修改密保问题", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 24, 0, 12) };
        security.Click += (_, _) => new SecurityQuestionWindow(store) { Owner = this }.ShowDialog(); Body.Children.Add(security);
        var rememberLogin = new CheckBox { Content = "已记住密码，下次自动填入（仍需确认登录）", IsChecked = store.RemembersPassword, IsEnabled = store.RemembersPassword, Margin = new Thickness(0, 12, 0, 12) };
        rememberLogin.Unchecked += (_, _) =>
        {
            try { store.ClearRememberedPassword(); rememberLogin.IsEnabled = false; Error.Text = "已清除记住的密码，下次打开需要手动登录。"; }
            catch (Exception ex) { rememberLogin.IsChecked = true; Error.Text = ex.Message; }
        };
        Body.Children.Add(rememberLogin);
        Body.Children.Add(accountLocation);
        var folder = new Button { Content = "打开配置目录", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 24, 0, 0) };
        folder.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(store.ConfigDirectory) { UseShellExecute = true }); } catch (Exception ex) { Error.Text = ex.Message; } }; Body.Children.Add(folder);
        var clear = new Button { Content = "清空全部库存及操作记录…", Background = Brushes.Firebrick, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 24, 0, 0) };
        clear.Click += (_, _) => new ClearDataWindow(store) { Owner = this }.ShowDialog(); Body.Children.Add(clear);
        foreach (var button in new[] { choose, specs, security, folder, clear })
        {
            button.Width = 260; button.Height = 42; button.Padding = new Thickness(12, 8, 12, 8); button.Margin = new Thickness(0, 12, 0, 12);
        }
        Submit("完成", () => DialogResult = true);
    }
}


public sealed class ClearDataWindow : FormWindow
{
    public ClearDataWindow(InventoryStore store) : base("清空数据确认", 600)
    {
        Heading("清空全部业务数据", $"将删除当前文件中的 {store.Data.Products.Count} 个商品（含图片）和 {store.Data.Movements.Count} 条操作记录，同时清空收支、销量及出入库历史。\n账户、密保、保存路径和规格选项保留。此操作不能通过返回键恢复，也不会自动备份；需要留存请先手动导出。");
        var confirm = Field("请输入“清空所有数据”确认", new TextBox());
        Submit("永久清空", () =>
        {
            if (confirm.Text != "清空所有数据") throw new InvalidOperationException("请输入完整确认文字。");
            store.ClearAllData(); MessageBox.Show(this, "库存及操作记录已清空。", "完成"); DialogResult = true;
        });
    }
}


public sealed class DataFileRecoveryWindow : FormWindow
{
    public DataFileRecoveryWindow(InventoryStore store, string reason) : base("重新连接库存文件", 720)
    {
        Heading("账户已登录，库存文件暂时无法打开", "账户已从当前数据目录读取。请选择现有库存文件继续使用；此处仅修复库存文件位置，整套目录迁移请在软件设置中操作。");
        Field("当前设置的库存路径", new TextBox { Text = store.DataPath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap });
        Body.Children.Add(new TextBlock { Text = reason, Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        Body.Children.Add(new TextBlock { Text = "如果文件被移动或重命名，请选择它的新位置；如果在移动硬盘或网络磁盘上，请先连接磁盘再重试。不会自动创建空库存或覆盖现有文件。", TextWrapping = TextWrapping.Wrap });
        void Connect(string path)
        {
            try { store.OpenExistingDataFile(path); DialogResult = true; }
            catch (Exception ex) { Error.Text = "未能打开库存文件：" + ex.Message; }
        }
        var choose = new Button { Content = "选择现有库存文件…", Margin = new Thickness(0, 14, 0, 8) };
        choose.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "库存文件|*.json", Title = "选择之前保存的库存 JSON 文件", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) Connect(dialog.FileName);
        };
        Body.Children.Add(choose);
        var retry = new Button { Content = "已连接磁盘，重试当前路径", Margin = new Thickness(0, 0, 0, 8) };
        retry.Click += (_, _) => Connect(store.DataPath); Body.Children.Add(retry);
        if (File.Exists(store.DefaultDataPath) && !string.Equals(store.DataPath, store.DefaultDataPath, StringComparison.OrdinalIgnoreCase))
        {
            Body.Children.Add(new TextBlock { Text = "还找到默认位置保留的库存：" + store.DefaultDataPath + "\n原文件可能不含更换路径后的操作，请确认后再使用。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray });
            var original = new Button { Content = "使用默认位置保留的库存…", Margin = new Thickness(0, 8, 0, 8) };
            original.Click += (_, _) =>
            {
                if (MessageBox.Show(this, "该文件可能是更换保存位置时保留的旧数据，之后的出入库不一定包含在内。确认使用？", "确认库存来源", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                    Connect(store.DefaultDataPath);
            };
            Body.Children.Add(original);
        }
        Submit("退出，稍后处理", Close, clickOnly: true);
    }
}
