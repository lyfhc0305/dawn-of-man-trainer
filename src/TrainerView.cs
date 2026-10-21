using System;
using System.Collections.Generic;
using System.Globalization;

namespace DawnTrainer
{
    // One layout is used by Unity and by the offline preview exporters.
    internal struct UiRect
    {
        internal float X, Y, W, H;
        internal UiRect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
        internal UiRect Inset(float n) { return new UiRect(X + n, Y + n, W - n * 2, H - n * 2); }
    }

    internal sealed class UiItem
    {
        internal string Id, Name;
        internal int Categories;
        internal object Image;
    }

    internal sealed partial class UiModel
    {
        internal string Version = "2.1.0", Search = "", Quantity = "10", AnimalQuantity = "1", Status = "准备就绪";
        internal string GameVersion = "";
        internal bool Ready, Domestic, BuildHotkey, KillHotkey, GiveHotkey, Busy, StatusError;
        internal bool[] Locks = new bool[7];
        internal int Page, Category, Location, Age = 1, Gender = 2, Knowledge, SelectionCount, BuildTargets, KillTargets;
        internal int ResourceCount, AnimalCount, Done, Total, Weather, SpawnTab;
        internal int StorageMultiplier = 1, BuildMultiplier = 1, CarryMultiplier = 1, TransportMultiplier = 1;
        internal int CraftMultiplier = 1, DryMultiplier = 1, GatherMultiplier = 1, FarmMultiplier = 1, EfficiencyTab;
        internal float Speed = 1, ResourceScroll, AnimalScroll, ActiveScroll;
        internal string SelectedResource, SelectedAnimal;
        internal string ResourceSearch = "", AnimalSearch = "", SelectionName = "", TaskName = "", CompletedName = "";
        internal int CompletedCount;
        internal bool ShowSuccess;
        internal void Navigate(int next)
        {
            if (next == Page) return;
            Page = next; ArmedAction = null;
            Emit("page", next.ToString());
        }
        // Resources and animals keep their own search text; the runtime stores only the visible one.
        internal void SelectSpawnTab(int next)
        {
            if (next == SpawnTab) return;
            if (SpawnTab == 0) ResourceSearch = Search; else if (SpawnTab == 1) AnimalSearch = Search;
            SpawnTab = next;
            if (next == 0) Search = ResourceSearch; else if (next == 1) Search = AnimalSearch;
            ArmedAction = null; Emit("focus.clear", "");
        }
        internal List<UiItem> Resources = new List<UiItem>(), Animals = new List<UiItem>();
        internal Action<string, string> Action;
        internal void Emit(string action, string value) { if (Action != null) Action(action, value); }
    }

    internal abstract class UiCanvas
    {
        internal abstract void Fill(UiRect rect, string color, float radius);
        internal abstract void Text(UiRect rect, string text, int size, string color, bool bold, int align);
        internal abstract void Line(float x1, float y1, float x2, float y2, string color, float width);
        internal abstract void Image(UiRect rect, object image);
        internal abstract bool Hit(UiRect rect, string id, bool enabled);
        internal abstract bool Hover(UiRect rect);
        internal abstract bool Focused(string id);
        internal abstract string Input(UiRect rect, string id, string value, int maximumLength, bool centered);
        internal abstract float Slider(UiRect rect, string id, float value, float min, float max);
        internal abstract float BeginScroll(UiRect viewport, float position, float contentHeight);
        internal abstract void EndScroll();
        internal abstract float Measure(string text, int size, bool bold);
    }

    // A narrow drawer docked beside the map: an icon rail, a persistent speed strip, one page body and a status footer.
    internal static partial class TrainerView
    {
        internal const int PageHome = 0, PageTargets = 1, PageSpawn = 2, PageWorld = 3, PageEfficiency = 4, PageSettings = 5, PageHelp = 6;
        internal const int DockRight = 0, DockLeft = 1, DockFloating = 2;
        internal const float Width = 400, Height = 620, RailWidth = 56, CollapsedHeight = 520;
        internal static float PanelWidth(bool collapsed) { return collapsed ? RailWidth : Width; }
        internal static float PanelHeight(bool collapsed) { return collapsed ? CollapsedHeight : Height; }
        // The title bar (or the logo of the collapsed rail) drags the panel; dragging a docked panel detaches it.
        internal static UiRect DragArea(bool collapsed) { return collapsed ? new UiRect(0, 0, RailWidth, 52) : new UiRect(RailWidth, 0, Width - RailWidth, 52); }

        internal const string Background = "#1A1816", Side = "#141210", Surface = "#24211D", Raise = "#2E2A25", Divider = "#2C2823", Frame = "#3C3730";
        internal const string TextMain = "#F2EEE6", Muted = "#BDB5A8", Quiet = "#948D81", Accent = "#A8B7FF", AccentInk = "#C9D3FF";
        // Selection tints are precomputed opaque blends of the accent over each background, so outlines never show through.
        private const string SelFill = "#35353A", SelFillBg = "#2D2D34", SelFillSide = "#27282F", SelLine = "#6C7398", RailHover = "#1E1B18";
        private const string Primary = "#DFE5FF", PrimaryHover = "#F0F3FF", PrimaryInk = "#1A2341", Logo = "#2F2B3E", Knob = "#DDE5FF";
        private const string Danger = "#EE9CA6", DangerFill = "#3A2F2C", DangerHover = "#4A3533", DangerLine = "#795557", DangerInk = "#2B1316", DangerSolidHover = "#F4B3BB";
        private const string Ok = "#8ECDB4", Warn = "#D9BC82", InputFill = "#15130F";
        private const float X0 = 72, BodyW = 312, Right = X0 + BodyW, BodyY = 98, FooterY = Height - 40;

        private static readonly string[] Pages = { "常用功能", "选中目标", "生成物品", "世界控制", "效率倍率", "设置", "使用说明" };
        private static readonly string[] RailLabels = { "常用", "选中", "生成", "世界", "效率", "设置" };
        private static readonly string[] RailIcons = { "home", "target", "spawn", "sun", "gauge", "gear" };
        private static readonly string[] StatNames = { "健康", "饱食", "口渴", "温度", "体力", "休息", "精神" };
        private static readonly string[] StatIcons = { "health", "food", "thirst", "temp", "stamina", "rest", "mind" };
        private static readonly float[] SpeedPresets = { .25f, .5f, 1, 2, 4, 8, 15 };

        private static UiRect R(float x, float y, float w, float h) { return new UiRect(x, y, w, h); }
        private static string Short(UiCanvas c, string text, float width, int size)
        {
            if (String.IsNullOrEmpty(text) || c.Measure(text, size, false) <= width) return text ?? "";
            while (text.Length > 0 && c.Measure(text + "…", size, false) > width) text = text.Substring(0, text.Length - 1);
            return text + "…";
        }
        private static string FormatSpeed(float speed) { return speed.ToString("0.##", CultureInfo.InvariantCulture) + "×"; }
        private static string KeyName(string key) { return key == "Insert" ? "Ins" : key == "Delete" ? "Del" : key == "PageUp" ? "PgUp" : key == "PageDown" ? "PgDn" : key; }

        // ---------- controls ----------
        private static bool Button(UiCanvas c, UiRect r, string id, string label, int style, bool enabled, int size = 13)
        {
            bool hover = enabled && c.Hover(r);
            // Styles: 0 normal, 1 primary, 2 danger outline, 3 danger solid (an armed confirmation).
            string fill = style == 1 ? Primary : style == 2 ? DangerFill : style == 3 ? Danger : Raise;
            string ink = style == 1 ? PrimaryInk : style == 2 ? Danger : style == 3 ? DangerInk : TextMain;
            if (!enabled) { fill = Surface; ink = Quiet; }
            else if (hover) fill = style == 1 ? PrimaryHover : style == 2 ? DangerHover : style == 3 ? DangerSolidHover : "#38342E";
            if (style == 2 && enabled) { c.Fill(r, DangerLine, 8); c.Fill(r.Inset(1), fill, 7); }
            else c.Fill(r, fill, 8);
            c.Text(r, label, size, ink, (style == 1 || style == 3) && enabled, 1);
            return c.Hit(r, id, enabled);
        }
        // An armed confirmation names the action and counts down to its expiry.
        private static string ConfirmLabel(UiModel m, string label) { return m.ArmedSeconds > 0 ? label + "（" + m.ArmedSeconds + "）" : label; }
        private static float ButtonWidth(UiCanvas c, string label, int size) { return Math.Max(64, (float)Math.Ceiling(c.Measure(label, size, false)) + 26); }
        // A destructive button that needs confirmation runs on its second click; the runtime expires the armed state.
        private static bool Confirmed(UiModel m, string id, bool needed, bool clicked)
        {
            if (!clicked || !needed) return clicked;
            if (m.ArmedAction == id) { m.ArmedAction = null; return true; }
            m.ArmedAction = id; m.Emit("confirm.arm", id); return false;
        }
        private static bool Switch(UiCanvas c, UiRect r, string id, bool value, UiRect? hitArea = null)
        {
            c.Fill(r, value ? Accent : c.Hover(hitArea ?? r) ? "#4A443C" : "#3F3A33", r.H / 2);
            c.Fill(R(r.X + (value ? r.W - r.H + 2 : 2), r.Y + 2, r.H - 4, r.H - 4), value ? "#18213B" : "#C4BCAF", (r.H - 4) / 2);
            return c.Hit(hitArea ?? r, id, true) ? !value : value;
        }
        // Segmented control: returns the index of a newly chosen segment, or -1.
        private static int Segmented(UiCanvas c, UiRect r, string[] ids, string[] labels, string[] counts, int selected, bool enabled, int size = 13)
        {
            c.Fill(r, Surface, 9);
            int n = labels.Length, clicked = -1; float w = (r.W - 6 - (n - 1) * 2) / n;
            for (int i = 0; i < n; i++)
            {
                UiRect s = R(r.X + 3 + i * (w + 2), r.Y + 3, w, r.H - 6); bool on = i == selected, hover = enabled && !on && c.Hover(s);
                if (on) { c.Fill(s, SelLine, 6); c.Fill(s.Inset(1), Raise, 5); } else if (hover) c.Fill(s, Raise, 6);
                string ink = !enabled ? Quiet : on ? TextMain : Muted, count = counts == null ? null : counts[i];
                if (String.IsNullOrEmpty(count)) c.Text(s, labels[i], size, ink, false, 1);
                else
                {
                    float lw = c.Measure(labels[i], size, false), cw = c.Measure(count, 11, false), x = s.X + (s.W - lw - 5 - cw) / 2;
                    c.Text(R(x, s.Y, lw + 2, s.H), labels[i], size, ink, false, 0);
                    c.Text(R(x + lw + 5, s.Y, cw + 2, s.H), count, 11, on ? AccentInk : Quiet, false, 0);
                }
                if (c.Hit(s, ids[i], enabled) && !on) clicked = i;
            }
            return clicked;
        }
        // Pill-shaped choice on the page background (categories, quantity presets).
        private static bool Choice(UiCanvas c, UiRect r, string id, string text, bool selected, bool enabled = true, float radius = 8)
        {
            if (selected) { c.Fill(r, SelLine, radius); c.Fill(r.Inset(1), SelFillBg, radius - 1); }
            else c.Fill(r, enabled && c.Hover(r) ? Raise : Surface, radius);
            c.Text(r, text, 12, !enabled ? Quiet : selected ? AccentInk : Muted, false, 1);
            return c.Hit(r, id, enabled);
        }
        // Tiles on the page background; returns the fill drawn, for glyph cut-outs.
        private static string Tile(UiCanvas c, UiRect r, bool selected)
        {
            if (selected) { c.Fill(r, SelLine, 10); c.Fill(r.Inset(1), SelFill, 9); return SelFill; }
            string fill = c.Hover(r) ? Raise : Surface; c.Fill(r, fill, 10); return fill;
        }
        private static bool Section(UiCanvas c, float y, string title, string link, string linkId, bool linkEnabled)
        {
            c.Text(R(X0, y, BodyW - 90, 16), title, 12, Quiet, false, 0);
            if (link == null) return false;
            float w = c.Measure(link, 12, false) + 8; UiRect r = R(Right - w, y - 4, w, 24);
            c.Text(r, link, 12, linkEnabled ? Accent : Quiet, false, 2);
            return c.Hit(r, linkId, linkEnabled);
        }
        private static void SectionNote(UiCanvas c, float y, string text)
        { c.Text(R(X0 + 96, y, BodyW - 96, 16), Short(c, text, BodyW - 96, 12), 12, Muted, false, 2); }
        private static void Key(UiCanvas c, UiRect r, string text)
        { c.Fill(r, Raise, 5); c.Text(r, text, 12, Muted, false, 1); }
        private static void Badge(UiCanvas c, float right, float y, string text)
        {
            float w = Math.Max(16, c.Measure(text, 11, true) + 8);
            c.Fill(R(right - w, y, w, 16), Accent, 8); c.Text(R(right - w, y, w, 16), text, 11, PrimaryInk, true, 1);
        }
        // Row with an icon, title, note and a trailing button; returns whether the button was clicked.
        private static bool ActionRow(UiCanvas c, float y, string icon, string title, string note, string noteColor,
            string id, string label, int style, bool enabled, bool last)
        {
            bool danger = style == 2;
            UiRect box = R(X0, y + 10, 32, 32);
            c.Fill(box, danger ? DangerFill : Surface, 9);
            Glyph(c, icon, box.X + 7, box.Y + 7, 18, danger ? Danger : Muted, danger ? DangerFill : Surface);
            float bw = ButtonWidth(c, label, 13), tw = BodyW - 42 - bw - 10;
            c.Text(R(X0 + 42, y + 8, tw, 19), Short(c, title, tw, 14), 14, TextMain, false, 0);
            c.Text(R(X0 + 42, y + 28, tw, 16), Short(c, note, tw, 12), 12, noteColor ?? Quiet, false, 0);
            if (!last) c.Line(X0, y + 52, Right, y + 52, Divider, 1);
            return Button(c, R(Right - bw, y + 11, bw, 30), id, label, style, enabled);
        }

        // ---------- glyphs (24-unit grid) ----------
        private static void Glyph(UiCanvas c, string name, float x, float y, float size, string color, string bg)
        {
            float s = size / 24f, w = Math.Max(1.2f, 1.7f * s);
            Action<float[]> poly = p => { for (int i = 0; i + 3 < p.Length; i += 2) c.Line(x + p[i] * s, y + p[i + 1] * s, x + p[i + 2] * s, y + p[i + 3] * s, color, w); };
            Action<float, float, float, float> line = (a, b, d, e) => c.Line(x + a * s, y + b * s, x + d * s, y + e * s, color, w);
            Action<float, float, float> disc = (a, b, r) => c.Fill(R(x + (a - r) * s, y + (b - r) * s, r * 2 * s, r * 2 * s), color, r * s);
            Action<float, float, float> ring = (a, b, r) => {
                disc(a, b, r);
                float inner = r * s - w; if (inner > 0) c.Fill(R(x + a * s - inner, y + b * s - inner, inner * 2, inner * 2), bg, inner);
            };
            // Arcs run clockwise on screen from a0 to a1 degrees; 0 points right, 90 down.
            Action<float, float, float, float, float> arc = (a, b, r, a0, a1) => {
                int n = Math.Max(4, (int)Math.Ceiling(Math.Abs(a1 - a0) / 22.5));
                for (int i = 0; i < n; i++)
                {
                    double t0 = (a0 + (a1 - a0) * i / n) * Math.PI / 180, t1 = (a0 + (a1 - a0) * (i + 1) / n) * Math.PI / 180;
                    line(a + r * (float)Math.Cos(t0), b + r * (float)Math.Sin(t0), a + r * (float)Math.Cos(t1), b + r * (float)Math.Sin(t1));
                }
            };
            float[] cloud = { 6, 15.5f, 4, 14.2f, 3.6f, 12, 4.8f, 10.2f, 7, 9.8f, 8, 7.4f, 10.4f, 5.6f, 13.4f, 5.4f, 15.8f, 6.8f, 17, 9, 19.2f, 9.4f, 20.6f, 11.2f, 20.6f, 13.6f, 19, 15.2f, 17, 15.5f, 6, 15.5f };
            switch (name)
            {
                case "logo": poly(new float[] { 12, 3, 15.4f, 7.6f, 16.2f, 13.4f, 12, 21, 7.8f, 13.4f, 8.6f, 7.6f, 12, 3 }); line(12, 8.5f, 12, 15.5f); break;
                case "home": poly(new float[] { 4, 11, 12, 4, 20, 11 }); poly(new float[] { 6, 9.5f, 6, 20, 18, 20, 18, 9.5f }); poly(new float[] { 10, 20, 10, 15, 14, 15, 14, 20 }); break;
                case "target": ring(12, 12, 7); disc(12, 12, 2.2f); line(12, 2.5f, 12, 5.5f); line(12, 18.5f, 12, 21.5f); line(2.5f, 12, 5.5f, 12); line(18.5f, 12, 21.5f, 12); break;
                case "spawn": poly(new float[] { 12, 3, 20, 7.5f, 20, 16.5f, 12, 21, 4, 16.5f, 4, 7.5f, 12, 3 }); poly(new float[] { 4, 7.5f, 12, 12, 20, 7.5f }); line(12, 12, 12, 21); break;
                case "sun":
                    ring(12, 12, 4.2f);
                    for (int i = 0; i < 8; i++) { double t = i * Math.PI / 4; line(12 + (float)Math.Cos(t) * 7.2f, 12 + (float)Math.Sin(t) * 7.2f, 12 + (float)Math.Cos(t) * 9.8f, 12 + (float)Math.Sin(t) * 9.8f); }
                    break;
                case "gauge": arc(12, 16.5f, 8.5f, 180, 360); line(12, 16.5f, 16.5f, 11); disc(12, 16.5f, 1.7f); line(3.5f, 16.5f, 5.5f, 16.5f); line(18.5f, 16.5f, 20.5f, 16.5f); break;
                case "gear":
                    {
                        // Eight teeth as one outline, drawn with the same stroke as every other icon.
                        float[] p = new float[8 * 4 * 2 + 2]; int k = 0;
                        for (int i = 0; i < 8; i++)
                        {
                            double center = i * 45;
                            double[] angles = { center - 15, center - 8, center + 8, center + 15 }; float[] radii = { 7.2f, 9.6f, 9.6f, 7.2f };
                            for (int j = 0; j < 4; j++) { double t = angles[j] * Math.PI / 180; p[k++] = 12 + radii[j] * (float)Math.Cos(t); p[k++] = 12 + radii[j] * (float)Math.Sin(t); }
                        }
                        p[k++] = p[0]; p[k] = p[1];
                        poly(p); ring(12, 12, 3);
                    }
                    break;
                case "lock": poly(new float[] { 5.5f, 10.5f, 18.5f, 10.5f, 18.5f, 20, 5.5f, 20, 5.5f, 10.5f }); arc(12, 10.5f, 4.2f, 180, 360); line(7.8f, 10.5f, 7.8f, 8.5f); line(16.2f, 10.5f, 16.2f, 8.5f); disc(12, 15, 1.3f); break;
                case "pencil": poly(new float[] { 4.5f, 19.5f, 5.2f, 15.6f, 15.6f, 5.2f, 18.8f, 8.4f, 8.4f, 18.8f, 4.5f, 19.5f }); line(13.4f, 7.4f, 16.6f, 10.6f); break;
                case "help": ring(12, 12, 9); poly(new float[] { 9.6f, 9.6f, 10.2f, 8, 12, 7.2f, 13.8f, 8, 14.4f, 9.6f, 13.6f, 11, 12.4f, 11.8f, 12, 12.8f, 12, 13.8f }); disc(12, 16.8f, 1.1f); break;
                case "collapse": poly(new float[] { 3.5f, 4.5f, 20.5f, 4.5f, 20.5f, 19.5f, 3.5f, 19.5f, 3.5f, 4.5f }); line(15, 4.5f, 15, 19.5f); poly(new float[] { 8, 9.5f, 10.5f, 12, 8, 14.5f }); break;
                case "expand": poly(new float[] { 3.5f, 4.5f, 20.5f, 4.5f, 20.5f, 19.5f, 3.5f, 19.5f, 3.5f, 4.5f }); line(9, 4.5f, 9, 19.5f); poly(new float[] { 16, 9.5f, 13.5f, 12, 16, 14.5f }); break;
                case "close": line(6.5f, 6.5f, 17.5f, 17.5f); line(17.5f, 6.5f, 6.5f, 17.5f); break;
                case "pause": c.Line(x + 9 * s, y + 6 * s, x + 9 * s, y + 18 * s, color, w * 1.4f); c.Line(x + 15 * s, y + 6 * s, x + 15 * s, y + 18 * s, color, w * 1.4f); break;
                case "search": ring(11, 11, 6.2f); line(15.6f, 15.6f, 20, 20); break;
                case "star":
                    {
                        float[] p = new float[22];
                        for (int i = 0; i <= 10; i++) { double t = (-90 + i * 36) * Math.PI / 180; float r = i % 2 == 0 ? 9 : 3.9f; p[i * 2] = 12 + r * (float)Math.Cos(t); p[i * 2 + 1] = 13 + r * (float)Math.Sin(t); }
                        poly(p);
                    }
                    break;
                case "minus": line(6, 12, 18, 12); break;
                case "plus": line(6, 12, 18, 12); line(12, 6, 12, 18); break;
                case "check": poly(new float[] { 5, 12.5f, 9.5f, 17, 19, 7 }); break;
                case "back": poly(new float[] { 14.5f, 6, 8.5f, 12, 14.5f, 18 }); break;
                case "health": poly(new float[] { 9.5f, 4, 14.5f, 4, 14.5f, 9.5f, 20, 9.5f, 20, 14.5f, 14.5f, 14.5f, 14.5f, 20, 9.5f, 20, 9.5f, 14.5f, 4, 14.5f, 4, 9.5f, 9.5f, 9.5f, 9.5f, 4 }); break;
                case "food": line(3.5f, 11.5f, 20.5f, 11.5f); arc(12, 11.5f, 8.5f, 0, 180); poly(new float[] { 9, 8.5f, 9.8f, 6.5f, 9, 4.5f }); poly(new float[] { 14.5f, 8.5f, 15.3f, 6.5f, 14.5f, 4.5f }); break;
                case "thirst": arc(12, 14.5f, 6, -30, 210); line(17.2f, 11.5f, 12, 3.5f); line(12, 3.5f, 6.8f, 11.5f); break;
                case "temp": line(10, 4.5f, 10, 13.2f); line(14, 4.5f, 14, 13.2f); arc(12, 4.5f, 2, 180, 360); arc(12, 16.5f, 4, -60, 240); c.Line(x + 12 * s, y + 9 * s, x + 12 * s, y + 16.5f * s, color, w * 1.4f); break;
                case "stamina": poly(new float[] { 13.5f, 3, 5, 13.5f, 11, 13.5f, 10, 21, 18.5f, 10.5f, 12.5f, 10.5f, 13.5f, 3 }); break;
                case "rest": poly(new float[] { 4, 9, 11, 9, 4, 18, 11, 18 }); poly(new float[] { 13.5f, 5, 19.5f, 5, 13.5f, 12, 19.5f, 12 }); break;
                case "mind": ring(12, 12, 8.6f); arc(12, 12.5f, 4, 25, 155); disc(9.4f, 10, 1.1f); disc(14.6f, 10, 1.1f); break;
                case "paw":
                    disc(6.5f, 10, 1.9f); disc(9.9f, 5.9f, 1.9f); disc(14.1f, 5.9f, 1.9f); disc(17.5f, 10, 1.9f);
                    c.Fill(R(x + 7.8f * s, y + 12.4f * s, 8.4f * s, 7.4f * s), color, 3.6f * s); break;
                case "rain": poly(cloud); line(8.5f, 17.5f, 7.5f, 20.5f); line(12, 17.5f, 11, 20.5f); line(15.5f, 17.5f, 14.5f, 20.5f); break;
                case "snow": poly(cloud); disc(8.5f, 18.6f, 1); disc(12, 18.6f, 1); disc(15.5f, 18.6f, 1); disc(10.3f, 21.4f, 1); disc(13.7f, 21.4f, 1); break;
                case "storm": poly(cloud); poly(new float[] { 12.8f, 16, 10.8f, 18.8f, 13.8f, 18.8f, 11.8f, 21.8f }); break;
                case "blizzard": poly(cloud); line(4.5f, 18.5f, 13.5f, 18.5f); line(8, 21.2f, 18.5f, 21.2f); line(16, 18.5f, 19.5f, 18.5f); break;
                case "people": ring(9, 8, 3.1f); arc(9, 19.5f, 5.5f, 180, 360); ring(16.8f, 9, 2.4f); arc(16.8f, 19.5f, 4.3f, 235, 360); break;
                case "bag": poly(new float[] { 5, 8.5f, 19, 8.5f, 17.8f, 19.5f, 6.2f, 19.5f, 5, 8.5f }); arc(12, 8.5f, 3, 180, 360); break;
                case "clock": ring(12, 12, 8.6f); poly(new float[] { 12, 7.5f, 12, 12, 15.2f, 14 }); break;
                case "heart": poly(new float[] { 12, 19.5f, 7, 15.5f, 4.6f, 12, 4.5f, 9, 6, 6.8f, 8.6f, 6, 10.8f, 6.8f, 12, 8.4f, 13.2f, 6.8f, 15.4f, 6, 18, 6.8f, 19.5f, 9, 19.4f, 12, 17, 15.5f, 12, 19.5f }); break;
                case "refill": poly(new float[] { 3.5f, 7.5f, 18.5f, 7.5f, 18.5f, 16.5f, 3.5f, 16.5f, 3.5f, 7.5f }); line(21, 10.5f, 21, 13.5f); line(7.5f, 12, 14.5f, 12); line(11, 9, 11, 15); break;
                case "skull":
                    arc(12, 10.5f, 7, 140, 400); poly(new float[] { 17.4f, 15, 16.5f, 15.8f, 16.5f, 19.2f, 7.5f, 19.2f, 7.5f, 15.8f, 6.6f, 15 });
                    disc(9.3f, 11, 1.4f); disc(14.7f, 11, 1.4f); line(10.6f, 19.2f, 10.6f, 17.2f); line(13.4f, 19.2f, 13.4f, 17.2f); break;
                case "hammer": c.Line(x + 5 * s, y + 19.5f * s, x + 12.5f * s, y + 12 * s, color, w * 1.3f); poly(new float[] { 10.5f, 8, 14.5f, 4, 20.5f, 10, 16.5f, 14, 10.5f, 8 }); break;
                case "house": poly(new float[] { 3.5f, 11.5f, 12, 4, 20.5f, 11.5f }); poly(new float[] { 6, 10, 6, 20, 18, 20, 18, 10 }); poly(new float[] { 9.5f, 15.5f, 11.5f, 17.5f, 15, 13.5f }); break;
                case "reset": arc(12, 12, 7.5f, 215, 505); poly(new float[] { 4.6f, 4.2f, 5.9f, 7.7f, 9.6f, 6.9f }); break;
                case "book": poly(new float[] { 5, 4.5f, 15.5f, 4.5f, 18.5f, 7.5f, 18.5f, 19.5f, 8, 19.5f, 5, 16.5f, 5, 4.5f }); poly(new float[] { 5, 16.5f, 8, 13.5f, 18.5f, 13.5f }); break;
                default: disc(12, 12, 3); break;
            }
        }

        // ---------- shell ----------
        internal static void Render(UiCanvas c, UiModel m)
        {
            bool collapsed = m.Preferences.Collapsed;
            float w = PanelWidth(collapsed), h = PanelHeight(collapsed);
            c.Fill(R(0, 0, w, h), Frame, 16);
            c.Fill(R(1, 1, w - 2, h - 2), collapsed ? Side : Background, 15);
            if (!collapsed) { c.Fill(R(1, 1, RailWidth - 1, h - 2), Side, 15); c.Fill(R(RailWidth - 16, 1, 16, h - 2), Side, 0); c.Line(RailWidth, 1, RailWidth, h - 1, Divider, 1); }
            DrawRail(c, m, collapsed, h);
            if (collapsed) return;
            int page = Math.Max(PageHome, Math.Min(PageHelp, m.Page));
            DrawHeader(c, m, page);
            DrawSpeedStrip(c, m);
            bool needsWorld = page == PageHome || page == PageTargets || page == PageWorld;
            if (page == PageSpawn) DrawSpawn(c, m);
            else if (!m.Ready && needsWorld) DrawEmptyWorld(c, BodyY);
            else if (page == PageHome) DrawHome(c, m);
            else if (page == PageTargets) DrawTargets(c, m);
            else if (page == PageWorld) DrawWorld(c, m);
            else if (page == PageEfficiency) DrawMultipliers(c, m);
            else if (page == PageSettings) DrawSettings(c, m);
            else DrawHelp(c, m);
            DrawFooter(c, m);
        }

        private static void DrawRail(UiCanvas c, UiModel m, bool collapsed, float h)
        {
            c.Fill(R(12, 12, 32, 32), Logo, 9); Glyph(c, "logo", 18.5f, 18.5f, 19, Accent, Logo);
            for (int i = 0; i < RailLabels.Length; i++)
            {
                UiRect r = R(5, 58 + i * 54, 46, 50);
                bool selected = !collapsed && m.Page == i, hover = !selected && c.Hover(r);
                string fill = selected ? SelFillSide : hover ? RailHover : Side, ink = selected ? Accent : hover ? Muted : Quiet;
                if (selected || hover) c.Fill(r, fill, 10);
                Glyph(c, RailIcons[i], r.X + 13, r.Y + 7, 20, ink, fill);
                c.Text(R(r.X, r.Y + 30, r.W, 16), RailLabels[i], 12, selected ? AccentInk : ink, false, 1);
                if (i == PageTargets && m.Ready && m.SelectionCount > 0) Badge(c, r.X + r.W + 3, r.Y + 1, m.SelectionCount > 99 ? "99+" : m.SelectionCount.ToString());
                if (i == PageEfficiency && m.Ready && AnyMultiplier(m)) c.Fill(R(r.X + r.W - 14, r.Y + 7, 6, 6), Accent, 3);
                if (c.Hit(r, "nav." + i, true))
                {
                    if (collapsed) { m.Preferences.Collapsed = false; m.Emit("focus.clear", ""); }
                    m.Navigate(i);
                }
            }
            if (!collapsed)
            {
                UiRect help = R(11, h - 46, 34, 34); bool on = m.Page == PageHelp, hover = !on && c.Hover(help);
                string fill = on ? SelFillSide : hover ? RailHover : Side;
                if (on || hover) c.Fill(help, fill, 9);
                Glyph(c, "help", help.X + 8, help.Y + 8, 18, on ? Accent : Quiet, fill);
                if (c.Hit(help, "nav." + PageHelp, true)) m.Navigate(PageHelp);
                return;
            }
            // Collapsed: the current speed, a pause toggle and expansion stay within reach.
            c.Fill(R(8, h - 124, 40, 26), Surface, 8);
            c.Text(R(8, h - 124, 40, 26), m.Ready ? (m.AutoPauseActive || m.Speed == 0 ? "暂停" : FormatSpeed(m.Speed)) : "—", 12, m.AutoPauseActive ? AccentInk : TextMain, true, 1);
            UiRect pause = R(11, h - 90, 34, 34), expand = R(11, h - 48, 34, 34);
            bool pauseHover = m.Ready && c.Hover(pause), expandHover = c.Hover(expand);
            if (pauseHover) c.Fill(pause, RailHover, 9);
            Glyph(c, "pause", pause.X + 8, pause.Y + 8, 18, m.Ready ? Muted : "#5B554C", pauseHover ? RailHover : Side);
            if (c.Hit(pause, "speed.pause", m.Ready)) m.Emit("pause", "");
            if (expandHover) c.Fill(expand, RailHover, 9);
            Glyph(c, "expand", expand.X + 8, expand.Y + 8, 18, Muted, expandHover ? RailHover : Side);
            if (c.Hit(expand, "expand", true)) m.Preferences.Collapsed = false;
        }

        private static void DrawHeader(UiCanvas c, UiModel m, int page)
        {
            c.Text(R(X0, 14, 170, 24), Pages[page], 17, TextMain, true, 0);
            if (m.AutoPauseActive) { UiRect pill = R(256, 16, 66, 20); c.Fill(pill, SelFillBg, 10); c.Text(pill, "面板暂停", 12, AccentInk, false, 1); }
            UiRect collapse = R(330, 12, 28, 28), close = R(362, 12, 28, 28);
            bool hc = c.Hover(collapse), hx = c.Hover(close);
            if (hc) c.Fill(collapse, Raise, 7);
            Glyph(c, "collapse", collapse.X + 5, collapse.Y + 5, 18, Muted, hc ? Raise : Background);
            if (c.Hit(collapse, "collapse", true)) { m.Preferences.Collapsed = true; m.ArmedAction = null; m.Emit("focus.clear", ""); }
            if (hx) c.Fill(close, Raise, 7);
            Glyph(c, "close", close.X + 5, close.Y + 5, 18, Muted, hx ? Raise : Background);
            if (c.Hit(close, "close", true)) m.Emit("close", "");
        }

        // Every page keeps the most used control: pause and the preset game speeds.
        private static void DrawSpeedStrip(UiCanvas c, UiModel m)
        {
            UiRect r = R(X0, 52, BodyW, 32); c.Fill(r, Surface, 9);
            float segW = (BodyW - 6 - 2 * 7) / 8f;
            for (int i = 0; i < 8; i++)
            {
                UiRect s = R(r.X + 3 + i * (segW + 2), r.Y + 3, segW, r.H - 6);
                bool pause = i == 0; float value = pause ? 0 : SpeedPresets[i - 1];
                // While the panel holds its automatic pause, Speed is the one restored on close.
                bool selected = m.Ready && m.Speed == value, hover = m.Ready && !selected && c.Hover(s);
                if (selected) { c.Fill(s, SelLine, 6); c.Fill(s.Inset(1), Raise, 5); } else if (hover) c.Fill(s, Raise, 6);
                string ink = !m.Ready ? Quiet : selected ? TextMain : Muted, invariant = value.ToString(CultureInfo.InvariantCulture);
                if (pause) Glyph(c, "pause", s.X + (segW - 14) / 2, s.Y + 6, 14, ink, selected || hover ? Raise : Surface);
                else c.Text(s, value == 1 ? "1×" : value.ToString("0.##", CultureInfo.InvariantCulture), 12, ink, selected, 1);
                if (c.Hit(s, pause ? "speed.pause" : "speed." + invariant, m.Ready))
                { if (pause) m.Emit("pause", ""); else m.Emit("speed", invariant); }
            }
        }

        private static void DrawFooter(UiCanvas c, UiModel m)
        {
            c.Line(RailWidth + 1, FooterY, Width - 1, FooterY, Divider, 1);
            float statusRight = Width - 10;
            bool resetArmed = m.ArmedAction == "reset";
            if (m.Busy)
            {
                // Spawning can come from the catalog, a pack or a building supply; stopping is always in the same place.
                UiRect stop = R(Width - 10 - 56, FooterY + 8, 56, 24);
                if (Button(c, stop, "spawn.cancel", "停止", 2, true, 12)) m.Emit("spawn.cancel", "");
                statusRight = stop.X - 8;
            }
            else if (m.Ready)
            {
                int n = ActiveItems(m).Count;
                if (n > 0 && resetArmed)
                {
                    // The global switch-off runs only on a second click, before the runtime expires the armed state.
                    string label = ConfirmLabel(m, "确认全部关闭"); float w = c.Measure(label, 12, true) + 22;
                    UiRect confirm = R(Width - 10 - w, FooterY + 8, w, 24);
                    if (Confirmed(m, "reset", true, Button(c, confirm, "reset", label, 3, true, 12))) m.Emit("reset", "");
                    statusRight = confirm.X - 8;
                }
                else if (n == 0)
                {
                    float w = c.Measure("无修改", 12, false) + 20; UiRect chip = R(Width - 10 - w, FooterY + 8, w, 24);
                    c.Fill(chip, Surface, 12); c.Text(chip, "无修改", 12, Quiet, false, 1); statusRight = chip.X - 8;
                }
                else
                {
                    string label = n + " 项生效"; float tw = c.Measure(label, 12, false), w = 10 + 6 + 6 + tw + 6 + 20;
                    UiRect chip = R(Width - 10 - w, FooterY + 8, w, 24), open = R(chip.X, chip.Y, w - 22, 24), reset = R(chip.X + w - 22, chip.Y, 22, 24);
                    c.Fill(chip, c.Hover(open) ? Raise : Surface, 12);
                    c.Fill(R(chip.X + 10, chip.Y + 9, 6, 6), Accent, 3);
                    c.Text(R(chip.X + 22, chip.Y, tw + 2, 24), label, 12, TextMain, false, 0);
                    Glyph(c, "close", reset.X + 4, reset.Y + 5, 13, c.Hover(reset) ? TextMain : Quiet, Surface);
                    if (c.Hit(open, "active.open", true)) m.Navigate(PageHome);
                    if (Confirmed(m, "reset", true, c.Hit(reset, "reset", true))) m.Emit("reset", "");
                    statusRight = chip.X - 8;
                }
            }
            // A notice that does not fit wraps onto a second line instead of losing its end.
            // While the reset waits for its second click, the status says what that click will turn off.
            bool explain = resetArmed && m.Ready && !m.Busy && ActiveItems(m).Count > 0;
            string status = explain ? "将关闭 " + ActiveItems(m).Count + " 项修改" : m.Status;
            float tx = X0 + 14, available = statusRight - tx; string[] lines = TwoLines(c, status, available, 12);
            string ink = m.StatusError || explain ? Danger : Muted;
            c.Fill(R(X0, FooterY + (lines.Length > 1 ? 9 : 17), 7, 7), m.StatusError || explain ? Danger : m.Busy ? Accent : m.Ready ? Ok : Warn, 3.5f);
            if (lines.Length == 1) c.Text(R(tx, FooterY + 1, available, 38), lines[0], 12, ink, false, 0);
            else { c.Text(R(tx, FooterY + 3, available, 17), lines[0], 12, ink, false, 0); c.Text(R(tx, FooterY + 20, available, 17), lines[1], 12, ink, false, 0); }
        }
        // Breaks text into at most two lines of the given width, preferring a break after punctuation.
        internal static string[] TwoLines(UiCanvas c, string text, float width, int size)
        {
            text = text ?? "";
            if (c.Measure(text, size, false) <= width) return new string[] { text };
            int fit = 1;
            while (fit < text.Length && c.Measure(text.Substring(0, fit + 1), size, false) <= width) fit++;
            int split = fit;
            for (int i = fit; i > Math.Max(0, fit - 6); i--) if ("，；、：。 ".IndexOf(text[i - 1]) >= 0) { split = i; break; }
            return new string[] { text.Substring(0, split), Short(c, text.Substring(split).TrimStart(), width, size) };
        }

        internal static void RenderToast(UiCanvas c, float screenWidth, string text, bool error)
        {
            float w = Math.Min(640, c.Measure(text, 14, false) + 60), x = (screenWidth - w) / 2;
            c.Fill(R(x, 80, w, 44), Frame, 12); c.Fill(R(x + 1, 81, w - 2, 42), Background, 11);
            c.Fill(R(x + 18, 98, 8, 8), error ? Danger : Ok, 4);
            c.Text(R(x + 36, 88, w - 52, 28), Short(c, text, w - 52, 14), 14, error ? Danger : TextMain, false, 0);
        }

        private static void DrawEmptyWorld(UiCanvas c, float top)
        {
            float y = top + (FooterY - top) / 2 - 70;
            c.Fill(R(X0 + BodyW / 2 - 24, y, 48, 48), Surface, 14);
            Glyph(c, "logo", X0 + BodyW / 2 - 13, y + 11, 26, Accent, Surface);
            c.Text(R(X0, y + 64, BodyW, 24), "进入地图后开始使用", 16, TextMain, true, 1);
            c.Text(R(X0, y + 92, BodyW, 18), "新建游戏或载入存档，即可管理人物、资源与世界", 12, Muted, false, 1);
            c.Text(R(X0, y + 114, BodyW, 18), "Shift+F11 隐藏此面板，继续游戏", 12, Quiet, false, 1);
        }

        // ---------- active modifications ----------
        private static bool AnyMultiplier(UiModel m) { return Changed(SpeedMultipliers(m)) + Changed(CapacityMultipliers(m)) > 0; }
        private static int Changed(int[] values) { int count = 0; foreach (int value in values) if (value != 1) count++; return count; }
        // Efficiency tab order: work speed, then capacity.
        private static int[] SpeedMultipliers(UiModel m) { return new int[] { m.BuildMultiplier, m.CraftMultiplier, m.DryMultiplier, m.GatherMultiplier, m.FarmMultiplier }; }
        private static int[] CapacityMultipliers(UiModel m) { return new int[] { m.StorageMultiplier, m.CarryMultiplier, m.TransportMultiplier }; }
        private static readonly string[] SpeedKeys = { "build", "craft", "dry", "gather", "farm" }, CapacityKeys = { "storage", "carry", "transport" };
        private static readonly string[] SpeedNames = { "建造速度", "制作速度", "晾晒速度", "采集速度", "农牧速度" }, CapacityNames = { "仓储容量", "人力搬运", "运输工具容量" };
        // Everything "关闭所有开关" resets, so the list and the footer count describe exactly what reset would change.
        internal static List<string[]> ActiveItems(UiModel m)
        {
            List<string[]> list = new List<string[]>();
            for (int i = 0; i < 7; i++) if (m.Locks[i]) list.Add(new string[] { "lock." + i, StatNames[i], "保持满值", "" });
            if (m.Domestic) list.Add(new string[] { "domestic", "人物保护", "含玩家家畜", "" });
            if (m.Ready)
            {
                if (m.Speed != 1) list.Add(new string[] { "speed", "游戏速度", "", m.Speed == 0 ? "已暂停" : FormatSpeed(m.Speed) });
                int[] speeds = SpeedMultipliers(m), capacities = CapacityMultipliers(m);
                for (int i = 0; i < speeds.Length; i++) if (speeds[i] != 1) list.Add(new string[] { "multiplier." + SpeedKeys[i], SpeedNames[i], "", speeds[i] + "×" });
                for (int i = 0; i < capacities.Length; i++) if (capacities[i] != 1) list.Add(new string[] { "multiplier." + CapacityKeys[i], CapacityNames[i], "", capacities[i] + "×" });
            }
            if (m.BuildHotkey) list.Add(new string[] { "hotkey.build", "快捷键 " + KeyName(m.Preferences.Keys[0]), "快速完成建造", "" });
            if (m.KillHotkey) list.Add(new string[] { "hotkey.kill", "快捷键 " + KeyName(m.Preferences.Keys[1]), "杀死选中单位", "" });
            if (m.GiveHotkey) list.Add(new string[] { "hotkey.give", "快捷键 " + KeyName(m.Preferences.Keys[2]), "重复添加资源", "" });
            return list;
        }
        private static void TurnOff(UiModel m, string key)
        {
            if (key.StartsWith("lock.", StringComparison.Ordinal)) m.Locks[Int32.Parse(key.Substring(5), CultureInfo.InvariantCulture)] = false;
            else if (key == "domestic") m.Domestic = false;
            else if (key == "speed") m.Emit("speed", "1");
            else if (key == "hotkey.build") m.BuildHotkey = false;
            else if (key == "hotkey.kill") m.KillHotkey = false;
            else if (key == "hotkey.give") m.GiveHotkey = false;
            else if (key.StartsWith("multiplier.", StringComparison.Ordinal)) m.Emit(key, "1");
        }

        // ---------- 常用 ----------
        private static void DrawHome(UiCanvas c, UiModel m)
        {
            int enabled = 0; foreach (bool v in m.Locks) if (v) enabled++;
            Section(c, BodyY, "属性锁定", null, null, false);
            float tileW = (BodyW - 18) / 4f;
            // Seven stat locks and an eighth tile that locks or releases all of them.
            for (int i = 0; i < 8; i++)
            {
                UiRect r = R(X0 + (i % 4) * (tileW + 6), 120 + (i / 4) * 70, tileW, 64);
                bool all = i == 7, on = all ? enabled == 7 : m.Locks[i];
                string fill = Tile(c, r, on);
                Glyph(c, all ? "lock" : StatIcons[i], r.X + (tileW - 19) / 2, r.Y + 8, 19, on ? Accent : Muted, fill);
                c.Text(R(r.X, r.Y + 29, r.W, 17), all ? "全部" : StatNames[i], 13, TextMain, false, 1);
                c.Text(R(r.X, r.Y + 46, r.W, 14), all ? (enabled == 7 ? "全部锁定" : enabled + "/7 已锁定") : on ? "保持满值" : "自然消耗", 12, on ? AccentInk : Quiet, false, 1);
                if (c.Hit(r, all ? "stats.all" : "lock." + i, true)) { if (all) { for (int j = 0; j < 7; j++) m.Locks[j] = enabled != 7; } else m.Locks[i] = !on; }
            }
            // Livestock protection widens the scope of the locks above; it is not a stat of its own.
            UiRect scope = R(X0, 260, BodyW, 44);
            c.Fill(R(X0, 266, 32, 32), Surface, 9); Glyph(c, "paw", X0 + 7, 273, 18, m.Domestic ? Accent : Muted, Surface);
            c.Text(R(X0 + 42, 263, BodyW - 96, 19), "同时保护家畜", 14, TextMain, false, 0);
            c.Text(R(X0 + 42, 282, BodyW - 96, 16), m.Domestic ? "锁定也作用于玩家所属动物" : "锁定只作用于玩家人物", 12, m.Domestic ? AccentInk : Quiet, false, 0);
            m.Domestic = Switch(c, R(Right - 36, 272, 36, 20), "domestic", m.Domestic, scope);

            Section(c, 318, "知识点 · 可用于解锁科技", null, null, false);
            c.Text(R(X0, 336, 130, 32), m.Knowledge.ToString("N0", CultureInfo.InvariantCulture), 22, TextMain, true, 0);
            int[] amounts = { 10, 100, 1000 };
            for (int i = 0; i < 3; i++) if (Button(c, R(Right - 174 + i * 60, 337, 54, 30), "knowledge." + amounts[i], "+" + amounts[i], 0, true)) m.Emit("knowledge", amounts[i].ToString(CultureInfo.InvariantCulture));

            List<string[]> items = ActiveItems(m);
            if (Section(c, 384, "正在生效 · " + items.Count + " 项", "全部关闭", "active.reset", items.Count > 0)) m.Emit("reset", "");
            UiRect view = R(X0, 404, BodyW, FooterY - 8 - 404);
            if (items.Count == 0)
            {
                c.Text(R(X0, 428, BodyW, 22), "当前没有生效的修改", 13, Muted, false, 1);
                c.Text(R(X0, 452, BodyW, 18), "开启的锁定、倍率与快捷键会列在这里，可逐项关闭", 12, Quiet, false, 1);
                return;
            }
            // Six 28-unit rows fit without scrolling, which covers the usual combinations.
            const float rowH = 28;
            bool scrolls = items.Count * rowH > view.H; float rowW = view.W - (scrolls ? 14 : 0);
            m.ActiveScroll = c.BeginScroll(view, m.ActiveScroll, items.Count * rowH);
            for (int i = 0; i < items.Count; i++)
            {
                string[] it = items[i]; float y = i * rowH, nameW = Math.Min(130, c.Measure(it[1], 13, false) + 2);
                c.Fill(R(0, y + 11, 6, 6), Accent, 3);
                c.Text(R(14, y, nameW, rowH), it[1], 13, TextMain, false, 0);
                if (it[2].Length > 0) c.Text(R(14 + nameW + 6, y, rowW - 104 - nameW - 20, rowH), it[2], 12, Muted, false, 0);
                if (it[3].Length > 0) c.Text(R(rowW - 104, y, 70, rowH), it[3], 13, AccentInk, true, 2);
                UiRect off = R(rowW - 28, y + 1, 26, 26); bool hover = c.Hover(off);
                if (hover) c.Fill(off, Raise, 6);
                Glyph(c, "close", off.X + 6, off.Y + 6, 14, hover ? TextMain : Quiet, hover ? Raise : Background);
                if (c.Hit(off, "active.off." + it[0], true)) TurnOff(m, it[0]);
                if (i < items.Count - 1) c.Line(0, y + rowH, rowW, y + rowH, Divider, 1);
            }
            c.EndScroll();
        }

        // ---------- 生成 ----------
        private static void DrawSpawn(UiCanvas c, UiModel m)
        {
            string[] counts = { m.ResourceCount > 0 ? m.ResourceCount.ToString() : "", m.AnimalCount > 0 ? m.AnimalCount.ToString() : "", m.Preferences.Packs.Count.ToString() };
            int tab = Segmented(c, R(X0, BodyY, BodyW, 32), new string[] { "spawn.tab.0", "spawn.tab.1", "spawn.tab.2" }, new string[] { "资源", "动物", "物资包" }, counts, m.SpawnTab, true);
            if (tab >= 0) m.SelectSpawnTab(tab);
            if (m.SpawnTab == 2) { DrawPacks(c, m); return; }
            if (!m.Ready) { DrawEmptyWorld(c, 140); return; }
            DrawCatalog(c, m, m.SpawnTab == 1);
        }

        private static List<UiItem> Filter(UiModel m, bool animal)
        {
            List<UiItem> found = new List<UiItem>();
            string query = (m.Search ?? "").Trim();
            foreach (UiItem item in animal ? m.Animals : m.Resources)
            {
                if (query.Length > 0 && item.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 && item.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!animal && m.Category == 5 && !m.Preferences.Favorites.Contains(item.Id)) continue;
                if (!animal && m.Category > 0 && m.Category < 5 && (item.Categories & (1 << (m.Category - 1))) == 0) continue;
                found.Add(item);
            }
            return found;
        }
        private static UiItem Selected(UiModel m, bool animal)
        { string id = animal ? m.SelectedAnimal : m.SelectedResource; return (animal ? m.Animals : m.Resources).Find(i => i.Id == id); }
        private static void ResetScroll(UiModel m, bool animal) { if (animal) m.AnimalScroll = 0; else m.ResourceScroll = 0; }

        private static void DrawCatalog(UiCanvas c, UiModel m, bool animal)
        {
            UiRect search = R(X0, 140, BodyW, 34);
            if (c.Focused("search")) { c.Fill(search, SelLine, 9); c.Fill(search.Inset(1), Surface, 8); } else c.Fill(search, Surface, 9);
            Glyph(c, "search", X0 + 10, 149, 16, Quiet, Surface);
            string next = c.Input(R(X0 + 32, 143, BodyW - 66, 28), "search", m.Search, 60, false);
            if (next != m.Search) { m.Search = next; ResetScroll(m, animal); }
            if (String.IsNullOrEmpty(m.Search)) c.Text(R(X0 + 36, 140, BodyW - 70, 34), animal ? "搜索动物名称或英文 ID" : "搜索物品名称或英文 ID", 13, Quiet, false, 0);
            else
            {
                UiRect clear = R(Right - 32, 143, 28, 28);
                Glyph(c, "close", clear.X + 7, clear.Y + 7, 14, Muted, Surface);
                if (c.Hit(clear, "search.clear", true)) { m.Search = ""; ResetScroll(m, animal); }
            }
            float gridY, dockY = animal ? 408 : 446;
            if (!animal)
            {
                string[] categories = { "全部", "食物", "材料", "工具", "衣物", "收藏" }; float x = X0;
                for (int i = 0; i < categories.Length; i++)
                {
                    float w = c.Measure(categories[i], 12, false) + (i == 5 ? 34 : 20); UiRect chip = R(x, 184, w, 26);
                    bool on = m.Category == i;
                    if (Choice(c, chip, "category." + i, i == 5 ? "" : categories[i], on, true, 13)) { m.Category = i; m.ResourceScroll = 0; }
                    if (i == 5)
                    {
                        Glyph(c, "star", chip.X + 9, chip.Y + 7, 12, on ? AccentInk : Muted, on ? SelFillBg : Surface);
                        c.Text(R(chip.X + 24, chip.Y, w - 30, 26), categories[i], 12, on ? AccentInk : Muted, false, 0);
                    }
                    x += w + 6;
                }
                gridY = 220;
            }
            else { c.Text(R(X0, 184, BodyW, 26), "全部物种 · 保留物种原有阵营", 12, Quiet, false, 0); gridY = 214; }

            List<UiItem> found = Filter(m, animal);
            UiRect viewport = R(X0, gridY, BodyW, dockY - 8 - gridY);
            if (found.Count == 0)
            {
                Glyph(c, "search", X0 + BodyW / 2 - 14, viewport.Y + 26, 28, Quiet, Background);
                c.Text(R(X0, viewport.Y + 64, BodyW, 22), "没有找到匹配项", 15, TextMain, true, 1);
                c.Text(R(X0, viewport.Y + 90, BodyW, 18), "试试其他名称，或清空搜索与分类", 12, Muted, false, 1);
                if (Button(c, R(X0 + (BodyW - 112) / 2, viewport.Y + 120, 112, 32), "filters.clear", "清空筛选", 0, true)) { m.Search = ""; m.Category = 0; ResetScroll(m, animal); }
            }
            else
            {
                // The rightmost 14 units stay free for the scroll bar at every interface size.
                const int columns = 4; const float gap = 6, cellH = 76; float cellW = (BodyW - 14 - gap * (columns - 1)) / columns;
                int rows = (found.Count + columns - 1) / columns;
                float scroll = c.BeginScroll(viewport, animal ? m.AnimalScroll : m.ResourceScroll, rows * (cellH + gap) - gap);
                if (animal) m.AnimalScroll = scroll; else m.ResourceScroll = scroll;
                int first = Math.Max(0, (int)(scroll / (cellH + gap))), last = Math.Min(rows - 1, (int)((scroll + viewport.H) / (cellH + gap)));
                UiItem selected = Selected(m, animal);
                for (int row = first; row <= last; row++) for (int col = 0; col < columns; col++)
                {
                    int index = row * columns + col; if (index >= found.Count) break; UiItem item = found[index];
                    UiRect r = R(col * (cellW + gap), row * (cellH + gap), cellW, cellH); bool active = selected != null && selected.Id == item.Id;
                    string fill = Tile(c, r, active);
                    if (item.Image != null) c.Image(R(r.X + (cellW - 34) / 2, r.Y + 10, 34, 34), item.Image);
                    else Glyph(c, animal ? "paw" : "spawn", r.X + (cellW - 28) / 2, r.Y + 13, 28, Quiet, fill);
                    c.Text(R(r.X + 4, r.Y + 50, cellW - 8, 18), Short(c, item.Name, cellW - 8, 12), 12, active ? TextMain : Muted, false, 1);
                    if (active) c.Fill(R(r.X + cellW - 13, r.Y + 6, 7, 7), Accent, 3.5f);
                    if (!animal && m.Preferences.Favorites.Contains(item.Id)) Glyph(c, "star", r.X + 5, r.Y + 5, 11, Warn, fill);
                    if (c.Hit(r, "item." + item.Id, true)) { if (animal) m.SelectedAnimal = item.Id; else m.SelectedResource = item.Id; }
                }
                c.EndScroll();
            }
            DrawSpawnDock(c, m, animal, found, dockY);
        }

        private static string SpawnPlace(UiCanvas c, UiModel m, float width)
        {
            if (m.Location == 1) return "生成在画面中心地面";
            if (m.SelectionCount == 0) return "当前将使用画面中心";
            return "生成在 " + Short(c, m.SelectionName, Math.Max(40, width - 80), 12) + " 附近";
        }

        // The selected item, its quantity and the add button stay fixed below the scrolling catalog.
        private static void DrawSpawnDock(UiCanvas c, UiModel m, bool animal, List<UiItem> filtered, float y)
        {
            c.Line(RailWidth + 1, y, Width - 1, y, Divider, 1);
            UiItem item = Selected(m, animal);
            string quantity = animal ? m.AnimalQuantity : m.Quantity; int maximum = animal ? 50 : 1000, count;
            bool valid = Int32.TryParse(quantity, out count) && count >= 1 && count <= maximum;
            float infoW = animal ? BodyW - 38 : BodyW - 38 - 126;
            if (item != null && item.Image != null) c.Image(R(X0, y + 11, 30, 30), item.Image);
            else Glyph(c, animal ? "paw" : "spawn", X0 + 3, y + 14, 24, Quiet, Background);
            c.Text(R(X0 + 38, y + 8, infoW, 19), item == null ? "请先选择一项" : Short(c, item.Name, infoW, 14), 14, TextMain, true, 0);
            string note, noteColor = Quiet;
            if (!valid) { note = "请输入 1–" + maximum + " 的整数"; noteColor = Danger; }
            else if (m.Busy) { note = "正在添加 " + Short(c, m.TaskName, 90, 12) + " · " + m.Done + "/" + m.Total; noteColor = AccentInk; }
            else if (m.ShowSuccess) { note = "已添加 " + Short(c, m.CompletedName, 90, 12) + " × " + m.CompletedCount; noteColor = Ok; }
            else if (item != null && !filtered.Exists(i => i.Id == item.Id)) { note = "已选项不在当前筛选中"; noteColor = Warn; }
            else note = SpawnPlace(c, m, infoW);
            c.Text(R(X0 + 38, y + 28, infoW, 16), Short(c, note, infoW, 12), 12, noteColor, false, 0);
            if (m.Busy) { c.Fill(R(X0, y + 46, BodyW, 2), Raise, 1); c.Fill(R(X0, y + 46, BodyW * Math.Max(0, Math.Min(1, (float)m.Done / Math.Max(1, m.Total))), 2), Accent, 1); }
            if (!animal)
            {
                bool favorite = item != null && m.Preferences.Favorites.Contains(item.Id);
                if (Button(c, R(Right - 120, y + 12, 82, 28), "pack.add", "加入物资包", 0, item != null && valid, 12)) AddPackItem(m, item, quantity, true);
                UiRect star = R(Right - 32, y + 12, 32, 28); bool hover = item != null && c.Hover(star);
                c.Fill(star, hover ? "#38342E" : Raise, 8);
                Glyph(c, "star", star.X + 8, star.Y + 5, 17, favorite ? Warn : item == null ? Quiet : Muted, hover ? "#38342E" : Raise);
                if (c.Hit(star, "favorite", item != null)) { if (!m.Preferences.Favorites.Remove(item.Id)) m.Preferences.Favorites.Add(item.Id); }
            }

            float qy = y + 50;
            c.Fill(R(X0, qy, 118, 30), Surface, 8);
            UiRect minus = R(X0, qy, 30, 30), plus = R(X0 + 88, qy, 30, 30), field = R(X0 + 32, qy + 3, 54, 24);
            if (c.Hover(minus)) c.Fill(minus, Raise, 8);
            Glyph(c, "minus", minus.X + 8, minus.Y + 8, 14, Muted, Surface);
            if (c.Hit(minus, "quantity.minus", true) && Int32.TryParse(quantity, out count)) quantity = Math.Max(1, Math.Min(maximum, count - 1)).ToString();
            string border = !valid ? DangerLine : c.Focused("quantity") ? SelLine : InputFill;
            c.Fill(field, border, 6); c.Fill(field.Inset(1), InputFill, 5);
            quantity = c.Input(field.Inset(2), "quantity", quantity, 4, true);
            if (c.Hover(plus)) c.Fill(plus, Raise, 8);
            Glyph(c, "plus", plus.X + 8, plus.Y + 8, 14, Muted, Surface);
            if (c.Hit(plus, "quantity.plus", true) && Int32.TryParse(quantity, out count)) quantity = Math.Max(1, Math.Min(maximum, count + 1)).ToString();
            int[] amounts = animal ? new int[] { 1, 5, 10, 50 } : new int[] { 1, 10, 50, 100 };
            float px = X0 + 124, pw = (Right - px - 18) / 4f;
            for (int i = 0; i < amounts.Length; i++)
                if (Choice(c, R(px + i * (pw + 6), qy, pw, 30), "quantity." + amounts[i], amounts[i].ToString(), quantity == amounts[i].ToString())) quantity = amounts[i].ToString();
            if (animal) m.AnimalQuantity = quantity; else m.Quantity = quantity;
            valid = Int32.TryParse(quantity, out count) && count >= 1 && count <= maximum;

            float ly = qy + 38;
            if (animal)
            {
                int age = Segmented(c, R(X0, ly, 153, 30), new string[] { "age.0", "age.1", "age.2" }, new string[] { "幼年", "成年", "老年" }, null, m.Age, true, 12);
                if (age >= 0) m.Age = age;
                int gender = Segmented(c, R(X0 + 159, ly, 153, 30), new string[] { "gender.0", "gender.1", "gender.2" }, new string[] { "雄性", "雌性", "随机" }, null, m.Gender, true, 12);
                if (gender >= 0) m.Gender = gender;
                ly += 38;
            }
            int location = Segmented(c, R(X0, ly, 176, 34), new string[] { "location.target", "location.center" }, new string[] { "目标附近", "画面中心" }, null, m.Location, true, 12);
            if (location >= 0) m.Location = location;
            string primary = m.Busy ? "正在添加 " + (m.Total > 0 ? Math.Round(100f * m.Done / m.Total) : 0) + "%" : item == null ? "请先选择" : !valid ? "数量无效"
                : (animal ? "生成 " + count + " 只" : "添加 " + count + " 件");
            if (Button(c, R(Right - 128, ly, 128, 34), "spawn", primary, 1, item != null && valid && !m.Busy, 14)) m.Emit(animal ? "spawn.animal" : "spawn.resource", "");
        }

        // ---------- 世界 ----------
        // The slider is logarithmic: every doubling of speed gets the same width, so 0.25×–1× is as easy to hit as 4×–15×.
        internal static float SpeedTrack(float speed) { return (float)(Math.Log(Math.Max(.25f, Math.Min(15, speed)) / .25) / Math.Log(60)); }
        private static float TrackSpeed(float track)
        {
            float speed = (float)(.25 * Math.Pow(60, Math.Max(0, Math.Min(1, track))));
            return speed < 1 ? (float)Math.Round(speed * 4) / 4 : (float)Math.Round(speed);
        }

        private static void DrawWorld(UiCanvas c, UiModel m)
        {
            Section(c, BodyY, "游戏速度", null, null, false);
            SectionNote(c, BodyY, m.AutoPauseActive ? (m.Speed == 0 ? "面板暂停 · 关闭后保持暂停" : "面板暂停 · 关闭后恢复 " + FormatSpeed(m.Speed)) : m.Speed == 0 ? "已暂停" : "当前 " + FormatSpeed(m.Speed));
            float current = Math.Max(.25f, Math.Min(15, m.Speed)), track = SpeedTrack(current);
            float raw = c.Slider(R(X0, 120, BodyW, 28), "speed.slider", track, 0, 1);
            c.Fill(R(X0, 132, BodyW, 4), Raise, 2); c.Fill(R(X0, 132, BodyW * track, 4), Accent, 2);
            c.Fill(R(X0 + BodyW * track - 7, 127, 14, 14), Knob, 7);
            // Only an actual drag dispatches, so a speed between the slider steps is never rewritten on its own.
            float next = TrackSpeed(raw);
            if (Math.Abs(raw - track) > .0001f && next != current) m.Emit("speed", next.ToString(CultureInfo.InvariantCulture));
            c.Text(R(X0, 150, 50, 16), "0.25×", 12, Quiet, false, 0);
            c.Text(R(X0 + 50, 150, BodyW - 100, 16), "拖动可选 1–15 之间任意整数倍", 12, Quiet, false, 1);
            c.Text(R(Right - 50, 150, 50, 16), "15×", 12, Quiet, false, 2);

            Section(c, 180, "天气", null, null, false); SectionNote(c, 180, "逐渐过渡，之后仍会自然变化");
            string[] names = { "晴朗", "降雨", "降雪", "雷暴", "暴风雪" }; string[] icons = { "sun", "rain", "snow", "storm", "blizzard" };
            float tw = (BodyW - 24) / 5f;
            for (int i = 0; i < 5; i++)
            {
                UiRect r = R(X0 + i * (tw + 6), 200, tw, 66); bool selected = m.Weather == i;
                string fill = Tile(c, r, selected);
                Glyph(c, icons[i], r.X + (tw - 22) / 2, r.Y + 11, 22, selected ? Accent : Muted, fill);
                c.Text(R(r.X, r.Y + 40, r.W, 18), names[i], 12, selected ? TextMain : Muted, false, 1);
                if (c.Hit(r, "weather." + i, true)) m.Emit("weather", i.ToString());
            }
            DrawVisitors(c, m, 282);

            Section(c, 420, "面板", null, null, false);
            UiRect row = R(X0, 438, BodyW, 52);
            c.Fill(R(X0, 448, 32, 32), Surface, 9); Glyph(c, "clock", X0 + 7, 455, 18, Muted, Surface);
            c.Text(R(X0 + 42, 446, BodyW - 96, 19), "打开面板时暂停", 14, TextMain, false, 0);
            c.Text(R(X0 + 42, 466, BodyW - 96, 16), m.AutoPauseActive ? "当前由面板持有暂停，关闭后恢复" : "关闭后恢复原倍率，原本暂停的不继续", 12, m.AutoPauseActive ? AccentInk : Quiet, false, 0);
            m.Preferences.AutoPause = Switch(c, R(Right - 36, 454, 36, 20), "prefs.autopause", m.Preferences.AutoPause, row);
        }

        // ---------- 使用说明 ----------
        private static void DrawHelp(UiCanvas c, UiModel m)
        {
            Section(c, BodyY, "键盘操作", null, null, false);
            string[] keys = { "F11", "Shift+F11", KeyName(m.Preferences.Keys[0]), KeyName(m.Preferences.Keys[1]), KeyName(m.Preferences.Keys[2]) };
            string[] labels = { "展开或收起面板，隐藏时直接打开", "显示或隐藏修改器", "快速完成建造 · 在设置页启用", "杀死选中单位 · 在设置页启用", "重复添加资源 · 在设置页启用" };
            for (int i = 0; i < 5; i++) { float y = 118 + i * 32; Key(c, R(X0, y + 4, 70, 22), Short(c, keys[i], 66, 12)); c.Text(R(X0 + 82, y, BodyW - 82, 30), labels[i], 13, TextMain, false, 0); }
            Section(c, 288, "添加资源", null, null, false);
            string[] steps = { "搜索名称或英文 ID，点击物品卡片", "填写数量，选择生成位置", "点击添加，再由村民搬运入库" };
            for (int i = 0; i < 3; i++)
            {
                float y = 308 + i * 28;
                c.Fill(R(X0, y + 4, 20, 20), SelFillBg, 10); c.Text(R(X0, y + 4, 20, 20), (i + 1).ToString(), 11, AccentInk, true, 1);
                c.Text(R(X0 + 30, y, BodyW - 30, 28), steps[i], 13, TextMain, false, 0);
            }
            Section(c, 404, "使用提示", null, null, false);
            string[] tips = {
                "开关每次启动默认关闭，人物保护只作用于玩家",
                "面板打开时，可直接点选面板外的地图目标",
                "添加物品、知识、建造与死亡结果随存档保存",
                "健康锁不拦截寿命结束、雷击等直接死亡",
                "拖动标题栏可让面板浮动，在设置页重新停靠"
            };
            for (int i = 0; i < tips.Length; i++)
            {
                float y = 424 + i * 28;
                c.Fill(R(X0 + 2, y + 12, 5, 5), Quiet, 2.5f);
                c.Text(R(X0 + 16, y, BodyW - 16, 28), Short(c, tips[i], BodyW - 16, 12), 12, Muted, false, 0);
            }
        }
    }
}
