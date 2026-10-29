using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Lamplight
{
    public partial class MainForm
    {
        public int SettingsSection;          // 0 阅读 1 外观 2 快捷键 3 书源 4 关于
        public float SettingsScroll;
        public float SettingsContentH;
        public View PreviousScreen = View.Shelf;
        public bool HelpVisible;

        readonly List<object[]> SetHits = new List<object[]>();

        void SetHit(RectangleF r, string action, int index)
        {
            SetHits.Add(new object[] { r, action, index });
        }

        public void SaveSettings()
        {
            Store.Save();
            BuildLayout();
            Invalidate();
        }

        public void ApplyTheme()
        {
            Th = Theme.Create(Kind);
            Store.Lib.Settings.Theme = Kind.ToString();
            BackColor = Th.Window;
            StyleInputs();
            Store.Save();
            Invalidate();
        }

        public void OpenSettings()
        {
            if (Current != View.Settings)
            {
                if (Current == View.Reader) SaveProgress();
                PreviousScreen = Current;
            }
            Current = View.Settings;
            OpenPanel = Panel.None;
            HideOnlineBox();
            SettingsScroll = 0;
            Invalidate();
        }

        public void CloseSettings()
        {
            Current = PreviousScreen;
            if (Current == View.Online)
            {
                PlaceOnlineBox();
                OnlineWanted = true;
                ShowOnlineBoxSoon();
            }
            else HideOnlineBox();
            Invalidate();
        }

        // ------------------------------------------------------------ 绘制
        void DrawSettings(Painter p, RectangleF area)
        {
            SetHits.Clear();
            float w = area.Width, h = area.Height;

            p.Fill(Th.Window, new RectangleF(0, 0, w, 44));
            p.Line(Th.Line, 0, 44, w, 44, 1);
            if (HoverPanel == 40) p.FillRound(Th.Hover, new RectangleF(14, 8, 34, 28), 7);
            p.DrawIcon(Glyph.Back, new RectangleF(22, 14, 18, 16), Th.Muted, 1.5f);
            SetHit(new RectangleF(14, 8, 34, 28), "back", 0);
            p.TextMid("设置", p.F(14, true), Th.Ink, 60, 22);

            float navW = 208;
            p.Fill(Th.Panel, new RectangleF(0, 44, navW, h - 44));
            p.Line(Th.Line, navW, 44, navW, h, 1);
            string[] nav = { "阅读", "外观与窗口", "快捷键", "书源", "关于" };
            for (int i = 0; i < nav.Length; i++)
            {
                RectangleF r = new RectangleF(16, 64 + i * 46, navW - 32, 38);
                bool on = SettingsSection == i;
                if (on) p.FillRound(Th.AccentSoft, r, 9);
                else if (HoverPanel == 50 + i) p.FillRound(Th.Hover, r, 9);
                p.TextMid(nav[i], p.F(12.5f, on), on ? Th.Accent : Th.Body, r.X + 16, r.Y + 19);
                SetHit(r, "section", i);
            }
            p.TextMid("灯下 Lamplight 0.2", p.F(10), Th.Faint, 24, h - 26);

            float x = navW + 40, right = w - 40, top = 62, bottom = h - 24;
            Region old = p.G.Clip;
            p.G.SetClip(new RectangleF(navW + 1, 44, w - navW - 1, h - 44));
            float cy = top - SettingsScroll;
            if (SettingsSection == 0) cy = DrawSetRead(p, x, right, cy);
            else if (SettingsSection == 1) cy = DrawSetLook(p, x, right, cy);
            else if (SettingsSection == 2) cy = DrawSetKeys(p, x, right, cy);
            else if (SettingsSection == 3) cy = DrawSetSources(p, x, right, cy);
            else cy = DrawSetAbout(p, x, right, cy);
            SettingsContentH = cy + SettingsScroll - top;
            p.G.Clip = old;

            float viewH = bottom - top;
            if (SettingsContentH > viewH + 2)
            {
                float trackH = viewH - 8;
                float thumbH = Math.Max(28, trackH * viewH / SettingsContentH);
                float max = SettingsContentH - viewH;
                float t = max <= 0 ? 0 : Math.Min(1, SettingsScroll / max);
                p.FillRound(Th.Line, new RectangleF(w - 16, top + 4 + (trackH - thumbH) * t, 4, thumbH), 2);
            }
        }

        // ------------------------------------------------------------ 控件
        RectangleF CardBox(Painter p, float x, float right, float y, float height, string title)
        {
            RectangleF r = new RectangleF(x, y, right - x, height);
            p.FillRound(Th.Panel, r, 12);
            p.Stroke(Th.Line, r, 12, 1);
            if (!string.IsNullOrEmpty(title)) p.Text(title, p.F(10.5f), Th.Faint, x + 22, y + 16);
            return r;
        }

        float Label(Painter p, float x, float y, string label, string desc)
        {
            p.Text(label, p.F(13), Th.Ink, x + 22, y);
            if (!string.IsNullOrEmpty(desc)) p.Text(desc, p.F(10.5f), Th.Faint, x + 22, y + 21);
            return y + 44;
        }

        float Toggle(Painter p, float x, float y, bool on, string action, int idx)
        {
            RectangleF sw = new RectangleF(x, y - 4, 46, 24);
            p.FillRound(on ? Th.Accent : Th.Line, sw, 12);
            p.FillRound(Color.White, new RectangleF(on ? sw.Right - 20 : sw.X + 3, sw.Y + 3, 18, 18), 9);
            SetHit(sw, action, idx);
            return y + 30;
        }

        float Segmented(Painter p, float x, float right, float y, string[] items, int sel, string action)
        {
            float totalW = right - x - 44;
            float cw = Math.Min(168, (totalW - (items.Length - 1) * 8) / items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                RectangleF r = new RectangleF(x + 22 + i * (cw + 8), y, cw, 34);
                bool on = i == sel;
                p.FillRound(on ? Th.Accent : Th.Panel, r, 9);
                if (!on) p.Stroke(Th.Line, r, 9, 1);
                p.TextMidCenter(items[i], p.F(11.5f, on), on ? Color.White : Th.Body, r.X + cw / 2, r.Y + 17);
                SetHit(r, action, i);
            }
            return y + 44;
        }

        float Stepper(Painter p, float x, float right, float y, string value, string action, float width = 160)
        {
            float bx = right - width - 22;
            RectangleF minus = new RectangleF(bx, y - 4, 34, 30);
            RectangleF plus = new RectangleF(bx + width - 34, y - 4, 34, 30);
            float mid = (minus.Right + plus.X) / 2;
            p.FillRound(Th.Panel, minus, 8); p.Stroke(Th.Line, minus, 8, 1);
            p.DrawIcon(Glyph.Min, new RectangleF(minus.X + 10, minus.Y + 14, 14, 2), Th.Muted, 1.4f);
            p.FillRound(Th.Panel, plus, 8); p.Stroke(Th.Line, plus, 8, 1);
            p.DrawIcon(Glyph.Plus, new RectangleF(plus.X + 10, plus.Y + 8, 14, 14), Th.Muted, 1.4f);
            p.TextMidCenter(value, p.F(12, true), Th.Ink, mid, y + 11);
            SetHit(minus, action + "-", 0);
            SetHit(plus, action + "+", 0);
            return y + 34;
        }

        float Button(Painter p, float x, float y, float width, string text, string action, int idx, bool primary)
        {
            RectangleF r = new RectangleF(x, y, width, 34);
            if (primary) p.FillRound(Th.Accent, r, 9);
            else { p.FillRound(Th.Panel, r, 9); p.Stroke(Th.Line, r, 9, 1); }
            p.TextMidCenter(text, p.F(11, primary), primary ? Color.White : Th.Body, r.X + width / 2, r.Y + 17);
            SetHit(r, action, idx);
            return y + 42;
        }

        // ------------------------------------------------------------ 阅读
        float DrawSetRead(Painter p, float x, float right, float y)
        {
            float cardTop = y;
            CardBox(p, x, right, cardTop, 146, "翻页");
            float ry = cardTop + 44;
            ry = Label(p, x, ry, "单击翻页", "左右键：左键下一页、右键上一页；三区：点左/右三分之一");
            ry = Segmented(p, x, right, ry, new string[] { "左右键", "三区点击" }, Store.Lib.Settings.ClickMode, "clickmode");
            y = cardTop + 146 + 16;

            cardTop = y;
            CardBox(p, x, right, cardTop, 600, "排版");
            ry = cardTop + 44;
            ry = Label(p, x, ry, "字号", "也可用 Ctrl + = / -");
            ry = Stepper(p, x, right, ry, Store.Lib.Settings.FontSize.ToString("0") + " px", "font");
            ry = Label(p, x, ry, "行距", "行高倍数");
            ry = Stepper(p, x, right, ry, Store.Lib.Settings.LineScale.ToString("0.0"), "line");
            ry = Label(p, x, ry, "正文宽度", "单行最大宽度");
            ry = Stepper(p, x, right, ry, Store.Lib.Settings.PageWidth.ToString("0") + " px", "width");
            ry = Label(p, x, ry, "字距", "字与字之间的额外间距，负值更紧凑");
            ry = Stepper(p, x, right, ry, Store.Lib.Settings.LetterSpacing.ToString("0.0") + " px", "spacing");
            ry = Label(p, x, ry, "段距", "段落之间的额外留白");
            ry = Stepper(p, x, right, ry, Store.Lib.Settings.ParaGapScale.ToString("0.00") + " 倍", "paragap");
            ry = Label(p, x, ry, "页边距", "正文两侧留白，宽屏时更舒适");
            ry = Stepper(p, x, right, ry, Store.Lib.Settings.MarginX.ToString("0") + " px", "margin");
            ry = Label(p, x, ry, "首行缩进", "段首空两格");
            ry = Toggle(p, right - 70, ry, Store.Lib.Settings.Indent, "indent", 0);
            y = cardTop + 600 + 16;

            cardTop = y;
            CardBox(p, x, right, cardTop, 246, "文本处理");
            ry = cardTop + 44;
            ry = Label(p, x, ry, "压缩空行", "把连续空行压成一行");
            ry = Toggle(p, right - 70, ry, Store.Lib.Settings.CompressBlank, "compress", 0);
            ry = Label(p, x, ry, "目录识别正则", string.IsNullOrEmpty(Store.Lib.Settings.ChapterRegex)
                ? "留空使用内置规则（第 X 章 / Chapter X 等）"
                : Store.Lib.Settings.ChapterRegex);
            Button(p, x + 22, ry + 2, 96, "编辑正则", "editregex", 0, false);
            y = cardTop + 246 + 16;

            cardTop = y;
            CardBox(p, x, right, cardTop, 140, "自动翻页");
            ry = cardTop + 44;
            ry = Label(p, x, ry, "间隔", "空格开始 / 停止");
            ry = Stepper(p, x, right, ry - 6, (Store.Lib.Settings.AutoScrollMs / 1000f).ToString("0.0") + " 秒", "auto");
            y = cardTop + 140 + 16;

            cardTop = y;
            CardBox(p, x, right, cardTop, 404, "朗读（TTS）");
            string[] voices = Tts.Voices();
            string vname = !string.IsNullOrEmpty(Store.Lib.Settings.TtsVoice)
                ? Store.Lib.Settings.TtsVoice
                : (voices.Length > 0 ? voices[0] : "未检测到语音引擎");
            p.Text("语音：" + vname, p.F(11), Th.Muted, x + 22, cardTop + 42);
            Button(p, x + 22, cardTop + 62, 108, "切换语音", "ttsvoice", 0, false);
            p.Text("Ctrl+Shift+R 开始 / 停止；朗读时整句高亮并自动滚动", p.F(10.5f), Th.Faint, x + 22, cardTop + 108);
            ry = cardTop + 136;
            ry = Label(p, x, ry, "语速", "-10 最慢，10 最快");
            ry = Stepper(p, x, right, ry, Store.Lib.Settings.TtsRate.ToString("0"), "ttsrate");
            ry = Label(p, x, ry, "音量", "0 – 100");
            ry = Stepper(p, x, right, ry, Store.Lib.Settings.TtsVolume.ToString("0"), "ttsvol");
            ry = Label(p, x, ry, "定时停止", "到点自动停；朗读浮条上也能改");
            ry = Stepper(p, x, right, ry, Store.Lib.Settings.TtsAutoStopMinutes > 0
                ? Store.Lib.Settings.TtsAutoStopMinutes + " 分钟" : "关闭", "ttsmin");
            Button(p, x + 22, ry + 2, 96, "试听", "ttstest", 0, false);
            Button(p, x + 126, ry + 2, 128, "打开声音设置", "opensound", 0, false);
            Button(p, x + 262, ry + 2, 118, "测试扬声器", "beep", 0, false);
            y = cardTop + 404 + 16;
            return y;
        }

        // ------------------------------------------------------------ 外观
        float DrawSetLook(Painter p, float x, float right, float y)
        {
            float cardTop = y;
            CardBox(p, x, right, cardTop, 150, "主题");
            float ry = cardTop + 44;
            ry = Label(p, x, ry, "配色", "暖纸适合白天，夜读适合弱光");
            ry = Segmented(p, x, right, ry, new string[] { "暖纸", "夜读", "极简白" },
                Kind == ThemeKind.Paper ? 0 : Kind == ThemeKind.Night ? 1 : 2, "theme");
            y = cardTop + 150 + 16;

            cardTop = y;
            CardBox(p, x, right, cardTop, 220, "窗口");
            ry = cardTop + 44;
            ry = Label(p, x, ry, "置顶", "Ctrl + T，始终显示在其他窗口之上");
            ry = Toggle(p, right - 70, ry, Pinned, "pin", 0);
            ry = Label(p, x, ry, "不透明度", "Ctrl + 滚轮，或点右侧 +/-");
            ry = Stepper(p, x, right, ry, (int)(UiOpacity * 100) + " %", "opacity");
            y = cardTop + 246 + 16;

            cardTop = y;
            CardBox(p, x, right, cardTop, 364, "背景图片");
            string bgName = string.IsNullOrEmpty(Store.Lib.Settings.BgImage) ? "未设置（使用主题底色）" : Path.GetFileName(Store.Lib.Settings.BgImage);
            p.Text("当前：" + bgName, p.F(11), Th.Muted, x + 22, cardTop + 42);
            Button(p, x + 22, cardTop + 66, 108, "选择图片", "pickbg", 0, true);
            Button(p, x + 140, cardTop + 66, 88, "清除", "clearbg", 0, false);
            ry = cardTop + 120;
            p.Text("铺放方式", p.F(11), Th.Faint, x + 22, ry);
            ry = Segmented(p, x, right, ry + 18, new string[] { "铺满", "平铺", "居中" }, Store.Lib.Settings.BgMode, "bgmode");
            ry = Label(p, x, ry + 4, "图片压暗", "把背景整体压暗，0 表示不压");
            ry = Stepper(p, x, right, ry, Store.Lib.Settings.BgDim + " %", "dim");
            ry = Label(p, x, ry + 2, "纸面不透明度", "正文底板的浓度；调低可露出背景图，文字会自动转浅");
            ry = Stepper(p, x, right, ry, Store.Lib.Settings.PanelOpacity + " %", "panel");
            y = cardTop + 364 + 16;

            cardTop = y;
            CardBox(p, x, right, cardTop, 252, "正文字体与颜色");
            ry = cardTop + 44;
            ry = Label(p, x, ry, "字体", BodyFontName());
            Button(p, x + 22, ry + 4, 168, "切换字体", "fontfamily", 0, false);
            ry = Label(p, x, ry + 48, "字体颜色", string.IsNullOrEmpty(Store.Lib.Settings.TextColor)
                ? "跟随主题" : Store.Lib.Settings.TextColor);
            Color sw = ParseHexColor(Store.Lib.Settings.TextColor);
            if (sw == Color.Empty) sw = Th.Body;
            p.FillRound(sw, new RectangleF(right - 132, ry - 28, 26, 26), 6);
            p.Stroke(Th.Line, new RectangleF(right - 132, ry - 28, 26, 26), 6, 1);
            Button(p, x + 22, ry - 2, 118, "选择颜色", "pickcolor", 0, false);
            Button(p, x + 150, ry - 2, 118, "跟随主题", "resetcolor", 0, false);
            y = cardTop + 252 + 16;
            return y;
        }

        string BodyFontName()
        {
            string f = Store.Lib.Settings.FontFamily;
            return string.IsNullOrEmpty(f) ? "思源宋体（默认）" : f;
        }

        // ------------------------------------------------------------ 快捷键
        float DrawSetKeys(Painter p, float x, float right, float y)
        {
            float cardTop = y;
            float h = 96 + KeyMap.Items.Count * 38;
            CardBox(p, x, right, cardTop, h, "快捷键（点击任意一行修改；Esc 取消，Delete 清除）");
            p.Text("改键后立即生效，保存到 library.json；F1 可随时查看。", p.F(10.5f), Th.Faint, x + 22, cardTop + 38);
            Button(p, x + 22, cardTop + 56, 104, "恢复默认", "resetkeys", 0, false);

            for (int i = 0; i < KeyMap.Items.Count; i++)
            {
                KeyMap.Item it = KeyMap.Items[i];
                float ry = cardTop + 100 + i * 38;
                RectangleF r = new RectangleF(x + 12, ry - 4, right - x - 24, 34);
                bool cap = CapturingAction == it.Action;
                if (cap) p.FillRound(Th.AccentSoft, r, 8);
                else if (HoverPanel == 200 + i) p.FillRound(Th.Hover, r, 8);
                p.TextMid(it.Label, p.F(11.5f), cap ? Th.Accent : Th.Body, x + 24, ry + 13);

                string combo = KeyMap.Get(it.Action);
                string shown = cap ? "按下新键…" : (string.IsNullOrEmpty(combo) ? "未设置" : combo);
                float bw = Math.Max(64, p.Width(shown, p.F(10.5f)) + 22);
                RectangleF pill = new RectangleF(r.Right - bw - 8, ry + 3, bw, 24);
                p.FillRound(cap ? Th.Accent : Th.Panel, pill, 12);
                if (!cap) p.Stroke(Th.Line, pill, 12, 1);
                p.TextMidCenter(shown, p.F(10.5f, cap), cap ? Color.White : Th.Ink, pill.X + bw / 2, pill.Y + 12);
                SetHit(r, "bind", i);
            }
            return cardTop + h + 16;
        }

        // ------------------------------------------------------------ 书源
        float DrawSetSources(Painter p, float x, float right, float y)
        {
            float cardTop = y;
            CardBox(p, x, right, cardTop, 168, "在线书源");
            p.Text("支持原版 Reader 的 bs.json（XPath 规则），也支持自有的正则规则格式。", p.F(11), Th.Muted, x + 22, cardTop + 42);
            p.Text("导入后可在「在线书源」里搜索，章节正文会自动缓存到本地。", p.F(11), Th.Muted, x + 22, cardTop + 64);
            Button(p, x + 22, cardTop + 92, 118, "导入书源", "importsource", 0, true);
            Button(p, x + 150, cardTop + 92, 118, "在线搜索", "onlinesearch", 0, false);
            y = cardTop + 168 + 16;

            cardTop = y;
            CardBox(p, x, right, cardTop, 118, "网络代理（仅在线书源使用）");
            p.Text(string.IsNullOrEmpty(Store.Lib.Settings.Proxy) ? "未设置" : Store.Lib.Settings.Proxy,
                p.F(11), Th.Muted, x + 22, cardTop + 42);
            Button(p, x + 22, cardTop + 66, 108, "设置代理", "editproxy", 0, false);
            y = cardTop + 118 + 16;

            if (Sources.Count == 0)
            {
                p.Text("还没有书源。点「导入书源」选择 bs.json 或 sources.json。", p.F(11.5f), Th.Faint, x + 22, y);
                return y + 40;
            }
            float h = 44 + Sources.Count * 54 + 8;
            cardTop = y;
            CardBox(p, x, right, cardTop, h, "已导入 " + Sources.Count + " 个");
            for (int i = 0; i < Sources.Count; i++)
            {
                SourceDef s = Sources[i];
                RectangleF r = new RectangleF(x + 14, cardTop + 44 + i * 54, right - x - 28, 48);
                if (HoverPanel == 70 + i) p.FillRound(Th.Hover, r, 8);
                p.TextMid(s.Name, p.F(12, true), Th.Ink, r.X + 12, r.Y + 16);
                p.TextMid(s.Host, p.F(10), Th.Faint, r.X + 12, r.Y + 34);
                p.TextMid(s.Enabled ? "已启用" : "已停用", p.F(10), s.Enabled ? Th.Accent : Th.Faint, r.Right - 170, r.Y + 24);
                Button(p, r.Right - 96, r.Y + 7, 82, s.Enabled ? "停用" : "启用", "togglesource", i, false);
            }
            return cardTop + h + 16;
        }

        // ------------------------------------------------------------ 关于
        float DrawSetAbout(Painter p, float x, float right, float y)
        {
            float cardTop = y;
            CardBox(p, x, right, cardTop, 168, "版本");
            p.Text("灯下 Lamplight 0.2", p.F(16, true), Th.Ink, x + 22, cardTop + 40);
            p.Text("单文件 · 免安装 · 本地优先 · 约 120 KB", p.F(11), Th.Faint, x + 22, cardTop + 68);
            p.Text("书签、目录、统计、在线书源俱全，不联网时不发任何请求。", p.F(11), Th.Muted, x + 22, cardTop + 92);
            string stamp = "-";
            try { stamp = System.IO.File.GetLastWriteTime(System.Windows.Forms.Application.ExecutablePath).ToString("yyyy-MM-dd HH:mm"); } catch { }
            p.Text("构建时间：" + stamp, p.F(10.5f), Th.Faint, x + 22, cardTop + 122);
            y = cardTop + 168 + 16;

            cardTop = y;
            CardBox(p, x, right, cardTop, 132, "数据");
            p.TextEllipsis(Store.DataFile, p.F(10.5f), Th.Muted, new RectangleF(x + 22, cardTop + 42, right - x - 44, 18));
            Button(p, x + 22, cardTop + 70, 132, "打开数据目录", "opendata", 0, false);
            y = cardTop + 132 + 16;

            cardTop = y;
            CardBox(p, x, right, cardTop, 200, "为什么提示“不安全”");
            p.Text("程序没有购买代码签名证书，Windows SmartScreen 与部分杀软会提示“未知发布者”。", p.F(11), Th.Muted, x + 22, cardTop + 42);
            p.Text("这不是病毒：不联网、不写启动项、不申请管理员权限，源码与构建脚本都在旁边。", p.F(11), Th.Muted, x + 22, cardTop + 66);
            p.Text("处理：右键 exe → 属性 → 勾选“解除锁定”；或在杀软里把本目录加入信任。", p.F(11), Th.Muted, x + 22, cardTop + 90);
            p.Text("发布页同时给出 SHA-256，可自行核对。", p.F(11), Th.Muted, x + 22, cardTop + 114);
            y = cardTop + 200 + 16;
            return y;
        }

        // ------------------------------------------------------------ 交互
        int SettingsHit(PointF m)
        {
            for (int i = SetHits.Count - 1; i >= 0; i--)
                if (((RectangleF)SetHits[i][0]).Contains(m)) return i;
            return -1;
        }

        bool SettingsClick(PointF m)
        {
            int idx = SettingsHit(m);
            if (idx < 0) return false;
            string a = (string)SetHits[idx][1];
            int v = (int)SetHits[idx][2];
            Settings s = Store.Lib.Settings;
            if (a == "back") { CloseSettings(); return true; }
            if (a == "section") { SettingsSection = v; SettingsScroll = 0; Invalidate(); return true; }
            if (a == "pagemode") { return true; }
            if (a == "clickmode") { s.ClickMode = v; SaveSettings(); return true; }
            if (a == "theme") { Kind = v == 0 ? ThemeKind.Paper : v == 1 ? ThemeKind.Night : ThemeKind.White; ApplyTheme(); return true; }
            if (a == "indent") { s.Indent = !s.Indent; SaveSettings(); return true; }
            if (a == "compress") { s.CompressBlank = !s.CompressBlank; SaveSettings(); return true; }
            if (a == "pin") { TogglePinned(); Invalidate(); return true; }
            if (a == "font-") { SetFontSize(s.FontSize - 1); return true; }
            if (a == "font+") { SetFontSize(s.FontSize + 1); return true; }
            if (a == "line-") { s.LineScale = Math.Max(1.4f, s.LineScale - 0.1f); SaveSettings(); return true; }
            if (a == "line+") { s.LineScale = Math.Min(2.8f, s.LineScale + 0.1f); SaveSettings(); return true; }
            if (a == "width-") { s.PageWidth = Math.Max(460, s.PageWidth - 40); SaveSettings(); return true; }
            if (a == "width+") { s.PageWidth = Math.Min(1200, s.PageWidth + 40); SaveSettings(); return true; }
            if (a == "auto-") { s.AutoScrollMs = Math.Max(1000, s.AutoScrollMs - 500); SaveSettings(); return true; }
            if (a == "auto+") { s.AutoScrollMs = Math.Min(20000, s.AutoScrollMs + 500); SaveSettings(); return true; }
            if (a == "opacity-") { AdjustOpacity(-1); Invalidate(); return true; }
            if (a == "opacity+") { AdjustOpacity(1); Invalidate(); return true; }
            if (a == "bind") { CapturingAction = KeyMap.Items[v].Action; Invalidate(); return true; }
            if (a == "resetkeys") { KeyMap.Reset(); ShowToast("已恢复默认快捷键"); Invalidate(); return true; }
            if (a == "pickcolor")
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    Color cur = ParseHexColor(Store.Lib.Settings.TextColor);
                    cd.Color = cur == Color.Empty ? Th.Body : cur;
                    cd.FullOpen = true;
                    if (cd.ShowDialog(this) == DialogResult.OK)
                    {
                        s.TextColor = "#" + cd.Color.R.ToString("X2") + cd.Color.G.ToString("X2") + cd.Color.B.ToString("X2");
                        Store.Save();
                        Invalidate();
                    }
                }
                return true;
            }
            if (a == "resetcolor") { s.TextColor = ""; Store.Save(); Invalidate(); return true; }
            if (a == "spacing-") { s.LetterSpacing = Math.Max(-2f, s.LetterSpacing - 0.5f); SaveSettings(); return true; }
            if (a == "spacing+") { s.LetterSpacing = Math.Min(10f, s.LetterSpacing + 0.5f); SaveSettings(); return true; }
            if (a == "paragap-") { s.ParaGapScale = Math.Max(0.1f, s.ParaGapScale - 0.1f); SaveSettings(); return true; }
            if (a == "paragap+") { s.ParaGapScale = Math.Min(1.5f, s.ParaGapScale + 0.1f); SaveSettings(); return true; }
            if (a == "margin-") { s.MarginX = Math.Max(0f, s.MarginX - 20f); SaveSettings(); return true; }
            if (a == "margin+") { s.MarginX = Math.Min(200f, s.MarginX + 20f); SaveSettings(); return true; }
            if (a == "pickbg")
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Filter = "图片 (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件 (*.*)|*.*";
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        s.BgImage = d.FileName;
                        Store.Save();
                        ReloadBackground();
                        ShowToast("背景已设置");
                        Invalidate();
                    }
                }
                return true;
            }
            if (a == "clearbg") { s.BgImage = ""; Store.Save(); ReloadBackground(); Invalidate(); return true; }
            if (a == "bgmode") { s.BgMode = v; Store.Save(); Invalidate(); return true; }
            if (a == "panel-") { s.PanelOpacity = Math.Max(0, s.PanelOpacity - 10); Store.Save(); Invalidate(); return true; }
            if (a == "panel+") { s.PanelOpacity = Math.Min(100, s.PanelOpacity + 10); Store.Save(); Invalidate(); return true; }
            if (a == "dim-") { s.BgDim = Math.Max(0, s.BgDim - 5); Store.Save(); Invalidate(); return true; }
            if (a == "dim+") { s.BgDim = Math.Min(85, s.BgDim + 5); Store.Save(); Invalidate(); return true; }
            if (a == "fontfamily") { CycleFontFamily(); return true; }
            if (a == "opendata") { OpenPath(Store.DataFile); return true; }
            if (a == "importsource") { ImportSources(); return true; }
            if (a == "onlinesearch") { OpenOnlineSearch(); return true; }
            if (a == "editregex")
            {
                string rv = PromptForm.Ask(this, "目录识别正则", "每行匹配即视为新章节。留空恢复内置规则。", s.ChapterRegex, Th);
                if (rv != null) { s.ChapterRegex = rv.Trim(); SaveSettings(); ShowToast("目录规则已更新，重新打开书籍生效"); }
                return true;
            }
            if (a == "editproxy")
            {
                string pv = PromptForm.Ask(this, "网络代理", "格式如 http://127.0.0.1:7890，留空表示直连。", s.Proxy, Th);
                if (pv != null) { s.Proxy = pv.Trim(); Store.Save(); ShowToast("代理已保存"); }
                return true;
            }
            if (a == "togglesource") { if (v >= 0 && v < Sources.Count) { Sources[v].Enabled = !Sources[v].Enabled; SaveSources(); Invalidate(); } return true; }
            return true;
        }

        void CycleFontFamily()
        {
            string[] fams = { "", "宋体", "微软雅黑", "楷体", "仿宋", "等线" };
            int i = Array.IndexOf(fams, Store.Lib.Settings.FontFamily);
            i = (i + 1) % fams.Length;
            Store.Lib.Settings.FontFamily = fams[i];
            Store.Save();
            BuildLayout();
            ShowToast("正文字体：" + BodyFontName());
            Invalidate();
        }
    }
}
