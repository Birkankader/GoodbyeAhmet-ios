using System;
using System.Drawing;
using System.Windows.Forms;

namespace GoodbyeAhmetWPF.Services
{
    public class NotificationService : IDisposable
    {
        private static NotificationService? _instance;
        public static NotificationService Instance => _instance ??= new NotificationService();

        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;
        private EventHandler? _doubleClickHandler;

        private NotificationService()
        {
            _notifyIcon = new NotifyIcon();
            _contextMenu = new ContextMenuStrip();
            _notifyIcon.ContextMenuStrip = _contextMenu;

            // Need an icon. I can pull it from the current application or a resource
            // For now, I'll try to extract the icon from the entry assembly.
            try
            {
                // In single-file deployments, Assembly.Location is empty, so resolve the executable path from the process or app base directory.
                var executablePath = Environment.ProcessPath
                    ?? System.IO.Path.Combine(AppContext.BaseDirectory, "GoodbyeAhmetWPF.exe");
                _notifyIcon.Icon = Icon.ExtractAssociatedIcon(executablePath);
            }
            catch
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "GoodbyeAhmet";
        }

        public void EnsureVisible()
        {
            try { _notifyIcon.Visible = true; }
            catch { /* ignore */ }
        }

        public void SetDoubleClickAction(Action action)
        {
            // Remove any previous handler so multiple invocations don't stack.
            if (_doubleClickHandler != null)
            {
                _notifyIcon.DoubleClick -= _doubleClickHandler;
            }
            _doubleClickHandler = (s, e) => action?.Invoke();
            _notifyIcon.DoubleClick += _doubleClickHandler;
        }

        public void AddContextMenuItem(string text, Action action)
        {
            _contextMenu.Items.Add(text, null, (s, e) => action?.Invoke());
        }

        public void ShowNotification(string title, string content, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _notifyIcon.ShowBalloonTip(3000, title, content, icon);
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
        }
    }
}
