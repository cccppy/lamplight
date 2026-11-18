using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Lamplight
{
    /// <summary>快捷键：动作表、组合键格式化/解析。</summary>
    public static class KeyMap
    {
        public class Item
        {
            public string Action;
            public string Label;
            public string Default;
        }

        public static readonly List<Item> Items = new List<Item>
        {
            new Item { Action = "auto", Label = "自动翻页 开/停", Default = "Space" },
            new Item { Action = "nextpage", Label = "下一页", Default = "Right" },
            new Item { Action = "prevpage", Label = "上一页", Default = "Left" },
            new Item { Action = "scrollup", Label = "向上滚动", Default = "Up" },
            new Item { Action = "scrolldown", Label = "向下滚动", Default = "Down" },
            new Item { Action = "nextchapter", Label = "下一章", Default = "Ctrl+Right" },
            new Item { Action = "prevchapter", Label = "上一章", Default = "Ctrl+Left" },
            new Item { Action = "pageup", Label = "上一页（PageUp）", Default = "PageUp" },
            new Item { Action = "pagedown", Label = "下一页（PageDown）", Default = "PageDown" },
            new Item { Action = "home", Label = "本章开头", Default = "Home" },
            new Item { Action = "end", Label = "本章结尾", Default = "End" },
            new Item { Action = "open", Label = "打开文件", Default = "Ctrl+O" },
            new Item { Action = "find", Label = "查找", Default = "Ctrl+F" },
            new Item { Action = "jump", Label = "按百分比跳转", Default = "Ctrl+G" },
            new Item { Action = "addbookmark", Label = "添加书签", Default = "Ctrl+B" },
            new Item { Action = "bookmarks", Label = "书签面板", Default = "B" },
            new Item { Action = "toc", Label = "目录面板", Default = "T" },
            new Item { Action = "stats", Label = "阅读统计", Default = "I" },
            new Item { Action = "shelf", Label = "回到书架", Default = "Ctrl+L" },
            new Item { Action = "settings", Label = "打开设置", Default = "Ctrl+," },
            new Item { Action = "pin", Label = "窗口置顶", Default = "Ctrl+T" },
            new Item { Action = "theme", Label = "切换主题", Default = "Ctrl+Shift+T" },
            new Item { Action = "help", Label = "快捷键帮助", Default = "F1" },
            new Item { Action = "fullscreen", Label = "最大化 / 还原", Default = "F11" },
            new Item { Action = "chrome", Label = "隐藏 / 显示状态栏", Default = "F12" },
            new Item { Action = "zoomin", Label = "字号增大", Default = "Ctrl+=" },
            new Item { Action = "zoomout", Label = "字号减小", Default = "Ctrl+-" },
            new Item { Action = "hide", Label = "隐藏到托盘", Default = "Alt+H" },
            new Item { Action = "copy", Label = "复制选中文字", Default = "Ctrl+C" },
            new Item { Action = "reload", Label = "重新加载本章", Default = "F5" },
            new Item { Action = "tts", Label = "朗读 / 停止", Default = "Ctrl+Shift+R" },
        };

        public static Dictionary<string, string> Defaults()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            foreach (Item it in Items) map[it.Action] = it.Default;
            return map;
        }

        public static Dictionary<string, string> Map()
        {
            if (Store.Lib.Settings.KeyMap == null) Store.Lib.Settings.KeyMap = Defaults();
            foreach (Item it in Items)
                if (!Store.Lib.Settings.KeyMap.ContainsKey(it.Action)) Store.Lib.Settings.KeyMap[it.Action] = it.Default;
            return Store.Lib.Settings.KeyMap;
        }

        public static string Label(string action)
        {
            foreach (Item it in Items) if (it.Action == action) return it.Label;
            return action;
        }

        public static string Get(string action)
        {
            string v;
            return Map().TryGetValue(action, out v) ? v : "";
        }

        public static void Set(string action, string combo)
        {
            Map()[action] = combo ?? "";
            Store.Save();
        }

        public static void Reset()
        {
            Store.Lib.Settings.KeyMap = Defaults();
            Store.Save();
        }

        public static string FindAction(string combo)
        {
            if (string.IsNullOrEmpty(combo)) return null;
            foreach (KeyValuePair<string, string> kv in Map())
                if (!string.IsNullOrEmpty(kv.Value) && string.Equals(kv.Value, combo, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;
            return null;
        }

        public static string FindActionExcept(string combo, string except)
        {
            if (string.IsNullOrEmpty(combo)) return null;
            foreach (KeyValuePair<string, string> kv in Map())
                if (kv.Key != except && !string.IsNullOrEmpty(kv.Value) &&
                    string.Equals(kv.Value, combo, StringComparison.OrdinalIgnoreCase)) return kv.Key;
            return null;
        }

        public static string Format(Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            string name = KeyName(code);
            if (name == null) return null;
            List<string> parts = new List<string>();
            if ((keyData & Keys.Control) == Keys.Control) parts.Add("Ctrl");
            if ((keyData & Keys.Alt) == Keys.Alt) parts.Add("Alt");
            if ((keyData & Keys.Shift) == Keys.Shift) parts.Add("Shift");
            parts.Add(name);
            return string.Join("+", parts.ToArray());
        }

        public static string KeyName(Keys code)
        {
            switch (code)
            {
                case Keys.Oemplus: return "=";
                case Keys.OemMinus: return "-";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.OemSemicolon: return ";";
                case Keys.D0: return "0";
                case Keys.D1: return "1";
                case Keys.D2: return "2";
                case Keys.D3: return "3";
                case Keys.D4: return "4";
                case Keys.D5: return "5";
                case Keys.D6: return "6";
                case Keys.D7: return "7";
                case Keys.D8: return "8";
                case Keys.D9: return "9";
            }
            string s = code.ToString();
            if (s.StartsWith("Oem") || s == "None" || s == "ControlKey" || s == "ShiftKey" || s == "Menu") return null;
            return s;
        }

        /// <summary>鼠标键名：MouseLeft/MouseRight/MouseMiddle/MouseX1/MouseX2。</summary>
        public static string MouseName(MouseButtons b)
        {
            switch (b)
            {
                case MouseButtons.Left: return "MouseLeft";
                case MouseButtons.Right: return "MouseRight";
                case MouseButtons.Middle: return "MouseMiddle";
                case MouseButtons.XButton1: return "MouseX1";
                case MouseButtons.XButton2: return "MouseX2";
            }
            return null;
        }

        public static bool IsMouseCombo(string combo)
        {
            return !string.IsNullOrEmpty(combo) && combo.IndexOf("Mouse", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>把鼠标键与当前修饰键格式化成可保存的组合串。</summary>
        public static string FormatMouse(MouseButtons b)
        {
            string name = MouseName(b);
            if (name == null) return null;
            List<string> parts = new List<string>();
            Keys mods = Control.ModifierKeys & (Keys.Control | Keys.Alt | Keys.Shift);
            if ((mods & Keys.Control) == Keys.Control) parts.Add("Ctrl");
            if ((mods & Keys.Alt) == Keys.Alt) parts.Add("Alt");
            if ((mods & Keys.Shift) == Keys.Shift) parts.Add("Shift");
            parts.Add(name);
            return string.Join("+", parts.ToArray());
        }

        /// <summary>显示用：MouseRight → 鼠标右键。</summary>
        public static string Pretty(string combo)
        {
            if (string.IsNullOrEmpty(combo)) return combo;
            string s = combo;
            s = s.Replace("MouseLeft", "鼠标左键").Replace("MouseRight", "鼠标右键")
                 .Replace("MouseMiddle", "鼠标中键").Replace("MouseX1", "鼠标侧键1")
                 .Replace("MouseX2", "鼠标侧键2");
            return s;
        }

        public static bool TryParse(string combo, out Keys keyData)
        {
            keyData = Keys.None;
            if (string.IsNullOrEmpty(combo)) return false;
            string[] parts = combo.Split('+');
            Keys code = Keys.None;
            Keys mods = Keys.None;
            foreach (string raw in parts)
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                if (string.Equals(p, "Ctrl", StringComparison.OrdinalIgnoreCase)) { mods |= Keys.Control; continue; }
                if (string.Equals(p, "Alt", StringComparison.OrdinalIgnoreCase)) { mods |= Keys.Alt; continue; }
                if (string.Equals(p, "Shift", StringComparison.OrdinalIgnoreCase)) { mods |= Keys.Shift; continue; }
                switch (p)
                {
                    case "=": code = Keys.Oemplus; break;
                    case "-": code = Keys.OemMinus; break;
                    case ",": code = Keys.Oemcomma; break;
                    case ".": code = Keys.OemPeriod; break;
                    case "/": code = Keys.OemQuestion; break;
                    case ";": code = Keys.OemSemicolon; break;
                    default:
                        try { code = (Keys)Enum.Parse(typeof(Keys), p, true); }
                        catch { return false; }
                        break;
                }
            }
            if (code == Keys.None) return false;
            keyData = code | mods;
            return true;
        }
    }
}
