using System;
using System.Drawing;
using System.Windows.Forms;

namespace Lamplight
{
    public partial class MainForm
    {
        NotifyIcon Tray;
        public bool Pinned;
        public float UiOpacity = 1f;

        void InitExtras()
        {
            try
            {
                Tray = new NotifyIcon();
                Tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                Tray.Text = "灯下 Lamplight";
                Tray.Visible = false;
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Items.Add("显示主界面", null, delegate { RestoreFromTray(); });
                menu.Items.Add("切换主题", null, delegate { CycleTheme(); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("退出", null, delegate { Close(); });
                Tray.ContextMenuStrip = menu;
                Tray.DoubleClick += delegate { RestoreFromTray(); };
            }
            catch { Tray = null; }
        }

        public void TogglePinned()
        {
            Pinned = !Pinned;
            TopMost = Pinned;
            Store.Lib.Settings.TopMost = Pinned;
            Store.Save();
            ShowToast(Pinned ? "已置顶 · Ctrl+T 取消" : "已取消置顶");
        }

        public void AdjustOpacity(int delta)
        {
            UiOpacity = Math.Max(0.35f, Math.Min(1f, UiOpacity + delta * 0.05f));
            Opacity = UiOpacity;
            Store.Lib.Settings.Opacity = UiOpacity;
            Store.Save();
            ShowToast("不透明度 " + (int)Math.Round(UiOpacity * 100) + "%");
        }

        public void HideToTray()
        {
            if (Tray != null)
            {
                Tray.Visible = true;
                Tray.ShowBalloonTip(1200, "灯下", "已隐藏，双击托盘图标或按 Alt+H 返回", ToolTipIcon.None);
            }
            Hide();
        }

        public void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            if (Tray != null) Tray.Visible = false;
        }


    }
}
