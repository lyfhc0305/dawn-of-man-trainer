using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DawnOfMan;
using MadrugaShared;
using UnityEngine;

namespace DawnTrainer
{
    // All game work runs on Unity's main thread. No remote endpoints or injection service.
    public sealed partial class Trainer : MonoBehaviour
    {
        public const string Version = "1.3.1";
        private static Trainer instance;
        internal static readonly bool[] Locks = new bool[7];
        private static readonly HashSet<Stat> lockedStats = new HashSet<Stat>();
        private static readonly FieldInfo gameReady = typeof(GameManager).GetField("mReady", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo gameState = typeof(GameManager).GetField("mGameState", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly string[] statNames = { "无限健康", "无限饱食", "无限口渴", "无限温度", "无限体力", "无限疲劳", "无限精神" };
        private static readonly string[] statEnglish = { "Health", "Nutrition", "Hydration", "Temperature", "Stamina", "Rest", "Morale" };
        private readonly Queue<Action> actions = new Queue<Action>();
        private EntityManager manager;
        private readonly List<EntityType> resources = new List<EntityType>();
        private readonly List<EntityType> animals = new List<EntityType>();
        private readonly Queue<SpawnJob> jobs = new Queue<SpawnJob>();
        private bool visible, domestic, buildHotkey, killHotkey, giveHotkey;
        private int page, category, spawnLocation, gender = 2, age = 1;
        private string search = "", quantity = "10", animalQuantity = "1";
        private EntityType selectedResource, selectedAnimal;
        private Vector2 resourceScroll, animalScroll;
        private Rect window = new Rect(0, 0, 1180, 800);
        private float scale = 1, statusUntil;
        private string status = "F11 显示 / 隐藏面板";
        private string lastSpawnName = "";
        private int lastSpawnCount;
        private float lastSpawnUntil;
        private string lastError;
        private float lastErrorTime;
        private GUISkin skin;
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private bool diagnostic, isolatedTestSession;
        private float diagnosticStart;
        private int diagnosticPhase;
        private int inputBlockedUntilFrame;

        private sealed class SpawnJob
        {
            public EntityManager Manager;
            public EntityType Type;
            public Vector3 Position;
            public int Remaining, Total;
            public Age Age;
            public Gender Gender;
        }

        public static void Bootstrap()
        {
            if (instance != null) return;
            try
            {
                GameObject root = new GameObject("DawnOfManTrainer");
                DontDestroyOnLoad(root);
                instance = root.AddComponent<Trainer>();
                Debug.Log("[DawnTrainer] Loaded " + Version + "; F11 toggles panel");
            }
            catch (Exception ex) { Debug.LogError("[DawnTrainer] Bootstrap: " + ex); }
        }

        private void Awake()
        {
            instance = this;
            diagnostic = Array.IndexOf(Environment.GetCommandLineArgs(), "--trainer-self-test") >= 0;
            isolatedTestSession = diagnostic;
            LoadPreferences();
            useGUILayout = true;
            diagnosticStart = Time.realtimeSinceStartup;
        }

        // Called before the game's Update, so opening the overlay blocks game input immediately.
        public static void Frame()
        {
            if (instance == null) Bootstrap();
            if (instance == null) return;
            try { instance.Tick(); }
            catch (Exception ex) { instance.Report(ex); }
        }

        public static bool BlockInput() { return instance != null && (instance.visible || Time.frameCount <= instance.inputBlockedUntilFrame); }
        public static bool IsLocked(Stat stat) { return stat != null && lockedStats.Contains(stat); }

        internal static bool Ready()
        {
            GameManager gm = Singleton<GameManager>.Instance;
            return gm.getCurrentGameStateId() == "GameStateGame" && (bool)gameReady.GetValue(gm);
        }

        private void Tick()
        {
            SavePreferences(false);
            bool captured = CaptureShortcut();
            if (!captured && Input.GetKeyDown(KeyCode.F11)) { visible = !visible; inputBlockedUntilFrame = Time.frameCount + 1; GUI.FocusControl(null); }
            if (!captured && visible && Input.GetKeyDown(KeyCode.Escape)) { visible = false; inputBlockedUntilFrame = Time.frameCount + 1; GUI.FocusControl(null); }
            if (diagnostic) DiagnosticTick();
            if (!Ready())
            {
                overlayPause.Clear(); autoPauseRequested = false;
                lastSpawnUntil = 0;
                lockedStats.Clear();
                jobs.Clear();
                actions.Clear();
                if (manager != null) { manager = null; resources.Clear(); animals.Clear(); }
                return;
            }
            EntityManager current = TransientSingleton<EntityManager>.CurrentInstance;
            if (isolatedTestSession) typeof(SavegameManager).GetField("mAutosaveTimer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Singleton<SavegameManager>.Instance, 0f);
            if (manager != current)
            {
                overlayPause.Clear(); autoPauseRequested = false;
                float initialSpeed = TransientSingleton<TimeManager>.CurrentInstance.getTimeScale();
                lastRunningSpeed = initialSpeed > 0 ? initialSpeed : 1;
                lastSpawnUntil = 0;
                manager = current;
                lockedStats.Clear(); jobs.Clear(); actions.Clear();
                resources.Clear(); animals.Clear();
                EntityTypeManager types = TransientSingleton<EntityTypeManager>.CurrentInstance;
                resources.AddRange(types.getTypeEntities<ResourceType>() ?? new List<EntityType>());
                animals.AddRange(types.getTypeEntities<AnimalType>() ?? new List<EntityType>());
                selectedResource = resources.Find(t => t.getId() == preferences.Resource) ?? (resources.Count > 0 ? resources[0] : null);
                selectedAnimal = animals.Find(t => t.getId() == preferences.Animal) ?? (animals.Count > 0 ? animals[0] : null);
                Debug.Log("[DawnTrainer] Catalog: " + resources.Count + " resources; " + animals.Count + " animals");
            }
            SyncOverlayPause();
            RefreshLocks();
            while (actions.Count > 0) { try { actions.Dequeue()(); } catch (Exception ex) { Report(ex); } }
            HandleHotkeys();
            ProcessJobs();
        }

        private void HandleHotkeys()
        {
            GameCamera camera = TransientSingleton<GameCamera>.CurrentInstance;
            TimeManager time = TransientSingleton<TimeManager>.CurrentInstance;
            bool allowed = TrainerPolicy.AllowsHotkeys(Ready(), BlockInput(),
                camera != null && camera.isInteractive() && !camera.isTransitioning(),
                time == null || time.isRealPaused(), Singleton<GuiManager>.Instance.anyModalPanels(),
                GUIUtility.keyboardControl != 0);
            if (!allowed) return;
            bool modified = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)
                || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            TrainerCommand command = TrainerPolicy.SelectCommand(allowed, modified, buildHotkey, killHotkey, giveHotkey,
                ActionKeyDown(0), ActionKeyDown(1), ActionKeyDown(2));
            if (command == TrainerCommand.None) return;
            // The game's original input handlers run after Frame(). Consume only this frame.
            inputBlockedUntilFrame = Time.frameCount;
            switch (command)
            {
                case TrainerCommand.CompleteSelection: CompleteSelected(); break;
                case TrainerCommand.KillSelection: KillSelected(); break;
                case TrainerCommand.GiveResource: QueueResource(); break;
            }
        }

        internal void RefreshLocks()
        {
            lockedStats.Clear();
            if (manager == null) return;
            List<Human> humans = manager.getPlayerHumans();
            if (humans != null) foreach (Human human in humans) Protect(human);
            if (domestic)
            {
                List<Entity> list = manager.getTypeEntities<Animal>(Team.Player);
                if (list != null) foreach (Entity entity in list) Protect(entity as Being);
            }
        }

        private void Protect(Being being)
        {
            if (being == null || being.isDestroyed() || !being.isAlive()) return;
            AddLock(being.getHealth(), 0); AddLock(being.getNutrition(), 1);
            AddLock(being.getHydration(), 2); AddLock(being.getTemperature(), 3); AddLock(being.getStamina(), 4);
            Human human = being as Human;
            if (human != null) { AddLock(human.getRest(), 5); AddLock(human.getMorale(), 6); }
        }

        private static void AddLock(Stat stat, int index)
        {
            if (Locks[index] && stat != null) { lockedStats.Add(stat); stat.setValue(1f); }
        }

        private void Enqueue(Action action) { actions.Enqueue(action); }
        private void Notify(string message)
        {
            uiStatusError = false;
            status = message; statusUntil = Time.realtimeSinceStartup + 8;
            Debug.Log("[DawnTrainer] " + message);
        }
        private void Report(Exception ex)
        {
            lastSpawnUntil = 0;
            uiStatusError = true;
            string message = ex.GetType().Name + ": " + ex.Message;
            status = "操作未完成：" + ex.Message; statusUntil = Time.realtimeSinceStartup + 10;
            if (message != lastError || Time.realtimeSinceStartup - lastErrorTime > 10)
            { Debug.LogError("[DawnTrainer] " + ex); lastError = message; lastErrorTime = Time.realtimeSinceStartup; }
        }

        internal int CompleteSelected()
        {
            Selection selection = TransientSingleton<Selection>.CurrentInstance;
            List<Buildable> targets = new List<Buildable>();
            for (int i = 0; i < selection.getCount(); i++)
            {
                Buildable buildable = selection.getEntity(i) as Buildable;
                if (buildable != null && buildable.getTeam() == Team.Player && !buildable.isBuilt() && !buildable.isDestroyed()) targets.Add(buildable);
            }
            int completed = 0;
            KnowledgeManager knowledge = TransientSingleton<KnowledgeManager>.CurrentInstance;
            foreach (Buildable target in targets)
            {
                if (TrainerPolicy.CompleteBuild(target.isBuilt(),
                    delegate { return target.increaseBuildProgress(1f - target.getBuildProgress()); },
                    delegate { knowledge.onBuildableBuilt(target); })) completed++;
            }
            Notify(completed > 0 ? "已完成 " + completed + " 个选中建筑 / 运输工具" : "请先关闭面板，选中未完工的建筑");
            return completed;
        }

        internal int KillSelected()
        {
            Selection selection = TransientSingleton<Selection>.CurrentInstance;
            List<Being> targets = new List<Being>();
            for (int i = 0; i < selection.getCount(); i++)
            {
                Being being = selection.getEntity(i) as Being;
                if (being != null && being.isAlive() && !being.isDestroyed()) targets.Add(being);
            }
            foreach (Being target in targets) { target.getHealth().setValue(0f); target.triggerDeath(DeathType.Killed); }
            Notify(targets.Count > 0 ? "已杀死 " + targets.Count + " 个选中单位" : "请先选中活着的人物或动物");
            return targets.Count;
        }

        internal void AddKnowledge(int amount)
        {
            TransientSingleton<KnowledgeManager>.CurrentInstance.addKnowledge("修改器：添加知识点", amount, null, Vector3.zero);
            Notify("已添加 " + amount + " 知识点");
        }

        private Vector3 SpawnPosition()
        {
            Selection selection = TransientSingleton<Selection>.CurrentInstance;
            if (spawnLocation == 0 && selection.getCount() > 0)
                return selection.getEntity(0).getPosition();
            Camera camera = TransientSingleton<GameCamera>.CurrentInstance.getCameraComponent();
            RaycastHit hit;
            if (Physics.Raycast(camera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0)), out hit, 2000f, TerrainConstants.TerrainLayerMask))
                return hit.point;
            throw new InvalidOperationException("画面中心未对准地面，请调整镜头或先选中一个单位");
        }

        private int ReadQuantity(string text, int maximum)
        {
            int amount;
            if (!Int32.TryParse(text, out amount) || amount < 1 || amount > maximum)
                throw new InvalidOperationException("数量请输入 1–" + maximum + " 的整数");
            return amount;
        }

        private void QueueResource()
        {
            if (selectedResource == null) return;
            QueueSpawn(selectedResource, ReadQuantity(quantity, 1000), SpawnPosition(), Age.Count, Gender.Count);
        }

        private void QueueAnimal()
        {
            if (selectedAnimal == null) return;
            QueueSpawn(selectedAnimal, ReadQuantity(animalQuantity, 50), SpawnPosition(), (Age)age, gender == 2 ? Gender.Count : (Gender)gender);
        }

        internal void QueueSpawn(EntityType type, int amount, Vector3 position, Age spawnAge, Gender spawnGender)
        {
            if (jobs.Count > 0) throw new InvalidOperationException("请等当前添加操作完成");
            lastSpawnUntil = 0;
            batchTotal = amount; batchDone = 0; batchLabel = type.getName();
            jobs.Enqueue(new SpawnJob { Manager = manager, Type = type, Position = position, Remaining = amount, Total = amount, Age = spawnAge, Gender = spawnGender });
            Notify("正在添加 " + type.getName() + " × " + amount);
        }

        private void ProcessJobs()
        {
            if (jobs.Count == 0) return;
            SpawnJob job = jobs.Peek();
            if (manager != job.Manager) { jobs.Clear(); return; }
            try
            {
                int batch = Math.Min(job.Type is AnimalType ? 2 : 12, job.Remaining);
                for (int i = 0; i < batch; i++)
                {
                    Vector2 offset = UnityEngine.Random.insideUnitCircle * (job.Type is AnimalType ? 4f : 1.6f);
                    Vector3 position = job.Position + new Vector3(offset.x, 0, offset.y);
                    ResourceType resource = job.Type as ResourceType;
                    if (resource != null)
                    {
                        Resource created = manager.createResourceOnFloor(resource, position);
                        TransientSingleton<KnowledgeManager>.CurrentInstance.onResourceAcquired(resource, created.getPosition());
                    }
                    else
                    {
                        Animal created = manager.createEntityOnFloor<Animal>(job.Type, new EntityParameters(position, Quaternion.identity, job.Age, job.Gender));
                        if (created.getTeam() == Team.Player) TransientSingleton<KnowledgeManager>.CurrentInstance.onNewResidentAnimal(created);
                        created.onSpawn();
                    }
                    job.Remaining--; batchDone++;
                }
                if (job.Remaining == 0)
                {
                    jobs.Dequeue();
                    TransientSingleton<GameGuiManager>.CurrentInstance.requestRefresh();
                    if (jobs.Count == 0)
                    {
                        lastSpawnName = batchLabel; lastSpawnCount = batchTotal; lastSpawnUntil = Time.realtimeSinceStartup + 6;
                        Notify("已添加 " + batchLabel + " × " + batchTotal + "，已在地图地面生成");
                    }
                }
            }
            catch { jobs.Clear(); throw; }
        }

        private void Reset()
        {
            lastSpawnUntil = 0;
            Array.Clear(Locks, 0, Locks.Length); lockedStats.Clear(); domestic = buildHotkey = killHotkey = giveHotkey = false;
            jobs.Clear(); SetSpeed(1);
            Notify("所有持续开关已关闭，速度恢复 1 倍");
        }

        private void OnDestroy()
        {
            SavePreferences(true);
            lockedStats.Clear(); instance = null;
            foreach (Texture2D texture in textures) Destroy(texture);
            if (skin != null) { if (skin.font != null) Destroy(skin.font); Destroy(skin); }
        }

        private void DiagnosticTick()
        {
            // Explicit opt-in integration checks in a new, unsaved world only.
            if (Time.realtimeSinceStartup - diagnosticStart > 180) { Debug.LogError("[DawnTrainer.Test] TIMEOUT"); diagnostic = false; return; }
            GameManager gm = Singleton<GameManager>.Instance;
            if (diagnosticPhase == 0 && gm.getCurrentGameStateId() == "GameStateTitle" && (bool)gameReady.GetValue(gm))
            {
                diagnosticPhase = 1;
                string scenario = Scenario.getScenarioPath("continental_dawn");
                gm.setState(new GameStateGame(new GameParameters(scenario, 0, "TrainerTest", GameModeType.Normal, StartMode.Settled, null)));
                Debug.Log("[DawnTrainer.Test] Loading temporary unsaved world: " + scenario);
            }
            else if (diagnosticPhase == 1 && gm.getCurrentGameStateId() == "GameStateGame")
            {
                GameState state = (GameState)gameState.GetValue(gm);
                if (state.isLoaded() && !(bool)gameReady.GetValue(gm)) { state.onReady(); gameReady.SetValue(gm, true); }
                if (Ready() && manager != null)
                {
                    diagnosticPhase = 2;
                    Enqueue(delegate { IntegrationChecks.Run(this, manager); });
                }
            }
            else if (diagnosticPhase == 2)
            {
                IntegrationChecks.Poll(this, manager);
                if (IntegrationChecks.Finished)
                {
                    diagnostic = false; visible = true; page = 5;
                    Reset(); TransientSingleton<TimeManager>.CurrentInstance.fakePause();
                    Notify("自检完成；当前为未保存的测试地图");
                }
            }
        }
    }
}
