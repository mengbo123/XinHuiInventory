using System.Configuration;
using System.Data;
using System.Windows;

namespace XingHui
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private Mutex? instance;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e); /*先执行父类WPF的启动方法*/
            // Mutex：互斥量，用于防止多开软件
            instance = new Mutex(true, "Local\\XingHuiInventory-" + Environment.UserName, out bool created);
            if (!created) { 
                MessageBox.Show("兴辉库存已经打开，请切换到现有窗口。"); 
                Shutdown(); 
                return; 
            }
            try
            {
                var store = new InventoryStore();       //初始化库存存储对象
                if (new LoginWindow(store).ShowDialog() != true) { Shutdown(); return; } //如果登录失败，则关闭应用程序
                try { store.Load(); }
                catch (Exception loadError)
                {
                    if (new DataFileRecoveryWindow(store, loadError.Message).ShowDialog() != true)
                    { Shutdown(); return; }
                }
                MainWindow = new Dashboard(store);      // 初始化库存存储的主窗口
                ShutdownMode = ShutdownMode.OnMainWindowClose;//当主窗口关闭时，应用程序退出
                MainWindow.Show();                      //显示主窗口
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败：" + ex.Message + "\n为保护数据，软件没有覆盖原文件。请检查保存路径，或使用你手动保留的数据文件恢复。", "兴辉库存", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }
        protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
    }

}
