using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DawnTrainer
{
    internal sealed class SupplyLine
    {
        internal string Id = "", Quantity = "10";
        internal int Count { get { int count; return Int32.TryParse(Quantity, out count) ? count : 0; } }
        internal SupplyLine Clone() { return new SupplyLine { Id = Id, Quantity = Quantity }; }
    }
    internal sealed class SupplyPack
    {
        internal string Name = "新物资包";
        internal readonly List<SupplyLine> Lines = new List<SupplyLine>();
    }
    internal sealed class TrainerPreferences
    {
        internal string[] Keys = { "Insert", "Delete", "F10" };
        internal bool AutoPause;
        internal int UiScale = 100;
        internal string Quantity = "10", AnimalQuantity = "1", Resource = "", Animal = "";
        internal float WindowX, WindowY;
        internal readonly HashSet<string> Favorites = new HashSet<string>(StringComparer.Ordinal);
        internal readonly List<SupplyPack> Packs = new List<SupplyPack>();
        internal static TrainerPreferences Defaults()
        {
            TrainerPreferences p = new TrainerPreferences();
            SupplyPack village = new SupplyPack { Name = "建村材料" };
            village.Lines.Add(new SupplyLine { Id = "log", Quantity = "20" });
            village.Lines.Add(new SupplyLine { Id = "sticks", Quantity = "30" });
            village.Lines.Add(new SupplyLine { Id = "stone", Quantity = "20" });
            village.Lines.Add(new SupplyLine { Id = "flint", Quantity = "10" });
            SupplyPack winter = new SupplyPack { Name = "过冬补给" };
            winter.Lines.Add(new SupplyLine { Id = "cured_meat", Quantity = "30" });
            winter.Lines.Add(new SupplyLine { Id = "dry_fish", Quantity = "20" });
            winter.Lines.Add(new SupplyLine { Id = "skins_outfit", Quantity = "10" });
            p.Packs.Add(village); p.Packs.Add(winter); return p;
        }
        private static string Encode(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")); }
        private static string Decode(string s) { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
        internal string Serialize()
        {
            StringBuilder b = new StringBuilder("Version=1\n");
            for (int i = 0; i < 3; i++) b.Append("Key" + i + "=" + Keys[i] + "\n");
            b.Append("AutoPause=" + AutoPause + "\nUiScale=" + UiScale + "\nQuantity=" + Quantity + "\nAnimalQuantity=" + AnimalQuantity + "\n");
            b.Append("Resource=" + Encode(Resource) + "\nAnimal=" + Encode(Animal) + "\n");
            b.Append("WindowX=" + WindowX.ToString("R", CultureInfo.InvariantCulture) + "\nWindowY=" + WindowY.ToString("R", CultureInfo.InvariantCulture) + "\n");
            List<string> favorites = new List<string>(Favorites); favorites.Sort(StringComparer.Ordinal);
            foreach (string id in favorites) b.Append("Favorite=" + Encode(id) + "\n");
            foreach (SupplyPack pack in Packs)
            {
                b.Append("Pack=" + Encode(pack.Name) + "\n");
                foreach (SupplyLine line in pack.Lines) b.Append("Item=" + Encode(line.Id) + "," + Encode(line.Quantity) + "\n");
            }
            return b.ToString();
        }
        internal static TrainerPreferences Deserialize(string text)
        {
            if (text.Length > 262144) throw new InvalidDataException("偏好文件过大");
            TrainerPreferences p = new TrainerPreferences(); SupplyPack current = null;
            foreach (string raw in text.Split('\n'))
            {
                string row = raw.TrimEnd('\r'); int split = row.IndexOf('='); if (split < 0) continue;
                string key = row.Substring(0, split), value = row.Substring(split + 1);
                try
                {
                    int count; float number; bool flag;
                    if (key == "Version" && value != "1") throw new InvalidDataException("偏好版本不兼容");
                    if (key == "Key0" || key == "Key1" || key == "Key2") { if (value.Length < 32) p.Keys[key[3] - '0'] = value; }
                    else if (key == "AutoPause" && Boolean.TryParse(value, out flag)) p.AutoPause = flag;
                    else if (key == "UiScale" && Int32.TryParse(value, out count) && Array.IndexOf(FeaturePolicy.UiScales, count) >= 0) p.UiScale = count;
                    else if (key == "Quantity" && Int32.TryParse(value, out count) && count >= 1 && count <= 1000) p.Quantity = value;
                    else if (key == "AnimalQuantity" && Int32.TryParse(value, out count) && count >= 1 && count <= 50) p.AnimalQuantity = value;
                    else if (key == "Resource") p.Resource = Decode(value);
                    else if (key == "Animal") p.Animal = Decode(value);
                    else if ((key == "WindowX" || key == "WindowY") && Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !Single.IsNaN(number) && !Single.IsInfinity(number) && number >= 0 && number < 10000)
                    { if (key == "WindowX") p.WindowX = number; else p.WindowY = number; }
                    else if (key == "Favorite" && p.Favorites.Count < 128) { string id = Decode(value); if (id.Length < 80) p.Favorites.Add(id); }
                    else if (key == "Pack")
                    {
                        current = null;
                        if (p.Packs.Count < 16) { current = new SupplyPack { Name = Decode(value) }; if (current.Name.Length > 32) current.Name = current.Name.Substring(0, 32); p.Packs.Add(current); }
                    }
                    else if (key == "Item" && current != null && current.Lines.Count < 64)
                    {
                        string[] cells = value.Split(','); if (cells.Length != 2) continue;
                        string id = Decode(cells[0]), quantity = Decode(cells[1]);
                        if (id.Length < 80 && quantity.Length <= 4) current.Lines.Add(new SupplyLine { Id = id, Quantity = quantity });
                    }
                }
                catch (FormatException) { /* Skip a damaged entry, without executing it. */ }
            }
            if (p.Keys[0] == p.Keys[1] || p.Keys[0] == p.Keys[2] || p.Keys[1] == p.Keys[2]) p.Keys = new string[] { "Insert", "Delete", "F10" };
            return p;
        }
        internal static TrainerPreferences Load(string path) { return File.Exists(path) ? Deserialize(File.ReadAllText(path, Encoding.UTF8)) : Defaults(); }
        // An unreadable file is backed up before defaults may replace it; if that fails, nothing is written this session.
        internal static TrainerPreferences LoadSafely(string path, out bool writable, out string problem)
        {
            writable = true; problem = null;
            try { return Load(path); }
            catch (Exception ex)
            {
                string backup = path + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                try { File.Copy(path, backup, false); problem = ex.Message + "；原文件已备份为 " + backup; }
                catch (Exception copy) { writable = false; problem = ex.Message + "；无法备份，本次不保存偏好：" + copy.Message; }
                return Defaults();
            }
        }
        internal void Save(string path)
        {
            string folder = Path.GetDirectoryName(path); Directory.CreateDirectory(folder);
            string staged = path + ".tmp"; File.WriteAllText(staged, Serialize(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(staged, path, null); else File.Move(staged, path);
        }
    }
    internal static class FeaturePolicy
    {
        internal const int MaximumTotal = 5000, MaximumJobs = 128;
        internal static readonly int[] UiScales = { 80, 90, 100, 110, 125, 150 };
        // 100% keeps the panel near 75% of screen height (never below 1:1); the result always fits and snaps to 0.05 steps.
        internal static float PanelScale(float screenWidth, float screenHeight, float panelWidth, float panelHeight, int percent)
        {
            float fit = Math.Min((screenWidth - 48) / panelWidth, (screenHeight - 48) / panelHeight);
            float scale = Math.Min(fit, Math.Max(1, screenHeight * .75f / panelHeight) * percent / 100f);
            if (Single.IsNaN(scale) || Single.IsInfinity(scale)) return 1;
            return Math.Max(.25f, (float)Math.Floor(scale * 20 + .0001f) / 20);
        }
        internal static float NextPauseSpeed(float current, ref float lastRunning)
        {
            if(current>0){lastRunning=current;return 0;}
            return Single.IsNaN(lastRunning)||Single.IsInfinity(lastRunning)?1:Math.Max(.25f,Math.Min(15,lastRunning));
        }
        internal static float ClampPanelPosition(float position, float screenExtent, float panelExtent)
        {
            float available = Math.Max(0, screenExtent - panelExtent), margin = Math.Min(12, available / 2);
            if (Single.IsNaN(position) || Single.IsInfinity(position)) return available / 2;
            return Math.Max(margin, Math.Min(available - margin, position));
        }
        // Packs are limited to MaximumJobs lines; building plans hold one job per building and material, bounded by MaximumTotal.
        internal static int ValidateSupply(IList<SupplyLine> lines, ISet<string> available, int maximumLines = MaximumJobs)
        {
            if (lines.Count == 0 || lines.Count > maximumLines) throw new InvalidOperationException("清单需要 1–" + maximumLines + " 项");
            long total = 0;
            foreach (SupplyLine line in lines)
            {
                if (!available.Contains(line.Id)) throw new InvalidOperationException("资源不存在：" + line.Id);
                if (line.Count < 1 || line.Count > 1000) throw new InvalidOperationException("每项数量需要为 1–1000");
                total += line.Count;
            }
            if (total > MaximumTotal) throw new InvalidOperationException("一次最多添加 5000 件，请拆分物资包");
            return (int)total;
        }
        internal static string ShortcutError(string key, int slot, string[] keys, ISet<string> reserved)
        {
            if (key == "None" || key.StartsWith("Mouse") || key.StartsWith("Joystick") || key.Contains("Shift") || key.Contains("Control") || key.Contains("Alt") || key.Contains("Command") || key.Contains("Windows")) return "请选择一个普通键盘按键";
            if (reserved.Contains(key)) return "该按键被游戏或系统占用，请换一个";
            for (int i = 0; i < keys.Length; i++) if (i != slot && keys[i] == key) return "与另一项修改器快捷键重复";
            return null;
        }
    }
    internal sealed class OverlayPauseState
    {
        internal bool Owns;
        internal float Desired = 1;
        internal bool ResetPending { get; private set; }
        internal float? Open(float current, bool realPaused)
        { if (Owns || realPaused || current <= 0) return null; Owns = true; Desired = current; return 0; }
        internal float? Close(float current, bool realPaused)
        { bool restore = Owns && !realPaused && current == 0; Owns = false; return restore ? (float?)Desired : null; }
        internal float RequestSpeed(float speed, bool holdForOverlay)
        {
            if(Owns||holdForOverlay){Owns=true;Desired=speed;return 0;}
            return speed;
        }
        // A reset waits for the native menu to release its pause, then uses the current overlay preference.
        internal float? ResetSpeed(bool realPaused, bool holdForOverlay)
        {
            Clear(); ResetPending = realPaused;
            return realPaused ? (float?)null : RequestSpeed(1, holdForOverlay);
        }
        internal float? ResumeReset(bool realPaused, bool holdForOverlay)
        { return ResetPending && !realPaused ? ResetSpeed(false, holdForOverlay) : null; }
        internal void Clear() { Owns = false; Desired = 1; ResetPending = false; }
        // A speed set by the game itself while the panel holds its pause ends that hold.
        internal void Observe(float current) { if (Owns && current > 0) Owns = false; }
    }

    internal static class ProductionPolicy
    {
        internal static bool ValidMultiplier(int value, int maximum) { return value >= 1 && value <= maximum; }
        internal static int ScaledCapacity(int capacity, int multiplier)
        {
            if (capacity <= 0) return capacity;
            if (!ValidMultiplier(multiplier, 10)) multiplier = 1;
            return (int)Math.Min(Int32.MaxValue, (long)capacity * multiplier);
        }
        internal static float ScaleBuildWork(float work, int multiplier)
        {
            if (Single.IsNaN(work) || Single.IsInfinity(work) || work <= 0) return 0;
            if (!ValidMultiplier(multiplier, 20)) multiplier = 1;
            double scaled = (double)work * multiplier;
            return (float)Math.Min(Single.MaxValue, scaled);
        }
    }

    internal sealed class SelectionConfirmation
    {
        private string signature;
        private float until;
        internal void Clear() { signature = null; until = 0; }
        internal void Observe(string current, float now)
        { if (signature != current || now >= until) Clear(); }
        internal bool Confirm(string current, float now)
        {
            Observe(current, now);
            if (signature != null) { Clear(); return true; }
            signature = current; until = now + 4; return false;
        }
    }
}
