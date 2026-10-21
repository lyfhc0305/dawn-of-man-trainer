using System;
using System.Collections.Generic;

namespace DawnTrainer
{
    internal sealed partial class UiModel
    {
        internal TrainerPreferences Preferences = TrainerPreferences.Defaults();
        internal int TargetTab, ActivePack, CapturingKey = -1;
        internal int RescueTargets, RepairTargets, ResourceRepairTargets, TameTargets, HousingFree, MigrantCount, TraderCount;
        internal bool AutoPauseActive;
        internal float PackScroll, PackListScroll, MaterialScroll;
        internal readonly float[] TargetStats = new float[7];
        internal string TameReason = "仅幼年、科技已解锁且没有任务的可驯化动物";
        internal readonly List<SupplyLine> Materials = new List<SupplyLine>();
        // ArmedAction: destructive button waiting for its second click. KillVillagers: player villagers in the kill selection.
        internal string ArmedAction, KillVillagers;
        // Whole seconds left before the runtime expires ArmedAction; 0 when unknown.
        internal int ArmedSeconds;
        internal string PackSearch = "";
    }

    internal static partial class TrainerView
    {
        private static bool StepButton(UiCanvas c, UiRect r, string id, string glyph, bool enabled)
        {
            bool hover = enabled && c.Hover(r);
            c.Fill(r, hover ? Raise : Surface, 7);
            Glyph(c, glyph, r.X + (r.W - 14) / 2, r.Y + (r.H - 14) / 2, 14, enabled ? Muted : "#5B554C", hover ? Raise : Surface);
            return c.Hit(r, id, enabled);
        }

        // ---------- 选中目标 ----------
        private static void DrawTargets(UiCanvas c, UiModel m)
        {
            int buildings = m.BuildTargets + m.RepairTargets + m.ResourceRepairTargets;
            int tab = Segmented(c, R(X0, BodyY, BodyW, 32), new string[] { "target.tab.0", "target.tab.1" }, new string[] { "单位", "建筑与物品" },
                new string[] { m.KillTargets.ToString(), buildings.ToString() }, m.TargetTab, true);
            if (tab >= 0) { m.TargetTab = tab; m.ArmedAction = null; }
            string who = m.SelectionCount == 0 ? "在面板外点选地图上的人物、动物或建筑"
                : String.IsNullOrEmpty(m.SelectionName) ? "已选 " + m.SelectionCount + " 个目标"
                : m.SelectionCount == 1 ? "已选 " + Short(c, m.SelectionName, 220, 12) : "已选 " + Short(c, m.SelectionName, 170, 12) + " 等 " + m.SelectionCount + " 个目标";
            c.Text(R(X0, 138, BodyW, 18), who, 12, m.SelectionCount == 0 ? Quiet : Muted, false, 0);
            if (m.TargetTab == 1) { DrawMaintenance(c, m); return; }

            c.Fill(R(X0, 162, BodyW, 140), Surface, 11);
            if (m.RescueTargets == 0)
            {
                c.Text(R(X0, 206, BodyW, 20), "选中玩家人物或家畜后显示生存状态", 13, Muted, false, 1);
                c.Text(R(X0, 230, BodyW, 18), "多选时显示平均值", 12, Quiet, false, 1);
            }
            else
            {
                c.Text(R(X0 + 12, 168, BodyW - 24, 16), "平均状态 · " + m.RescueTargets + " 个存活玩家单位", 12, Quiet, false, 0);
                float colW = (BodyW - 24 - 16) / 2f;
                for (int i = 0; i < 7; i++)
                {
                    float x = X0 + 12 + (i % 2) * (colW + 16), y = 190 + (i / 2) * 27, value = m.TargetStats[i];
                    bool low = value >= 0 && value < .25f;
                    Glyph(c, StatIcons[i], x, y + 2, 14, low ? Danger : Quiet, Surface);
                    c.Text(R(x + 20, y, 60, 18), StatNames[i], 12, Muted, false, 0);
                    c.Text(R(x + colW - 60, y, 60, 18), value < 0 ? "—" : Math.Round(value * 100) + "%", 12, low ? Danger : TextMain, false, 2);
                    c.Fill(R(x + 20, y + 20, colW - 20, 3), Raise, 1.5f);
                    if (value >= 0) c.Fill(R(x + 20, y + 20, (colW - 20) * Math.Max(0, Math.Min(1, value)), 3), low ? Danger : Accent, 1.5f);
                }
            }
            if (ActionRow(c, 306, "heart", "恢复健康", "只恢复选中玩家单位的健康值", null, "rescue.health", "恢复 " + m.RescueTargets, 1, m.RescueTargets > 0, false)) m.Emit("rescue.health", "");
            if (ActionRow(c, 358, "refill", "补满生存状态", "恢复现有属性，疾病与年龄不变", null, "rescue.all", "补满 " + m.RescueTargets, 0, m.RescueTargets > 0, false)) m.Emit("rescue.all", "");
            if (ActionRow(c, 410, "paw", "驯化", m.TameTargets > 0 ? m.TameTargets + " 只符合原生捕捉条件" : m.TameReason, null, "tame", "驯化 " + m.TameTargets, 0, m.TameTargets > 0, false)) m.Emit("tame", "");
            bool villagers = m.KillTargets > 0 && !String.IsNullOrEmpty(m.KillVillagers);
            // Naming the villagers matters more than the confirmation hint, which the button itself shows.
            string note = m.KillTargets == 0 ? "选择人物或动物后可用" : villagers ? "含村民 " + m.KillVillagers : "立即死亡，结果随存档保存";
            bool armed = m.ArmedAction == "kill";
            bool clicked = ActionRow(c, 462, "skull", "杀死选中单位", note, villagers ? Danger : null, "kill", armed ? ConfirmLabel(m, "确认杀死") : "杀死 " + m.KillTargets, armed ? 3 : 2, m.KillTargets > 0, true);
            if (Confirmed(m, "kill", villagers, clicked)) m.Emit("kill", "");
        }

        private static void DrawMaintenance(UiCanvas c, UiModel m)
        {
            long total = 0; foreach (SupplyLine line in m.Materials) total += line.Count;
            c.Fill(R(X0, 162, BodyW, 140), Surface, 11);
            c.Text(R(X0 + 12, 168, BodyW - 24, 16), m.Materials.Count == 0 ? "尚未交付的材料" : "尚未交付的材料 · 共 " + total + " 件", 12, Quiet, false, 0);
            if (m.Materials.Count == 0)
            {
                c.Text(R(X0, 212, BodyW, 20), "没有需要补给的材料", 13, Muted, false, 1);
                c.Text(R(X0, 236, BodyW, 18), "在面板外点选未完工的玩家建筑后显示", 12, Quiet, false, 1);
            }
            else
            {
                UiRect view = R(X0 + 12, 188, BodyW - 24, 108); bool scrolls = m.Materials.Count * 28 > view.H; float rowW = view.W - (scrolls ? 14 : 0);
                m.MaterialScroll = c.BeginScroll(view, m.MaterialScroll, m.Materials.Count * 28);
                for (int i = 0; i < m.Materials.Count; i++)
                {
                    SupplyLine line = m.Materials[i]; UiItem item = m.Resources.Find(r => r.Id == line.Id); float y = i * 28;
                    if (item != null && item.Image != null) c.Image(R(0, y + 3, 22, 22), item.Image);
                    c.Text(R(30, y, rowW - 110, 28), item == null ? line.Id : item.Name, 13, TextMain, false, 0);
                    c.Text(R(rowW - 80, y, 80, 28), "× " + line.Count, 13, AccentInk, false, 2);
                    if (i < m.Materials.Count - 1) c.Line(0, y + 28, rowW, y + 28, Raise, 1);
                }
                c.EndScroll();
            }
            bool tooMany = total > FeaturePolicy.MaximumTotal;
            if (ActionRow(c, 306, "spawn", "补齐缺料", tooMany ? "超过一次 5000 件的上限" : "建筑旁分批生成，含运输中材料", tooMany ? Danger : null,
                "supply.building", m.Busy ? "生成中…" : "补给 " + total + " 件", 1, m.Materials.Count > 0 && !m.Busy && !tooMany, false)) m.Emit("supply.building", "");
            if (ActionRow(c, 358, "hammer", "快速完成建造", "无需材料立即完工，计入原生奖励", null, "complete", "完成 " + m.BuildTargets, 0, m.BuildTargets > 0, false)) m.Emit("complete", "");
            if (ActionRow(c, 410, "house", "修缮建筑", "修复选中且已建成的玩家建筑", null, "repair.building", "修复 " + m.RepairTargets, 0, m.RepairTargets > 0, false)) m.Emit("repair.building", "");
            if (ActionRow(c, 462, "reset", "恢复耐久", "只处理直接选中的工具或衣物", null, "repair.resource", "恢复 " + m.ResourceRepairTargets, 0, m.ResourceRepairTargets > 0, true)) m.Emit("repair.resource", "");
        }

        private static void DrawVisitors(UiCanvas c, UiModel m, float y)
        {
            Section(c, y, "访客", null, null, false);
            if (ActionRow(c, y + 18, "people", "邀请移民", m.MigrantCount > 0 ? "已有 " + m.MigrantCount + " 名移民在途中" : "住房余量 " + m.HousingFree + " 人", null,
                "invite.migrant", "邀请", 0, m.HousingFree > 0 && m.MigrantCount == 0, false)) m.Emit("invite.migrant", "");
            if (ActionRow(c, y + 70, "bag", "邀请商人", m.TraderCount > 0 ? "地图上已有商队" : "从地图边缘前来，走原生来访流程", null,
                "invite.trader", "邀请", 0, m.TraderCount == 0, true)) m.Emit("invite.trader", "");
        }

        // ---------- 效率倍率 ----------
        private static void DrawMultipliers(UiCanvas c, UiModel m)
        {
            int speeds = Changed(SpeedMultipliers(m)), capacities = Changed(CapacityMultipliers(m));
            string[] counts = { m.Ready && speeds > 0 ? speeds.ToString() : "", m.Ready && capacities > 0 ? capacities.ToString() : "" };
            int tab = Segmented(c, R(X0, BodyY, BodyW, 32), new string[] { "efficiency.tab.0", "efficiency.tab.1" }, new string[] { "速度", "容量" }, counts, m.EfficiencyTab, true);
            if (tab >= 0) m.EfficiencyTab = tab;
            c.Text(R(X0, 136, BodyW - 70, 20), m.Ready ? "仅作用于玩家单位，切换地图后恢复 1×" : "进入地图后可调整，默认均为 1×", 12, Quiet, false, 0);
            bool any = AnyMultiplier(m); float lw = c.Measure("全部 1×", 12, false) + 8; UiRect all = R(Right - lw, 134, lw, 24);
            c.Text(all, "全部 1×", 12, m.Ready && any ? Accent : Quiet, false, 2);
            if (c.Hit(all, "efficiency.reset", m.Ready && any))
            {
                // Resets both tabs, in tab order, emitting only the values that changed.
                if (m.BuildMultiplier != 1) { m.BuildMultiplier = 1; m.Emit("multiplier.build", "1"); }
                if (m.CraftMultiplier != 1) { m.CraftMultiplier = 1; m.Emit("multiplier.craft", "1"); }
                if (m.DryMultiplier != 1) { m.DryMultiplier = 1; m.Emit("multiplier.dry", "1"); }
                if (m.GatherMultiplier != 1) { m.GatherMultiplier = 1; m.Emit("multiplier.gather", "1"); }
                if (m.FarmMultiplier != 1) { m.FarmMultiplier = 1; m.Emit("multiplier.farm", "1"); }
                if (m.StorageMultiplier != 1) { m.StorageMultiplier = 1; m.Emit("multiplier.storage", "1"); }
                if (m.CarryMultiplier != 1) { m.CarryMultiplier = 1; m.Emit("multiplier.carry", "1"); }
                if (m.TransportMultiplier != 1) { m.TransportMultiplier = 1; m.Emit("multiplier.transport", "1"); }
            }
            if (m.EfficiencyTab == 0)
            {
                m.BuildMultiplier = MultiplierBlock(c, m, 162, "build", "建造速度", m.BuildMultiplier, 20, "建造、修理与拆解；材料交付照常", false);
                m.CraftMultiplier = MultiplierBlock(c, m, 242, "craft", "制作速度", m.CraftMultiplier, 20, "工具、衣物、冶炼、面包、啤酒等配方", false);
                m.DryMultiplier = MultiplierBlock(c, m, 322, "dry", "晾晒速度", m.DryMultiplier, 20, "腌肉、干鱼与干皮，无需村民值守", false);
                m.GatherMultiplier = MultiplierBlock(c, m, 402, "gather", "采集速度", m.GatherMultiplier, 20, "砍伐、收获、采掘、捕鱼、挖土与取水", false);
                m.FarmMultiplier = MultiplierBlock(c, m, 482, "farm", "农牧速度", m.FarmMultiplier, 20, "种植、耕种、收割、挤奶、剪毛、驯服与屠宰", true);
                c.Text(R(X0, 558, BodyW, 16), "只加快进度；原料消耗、产出与奖励不变", 12, Quiet, false, 0);
            }
            else
            {
                m.StorageMultiplier = MultiplierBlock(c, m, 162, "storage", "仓储容量", m.StorageMultiplier, 10, "已建成的封闭仓储，不含露天堆场", false);
                m.CarryMultiplier = MultiplierBlock(c, m, 242, "carry", "人力搬运", m.CarryMultiplier, 10, "成年村民的普通货物；儿童与重物不变", false);
                m.TransportMultiplier = MultiplierBlock(c, m, 322, "transport", "运输工具容量", m.TransportMultiplier, 10, "仅拖板与拖车；犁和巨岩运输不变", true);
                c.Text(R(X0, 398, BodyW, 16), "降低容量保留现货，超额只出不进；装备槽不变", 12, Quiet, false, 0);
            }
        }

        private static int MultiplierBlock(UiCanvas c, UiModel m, float y, string key, string title, int value, int maximum, string note, bool last)
        {
            int current = Math.Max(1, Math.Min(maximum, value)), next = current;
            string id = "multiplier." + key;
            c.Text(R(X0, y, BodyW - 64, 22), title, 14, TextMain, true, 0);
            c.Text(R(X0, y + 23, BodyW - 68, 16), Short(c, note, BodyW - 68, 12), 12, Quiet, false, 0);
            UiRect minus = R(X0, y + 44, 28, 28), plus = R(Right - 28, y + 44, 28, 28), slider = R(X0 + 38, y + 44, BodyW - 76, 28);
            if (StepButton(c, minus, id + ".minus", "minus", m.Ready && current > 1)) next = current - 1;
            if (StepButton(c, plus, id + ".plus", "plus", m.Ready && current < maximum)) next = current + 1;
            float raw = current;
            if (m.Ready) raw = c.Slider(slider, id + ".slider", current, 1, maximum);
            else c.Hit(slider, "slider." + id + ".slider", false);
            // The canvas returns a continuous value. Snap every input to a finite integer;
            // an untouched or invalid slider must not overwrite the other controls.
            if (!Single.IsNaN(raw) && !Single.IsInfinity(raw))
            {
                int snapped = (int)Math.Round(Math.Max(1, Math.Min(maximum, raw)), MidpointRounding.AwayFromZero);
                if (snapped != current) next = snapped;
            }
            float ratio = (float)(next - 1) / (maximum - 1);
            c.Fill(R(slider.X, y + 56, slider.W, 4), m.Ready ? Raise : Surface, 2);
            if (ratio > 0) c.Fill(R(slider.X, y + 56, slider.W * ratio, 4), m.Ready ? Accent : "#4A4F66", 2);
            c.Fill(R(slider.X + slider.W * ratio - 7, y + 51, 14, 14), m.Ready ? Knob : "#6E6A62", 7);
            // The ceiling sits under the value, since the steppers leave no room at the track ends.
            c.Text(R(Right - 56, y, 56, 22), next + "×", next == 1 ? 15 : 17, !m.Ready || next == 1 ? Quiet : AccentInk, next != 1, 2);
            c.Text(R(Right - 64, y + 23, 64, 16), "最高 " + maximum + "×", 11, Quiet, false, 2);
            if (!last) c.Line(X0, y + 80, Right, y + 80, Divider, 1);
            if (next != current) m.Emit(id, next.ToString());
            return next;
        }

        // ---------- 物资包 ----------
        private static void AddPackItem(UiModel m, UiItem item, string quantity, bool fromCatalog)
        {
            if (m.Preferences.Packs.Count == 0) m.Preferences.Packs.Add(new SupplyPack());
            m.ActivePack = Math.Max(0, Math.Min(m.Preferences.Packs.Count - 1, m.ActivePack)); SupplyPack pack = m.Preferences.Packs[m.ActivePack];
            SupplyLine existing = pack.Lines.Find(l => l.Id == item.Id); int amount; if (!Int32.TryParse(quantity, out amount) || amount < 1 || amount > 1000) return;
            if (existing != null)
            {
                // Unparseable or nonpositive text is an empty amount when adding again. Check
                // the remaining capacity before addition, including malformed huge ints.
                int current = Math.Max(0, existing.Count);
                if (current > 1000 - amount) { m.Emit("pack.notice", "该物资包中的单项数量最多 1000"); return; }
                existing.Quantity = (current + amount).ToString();
            }
            else if (pack.Lines.Count < 64) pack.Lines.Add(new SupplyLine { Id = item.Id, Quantity = amount.ToString() });
            else { m.Emit("pack.notice", "每个物资包最多 64 种资源"); return; }
            m.Emit("pack.notice", fromCatalog ? "已加入物资包「" + pack.Name + "」" : "已加入 " + item.Name + " × " + amount);
        }
        // Items for the pack editor's own search; the first three matches are offered.
        private static List<UiItem> PackMatches(UiModel m)
        {
            List<UiItem> found = new List<UiItem>(); string query = (m.PackSearch ?? "").Trim(); if (query.Length == 0) return found;
            foreach (UiItem item in m.Resources)
            {
                if (item.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 && item.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                found.Add(item); if (found.Count == 3) break;
            }
            return found;
        }
        private static string PackError(UiModel m, SupplyPack pack, out int total)
        {
            total = 0; HashSet<string> available = new HashSet<string>(); foreach (UiItem item in m.Resources) available.Add(item.Id);
            try { total = FeaturePolicy.ValidateSupply(pack.Lines, available); return null; } catch (InvalidOperationException ex) { return ex.Message; }
        }
        private static void InputFrame(UiCanvas c, UiRect r, bool focused, bool invalid, float radius)
        {
            c.Fill(r, invalid ? DangerLine : focused ? SelLine : Surface, radius);
            c.Fill(r.Inset(1), InputFill, radius - 1);
        }

        private static void DrawPacks(UiCanvas c, UiModel m)
        {
            int count = m.Preferences.Packs.Count;
            Section(c, 140, "我的物资包 · " + count + "/16", null, null, false);
            bool armed = m.ArmedAction == "pack.delete"; string deleteLabel = armed ? ConfirmLabel(m, "确认删除") : "删除";
            float dw = c.Measure(deleteLabel, 12, false) + 8, nw = c.Measure("新建", 12, false) + 8;
            UiRect remove = R(Right - dw, 136, dw, 24), add = R(remove.X - 12 - nw, 136, nw, 24);
            c.Text(add, "新建", 12, count < 16 ? Accent : Quiet, false, 2);
            if (c.Hit(add, "pack.new", count < 16))
            { m.Preferences.Packs.Add(new SupplyPack { Name = "新物资包 " + (count + 1) }); m.ActivePack = m.Preferences.Packs.Count - 1; m.PackScroll = 0; m.PackListScroll = 10000; m.ArmedAction = null; }
            c.Text(remove, deleteLabel, 12, count > 0 ? Danger : Quiet, false, 2);
            if (Confirmed(m, "pack.delete", true, c.Hit(remove, "pack.delete", count > 0)))
            { m.Preferences.Packs.RemoveAt(Math.Max(0, Math.Min(m.ActivePack, m.Preferences.Packs.Count - 1))); m.ActivePack = 0; }
            count = m.Preferences.Packs.Count;
            if (count == 0)
            {
                c.Text(R(X0, 260, BodyW, 24), "创建你的第一个物资包", 15, TextMain, true, 1);
                c.Text(R(X0, 288, BodyW, 18), "点右上角“新建”，再搜索物品加入", 12, Muted, false, 1);
                return;
            }
            c.Fill(R(X0, 160, BodyW, 86), Surface, 10);
            UiRect listView = R(X0 + 4, 164, BodyW - 8, 78); bool listScrolls = count * 28 - 2 > listView.H; float listW = listView.W - (listScrolls ? 14 : 0);
            m.PackListScroll = c.BeginScroll(listView, m.PackListScroll, count * 28 - 2);
            for (int i = 0; i < count; i++)
            {
                SupplyPack entry = m.Preferences.Packs[i]; UiRect r = R(0, i * 28, listW, 26); bool on = i == m.ActivePack;
                if (on) c.Fill(r, SelFill, 7); else if (c.Hover(r)) c.Fill(r, Raise, 7);
                c.Text(R(10, r.Y, listW - 90, 26), Short(c, entry.Name, listW - 90, 13), 13, on ? AccentInk : TextMain, false, 0);
                c.Text(R(listW - 80, r.Y, 72, 26), entry.Lines.Count + " 种", 12, Quiet, false, 2);
                if (c.Hit(r, "pack.select." + i, true)) { m.ActivePack = i; m.PackScroll = 0; m.ArmedAction = null; m.Emit("focus.clear", ""); }
            }
            c.EndScroll();

            m.ActivePack = Math.Max(0, Math.Min(m.ActivePack, count - 1)); SupplyPack pack = m.Preferences.Packs[m.ActivePack];
            // A pencil and a placeholder mark the name as editable.
            bool naming = c.Focused("pack.name");
            InputFrame(c, R(X0, 254, BodyW, 32), naming, false, 8);
            pack.Name = c.Input(R(X0 + 10, 256, BodyW - 44, 28), "pack.name", pack.Name, 24, false);
            if (String.IsNullOrEmpty(pack.Name)) c.Text(R(X0 + 14, 254, BodyW - 50, 32), "输入物资包名称", 13, Quiet, false, 0);
            Glyph(c, "pencil", Right - 26, 263, 14, naming ? Accent : Quiet, InputFill);
            UiRect search = R(X0, 294, BodyW, 30);
            if (c.Focused("pack.search")) { c.Fill(search, SelLine, 8); c.Fill(search.Inset(1), Surface, 7); } else c.Fill(search, Surface, 8);
            Glyph(c, "search", X0 + 9, 301, 15, Quiet, Surface);
            m.PackSearch = c.Input(R(X0 + 30, 295, BodyW - 40, 28), "pack.search", m.PackSearch, 30, false);
            if (String.IsNullOrEmpty(m.PackSearch)) c.Text(R(X0 + 34, 294, BodyW - 44, 30), "搜索物品加入本包", 13, Quiet, false, 0);
            List<UiItem> matches = PackMatches(m);
            if (matches.Count == 0)
                c.Text(R(X0, 330, BodyW, 24), m.Resources.Count == 0 ? "进入地图后可搜索物品" : String.IsNullOrEmpty(m.PackSearch) ? "输入名称或 ID，点击结果加入 10 件" : "没有找到匹配项", 12, Quiet, false, 0);
            float cw = (BodyW - 12) / 3f;
            for (int i = 0; i < matches.Count; i++)
                if (Choice(c, R(X0 + i * (cw + 6), 330, cw, 24), "pack.pick." + matches[i].Id, "+ " + Short(c, matches[i].Name, cw - 24, 12), false, true, 12)) AddPackItem(m, matches[i], "10", false);

            UiRect view = R(X0, 362, BodyW, 124); bool scrolls = pack.Lines.Count * 40 > view.H; float rowW = view.W - (scrolls ? 14 : 0);
            m.PackScroll = c.BeginScroll(view, m.PackScroll, pack.Lines.Count * 40);
            for (int i = 0; i < pack.Lines.Count; i++)
            {
                SupplyLine line = pack.Lines[i]; UiItem item = m.Resources.Find(r => r.Id == line.Id); float y = i * 40;
                bool known = item != null || m.Resources.Count == 0, countValid = line.Count >= 1 && line.Count <= 1000;
                if (item != null && item.Image != null) c.Image(R(0, y + 8, 24, 24), item.Image);
                float nameW = rowW - 32 - 60 - 34 - 12;
                c.Text(R(32, y, nameW, 40), Short(c, item != null ? item.Name : known ? line.Id : line.Id + "（未找到）", nameW, 13), 13, known ? TextMain : Warn, false, 0);
                // A bad quantity is outlined on its own row, not only in the summary below.
                UiRect quantity = R(rowW - 60 - 34, y + 6, 60, 28);
                InputFrame(c, quantity, c.Focused("pack.quantity." + i), !countValid, 7);
                line.Quantity = c.Input(quantity.Inset(2), "pack.quantity." + i, line.Quantity, 4, true);
                UiRect drop = R(rowW - 28, y + 7, 28, 26); bool hover = c.Hover(drop);
                if (hover) c.Fill(drop, Raise, 6);
                Glyph(c, "close", drop.X + 7, drop.Y + 6, 14, hover ? TextMain : Quiet, hover ? Raise : Background);
                if (c.Hit(drop, "pack.remove." + i, true)) { pack.Lines.RemoveAt(i); break; }
                if (i < pack.Lines.Count - 1) c.Line(0, y + 40, rowW, y + 40, Divider, 1);
            }
            c.EndScroll();
            if (pack.Lines.Count == 0) c.Text(R(X0, 412, BodyW, 20), "这个物资包还是空的，用上方搜索加入物品", 12, Quiet, false, 1);

            int total; string error = PackError(m, pack, out total); bool noCatalog = m.Resources.Count == 0;
            string summary = error == null ? pack.Lines.Count + " 种资源 · 合计 " + total + " 件" : pack.Lines.Count == 0 ? "先加入至少一种物品" : noCatalog ? "进入地图后可校验并添加" : error;
            c.Text(R(X0, 492, BodyW, 20), Short(c, summary, BodyW, 12), 12, error == null || pack.Lines.Count == 0 || noCatalog ? Muted : Danger, false, 0);
            int location = Segmented(c, R(X0, 520, 176, 34), new string[] { "pack.location.target", "pack.location.center" }, new string[] { "目标附近", "画面中心" }, null, m.Location, true, 12);
            if (location >= 0) m.Location = location;
            string execute = m.Busy ? "生成中…" : error == null ? "添加整包 " + total + " 件" : pack.Lines.Count == 0 ? "先加入物品" : noCatalog ? "进入地图后可用" : "请先修正清单";
            if (Button(c, R(Right - 128, 520, 128, 34), "pack.execute", execute, 1, m.Ready && error == null && !m.Busy)) m.Emit("pack.execute", "");
        }

        // ---------- 设置 ----------
        private static void DrawSettings(UiCanvas c, UiModel m)
        {
            Section(c, BodyY, "动作快捷键", null, null, false); SectionNote(c, BodyY, "面板关闭时生效");
            string[] names = { "快速完成建造", "杀死选中单位", "重复添加资源" };
            string[] notes = { "作用于选中的未完工建筑", "含村民时需再按一次确认", "按“生成”页当前选择再添加一次" };
            for (int i = 0; i < 3; i++)
            {
                float y = 118 + i * 46; bool capturing = m.CapturingKey == i;
                c.Text(R(X0, y + 6, BodyW - 120, 19), names[i], 14, TextMain, false, 0);
                c.Text(R(X0, y + 25, BodyW - 120, 16), notes[i], 12, Quiet, false, 0);
                if (Button(c, R(Right - 44 - 62, y + 9, 62, 28), "key.capture." + i, capturing ? "按新键…" : Short(c, KeyName(m.Preferences.Keys[i]), 54, 13), capturing ? 1 : 0, true))
                    m.Emit("key.capture", i.ToString());
                UiRect toggle = R(Right - 36, y + 13, 36, 20);
                if (i == 0) m.BuildHotkey = Switch(c, toggle, "build.hotkey", m.BuildHotkey);
                else if (i == 1) m.KillHotkey = Switch(c, toggle, "kill.hotkey", m.KillHotkey);
                else m.GiveHotkey = Switch(c, toggle, "give.hotkey", m.GiveHotkey);
                c.Line(X0, y + 46, Right, y + 46, Divider, 1);
            }
            c.Text(R(X0, 262, BodyW - 120, 19), "展开 / 收起面板", 14, TextMain, false, 0);
            c.Text(R(X0, 281, BodyW - 120, 16), "固定键位；Shift+F11 显示或隐藏", 12, Quiet, false, 0);
            Key(c, R(Right - 44 - 62, 265, 62, 28), "F11");
            c.Text(R(X0, 308, BodyW, 16), "开关每次启动默认关闭；点按键后按新键，Esc 取消", 12, Quiet, false, 0);

            Section(c, 338, "面板", null, null, false);
            c.Text(R(X0, 356, 70, 32), "停靠位置", 13, Muted, false, 0);
            int[] docks = { DockLeft, DockRight, DockFloating };
            int dock = Segmented(c, R(X0 + 72, 356, BodyW - 72, 32), new string[] { "dock.1", "dock.0", "dock.2" }, new string[] { "左侧", "右侧", "浮动" }, null, Array.IndexOf(docks, m.Preferences.Dock), true, 12);
            if (dock >= 0) m.Preferences.Dock = docks[dock];
            c.Text(R(X0, 396, 70, 32), "界面大小", 13, Muted, false, 0);
            string[] scaleIds = new string[FeaturePolicy.UiScales.Length], scaleLabels = new string[FeaturePolicy.UiScales.Length];
            for (int i = 0; i < scaleIds.Length; i++) { scaleIds[i] = "ui.scale." + FeaturePolicy.UiScales[i]; scaleLabels[i] = FeaturePolicy.UiScales[i] + "%"; }
            int scale = Segmented(c, R(X0 + 72, 396, BodyW - 72, 32), scaleIds, scaleLabels, null, Array.IndexOf(FeaturePolicy.UiScales, m.Preferences.UiScale), true, 12);
            if (scale >= 0) m.Preferences.UiScale = FeaturePolicy.UiScales[scale];

            Section(c, 442, "偏好", null, null, false);
            c.Text(R(X0, 458, BodyW, 17), "自动保存收藏、物资包、数量、键位与界面设置；", 12, Muted, false, 0);
            c.Text(R(X0, 475, BodyW, 17), "不保存属性锁、危险操作开关与倍率。", 12, Muted, false, 0);
            c.Text(R(X0, 492, BodyW, 17), "恢复默认只重置键位与界面，保留收藏和物资包。", 12, Muted, false, 0);
            if (Button(c, R(X0, 514, 153, 32), "prefs.default", "恢复默认设置", 0, true)) m.Emit("prefs.default", "");
            if (Button(c, R(X0 + 159, 514, 153, 32), "help.open", "使用说明", 0, true)) m.Navigate(PageHelp);
            c.Text(R(X0, 554, BodyW, 18), "修改器 v" + m.Version + (String.IsNullOrEmpty(m.GameVersion) ? "" : " · Dawn of Man " + m.GameVersion), 12, Quiet, false, 1);
        }
    }
}
