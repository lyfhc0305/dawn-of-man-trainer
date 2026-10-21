using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DawnTrainer;

internal sealed class UiHit
{
    internal UiRect Rect;
    internal string Id;
    internal bool Enabled;
}

internal sealed class UiText
{
    internal UiRect Rect;
    internal string Text;
}

// Offline implementation of the Unity canvas contract. It records hits, text and inputs;
// a subclass only draws (PNG through System.Drawing on Windows, SVG anywhere). It never loads Unity.
internal abstract class CheckCanvas : UiCanvas, IDisposable
{
    internal readonly List<UiHit> Hits = new List<UiHit>();
    internal readonly List<UiText> Texts = new List<UiText>();
    internal readonly Dictionary<string, string> Inputs = new Dictionary<string, string>();
    internal string ClickId, FocusId;
    internal float? NextSlider;
    internal readonly Dictionary<string, float> NextSliders = new Dictionary<string, float>();
    internal readonly List<string> SliderCalls = new List<string>();
    private bool consumed;
    private float offsetX, offsetY;
    private readonly Stack<UiRect> clips = new Stack<UiRect>();
    private readonly Stack<float[]> offsets = new Stack<float[]>();

    protected abstract void DrawFill(UiRect r, string color, float radius);
    protected abstract void DrawText(UiRect r, string text, int size, string color, bool bold, int align);
    protected abstract void DrawLine(float x1, float y1, float x2, float y2, string color, float width);
    protected abstract void DrawImage(UiRect r, object image);
    // Clip to the viewport (current coordinates) and shift its content up by the scroll position.
    protected abstract void PushClip(UiRect viewport, float position);
    protected abstract void PopClip();
    public virtual void Dispose() { }

    internal static UiRect Intersect(UiRect a, UiRect b)
    {
        float x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        return new UiRect(x, y, Math.Min(a.X + a.W, b.X + b.W) - x, Math.Min(a.Y + a.H, b.Y + b.H) - y);
    }
    internal override void Fill(UiRect r, string color, float radius) { if (r.W > 0 && r.H > 0) DrawFill(r, color, radius); }
    // Text inside a scroll view is recorded only where it is visible, like hits.
    internal override void Text(UiRect r, string text, int size, string color, bool bold, int align)
    {
        UiRect absolute = new UiRect(r.X + offsetX, r.Y + offsetY, r.W, r.H);
        if (clips.Count > 0) absolute = Intersect(absolute, clips.Peek());
        if (absolute.W > 0 && absolute.H > 0) Texts.Add(new UiText { Rect = absolute, Text = text ?? "" });
        DrawText(r, text ?? "", size, color, bold, align);
    }
    internal override void Line(float x1, float y1, float x2, float y2, string color, float width) { DrawLine(x1, y1, x2, y2, color, width); }
    internal override void Image(UiRect r, object image) { DrawImage(r, image); }
    internal override bool Hit(UiRect r, string id, bool enabled)
    {
        UiRect absolute = new UiRect(r.X + offsetX, r.Y + offsetY, r.W, r.H);
        if (clips.Count > 0) absolute = Intersect(absolute, clips.Peek());
        if (absolute.W > 0 && absolute.H > 0) Hits.Add(new UiHit { Id = id, Enabled = enabled, Rect = absolute });
        if (!consumed && enabled && id == ClickId) { consumed = true; return true; }
        return false;
    }
    internal override bool Hover(UiRect r) { return false; }
    internal override bool Focused(string id) { return FocusId == id; }
    internal override string Input(UiRect r, string id, string value, int maximumLength, bool centered)
    {
        Hit(r, "input." + id, true);
        string result; if (!Inputs.TryGetValue(id, out result)) result = value ?? "";
        if (result.Length > maximumLength) result = result.Substring(0, maximumLength);
        Text(r.Inset(2), result, centered ? 16 : 14, TrainerView.TextMain, false, centered ? 1 : 0); return result;
    }
    internal override float Slider(UiRect r, string id, float value, float min, float max)
    { SliderCalls.Add(id); Hit(r, "slider." + id, true); float next; return NextSliders.TryGetValue(id, out next) ? next : NextSlider ?? value; }
    internal override float BeginScroll(UiRect viewport, float position, float contentHeight)
    {
        position = Math.Max(0, Math.Min(position, Math.Max(0, contentHeight - viewport.H)));
        if (contentHeight > viewport.H)
        {
            Fill(new UiRect(viewport.X + viewport.W - 5, viewport.Y, 5, viewport.H), "#171512", 2);
            float h = Math.Max(28, viewport.H * viewport.H / contentHeight), y = position / (contentHeight - viewport.H) * (viewport.H - h);
            Fill(new UiRect(viewport.X + viewport.W - 5, viewport.Y + y, 5, h), "#57514A", 2);
        }
        UiRect clip = new UiRect(viewport.X + offsetX, viewport.Y + offsetY, viewport.W, viewport.H);
        if (clips.Count > 0) clip = Intersect(clip, clips.Peek());
        clips.Push(clip); offsets.Push(new float[] { offsetX, offsetY });
        PushClip(viewport, position);
        offsetX += viewport.X; offsetY += viewport.Y - position; return position;
    }
    internal override void EndScroll() { PopClip(); clips.Pop(); float[] o = offsets.Pop(); offsetX = o[0]; offsetY = o[1]; }
}

internal static class UiChecks
{
    // Supplied by the exporter: creates a canvas of the given panel size.
    internal static Func<float, float, CheckCanvas> Create;
    private static int passed, failed;
    internal static int Failed { get { return failed; } }
    internal static void Check(bool condition, string message) { if (condition) { passed++; Console.WriteLine("PASS " + message); } else { failed++; Console.WriteLine("FAIL " + message); } }
    internal static int Result(string backend)
    { Console.WriteLine("UI RESULT passed=" + passed + " failed=" + failed + "; shared layout rendered offline (" + backend + "), game not launched"); return failed == 0 ? 0 : 1; }

    private const float Right = 384, Width = TrainerView.Width, Height = TrainerView.Height;
    internal static CheckCanvas Render(UiModel m) { return Render(m, null, null); }
    internal static CheckCanvas Render(UiModel m, string click) { return Render(m, click, null); }
    internal static CheckCanvas Render(UiModel m, string click, Action<CheckCanvas> setup)
    {
        CheckCanvas c = Create(TrainerView.PanelWidth(m.Preferences.Collapsed), TrainerView.PanelHeight(m.Preferences.Collapsed));
        c.ClickId = click; if (setup != null) setup(c);
        TrainerView.Render(c, m); return c;
    }

    internal static UiModel Model(string catalog)
    {
        UiModel m = new UiModel { GameVersion = "1.8.3", Ready = true, Knowledge = 128, SelectionCount = 3, BuildTargets = 2, KillTargets = 3, SelectedResource = "raw_meat", SelectedAnimal = "deer", StatusError = false, Weather = 0, SelectionName = "Vorghok", TaskName = "生肉", Status = "准备就绪，修改仅作用于当前地图" };
        m.RescueTargets = 2; m.RepairTargets = 1; m.ResourceRepairTargets = 1; m.TameTargets = 1; m.HousingFree = 18; m.MigrantCount = m.TraderCount = 0;
        m.CapturingKey = -1; m.AutoPauseActive = false;
        for (int i = 0; i < 7; i++) m.TargetStats[i] = .48f + i * .07f;
        m.Materials.Add(new SupplyLine { Id = "log", Quantity = "8" }); m.Materials.Add(new SupplyLine { Id = "stone", Quantity = "6" });
        m.Locks[0] = m.Locks[4] = true;
        if (catalog == "--fixtures")
        {
            // Synthetic catalog exercises long lists without shipping game assets.
            m.Resources.Add(new UiItem { Id = "raw_meat", Name = "肉", Categories = 1 });
            string[] ids = { "log", "sticks", "stone", "cured_meat", "dry_fish", "skins_outfit", "flint" }; string[] names = { "原木", "树枝", "石头", "腌肉", "干鱼", "兽皮装", "燧石" };
            for (int i = 0; i < ids.Length; i++) m.Resources.Add(new UiItem { Id = ids[i], Name = names[i], Categories = 2 });
            for (int i = 0; i < 5; i++) m.Resources.Add(new UiItem { Id = "flint_fixture_" + i, Name = "燧石测试 " + i, Categories = 2 });
            for (int i = m.Resources.Count; i < 87; i++) m.Resources.Add(new UiItem { Id = "resource_fixture_" + i, Name = "测试资源 " + i, Categories = 1 << (i % 4) });
            m.Animals.Add(new UiItem { Id = "deer", Name = "鹿" });
            for (int i = 1; i < 31; i++) m.Animals.Add(new UiItem { Id = "animal_fixture_" + i, Name = "测试动物 " + i });
        }
        else foreach (string line in File.ReadAllLines(catalog, Encoding.UTF8))
        {
            string[] cells = line.Split('\t'); if (cells.Length < 5) continue;
            UiItem item = new UiItem { Id = cells[1], Name = cells[2], Categories = Int32.Parse(cells[3]), Image = cells[4] };
            (cells[0] == "resource" ? m.Resources : m.Animals).Add(item);
        }
        m.ResourceCount = m.Resources.Count; m.AnimalCount = m.Animals.Count; return m;
    }

    internal static void Run(string catalog)
    {
        GeometryChecks(Model(catalog)); NavigationChecks(Model(catalog)); CatalogChecks(Model(catalog)); FeatureUiChecks(Model(catalog));
        PackQuantityChecks(Model(catalog)); MultiplierUiChecks(Model(catalog));
    }

    private static bool InWindow(CheckCanvas c, UiModel m)
    {
        float w = TrainerView.PanelWidth(m.Preferences.Collapsed), h = TrainerView.PanelHeight(m.Preferences.Collapsed);
        return c.Hits.All(x => x.Rect.X >= 0 && x.Rect.Y >= 0 && x.Rect.X + x.Rect.W <= w + .1f && x.Rect.Y + x.Rect.H <= h + .1f);
    }
    internal static bool Overlaps(List<UiHit> hits)
    {
        for (int a = 0; a < hits.Count; a++) for (int b = a + 1; b < hits.Count; b++) if (Intersects(hits[a].Rect, hits[b].Rect)) return true;
        return false;
    }
    internal static bool Intersects(UiRect a, UiRect b)
    { return Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X) > 1 && Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y) > 1; }
    private static UiHit HitOf(CheckCanvas c, string id) { return c.Hits.FirstOrDefault(h => h.Id == id); }

    private static void GeometryChecks(UiModel model)
    {
        string[] names = { "home", "targets", "spawn", "world", "efficiency", "settings", "help" };
        for (int page = 0; page <= TrainerView.PageHelp; page++)
        for (int tab = 0; tab < (page == TrainerView.PageSpawn ? 3 : page == TrainerView.PageTargets ? 2 : 1); tab++)
        {
            model.Page = page; model.SpawnTab = page == TrainerView.PageSpawn ? tab : 0; model.TargetTab = page == TrainerView.PageTargets ? tab : 0;
            string label = names[page] + (page == TrainerView.PageSpawn || page == TrainerView.PageTargets ? " tab " + tab : "");
            using (CheckCanvas c = Render(model))
            {
                Check(InWindow(c, model), label + " controls stay inside the 400x620 drawer");
                Check(!Overlaps(c.Hits), label + " interactive controls do not overlap");
                Check(c.Texts.All(t => t.Rect.Y >= 0 && t.Rect.Y + t.Rect.H <= Height + .1f && t.Rect.X >= 0 && t.Rect.X + t.Rect.W <= Width + .1f), label + " text stays inside the drawer");
                Check(Enumerable.Range(0, 6).All(i => c.Hits.Any(h => h.Id == "nav." + i)) && c.Hits.Any(h => h.Id == "nav.6") && c.Hits.Any(h => h.Id == "collapse") && c.Hits.Any(h => h.Id == "close"), label + " keeps the rail, help, collapse and close controls");
                Check(c.Hits.Count(h => h.Id.StartsWith("speed.", StringComparison.Ordinal) && h.Id != "speed.slider") == 8, label + " keeps the pause and seven speed presets at the top");
                Check(c.Hits.Where(h => h.Id != "slider.speed.slider" && !h.Id.StartsWith("nav.", StringComparison.Ordinal) && h.Id != "collapse" && h.Id != "close" && !h.Id.StartsWith("speed.", StringComparison.Ordinal) && h.Id != "active.open" && h.Id != "reset" && h.Id != "spawn.cancel")
                    .All(h => h.Rect.X >= 72 - .1f && h.Rect.X + h.Rect.W <= Right + .1f && h.Rect.Y >= 92 && h.Rect.Y + h.Rect.H <= Height - 40 + .1f), label + " page controls stay inside the body between speed strip and footer");
            }
        }
        model.Page = TrainerView.PageHome; model.SpawnTab = model.TargetTab = 0;
        model.Preferences.Collapsed = true;
        using (CheckCanvas c = Render(model))
        {
            Check(InWindow(c, model) && !Overlaps(c.Hits), "collapsed rail controls stay inside the 56x520 rail without overlap");
            Check(Enumerable.Range(0, 6).All(i => c.Hits.Any(h => h.Id == "nav." + i)) && HitOf(c, "speed.pause") != null && HitOf(c, "expand") != null && HitOf(c, "collapse") == null,
                "collapsed rail keeps every page, the pause toggle and expansion, and no page body");
            Check(c.Texts.Any(t => t.Text == "1×"), "collapsed rail shows the current game speed");
        }
        model.Preferences.Collapsed = false;
        model.Page = TrainerView.PageSettings;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text.Contains("Dawn of Man 1.8.3")) && !c.Texts.Any(t => t.Text.Contains("1.8.4")) && c.Texts.Any(t => t.Text.Contains("v2.1.0")), "settings show the supplied game version and the trainer version");
        model.Page = TrainerView.PageHelp;
        using (CheckCanvas c = Render(model))
        {
            Check(c.Texts.Any(t => t.Text == "F11") && c.Texts.Any(t => t.Text == "展开或收起面板，隐藏时直接打开") && c.Texts.Any(t => t.Text == "Shift+F11") && c.Texts.Any(t => t.Text == "显示或隐藏修改器"), "help lists F11 for expanding and collapsing and Shift+F11 for showing and hiding");
            Check(!c.Texts.Any(t => t.Text == "Esc" || t.Text == "关闭面板"), "help no longer promises that Esc closes the panel");
        }
        model.Page = TrainerView.PageSettings;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "展开 / 收起面板") && c.Texts.Any(t => t.Text == "固定键位；Shift+F11 显示或隐藏"), "settings name F11 for expanding and the fixed Shift+F11 for showing and hiding");
        model.Page = TrainerView.PageHome; model.Speed = .25f;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "0.25×") && !c.Texts.Any(t => t.Text == "0.3×"), "active list preserves exact quarter-speed precision");
        model.AutoPauseActive = true;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "面板暂停"), "header identifies the automatic panel pause on every page");
        model.AutoPauseActive = false; model.Speed = 1;
        using (CheckCanvas c = Render(model)) { UiHit lockHit = HitOf(c, "lock.0"); Check(lockHit != null && Math.Abs(lockHit.Rect.W - 73.5f) < .01f && lockHit.Rect.H == 64, "attribute tile is a full-size click target"); }
        using (CheckCanvas c = Render(model)) Check(!c.Hits.Any(h => h.Id == "kill" || h.Id == "complete" || h.Id.StartsWith("rescue.", StringComparison.Ordinal)), "home page holds no destructive or selection actions");
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "2/7 已锁定") && HitOf(c, "stats.all").Rect.W == HitOf(c, "lock.0").Rect.W, "the eighth tile summarizes how many stats are locked");
        using (CheckCanvas c = Render(model, "stats.all")) Check(model.Locks.All(v => v), "the all tile locks every stat");
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "全部锁定"), "the all tile shows when every stat is locked");
        using (CheckCanvas c = Render(model, "stats.all")) Check(model.Locks.All(v => !v), "the all tile releases every stat once all are locked");
        model.Locks[0] = model.Locks[4] = true;
        using (CheckCanvas c = Render(model, "domestic")) { UiHit row = HitOf(c, "domestic"); Check(model.Domestic && row.Rect.W == 312 && row.Rect.H == 44 && !c.Texts.Any(t => t.Text == "含家畜"), "livestock protection is a full-width switch row, not a stat tile"); }
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "锁定也作用于玩家所属动物"), "the livestock row says what it widens");
        model.Domestic = false;
        model.Ready = false;
        bool noActions = true;
        for (int page = 0; page <= TrainerView.PageHelp; page++) for (int tab = 0; tab < 3; tab++)
        {
            model.Page = page; model.SpawnTab = tab; model.TargetTab = tab % 2;
            using (CheckCanvas c = Render(model)) noActions &= !c.Hits.Any(h => (h.Id == "kill" || h.Id == "complete" || h.Id == "spawn" || h.Id.StartsWith("rescue.", StringComparison.Ordinal) || h.Id.StartsWith("weather.", StringComparison.Ordinal)) && h.Enabled)
                && c.Hits.Where(h => h.Id.StartsWith("speed.", StringComparison.Ordinal)).All(h => !h.Enabled);
        }
        Check(noActions, "title or loading screen exposes no enabled game action on any page");
        model.Page = TrainerView.PageHome; model.SpawnTab = 0;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "进入地图后开始使用") && !c.Hits.Any(h => h.Id == "reset"), "without a map the home page explains how to start and offers no reset");
        model.Ready = true;
    }

    private static void NavigationChecks(UiModel model)
    {
        List<string> actions = new List<string>(); model.Action = (a, v) => actions.Add(a + ":" + v);
        model.Page = TrainerView.PageHome;
        using (CheckCanvas c = Render(model, "nav.3")) Check(model.Page == TrainerView.PageWorld && actions.Contains("page:3"), "rail navigation changes page and tells the runtime");
        actions.Clear();
        using (CheckCanvas c = Render(model, "nav.3")) Check(actions.Count == 0, "clicking the current page does not re-navigate");
        using (CheckCanvas c = Render(model, "nav.6")) Check(model.Page == TrainerView.PageHelp, "help button opens the usage guide");
        model.Page = TrainerView.PageSettings;
        using (CheckCanvas c = Render(model, "help.open")) Check(model.Page == TrainerView.PageHelp, "settings link opens the usage guide");
        model.Page = TrainerView.PageWorld; actions.Clear();
        using (CheckCanvas c = Render(model, "collapse")) Check(model.Preferences.Collapsed && actions.Contains("focus.clear:"), "collapse button folds the drawer and clears typing focus");
        actions.Clear();
        using (CheckCanvas c = Render(model, "nav.2")) Check(!model.Preferences.Collapsed && model.Page == TrainerView.PageSpawn && actions.Contains("focus.clear:"), "a collapsed rail icon expands straight to its page");
        model.Preferences.Collapsed = true;
        using (CheckCanvas c = Render(model, "expand")) Check(!model.Preferences.Collapsed && model.Page == TrainerView.PageSpawn, "expand button restores the last page");
        model.Preferences.Collapsed = true; actions.Clear();
        using (CheckCanvas c = Render(model, "speed.pause")) Check(actions.SequenceEqual(new[] { "pause:" }), "collapsed rail pause toggles game time");
        model.Preferences.Collapsed = false; model.Page = TrainerView.PageHome; actions.Clear();
        using (CheckCanvas c = Render(model, "close")) Check(actions.SequenceEqual(new[] { "close:" }), "close button hides the panel");

        // Footer count and active list describe exactly what reset turns off.
        model.StorageMultiplier = 5; model.BuildMultiplier = 10; model.Speed = 2; model.KillHotkey = true;
        List<string[]> items = TrainerView.ActiveItems(model);
        Check(items.Count == 6 && items.Select(i => i[0]).SequenceEqual(new[] { "lock.0", "lock.4", "speed", "multiplier.build", "multiplier.storage", "hotkey.kill" }), "active list holds locks, speed, multipliers and enabled hotkeys in a stable order");
        using (CheckCanvas c = Render(model))
        {
            Check(c.Texts.Any(t => t.Text == "6 项生效") && c.Texts.Any(t => t.Text == "正在生效 · 6 项"), "footer and home page agree on the active modification count");
            Check(c.Hits.Count(h => h.Id.StartsWith("active.off.", StringComparison.Ordinal) && h.Rect.H == 26) == 6, "six active rows fit the home page without scrolling");
            Check(c.Texts.Any(t => t.Text == "快捷键 Del") && c.Texts.Any(t => t.Text == "10×") && c.Texts.Any(t => t.Text == "2×"), "active rows name the key and value of each modification");
        }
        actions.Clear();
        using (CheckCanvas c = Render(model, "active.off.lock.0")) Check(!model.Locks[0] && actions.Count == 0, "an active lock row turns off only that lock");
        using (CheckCanvas c = Render(model, "active.off.multiplier.build")) Check(actions.SequenceEqual(new[] { "multiplier.build:1" }), "an active multiplier row restores that multiplier to 1x");
        actions.Clear();
        using (CheckCanvas c = Render(model, "active.off.speed")) Check(actions.SequenceEqual(new[] { "speed:1" }), "an active speed row restores 1x");
        using (CheckCanvas c = Render(model, "active.off.hotkey.kill")) Check(!model.KillHotkey, "an active hotkey row disables that hotkey");
        actions.Clear();
        using (CheckCanvas c = Render(model, "reset")) Check(actions.SequenceEqual(new[] { "confirm.arm:reset" }) && model.ArmedAction == "reset", "footer reset waits for a second click");
        model.ArmedSeconds = 4;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "确认全部关闭（4）") && HitOf(c, "active.open") == null && InWindow(c, model) && !Overlaps(c.Hits), "an armed footer reset names the action, counts down and replaces the count chip");
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "将关闭 " + TrainerView.ActiveItems(model).Count + " 项修改"), "while armed, the footer says how many modifications the second click turns off");
        actions.Clear();
        using (CheckCanvas c = Render(model, "reset")) Check(actions.SequenceEqual(new[] { "reset:" }) && model.ArmedAction == null, "the second click runs the global switch-off");
        model.ArmedSeconds = 0; model.ArmedAction = "reset"; model.Navigate(TrainerView.PageWorld); model.Navigate(TrainerView.PageHome);
        Check(model.ArmedAction == null, "changing page cancels an armed footer reset");
        model.Status = "这是一条需要两行才能完整显示的较长提示文字，末尾必须可见";
        using (CheckCanvas c = Render(model))
        {
            List<UiText> lines = c.Texts.Where(t => t.Rect.Y >= Height - 40 && Math.Abs(t.Rect.X - 86) < .1f && t.Text.Length > 0).ToList();
            Check(lines.Count == 2 && lines.All(l => l.Rect.Y + l.Rect.H <= Height + .1f && !c.Hits.Any(h => Intersects(l.Rect, h.Rect))), "a long notice wraps onto two footer lines clear of the footer controls");
            Check(String.Concat(lines.Select(t => t.Text).ToArray()).Contains("末尾必须可见"), "the end of a two-line notice stays visible");
        }
        model.Status = "准备就绪，修改仅作用于当前地图";
        using (CheckCanvas c = Render(model)) Check(c.Texts.Count(t => t.Rect.Y >= Height - 40 && Math.Abs(t.Rect.X - 86) < .1f) == 1, "a short notice stays on one footer line");
        actions.Clear();
        using (CheckCanvas c = Render(model, "active.reset")) Check(actions.SequenceEqual(new[] { "reset:" }), "home list offers the same global switch-off");
        model.Page = TrainerView.PageWorld;
        using (CheckCanvas c = Render(model, "active.open")) Check(model.Page == TrainerView.PageHome, "footer count opens the active list");
        model.StorageMultiplier = model.BuildMultiplier = 1; model.Speed = 1; model.Locks[0] = model.Locks[4] = false; model.Domestic = false;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "无修改") && HitOf(c, "reset") == null && c.Texts.Any(t => t.Text == "当前没有生效的修改"), "with nothing active the footer and list say so and offer no reset");
        model.Busy = true; model.Status = "正在添加 生肉 × 10";
        using (CheckCanvas c = Render(model, "spawn.cancel")) Check(actions.Contains("spawn.cancel:") && HitOf(c, "reset") == null, "active generation replaces the footer count with a stop button");
        model.Busy = false; model.Status = "准备就绪";

        // Settings: dock side, scale, hotkey switches and capture.
        model.Page = TrainerView.PageSettings; actions.Clear();
        using (CheckCanvas c = Render(model, "dock.1")) Check(model.Preferences.Dock == TrainerView.DockLeft, "settings dock the drawer to the left");
        using (CheckCanvas c = Render(model, "dock.2")) Check(model.Preferences.Dock == TrainerView.DockFloating, "settings let the drawer float");
        using (CheckCanvas c = Render(model, "dock.0")) Check(model.Preferences.Dock == TrainerView.DockRight, "settings dock the drawer back to the right");
        using (CheckCanvas c = Render(model, "ui.scale.125")) Check(model.Preferences.UiScale == 125, "interface size preference is directly editable");
        using (CheckCanvas c = Render(model, "key.capture.0")) Check(actions.Contains("key.capture:0"), "shortcut button starts capture for the intended action");
        model.CapturingKey = 1;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "按新键…"), "the capturing shortcut asks for a new key");
        model.CapturingKey = -1;
        bool[] before = { model.BuildHotkey, model.KillHotkey, model.GiveHotkey };
        using (CheckCanvas c = Render(model, "build.hotkey")) { }
        using (CheckCanvas c = Render(model, "kill.hotkey")) { }
        using (CheckCanvas c = Render(model, "give.hotkey")) { }
        Check(model.BuildHotkey != before[0] && model.KillHotkey != before[1] && model.GiveHotkey != before[2], "all three action hotkeys are enabled from settings");
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "Ins") && c.Texts.Any(t => t.Text == "Del") && c.Texts.Any(t => t.Text == "F10") && c.Texts.Any(t => t.Text == "F11"), "settings list every key with short names");
        actions.Clear();
        using (CheckCanvas c = Render(model, "prefs.default")) Check(actions.Contains("prefs.default:"), "settings restore defaults through the runtime");
        model.BuildHotkey = model.KillHotkey = model.GiveHotkey = false; model.Action = null;
    }

    private static void CatalogChecks(UiModel model)
    {
        List<string> actions = new List<string>(); model.Action = (a, v) => actions.Add(a + ":" + v);
        model.Page = TrainerView.PageSpawn; model.SpawnTab = 0; model.Quantity = "1001";
        using (CheckCanvas c = Render(model)) Check(c.Hits.Any(h => h.Id == "spawn" && !h.Enabled) && c.Texts.Any(t => t.Text == "请输入 1–1000 的整数"), "oversized resource count disables add and explains the range");
        model.Quantity = "abc";
        using (CheckCanvas c = Render(model)) Check(c.Hits.Any(h => h.Id == "spawn" && !h.Enabled) && c.Texts.Any(t => t.Text == "数量无效"), "nonnumeric resource count disables add without a placeholder count");
        model.Quantity = "100"; model.Busy = true;
        using (CheckCanvas c = Render(model)) Check(c.Hits.Any(h => h.Id == "spawn" && !h.Enabled), "busy generation disables duplicate add");
        model.Busy = false; model.Quantity = "10"; model.Search = "nothing matches";
        using (CheckCanvas c = Render(model)) Check(c.Hits.Any(h => h.Id == "filters.clear") && c.Texts.Any(t => t.Text == "没有找到匹配项"), "empty search offers clear filters");
        using (CheckCanvas c = Render(model, "filters.clear")) Check(model.Search == "" && model.Category == 0, "clear filters resets search and category");
        model.Search = "flint";
        using (CheckCanvas c = Render(model)) Check(c.Hits.Count(h => h.Id.StartsWith("item.", StringComparison.Ordinal)) == 6, "live ID filter finds the six flint resources");
        model.Search = ""; model.SpawnTab = 1; model.AnimalQuantity = "51";
        using (CheckCanvas c = Render(model)) Check(c.Hits.Any(h => h.Id == "spawn" && !h.Enabled), "oversized animal count disables generation");
        model.AnimalQuantity = "1";
        actions.Clear();
        using (CheckCanvas c = Render(model, "spawn")) Check(actions.SequenceEqual(new[] { "spawn.animal:" }), "animal primary button dispatches its intended action");
        using (CheckCanvas c = Render(model, "age.0")) Check(model.Age == 0, "animal age is selectable");
        using (CheckCanvas c = Render(model, "gender.1")) Check(model.Gender == 1, "animal gender is selectable");
        model.Age = 1; model.Gender = 2;
        model.SpawnTab = 0; actions.Clear();
        using (CheckCanvas c = Render(model, "spawn")) Check(actions.SequenceEqual(new[] { "spawn.resource:" }), "resource primary button dispatches its intended action");
        using (CheckCanvas c = Render(model, "quantity.100")) Check(model.Quantity == "100", "quantity preset changes add quantity");
        using (CheckCanvas c = Render(model, "quantity.plus")) Check(model.Quantity == "101", "plus adds exactly one");
        using (CheckCanvas c = Render(model, "quantity.minus")) Check(model.Quantity == "100", "minus removes exactly one");
        model.Quantity = "10";
        for (int tab = 0; tab <= 1; tab++)
        for (int state = 0; state < 2; state++)
        {
            model.SpawnTab = tab; model.Quantity = model.AnimalQuantity = state == 0 ? "0" : "10"; model.Busy = state == 1; model.Done = 4; model.Total = 10;
            using (CheckCanvas c = Render(model))
            {
                UiText feedback = c.Texts.Single(t => t.Text.StartsWith(state == 0 ? "请输入 1–" : "正在添加 " + model.TaskName, StringComparison.Ordinal));
                string label = (tab == 0 ? "resource" : "animal") + " " + (state == 0 ? "invalid count" : "generation progress");
                Check(!c.Hits.Any(h => Intersects(feedback.Rect, h.Rect)), label + " text stays clear of controls");
                Check(!c.Texts.Any(t => !Object.ReferenceEquals(t, feedback) && !String.IsNullOrEmpty(t.Text) && Intersects(feedback.Rect, t.Rect)), label + " text has a separate row area");
            }
        }
        model.Busy = false; model.SpawnTab = 0; model.Quantity = "10"; model.Search = "  flint  ";
        using (CheckCanvas c = Render(model))
        {
            Check(c.Hits.Count(h => h.Id.StartsWith("item.", StringComparison.Ordinal)) == 6, "search ignores leading and trailing spaces");
            Check(c.Texts.Any(t => t.Text == "已选项不在当前筛选中"), "filtered-out selection is explicitly identified");
            Check(c.Texts.Any(t => t.Text == "添加 10 件") && c.Texts.Any(t => t.Text == "肉"), "dock names the actual item and the primary button its count");
        }
        model.ResourceScroll = 0; model.Category = 2;
        using (CheckCanvas c = Render(model, "spawn.tab.0")) Check(model.Search == "  flint  " && model.Category == 2, "clicking the current tab preserves search and category");
        model.Search = ""; model.Category = 0; model.ResourceScroll = 300;
        using (CheckCanvas c = Render(model, "nav.2")) Check(model.ResourceScroll == 300, "clicking the current page preserves a valid scroll position");
        model.Search = "flint";
        using (CheckCanvas c = Render(model, "search.clear")) Check(model.Search == "" && model.ResourceScroll == 0, "clearing search resets its scroll position");
        model.Search = "flint"; model.SelectSpawnTab(1); model.Search = "deer"; model.SelectSpawnTab(0);
        Check(model.Search == "flint", "resources and animals keep their own search");
        model.SelectSpawnTab(1); Check(model.Search == "deer", "returning to animals restores its own search");
        model.SelectSpawnTab(0); model.Navigate(TrainerView.PageWorld); model.Navigate(TrainerView.PageSpawn);
        Check(model.Search == "flint" && model.SpawnTab == 0, "leaving the spawn page keeps its tab and search");
        model.Search = ""; model.ResourceScroll = 0;
        using (CheckCanvas c = Render(model))
        {
            List<UiHit> items = c.Hits.Where(h => h.Id.StartsWith("item.", StringComparison.Ordinal)).ToList();
            Check(items.Count(h => h.Rect.H == 76) >= 8 && items.All(h => h.Rect.Y + h.Rect.H <= 438 + .1f), "catalog shows two complete rows and clips the rest above the fixed add bar");
            UiHit add = HitOf(c, "pack.add"), spawn = HitOf(c, "spawn");
            Check(add != null && spawn != null && spawn.Rect.Y - (add.Rect.Y + add.Rect.H) >= 8, "pack and primary buttons are clearly separated");
        }
        model.SpawnTab = 1; model.SelectionCount = 0; model.Location = 0;
        using (CheckCanvas c = Render(model)) { Check(c.Hits.Any(h => h.Id == "location.target") && c.Hits.Any(h => h.Id == "location.center"), "animal generation exposes both location choices"); Check(c.Texts.Any(t => t.Text == "当前将使用画面中心"), "missing selection explains the actual spawn location"); }
        model.SelectionCount = 3; model.SpawnTab = 0;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "生成在 Vorghok 附近"), "dock names the selected unit used as the spawn location");
        model.Busy = true; model.TaskName = "原任务"; model.SelectedResource = "flint_fixture_0"; model.Done = 4; model.Total = 10;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "正在添加 原任务 · 4/10"), "progress remains attached to original job after selection changes");
        model.Busy = false; model.ShowSuccess = true; model.CompletedName = "生肉"; model.CompletedCount = 10;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "已添加 生肉 × 10"), "success feedback names the completed item and amount");
        model.SelectedResource = "raw_meat"; model.ShowSuccess = false;
        using (CheckCanvas c = Render(model, null, x => x.FocusId = "search")) Check(c.Focused("search") && !c.Focused("quantity"), "focus states distinguish search and quantity fields");
        List<string> notices = new List<string>(); model.Action = (a, v) => { if (a == "pack.notice") notices.Add(v); };
        using (CheckCanvas c = Render(model, "pack.add")) Check(notices.Count == 1 && notices[0].Contains("建村材料"), "adding to a pack names the pack that receives the item");
        model.Action = null;
    }

    private static void FeatureUiChecks(UiModel model)
    {
        List<string> actions = new List<string>(); model.Action = (a, v) => actions.Add(a + ":" + v);
        model.Page = TrainerView.PageTargets; model.TargetTab = 0;
        using (CheckCanvas c = Render(model, "rescue.health")) Check(actions.SequenceEqual(new[] { "rescue.health:" }), "rescue button dispatches a one-time selection action");
        actions.Clear();
        using (CheckCanvas c = Render(model, "kill")) Check(actions.SequenceEqual(new[] { "kill:" }), "danger button dispatches only kill action");
        actions.Clear(); model.KillVillagers = "Tarnak";
        using (CheckCanvas c = Render(model, "kill")) Check(actions.SequenceEqual(new[] { "confirm.arm:kill" }) && model.ArmedAction == "kill" && c.Texts.Any(t => t.Text.Contains("含村民 Tarnak")), "killing selected villagers names them and waits for a second click");
        actions.Clear();
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "确认杀死"), "an armed kill asks for confirmation on its button");
        model.ArmedSeconds = 3;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "确认杀死（3）") && c.Texts.Any(t => t.Text == "含村民 Tarnak") && !Overlaps(c.Hits), "an armed kill counts down and still names the villagers");
        model.ArmedSeconds = 0;
        using (CheckCanvas c = Render(model, "kill")) Check(actions.SequenceEqual(new[] { "kill:" }) && model.ArmedAction == null, "second click confirms the villager kill");
        model.ArmedAction = "kill"; model.Navigate(TrainerView.PageHome); model.Navigate(TrainerView.PageTargets); Check(model.ArmedAction == null, "changing page cancels a pending confirmation");
        model.ArmedAction = "kill";
        using (CheckCanvas c = Render(model, "target.tab.1")) Check(model.ArmedAction == null && model.TargetTab == 1, "switching target tabs cancels a pending confirmation");
        model.KillVillagers = null; model.TargetTab = 0;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "已选 Vorghok 等 3 个目标"), "target page names the selection");
        model.RescueTargets = 0; model.TameTargets = 0;
        using (CheckCanvas c = Render(model)) Check(c.Hits.Where(h => h.Id.StartsWith("rescue.", StringComparison.Ordinal) || h.Id == "tame").All(h => !h.Enabled) && c.Texts.Any(t => t.Text == "选中玩家人物或家畜后显示生存状态"), "empty rescue and capture selections disable action buttons");
        model.SelectionCount = model.KillTargets = model.BuildTargets = 0;
        bool disabled = true;
        for (int tab = 0; tab < 2; tab++) { model.TargetTab = tab; using (CheckCanvas c = Render(model)) disabled &= c.Hits.Any(h => h.Id == (tab == 0 ? "kill" : "complete") && !h.Enabled); }
        Check(disabled, "empty selection disables target action buttons");
        model.SelectionCount = 3; model.KillTargets = 3; model.BuildTargets = 2; model.RescueTargets = 2; model.TameTargets = 1;
        model.TargetTab = 1; actions.Clear();
        using (CheckCanvas c = Render(model, "supply.building")) { Check(actions.Contains("supply.building:") && c.Texts.Any(t => t.Text == "补给 14 件"), "maintenance view dispatches live building supply with its total"); Check(InWindow(c, model), "maintenance controls remain inside window"); }
        actions.Clear();
        using (CheckCanvas c = Render(model, "complete")) Check(actions.SequenceEqual(new[] { "complete:" }), "quick build sits with the other building actions");
        model.Materials.Clear();
        using (CheckCanvas c = Render(model)) Check(c.Hits.Any(h => h.Id == "supply.building" && !h.Enabled) && c.Texts.Any(t => t.Text == "没有需要补给的材料"), "no outstanding material disables supply");
        model.Materials.Add(new SupplyLine { Id = "log", Quantity = "8" });
        model.TargetTab = 0;

        model.Page = TrainerView.PageSpawn; model.SpawnTab = 0; model.Quantity = "10"; actions.Clear();
        using (CheckCanvas c = Render(model, "favorite")) Check(model.Preferences.Favorites.Contains("raw_meat"), "favorite button updates the actual saved preference model");
        model.Category = 5;
        using (CheckCanvas c = Render(model)) Check(c.Hits.Count(h => h.Id.StartsWith("item.", StringComparison.Ordinal)) == 1, "favorites filter displays only saved favorites");
        model.Category = 0;
        int before = model.Preferences.Packs[0].Lines.Count;
        using (CheckCanvas c = Render(model, "pack.add")) Check(model.Preferences.Packs[0].Lines.Count == before + 1, "selected resource and quantity can be added to a pack");
        model.SpawnTab = 2; actions.Clear();
        using (CheckCanvas c = Render(model, "pack.execute")) Check(actions.Contains("pack.execute:"), "valid pack primary button dispatches batch creation");
        model.Preferences.Packs[0].Lines[0].Quantity = "0";
        using (CheckCanvas c = Render(model)) Check(c.Hits.Any(h => h.Id == "pack.execute" && !h.Enabled), "one invalid item disables the entire pack execution");
        int packs = model.Preferences.Packs.Count; actions.Clear();
        using (CheckCanvas c = Render(model, "pack.delete")) Check(model.Preferences.Packs.Count == packs && actions.Contains("confirm.arm:pack.delete"), "deleting a pack waits for a second click");
        model.ArmedSeconds = 2;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "确认删除（2）"), "an armed pack delete counts down to its second click");
        model.ArmedSeconds = 0;
        using (CheckCanvas c = Render(model, "pack.delete")) Check(model.Preferences.Packs.Count == packs - 1, "second click deletes the pack");
        using (CheckCanvas c = Render(model, "pack.new")) Check(model.Preferences.Packs.Count == packs && model.ActivePack == packs - 1, "new pack is created and selected");
        using (CheckCanvas c = Render(model, "pack.select.0")) Check(model.ActivePack == 0, "pack list selects a pack");
        string savedName = model.Preferences.Packs[0].Name; model.Preferences.Packs[0].Name = "";
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "输入物资包名称"), "an empty pack name shows that it is editable");
        model.Preferences.Packs[0].Name = savedName;
        model.PackSearch = "flint"; int lines = model.Preferences.Packs[0].Lines.Count;
        using (CheckCanvas c = Render(model, "pack.pick.flint")) Check(c.Hits.Count(h => h.Id.StartsWith("pack.pick.", StringComparison.Ordinal)) == 3 && model.Preferences.Packs[0].Lines.Count == lines + 1 && model.Preferences.Packs[0].Lines.Any(l => l.Id == "flint" && l.Quantity == "10"), "pack editor searches the catalog and adds items in place");
        model.PackSearch = ""; model.Preferences.Packs[0].Lines[0].Quantity = "0";
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "每项数量需要为 1–1000") && c.Texts.Any(t => t.Text == "请先修正清单"), "invalid pack names the problem and the button asks for a fix");
        model.Preferences.Packs[0].Lines[0].Quantity = "30";
        using (CheckCanvas c = Render(model, "pack.remove.0")) Check(model.Preferences.Packs[0].Lines.All(l => l.Quantity != "30"), "a pack row can be removed");
        model.Preferences.Packs.Clear();
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "创建你的第一个物资包") && c.Hits.Any(h => h.Id == "pack.new" && h.Enabled) && HitOf(c, "pack.execute") == null, "with no packs the editor offers only creation");
        model.Ready = false; model.Preferences.Packs.Add(new SupplyPack { Name = "离线" });
        using (CheckCanvas c = Render(model)) Check(c.Hits.Any(h => h.Id == "input.pack.name") && c.Hits.Any(h => h.Id == "pack.execute" && !h.Enabled), "packs stay editable without a map but cannot be executed");
        model.Ready = true;

        model.Page = TrainerView.PageWorld; model.HousingFree = 0; model.TraderCount = 1;
        using (CheckCanvas c = Render(model)) Check(c.Hits.Any(h => h.Id == "invite.migrant" && !h.Enabled) && c.Hits.Any(h => h.Id == "invite.trader" && !h.Enabled), "housing and existing visitors disable duplicate invitations");
        model.HousingFree = 18; model.TraderCount = 0; actions.Clear();
        using (CheckCanvas c = Render(model, "weather.3")) Check(actions.SequenceEqual(new[] { "weather:3" }), "weather tile dispatches its weather");
        actions.Clear();
        using (CheckCanvas c = Render(model, "speed.0.25")) Check(actions.Contains("speed:0.25"), "quarter-speed preset emits exact fractional speed");
        bool autoPause = model.Preferences.AutoPause;
        using (CheckCanvas c = Render(model, "prefs.autopause")) Check(model.Preferences.AutoPause != autoPause, "automatic pause preference is directly editable on the world page");
        actions.Clear();
        using (CheckCanvas c = Render(model, null, x => x.NextSlider = TrainerView.SpeedTrack(8))) Check(actions.Contains("speed:8"), "speed slider dispatches selected speed");
        model.Speed = 1.5f; actions.Clear();
        using (CheckCanvas c = Render(model)) Check(!actions.Any(a => a.StartsWith("speed:", StringComparison.Ordinal)), "an untouched slider never rewrites a speed between its steps");
        model.Speed = 1;
        Check(Math.Abs(TrainerView.SpeedTrack(1) - (TrainerView.SpeedTrack(4) - TrainerView.SpeedTrack(1))) < .001f, "logarithmic slider gives 0.25×–1× and 1×–4× the same width");
        model.AutoPauseActive = true; model.Speed = .5f;
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "面板暂停 · 关闭后恢复 0.5×"), "world page says which speed returns when the panel closes");
        model.AutoPauseActive = false; model.Speed = 1;
        model.Action = null;
    }

    private static void PackQuantityChecks(UiModel model)
    {
        model.Preferences.Packs.Clear(); SupplyPack pack = new SupplyPack(); model.Preferences.Packs.Add(pack);
        SupplyLine line = new SupplyLine { Id = "raw_meat" }; pack.Lines.Add(line);
        model.Page = TrainerView.PageSpawn; model.SpawnTab = 2; model.PackSearch = "raw_meat";
        string[] invalidExisting = { "-10", "0", "abc", "", Int32.MinValue.ToString() };
        foreach (string quantity in invalidExisting)
        {
            line.Quantity = quantity;
            using (CheckCanvas c = Render(model, "pack.pick.raw_meat"))
            {
                Check(line.Quantity == "10" && pack.Lines.Count == 1, "adding an existing pack item repairs quantity '" + quantity + "' without a duplicate row");
                Check(c.Hits.Any(h => h.Id == "pack.execute" && h.Enabled), "repaired pack quantity '" + quantity + "' allows execution");
            }
        }
        using (CheckCanvas c = Render(model, "pack.pick.raw_meat")) Check(line.Quantity == "20", "adding a repaired pack item continues from its repaired amount");

        // The catalog also adds to existing rows; huge saved values must be
        // rejected before integer addition, without the pack input truncating them.
        model.SpawnTab = 0; model.SelectedResource = "raw_meat"; model.Quantity = "10";
        List<string> notices = new List<string>(); model.Action = (a, v) => { if (a == "pack.notice") notices.Add(v); };
        string[] overLimit = { "1000", "1001", Int32.MaxValue.ToString() };
        foreach (string quantity in overLimit)
        {
            line.Quantity = quantity; notices.Clear();
            using (CheckCanvas c = Render(model, "pack.add"))
                Check(line.Quantity == quantity && pack.Lines.Count == 1 && notices.SequenceEqual(new[] { "该物资包中的单项数量最多 1000" }), "existing quantity '" + quantity + "' rejects addition without overflow or losing its value");
        }
        line.Quantity = "990"; notices.Clear();
        using (CheckCanvas c = Render(model, "pack.add")) Check(line.Quantity == "1000" && notices.Count == 1 && !notices[0].Contains("最多"), "adding exactly to the 1000 item boundary succeeds");

        string[] formatted = { " 10 ", "0010", "+10" };
        foreach (string quantity in formatted)
        {
            pack.Lines.Clear(); model.Quantity = quantity;
            using (CheckCanvas c = Render(model, "pack.add")) Check(pack.Lines.Count == 1 && pack.Lines[0].Quantity == "10", "new catalog item normalizes quantity '" + quantity + "'");
        }
        string[] invalidNew = { "-1", "0", "abc", "", "1001" };
        foreach (string quantity in invalidNew)
        {
            pack.Lines.Clear(); model.Quantity = quantity; notices.Clear();
            using (CheckCanvas c = Render(model, "pack.add"))
                Check(pack.Lines.Count == 0 && notices.Count == 0 && c.Hits.Any(h => h.Id == "pack.add" && !h.Enabled), "new catalog quantity '" + quantity + "' cannot insert an invalid pack row");
        }
        model.Action = null;
    }

    private static IEnumerable<UiText> ValueTexts(CheckCanvas c) { return c.Texts.Where(t => Math.Abs(t.Rect.X - (Right - 56)) < .1f && t.Rect.W == 56); }

    private static void MultiplierUiChecks(UiModel model)
    {
        Check(model.Version == "2.1.0" && model.StorageMultiplier == 1 && model.BuildMultiplier == 1 && model.CarryMultiplier == 1 && model.TransportMultiplier == 1 &&
            model.CraftMultiplier == 1 && model.DryMultiplier == 1 && model.GatherMultiplier == 1 && model.FarmMultiplier == 1 && model.EfficiencyTab == 0, "eight integer multipliers start at 1x on the speed tab in version 2.1.0");
        model.Page = TrainerView.PageEfficiency;
        // Speed tab first (index 0-4), then capacity tab (5-7).
        string[] keys = { "build", "craft", "dry", "gather", "farm", "storage", "carry", "transport" }; int[] maximums = { 20, 20, 20, 20, 20, 10, 10, 10 };
        Action<int>[] set = { n => model.BuildMultiplier = n, n => model.CraftMultiplier = n, n => model.DryMultiplier = n, n => model.GatherMultiplier = n, n => model.FarmMultiplier = n,
            n => model.StorageMultiplier = n, n => model.CarryMultiplier = n, n => model.TransportMultiplier = n };
        Func<int>[] get = { () => model.BuildMultiplier, () => model.CraftMultiplier, () => model.DryMultiplier, () => model.GatherMultiplier, () => model.FarmMultiplier,
            () => model.StorageMultiplier, () => model.CarryMultiplier, () => model.TransportMultiplier };
        Func<int, int> tabOf = i => i < 5 ? 0 : 1;
        List<string> actions = new List<string>(); model.Action = (a, v) => actions.Add(a + ":" + v);
        using (CheckCanvas c = Render(model))
        {
            Check(c.Hits.Where(h => h.Id.StartsWith("nav.", StringComparison.Ordinal)).Select(h => h.Id).SequenceEqual(new[] { "nav.0", "nav.1", "nav.2", "nav.3", "nav.4", "nav.5", "nav.6" }), "rail lists the six pages in order, then help");
            Check(c.Hits.Any(h => h.Id == "efficiency.tab.0") && c.Hits.Any(h => h.Id == "efficiency.tab.1") && c.Texts.Any(t => t.Text == "速度") && c.Texts.Any(t => t.Text == "容量"), "efficiency page splits into speed and capacity tabs");
            Check(ValueTexts(c).Count(t => t.Text == "1×") == 5, "each speed multiplier displays its current integer value");
            Check(c.Texts.Any(t => t.Text.Contains("建造、修理与拆解")) && c.Texts.Any(t => t.Text.Contains("冶炼")) && c.Texts.Any(t => t.Text.Contains("腌肉、干鱼与干皮")) &&
                c.Texts.Any(t => t.Text.Contains("砍伐、收获、采掘、捕鱼")) && c.Texts.Any(t => t.Text.Contains("挤奶、剪毛、驯服与屠宰")) && c.Texts.Any(t => t.Text.Contains("原料消耗、产出与奖励不变")),
                "speed tab names every hooked kind of work and says only progress speeds up");
            Check(c.Hits.Count(h => h.Id.Contains("multiplier.")) == 15 && c.Hits.Where(h => h.Id.Contains("multiplier.")).All(h => h.Rect.H >= 28), "each speed multiplier has exactly a slider and two steppers, all at least 28 units tall");
            Check(c.Texts.Count(t => t.Text == "最高 20×") == 5, "every speed multiplier states its ceiling");
            Check(c.Hits.Where(h => h.Id.StartsWith("slider.multiplier.", StringComparison.Ordinal)).All(h => h.Rect.W == 236) && !Overlaps(c.Hits), "integer sliders share their visible track width and do not overlap neighboring controls");
            Check(c.Texts.Where(t => t.Rect.Y >= 90 && t.Rect.Y < Height - 40).All(t => t.Rect.Y + t.Rect.H <= Height - 40 + .1f), "five speed blocks and the footnote fit above the footer");
            Check(c.Hits.Any(h => h.Id == "efficiency.reset" && !h.Enabled), "reset-all link is disabled while every multiplier is 1x");
        }
        using (CheckCanvas c = Render(model, "efficiency.tab.1"))
            Check(model.EfficiencyTab == 1 && actions.Count == 0, "the capacity tab is a view switch, not a game action");
        using (CheckCanvas c = Render(model))
        {
            Check(ValueTexts(c).Count(t => t.Text == "1×") == 3 && c.Hits.Count(h => h.Id.Contains("multiplier.")) == 9 && !Overlaps(c.Hits), "capacity tab shows its three multipliers");
            Check(c.Texts.Any(t => t.Text.Contains("不含露天堆场")) && c.Texts.Any(t => t.Text.Contains("儿童与重物不变")) && c.Texts.Any(t => t.Text.Contains("仅拖板与拖车")) && c.Texts.Any(t => t.Text.Contains("保留现货")), "capacity tab explains each scope and retains stock when capacity decreases");
            Check(c.Texts.Count(t => t.Text == "最高 10×") == 3, "every capacity multiplier states its ceiling");
            Check(TrainerView.PanelHeight(false) == 620 && TrainerView.PanelWidth(false) == 400, "every page shares the same 400x620 drawer");
        }
        for (int i = 0; i < keys.Length; i++)
        {
            string id = "multiplier." + keys[i]; int index = i; model.EfficiencyTab = tabOf(i);
            set[index](1); actions.Clear();
            using (CheckCanvas c = Render(model, id + ".minus")) Check(get[index]() == 1 && actions.Count == 0 && c.Hits.Any(h => h.Id == id + ".minus" && !h.Enabled), keys[i] + " multiplier cannot go below one");
            set[index](maximums[index]); actions.Clear();
            using (CheckCanvas c = Render(model, id + ".plus")) Check(get[index]() == maximums[index] && actions.Count == 0 && c.Hits.Any(h => h.Id == id + ".plus" && !h.Enabled), keys[i] + " multiplier cannot exceed its integer maximum");
            set[index](7); actions.Clear();
            using (CheckCanvas c = Render(model, id + ".plus")) Check(get[index]() == 8 && actions.SequenceEqual(new[] { id + ":8" }), keys[i] + " plus increments by exactly one and emits an integer");
            actions.Clear();
            using (CheckCanvas c = Render(model, id + ".minus")) Check(get[index]() == 7 && actions.SequenceEqual(new[] { id + ":7" }), keys[i] + " minus decrements by exactly one and emits an integer");
            using (CheckCanvas c = Render(model)) Check(!c.Hits.Any(h => h.Id == id + ".1" || h.Id == id + ".10"), keys[i] + " has no preset buttons beside its slider");
            model.EfficiencyTab = 1 - tabOf(i);
            using (CheckCanvas c = Render(model)) Check(!c.Hits.Any(h => h.Id.StartsWith(id + ".", StringComparison.Ordinal) || h.Id == "slider." + id + ".slider"), keys[i] + " is drawn only on its own tab");
            using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "速度" || t.Text == "容量") && c.Texts.Any(t => t.Text == "1"), keys[i] + " counts toward its tab badge while viewing the other tab");
            model.EfficiencyTab = tabOf(i);
            float[] rawValues = { 4.49f, 4.5f, -10, maximums[index] + 10, Single.MaxValue }; int[] expected = { 4, 5, 1, maximums[index], maximums[index] };
            for (int sample = 0; sample < rawValues.Length; sample++)
            {
                set[index](7); actions.Clear(); float raw = rawValues[sample];
                using (CheckCanvas c = Render(model, null, x => x.NextSliders[id + ".slider"] = raw))
                    Check(get[index]() == expected[sample] && actions.SequenceEqual(new[] { id + ":" + expected[sample] }) && ValueTexts(c).Any(t => t.Text == expected[sample] + "×"), keys[i] + " slider rounds or clamps input " + raw.ToString(CultureInfo.InvariantCulture) + " to one integer setting");
            }
            float[] unchangedValues = { 7, 7.49f, Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity };
            foreach (float raw in unchangedValues)
            {
                set[index](7); actions.Clear(); float value = raw;
                using (CheckCanvas c = Render(model, null, x => x.NextSliders[id + ".slider"] = value))
                    Check(get[index]() == 7 && actions.Count == 0, keys[i] + " slider ignores unchanged or nonfinite input " + raw.ToString(CultureInfo.InvariantCulture));
            }
            set[index](1);
        }
        model.EfficiencyTab = 0; model.BuildMultiplier = 1; model.GatherMultiplier = 3; model.StorageMultiplier = 5; model.CarryMultiplier = 2; actions.Clear();
        using (CheckCanvas c = Render(model)) Check(c.Texts.Any(t => t.Text == "1") && c.Texts.Any(t => t.Text == "2"), "tab badges count the changed multipliers on each tab");
        using (CheckCanvas c = Render(model, "efficiency.reset")) Check(actions.SequenceEqual(new[] { "multiplier.gather:1", "multiplier.storage:1", "multiplier.carry:1" }) && model.GatherMultiplier == 1 && model.StorageMultiplier == 1 && model.CarryMultiplier == 1, "reset-all restores only the multipliers that changed, on both tabs");
        model.Ready = false;
        for (int i = 0; i < keys.Length; i++)
        {
            set[i](5); actions.Clear(); string key = keys[i]; model.EfficiencyTab = tabOf(i);
            using (CheckCanvas c = Render(model, "multiplier." + key + ".plus", x => x.NextSliders["multiplier." + key + ".slider"] = 9.5f))
                Check(get[i]() == 5 && actions.Count == 0 && c.Hits.Count(h => h.Id.Contains("multiplier.")) == (i < 5 ? 15 : 9) && c.Hits.Where(h => h.Id.Contains("multiplier.")).All(h => !h.Enabled) && c.SliderCalls.Count == 0, keys[i] + " and every multiplier control are disabled without a map");
            set[i](1);
        }
        model.Ready = true; model.Action = null; model.EfficiencyTab = 0;
    }

    // Named states rendered by every exporter, so Windows PNGs and SVG previews show the same screens.
    internal static List<KeyValuePair<string, UiModel>> PreviewStates(string catalog)
    {
        List<KeyValuePair<string, UiModel>> states = new List<KeyValuePair<string, UiModel>>();
        Func<string, Action<UiModel>, UiModel> add = (name, setup) =>
        {
            UiModel m = Model(catalog); m.StorageMultiplier = 5; m.BuildMultiplier = 10; m.CarryMultiplier = 2; m.TransportMultiplier = 5;
            if (m.Resources.Exists(r => r.Id == "berries")) m.SelectedResource = "berries";
            setup(m); states.Add(new KeyValuePair<string, UiModel>(name, m)); return m;
        };
        add("01-常用功能", m => m.Page = TrainerView.PageHome);
        add("02-选中目标-单位", m => m.Page = TrainerView.PageTargets);
        add("03-选中目标-建筑", m => { m.Page = TrainerView.PageTargets; m.TargetTab = 1; });
        add("04-生成-资源", m => { m.Page = TrainerView.PageSpawn; m.Quantity = "50"; m.ShowSuccess = true; m.CompletedName = "浆果"; m.CompletedCount = 50; m.Status = "已添加 浆果 × 50"; m.Preferences.Favorites.Add("raw_meat"); m.Preferences.Favorites.Add("grain"); });
        add("05-生成-动物", m => { m.Page = TrainerView.PageSpawn; m.SpawnTab = 1; });
        add("06-生成-物资包", m => { m.Page = TrainerView.PageSpawn; m.SpawnTab = 2; });
        add("07-世界控制", m => m.Page = TrainerView.PageWorld);
        add("08-效率倍率", m => { m.Page = TrainerView.PageEfficiency; m.CraftMultiplier = 4; m.GatherMultiplier = 3; m.Status = "采集速度已设为 3×"; });
        add("08b-效率倍率-容量", m => { m.Page = TrainerView.PageEfficiency; m.EfficiencyTab = 1; m.Status = "仓储容量已设为 5×"; });
        add("09-设置", m => m.Page = TrainerView.PageSettings);
        add("10-使用说明", m => m.Page = TrainerView.PageHelp);
        add("11-收起侧栏", m => m.Preferences.Collapsed = true);
        add("12-搜索空状态", m => { m.Page = TrainerView.PageSpawn; m.Search = "找不到的物品"; });
        add("13-资源数量错误", m => { m.Page = TrainerView.PageSpawn; m.Quantity = "0"; });
        add("14-资源生成进度", m => { m.Page = TrainerView.PageSpawn; m.Busy = true; m.Done = 40; m.Total = 100; m.TaskName = "浆果"; m.Status = "正在添加 浆果 × 100"; });
        add("15-确认杀死村民", m => { m.Page = TrainerView.PageTargets; m.KillVillagers = "Vorghok 等 2 人"; m.ArmedAction = "kill"; m.ArmedSeconds = 3; });
        add("16-面板暂停", m => { m.Page = TrainerView.PageWorld; m.AutoPauseActive = true; m.Speed = .5f; m.Preferences.AutoPause = true; });
        add("17-未进入地图", m => { m.Page = TrainerView.PageHome; m.Ready = false; m.Status = "修改器已加载，等待进入地图"; });
        add("18-确认全部关闭", m => { m.Page = TrainerView.PageHome; m.ArmedAction = "reset"; m.ArmedSeconds = 3; });
        add("19-两行报错", m => { m.Page = TrainerView.PageTargets; m.StatusError = true; m.Status = "无法驯化：需要先解锁驯化科技，且动物不能正在执行任务"; });
        return states;
    }
}
