using Hardcodet.Wpf.TaskbarNotification;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace LightBlitz
{
    public partial class App : Application
    {
        private Mutex mutex;
        private TaskbarIcon notifyIcon;
        private Core core = new Core();

        protected override void OnStartup(StartupEventArgs e)
        {
            // Detect multiple session
            mutex = new Mutex(true, "LightBlitz", out bool isNew);

            if (!isNew)
            {
                Shutdown();
                return;
            }

            // Load settings
            Settings.Current = Settings.LoadDefault();

            // Initialize
            base.OnStartup(e);

            InitializeNotifyIcon(e);
            core.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            core.Stop();
            CleanupNotifyIcon();

            base.OnExit(e);
        }

        private void InitializeNotifyIcon(StartupEventArgs e)
        {
            MenuItem versionMenuItem;

            notifyIcon = new TaskbarIcon();
            notifyIcon.Icon = LightBlitz.Properties.Resources.TrayIcon;
            notifyIcon.ContextMenu = new ContextMenu();
            notifyIcon.ContextMenu.Items.Add(CreateMenuItem("Settings", SettingMenuItem_Click));
            notifyIcon.ContextMenu.Items.Add(new Separator());
            notifyIcon.ContextMenu.Items.Add(versionMenuItem = CreateMenuItem("", VersionMenuItem_Click));
            notifyIcon.ContextMenu.Items.Add(new Separator());
            notifyIcon.ContextMenu.Items.Add(CreateMenuItem("Exit", ExitMenuItem_Click));
            notifyIcon.TrayMouseDoubleClick += SettingMenuItem_Click;

            var version = Assembly.GetExecutingAssembly().GetName().Version;

            versionMenuItem.Header = string.Format("v{0}.{1}.{2}.{3}", version.Major, version.Minor, version.Build, version.Revision);
        }

        private void CleanupNotifyIcon()
        {
            if (notifyIcon != null)
                notifyIcon.Dispose();
        }

        private void SettingMenuItem_Click(object sender, RoutedEventArgs e)
        {
            foreach (Window window in Current.Windows)
            {
                if (window is SettingWindow)
                    return;
            }

            new SettingWindow().ShowDialog();
        }

        private void VersionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("https://github.com/zniqco/LightBlitz/releases");
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Shutdown();
        }

        private MenuItem CreateMenuItem(string caption, RoutedEventHandler callback = null)
        {
            var item = new MenuItem();

            item.Header = caption;

            if (callback != null)
                item.Click += callback;

            return item;
        }
    }
}