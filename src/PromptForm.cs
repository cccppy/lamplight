using System;
using System.Drawing;
using System.Windows.Forms;

namespace Lamplight
{
    /// <summary>简单的文本输入对话框（主题化）。</summary>
    class PromptForm : Form
    {
        TextBox Box;
        public string Value { get { return Box.Text; } }

        public PromptForm(string title, string hint, string initial, Theme th)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 150);
            BackColor = th.Window;
            Font = new Font("Microsoft YaHei UI", 9f);

            Label lb = new Label();
            lb.Text = hint;
            lb.ForeColor = th.Muted;
            lb.AutoSize = false;
            lb.Bounds = new Rectangle(22, 16, 476, 40);
            Controls.Add(lb);

            Box = new TextBox();
            Box.Text = initial ?? "";
            Box.Bounds = new Rectangle(22, 60, 476, 26);
            Box.BackColor = th.Panel;
            Box.ForeColor = th.Ink;
            Box.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(Box);

            Button ok = new Button();
            ok.Text = "确定";
            ok.Bounds = new Rectangle(314, 102, 88, 30);
            ok.DialogResult = DialogResult.OK;
            ok.BackColor = th.Accent;
            ok.ForeColor = Color.White;
            ok.FlatStyle = FlatStyle.Flat;
            ok.FlatAppearance.BorderSize = 0;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Bounds = new Rectangle(410, 102, 88, 30);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = th.Panel;
            cancel.ForeColor = th.Ink;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        public static string Ask(IWin32Window owner, string title, string hint, string initial, Theme th)
        {
            using (PromptForm f = new PromptForm(title, hint, initial, th))
            {
                return f.ShowDialog(owner) == DialogResult.OK ? f.Value : null;
            }
        }
    }
}
