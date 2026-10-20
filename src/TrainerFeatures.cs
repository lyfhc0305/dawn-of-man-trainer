using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DawnOfMan;
using MadrugaShared;
using UnityEngine;

namespace DawnTrainer
{
    public sealed partial class Trainer
    {
        private TrainerPreferences preferences;
        private string preferencePath, lastPreferenceText = "";
        private float nextPreferenceSave, lastRunningSpeed = 1;
        private readonly OverlayPauseState overlayPause = new OverlayPauseState();
        private bool autoPauseRequested, preferencesWritable = true;
        private int capturingKey = -1, batchTotal, batchDone;
        private string batchLabel = "";

        private void LoadPreferences()
        {
            preferencePath = Path.Combine(Path.GetDirectoryName(Application.dataPath), @"DawnTrainer\preferences.ini");
            if (isolatedTestSession) preferencePath = Path.Combine(IntegrationChecks.ArtifactDirectory(), "test-preferences.ini");
            string problem; preferences = TrainerPreferences.LoadSafely(preferencePath, out preferencesWritable, out problem);
            if (problem != null) Debug.LogWarning("[DawnTrainer] Preferences: " + problem);
            for (int i = 0; i < 3; i++) { KeyCode key; if (!Enum.TryParse<KeyCode>(preferences.Keys[i], out key) || !Enum.IsDefined(typeof(KeyCode), key)) preferences.Keys[i] = new string[] { "Insert", "Delete", "F10" }[i]; }
            if (preferences.Keys[0] == preferences.Keys[1] || preferences.Keys[0] == preferences.Keys[2] || preferences.Keys[1] == preferences.Keys[2]) preferences.Keys = new string[] { "Insert", "Delete", "F10" };
            quantity = preferences.Quantity; animalQuantity = preferences.AnimalQuantity;
            window.x = preferences.WindowX; window.y = preferences.WindowY;
            windowPositioned = window.x != 0 || window.y != 0;
            lastPreferenceText = preferences.Serialize();
        }
        private void SavePreferences(bool force)
        {
            if (preferences == null || !preferencesWritable || (!force && Time.realtimeSinceStartup < nextPreferenceSave)) return;
            nextPreferenceSave = Time.realtimeSinceStartup + 1;
            preferences.WindowX = window.x; preferences.WindowY = window.y;
            int amount;
            if (Int32.TryParse(quantity, out amount) && amount >= 1 && amount <= 1000) preferences.Quantity = quantity;
            if (Int32.TryParse(animalQuantity, out amount) && amount >= 1 && amount <= 50) preferences.AnimalQuantity = animalQuantity;
            if (selectedResource != null) preferences.Resource = selectedResource.getId();
            if (selectedAnimal != null) preferences.Animal = selectedAnimal.getId();
            string text = preferences.Serialize(); if (text == lastPreferenceText) return;
            try { preferences.Save(preferencePath); lastPreferenceText = text; }
            catch (Exception ex) { nextPreferenceSave += 15; Debug.LogWarning("[DawnTrainer] Save preferences: " + ex.Message); }
        }
        private HashSet<string> ReservedKeys()
        {
            HashSet<string> keys = new HashSet<string> { "Escape", "F8", "F9", "F11", "F12", "Print", "SysReq", "Pause", "KeypadEnter" };
            foreach (GameAction action in Enum.GetValues(typeof(GameAction)))
            {
                if (action == GameAction.Count) continue;
                Binding binding = Singleton<Settings>.Instance.getBinding(action);
                keys.Add(binding.getKeyCode().ToString()); keys.Add(binding.getAltKeyCode().ToString());
            }
            return keys;
        }
        private bool CaptureShortcut()
        {
            if (capturingKey < 0) return false;
            if (!visible) { capturingKey = -1; return false; }
            if (Input.GetKeyDown(KeyCode.Escape)) { capturingKey = -1; inputBlockedUntilFrame = Time.frameCount + 1; return true; }
            foreach (KeyCode code in Enum.GetValues(typeof(KeyCode)))
            {
                if (code == KeyCode.None || (int)code >= (int)KeyCode.Mouse0 || !Input.GetKeyDown(code)) continue;
                string error = FeaturePolicy.ShortcutError(code.ToString(), capturingKey, preferences.Keys, ReservedKeys());
                if (error != null) { Notify(error + "；Esc 取消"); return true; }
                preferences.Keys[capturingKey] = code.ToString(); capturingKey = -1;
                inputBlockedUntilFrame = Time.frameCount + 1; GUI.FocusControl(null); SavePreferences(true);
                Notify("快捷键已保存，对应开关仍需手动启用"); return true;
            }
            return true;
        }
        private bool ActionKeyDown(int slot)
        {
            KeyCode code;
            if (!Enum.TryParse<KeyCode>(preferences.Keys[slot], out code)) return false;
            if (!Input.GetKeyDown(code)) return false;
            // Default action keys intentionally take over the original same-frame input.
            string original = new string[] { "Insert", "Delete", "F10" }[slot];
            if (preferences.Keys[slot] != original && ReservedKeys().Contains(preferences.Keys[slot])) return false;
            return true;
        }
        private void SyncOverlayPause()
        {
            TimeManager time = TransientSingleton<TimeManager>.CurrentInstance;
            float? resetSpeed = overlayPause.ResumeReset(time.isRealPaused(), visible && preferences.AutoPause);
            if (resetSpeed.HasValue) time.setTimeScale(resetSpeed.Value);
            overlayPause.Observe(time.getTimeScale());
            bool requested = visible && preferences.AutoPause;
            float? desired = null;
            if (requested && !autoPauseRequested) desired = overlayPause.Open(time.getTimeScale(), time.isRealPaused());
            if (!requested && autoPauseRequested) desired = overlayPause.Close(time.getTimeScale(), time.isRealPaused());
            autoPauseRequested = requested;
            if (desired.HasValue) time.setTimeScale(desired.Value);
        }
        private void SetSpeed(float speed)
        {
            if (Single.IsNaN(speed) || Single.IsInfinity(speed) || speed < 0 || speed > 15) return;
            TimeManager time = TransientSingleton<TimeManager>.CurrentInstance;
            if (time.isRealPaused()) throw new InvalidOperationException("请先关闭游戏暂停菜单，再调整速度");
            if (speed > 0) lastRunningSpeed = speed;
            float actual = overlayPause.RequestSpeed(speed, visible && preferences.AutoPause);
            time.setTimeScale(actual);
        }
        private void TogglePause()
        {
            float current = overlayPause.Owns ? overlayPause.Desired : TransientSingleton<TimeManager>.CurrentInstance.getTimeScale();
            SetSpeed(FeaturePolicy.NextPauseSpeed(current, ref lastRunningSpeed));
        }
        private List<PhysicalEntity> SelectionSnapshot()
        {
            List<PhysicalEntity> result = new List<PhysicalEntity>(); Selection selection = TransientSingleton<Selection>.CurrentInstance;
            for (int i = 0; i < selection.getCount(); i++) { PhysicalEntity e = selection.getEntity(i); if (Valid(e)) result.Add(e); }
            return result;
        }
        private bool Valid(PhysicalEntity entity) { return manager != null && entity != null && !entity.isDestroyed() && !manager.isPendingDestruction(entity); }
        private bool CanComplete(PhysicalEntity entity)
        {
            Buildable buildable = entity as Buildable;
            return buildable != null && Valid(buildable) && buildable.getTeam() == Team.Player && !buildable.isBuilt() && !buildable.isBeingRecycled();
        }
        private bool CanKill(PhysicalEntity entity)
        { Being being = entity as Being; return being != null && Valid(being) && being.isAlive(); }
        private static Stat[] BeingStats(Being being)
        {
            Human human = being as Human;
            return new Stat[] { being.getHealth(), being.getNutrition(), being.getHydration(), being.getTemperature(), being.getStamina(), human == null ? null : human.getRest(), human == null ? null : human.getMorale() };
        }
        private bool CanRescue(PhysicalEntity e) { Being being = e as Being; return being != null && being.getTeam() == Team.Player && being.isAlive() && Valid(being); }
        private bool CanRepair(PhysicalEntity e) { Structure s = e as Structure; return s != null && s.getTeam() == Team.Player && s.isBuilt() && !s.isBeingRecycled() && Valid(s); }
        private bool CanRestoreResource(PhysicalEntity e) { Resource r = e as Resource; return r != null && Valid(r) && r.hasAnyFlag(EntityFlags.Tool | EntityFlags.Clothing); }
        private bool CanTame(PhysicalEntity e, out string reason)
        {
            reason = "仅幼年、科技已解锁且没有任务的可驯化动物"; Animal animal = e as Animal;
            if (animal == null || !Valid(animal) || !animal.isAlive() || animal.getTeam() != Team.Nature || animal.getAnimalType().getDomesticatedVariant() == null) return false;
            foreach (ContextAction action in animal.getContextActions())
                if (action.getContextActionType() == ContextActionType.Capture) return action.doable(animal, ref reason);
            return false;
        }
        private void RescueSelected(bool all)
        {
            int done = 0;
            foreach (PhysicalEntity e in SelectionSnapshot()) if (CanRescue(e))
            {
                Stat[] stats = BeingStats((Being)e); int limit = all ? stats.Length : 1;
                for (int i = 0; i < limit; i++) if (stats[i] != null) stats[i].setValue(1);
                done++;
            }
            TransientSingleton<GameGuiManager>.CurrentInstance.requestRefresh();
            Notify(done == 0 ? "请先选择存活的玩家人物或家畜" : "已恢复 " + done + " 个选中单位的" + (all ? "生存状态" : "健康值"));
        }
        private void RepairSelected(bool resourcesOnly)
        {
            int done = 0; GameGuiManager gui = TransientSingleton<GameGuiManager>.CurrentInstance;
            foreach (PhysicalEntity e in SelectionSnapshot())
            {
                if (!resourcesOnly && CanRepair(e))
                { Structure s = (Structure)e; s.increaseCondition(1 - s.getCondition().getValue()); s.resetRepairMaterials(); gui.requestRefresh(s); done++; }
                if (resourcesOnly && CanRestoreResource(e)) { ((Resource)e).getCondition().setValue(1); gui.requestRefresh(e); done++; }
            }
            Notify("已恢复 " + done + (resourcesOnly ? " 件选中工具 / 衣物的耐久" : " 个选中建筑的状态"));
        }
        private void TameSelected()
        {
            int done = 0; string reason = "";
            foreach (PhysicalEntity e in SelectionSnapshot()) if (CanTame(e, out reason))
            {
                Animal animal = (Animal)e;
                Animal result = (Animal)manager.morphBeing(animal, animal.getAnimalType().getDomesticatedVariant());
                result.onSpawn(); TransientSingleton<KnowledgeManager>.CurrentInstance.onNewResidentAnimal(result); done++;
            }
            TransientSingleton<GameGuiManager>.CurrentInstance.requestRefresh();
            Notify(done > 0 ? "已将 " + done + " 只符合条件的动物转为对应家畜" : "没有符合驯化条件的动物：" + reason);
        }
        private int CountVisitors<T>() where T : Being
        {
            int count = 0; List<Entity> list = manager.getTypeEntities<T>();
            if (list != null) foreach (Entity entity in list) { Being being = entity as Being; if (being != null && being.isAlive() && Valid(being)) count++; }
            return count;
        }
        private void Invite(bool migrant)
        {
            if (migrant && CountVisitors<Migrant>() > 0) throw new InvalidOperationException("已有移民在前来途中，请等待抵达");
            if (!migrant && CountVisitors<Trader>() > 0) throw new InvalidOperationException("地图上已有商人，请等待离开后再邀请");
            if (migrant && !manager.hasResidentCapacity()) throw new InvalidOperationException("住房没有剩余容量，请先增加住房");
            int before = migrant ? CountVisitors<Migrant>() : CountVisitors<Trader>();
            SettlementManager settlement = TransientSingleton<SettlementManager>.CurrentInstance;
            if (migrant) settlement.spawnMigrantGroup(); else settlement.spawnTrader();
            int created = (migrant ? CountVisitors<Migrant>() : CountVisitors<Trader>()) - before;
            Notify(created > 0 ? (migrant ? "已有 " + created + " 名移民" : "商队已出发") + "，将从地图边缘前来" : "当前场景没有生成访客，请检查场景移民设置");
        }
        private List<SupplyLine> MissingMaterials(List<PhysicalEntity> selected, out List<SpawnJob> plan)
        {
            List<SupplyLine> totals = new List<SupplyLine>(); plan = new List<SpawnJob>();
            foreach (PhysicalEntity e in selected)
            {
                Buildable b = e as Buildable;
                if (b == null || b.getTeam() != Team.Player || b.isBuilt() || b.isBeingRecycled() || !Valid(b)) continue;
                EntityAmounts amounts = b.getPendingConstructionMaterials(); if (amounts == null) continue;
                EntityAmounts copy = amounts.clone();
                for (int i = 0; i < copy.getEntryCount(); i++)
                {
                    EntityAmount amount = copy.getEntry(i); if (amount.getAmount() <= 0) continue;
                    ResourceType type = amount.getResourceType(); SupplyLine line = totals.Find(l => l.Id == type.getId());
                    if (line == null) { line = new SupplyLine { Id = type.getId(), Quantity = "0" }; totals.Add(line); }
                    line.Quantity = checked(line.Count + amount.getAmount()).ToString();
                    plan.Add(new SpawnJob { Manager = manager, Type = type, Position = b.getPosition(), Remaining = amount.getAmount(), Total = amount.getAmount(), Age = Age.Count, Gender = Gender.Count });
                }
            }
            return totals;
        }
        private void CommitPlan(List<SpawnJob> plan, string label)
        {
            if (jobs.Count > 0) throw new InvalidOperationException("请等当前添加完成，或先停止生成");
            List<SupplyLine> lines = new List<SupplyLine>(); HashSet<string> available = new HashSet<string>();
            foreach (EntityType r in resources) available.Add(r.getId());
            foreach (SpawnJob job in plan) lines.Add(new SupplyLine { Id = job.Type.getId(), Quantity = job.Total.ToString() });
            int total = FeaturePolicy.ValidateSupply(lines, available, FeaturePolicy.MaximumTotal);
            foreach (SpawnJob job in plan) jobs.Enqueue(job);
            batchTotal = total; batchDone = 0; batchLabel = label; lastSpawnUntil = 0;
            Notify("正在添加 " + label + "，共 " + total + " 件");
        }
        private void SupplyBuildings()
        {
            List<SpawnJob> plan; MissingMaterials(SelectionSnapshot(), out plan);
            if (plan.Count == 0) { Notify("所选未完工建筑没有尚未交付的材料"); return; }
            CommitPlan(plan, "建筑缺料补给");
        }
        private void QueuePack(List<SupplyLine> lines, string name)
        {
            HashSet<string> available = new HashSet<string>(); foreach (EntityType type in resources) available.Add(type.getId());
            FeaturePolicy.ValidateSupply(lines, available); Vector3 position = SpawnPosition(); List<SpawnJob> plan = new List<SpawnJob>();
            foreach (SupplyLine line in lines) plan.Add(new SpawnJob { Manager = manager, Type = resources.Find(t => t.getId() == line.Id), Position = position, Total = line.Count, Remaining = line.Count, Age = Age.Count, Gender = Gender.Count });
            CommitPlan(plan, name);
        }
        private void CancelSpawns()
        { jobs.Clear(); lastSpawnUntil = 0; Notify("已停止后续生成，已生成的 " + batchDone + " 件会保留在地图"); }

        private void UpdateFeatureModel()
        {
            UpdateMultiplierModel();
            ui.Preferences = preferences; ui.CapturingKey = capturingKey; ui.AutoPauseActive = overlayPause.Owns;
            ui.RescueTargets = ui.RepairTargets = ui.ResourceRepairTargets = ui.TameTargets = ui.HousingFree = ui.MigrantCount = ui.TraderCount = 0;
            ui.Materials.Clear(); ui.TameReason = "仅幼年、科技已解锁且没有任务的可驯化动物";
            for (int i = 0; i < 7; i++) ui.TargetStats[i] = -1;
            if (!WorldReady()) return;
            if (overlayPause.Owns) ui.Speed = overlayPause.Desired;
            if (ui.Busy) { ui.Done = batchDone; ui.Total = batchTotal; ui.TaskName = batchLabel; }
            ui.HousingFree = Math.Max(0, manager.getHumanCapacity() - manager.getAliveResidentCount() - manager.getPregnantHumanCount());
            ui.MigrantCount = CountVisitors<Migrant>(); ui.TraderCount = CountVisitors<Trader>();
            float[] sums = new float[7]; int[] counts = new int[7]; List<PhysicalEntity> selected = SelectionSnapshot();
            foreach (PhysicalEntity e in selected)
            {
                if (CanRescue(e)) { ui.RescueTargets++; Stat[] stats = BeingStats((Being)e); for (int i = 0; i < 7; i++) if (stats[i] != null) { sums[i] += stats[i].getValue(); counts[i]++; } }
                if (CanRepair(e)) ui.RepairTargets++;
                if (CanRestoreResource(e)) ui.ResourceRepairTargets++;
                string reason; if (CanTame(e, out reason)) ui.TameTargets++; else if (e is Animal && !String.IsNullOrEmpty(reason)) ui.TameReason = reason;
            }
            for (int i = 0; i < 7; i++) if (counts[i] > 0) ui.TargetStats[i] = sums[i] / counts[i];
            List<SpawnJob> ignored; ui.Materials.AddRange(MissingMaterials(selected, out ignored));
        }
        private bool FeatureUiAction(string action, string value)
        {
            if (action == "pack.notice") { Notify(value); return true; }
            if (action == "focus.clear") { GUI.FocusControl(null); return true; }
            if (action == "key.capture") { capturingKey = Int32.Parse(value); GUI.FocusControl(null); return true; }
            if (action == "prefs.default") { preferences.Keys = new string[] { "Insert", "Delete", "F10" }; preferences.AutoPause = false; preferences.UiScale = 100; capturingKey = -1; SavePreferences(true); return true; }
            if (!Ready()) return false;
            if (action.StartsWith("multiplier.", StringComparison.Ordinal))
            {
                string kind = action.Substring("multiplier.".Length);
                int multiplier;
                if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out multiplier)) throw new InvalidOperationException("倍率必须是大于等于 1 的整数");
                Enqueue(delegate { SetMultiplier(kind, multiplier); });
            }
            else if (action == "rescue.health") Enqueue(delegate { RescueSelected(false); });
            else if (action == "rescue.all") Enqueue(delegate { RescueSelected(true); });
            else if (action == "repair.building") Enqueue(delegate { RepairSelected(false); });
            else if (action == "repair.resource") Enqueue(delegate { RepairSelected(true); });
            else if (action == "tame") Enqueue(TameSelected);
            else if (action == "supply.building") Enqueue(SupplyBuildings);
            else if (action == "invite.migrant") Enqueue(delegate { Invite(true); });
            else if (action == "invite.trader") Enqueue(delegate { Invite(false); });
            else if (action == "spawn.cancel") Enqueue(CancelSpawns);
            else if (action == "pack.execute")
            {
                if (ui.ActivePack < 0 || ui.ActivePack >= preferences.Packs.Count) return true;
                SupplyPack source = preferences.Packs[ui.ActivePack]; List<SupplyLine> snapshot = source.Lines.ConvertAll(l => l.Clone()); string title = source.Name;
                Enqueue(delegate { QueuePack(snapshot, title); });
            }
            else return false;
            return true;
        }
    }
}
