using System;
using System.Collections.Generic;
using DawnOfMan;
using MadrugaShared;
using UnityEngine;

namespace DawnTrainer
{
    public sealed partial class Trainer
    {
        private UnityCanvas uiCanvas;
        private UiModel ui;
        private EntityManager uiCatalogManager;
        private readonly Dictionary<string, UiItem> uiResources = new Dictionary<string, UiItem>();
        private readonly Dictionary<string, UiItem> uiAnimals = new Dictionary<string, UiItem>();
        private bool uiStatusError;

        private void InitInterface()
        {
            skin = Instantiate(GUI.skin);
            skin.font = Font.CreateDynamicFontFromOSFont(new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 16);
            uiCanvas = new UnityCanvas(skin, textures);
            uiCanvas.TraceEvents = isolatedTestSession;
            ui = new UiModel { Version = Version, Action = OnUiAction, Locks = Locks, Preferences = preferences };
        }

        private UiItem CreateUiItem(EntityType type)
        {
            ResourceType resource = type as ResourceType;
            int mask = 0;
            if (resource != null)
            {
                if (type.hasAnyFlag(EntityFlags.Food | EntityFlags.Drink)) mask |= 1;
                if (type.hasAnyFlag(EntityFlags.Material)) mask |= 2;
                if (type.hasAnyFlag(EntityFlags.Tool)) mask |= 4;
                if (type.hasAnyFlag(EntityFlags.Clothing)) mask |= 8;
            }
            return new UiItem { Id = type.getId(), Name = type.getName(), Image = type.getIcon(), Categories = mask };
        }

        private void UpdateUiModel()
        {
            bool ready = Ready();
            if (uiCatalogManager != manager)
            {
                uiCatalogManager = manager; ui.Resources.Clear(); ui.Animals.Clear(); uiResources.Clear(); uiAnimals.Clear();
                if (manager != null)
                {
                    foreach (EntityType type in resources) { UiItem item = CreateUiItem(type); ui.Resources.Add(item); uiResources[item.Id] = item; }
                    foreach (EntityType type in animals) { UiItem item = CreateUiItem(type); ui.Animals.Add(item); uiAnimals[item.Id] = item; }
                }
            }
            ui.Ready = ready; ui.Page = page; ui.Search = search; ui.Category = category; ui.Location = spawnLocation;
            ui.Age = age; ui.Gender = gender; ui.Quantity = quantity; ui.AnimalQuantity = animalQuantity;
            ui.Domestic = domestic; ui.BuildHotkey = buildHotkey; ui.KillHotkey = killHotkey; ui.GiveHotkey = giveHotkey;
            ui.ResourceCount = resources.Count; ui.AnimalCount = animals.Count;
            ui.SelectedResource = selectedResource == null ? null : selectedResource.getId();
            ui.SelectedAnimal = selectedAnimal == null ? null : selectedAnimal.getId();
            ui.ResourceScroll = resourceScroll.y; ui.AnimalScroll = animalScroll.y;
            ui.Status = statusUntil > Time.realtimeSinceStartup ? status : ready ? "准备就绪，修改仅作用于当前地图。" : "修改器已加载，等待进入地图。";
            ui.StatusError = uiStatusError && statusUntil > Time.realtimeSinceStartup;
            ui.Busy = jobs.Count > 0; ui.Done = ui.Total = 0; ui.TaskName = "";
            ui.CompletedName = lastSpawnName; ui.CompletedCount = lastSpawnCount;
            ui.ShowSuccess = lastSpawnUntil > Time.realtimeSinceStartup && !ui.Busy;
            if (ui.Busy) { SpawnJob job = jobs.Peek(); ui.Done = job.Total - job.Remaining; ui.Total = job.Total; ui.TaskName = job.Type.getName(); }
            ui.SelectionCount = ui.BuildTargets = ui.KillTargets = 0; ui.SelectionName = "";
            if (ready)
            {
                ui.Speed = TransientSingleton<TimeManager>.CurrentInstance.getTimeScale();
                ui.Knowledge = TransientSingleton<KnowledgeManager>.CurrentInstance.getKnowledgeAmount();
                ui.Weather = (int)TransientSingleton<TerrainManager>.CurrentInstance.getWeather();
                Selection selection = TransientSingleton<Selection>.CurrentInstance; ui.SelectionCount = selection.getCount();
                if(ui.SelectionCount > 0) ui.SelectionName = selection.getEntity(0).getName();
                for (int i = 0; i < selection.getCount(); i++)
                {
                    PhysicalEntity entity = selection.getEntity(i); Buildable buildable = entity as Buildable; Being being = entity as Being;
                    if (buildable != null && buildable.getTeam() == Team.Player && !buildable.isBuilt() && !buildable.isDestroyed()) ui.BuildTargets++;
                    if (being != null && being.isAlive() && !being.isDestroyed()) ui.KillTargets++;
                }
            }
    UpdateFeatureModel();
        }

        private void ApplyUiModel()
        {
            page = ui.Page; search = ui.Search; category = ui.Category; spawnLocation = ui.Location; age = ui.Age; gender = ui.Gender;
            quantity = ui.Quantity; animalQuantity = ui.AnimalQuantity;
            domestic = ui.Domestic; buildHotkey = ui.BuildHotkey; killHotkey = ui.KillHotkey; giveHotkey = ui.GiveHotkey;
            resourceScroll.y = ui.ResourceScroll; animalScroll.y = ui.AnimalScroll;
            if (ui.SelectedResource != null) selectedResource = resources.Find(t => t.getId() == ui.SelectedResource);
            if (ui.SelectedAnimal != null) selectedAnimal = animals.Find(t => t.getId() == ui.SelectedAnimal);
        }

        private void OnUiAction(string action, string value)
        {
            if (action == "close") { visible = false; inputBlockedUntilFrame = Time.frameCount + 1; GUI.FocusControl(null); return; }
            if (action == "page") { GUI.FocusControl(null); return; }
            if (FeatureUiAction(action, value)) return;
            if (!Ready()) return;
            if (action == "reset") Enqueue(Reset);
            else if (action == "knowledge") { int amount = Int32.Parse(value); Enqueue(delegate { AddKnowledge(amount); }); }
            else if (action == "speed") { float speed = Single.Parse(value, System.Globalization.CultureInfo.InvariantCulture); Enqueue(delegate { SetSpeed(speed); }); }
            else if (action == "pause") Enqueue(TogglePause);
            else if (action == "complete") Enqueue(delegate { CompleteSelected(); });
            else if (action == "kill") Enqueue(delegate { KillSelected(); });
            else if (action == "spawn.resource") Enqueue(QueueResource);
            else if (action == "spawn.animal") Enqueue(QueueAnimal);
            else if (action == "weather")
            {
                int index = Int32.Parse(value);
                Enqueue(delegate { TransientSingleton<SeasonManager>.CurrentInstance.setWeather((Weather)index); Notify("天气已切换为" + new string[] { "晴朗", "降雨", "降雪", "雷暴", "暴风雪" }[index]); });
            }
        }

        private void OnGUI()
        {
            if (uiCanvas == null) InitInterface();
            GUISkin oldSkin = GUI.skin; Matrix4x4 oldMatrix = GUI.matrix; int oldDepth = GUI.depth;
            Color oldColor = GUI.color, oldContent = GUI.contentColor, oldBackground = GUI.backgroundColor; bool oldEnabled = GUI.enabled;
            try
            {
                GUI.skin = skin; GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white; GUI.enabled = true; GUI.depth = -1000;
                if (!visible) return;
                scale = Mathf.Min((Screen.width - 48f) / TrainerView.Width, (Screen.height - 48f) / TrainerView.Height, 1.4f);
                scale = Mathf.Max(.25f, scale); GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
                float width = Screen.width / scale, height = Screen.height / scale;
                window.width = TrainerView.Width; window.height = TrainerView.Height;
                if (window.x <= 0 || window.x + window.width > width - 12) window.x = (width - window.width) / 2;
                if (window.y <= 0 || window.y + window.height > height - 12) window.y = (height - window.height) / 2;
                uiCanvas.Fill(new UiRect(0, 0, width, height), "#07090D55", 0);
                uiCanvas.Fill(new UiRect(window.x + 3, window.y + 10, window.width, window.height), "#02030677", 22);
                UpdateUiModel();
                window = GUI.Window(884184, window, DrawInterfaceWindow, GUIContent.none, GUIStyle.none);
            }
            finally
            {
                GUI.skin = oldSkin; GUI.matrix = oldMatrix; GUI.depth = oldDepth;
                GUI.color = oldColor; GUI.contentColor = oldContent; GUI.backgroundColor = oldBackground; GUI.enabled = oldEnabled;
            }
        }

        private void DrawInterfaceWindow(int id)
        {
            TrainerView.Render(uiCanvas, ui);
            // Unity can invoke a window callback after GUI.Window returns. Commit edits
            // here before the next OnGUI event refreshes the model from runtime state.
            ApplyUiModel();
            GUI.DragWindow(new Rect(12, 12, 970, 80));
        }
    }

    internal sealed class UnityCanvas : UiCanvas
    {
        private readonly GUISkin skin;
        private readonly List<Texture2D> ownedTextures;
        private readonly Dictionary<string, Texture2D> colors = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Texture2D> lines = new Dictionary<string, Texture2D>();
        internal bool TraceEvents;
        private readonly Dictionary<string, GUIStyle> labels = new Dictionary<string, GUIStyle>();
        private readonly GUIStyle inputLeft, inputCenter, scrollTrack, scrollThumb;
        private static Rect Rect(UiRect r) { return new Rect(r.X, r.Y, r.W, r.H); }
        internal UnityCanvas(GUISkin skin, List<Texture2D> owned)
        {
            this.skin = skin; ownedTextures = owned;
            inputLeft = new GUIStyle(skin.textField) { font = skin.font, fontSize = 14, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(4, 4, 0, 0), border = new RectOffset(), margin = new RectOffset(), clipping = TextClipping.Clip };
            inputLeft.normal.textColor = Parse("#F0F1F4"); inputLeft.focused.textColor = inputLeft.normal.textColor;
            inputLeft.normal.background = inputLeft.hover.background = inputLeft.focused.background = null;
            inputCenter = new GUIStyle(inputLeft) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
            scrollTrack = new GUIStyle(skin.verticalScrollbar) { fixedWidth = 5, margin = new RectOffset(8, 0, 0, 0), border = new RectOffset() };
            scrollThumb = new GUIStyle(skin.verticalScrollbarThumb) { border = new RectOffset() };
            scrollTrack.normal.background = Pixel("#15171B"); scrollThumb.normal.background = scrollThumb.hover.background = scrollThumb.active.background = Pixel("#474E5D");
            skin.verticalScrollbar = scrollTrack; skin.verticalScrollbarThumb = scrollThumb;
            skin.verticalScrollbarUpButton = new GUIStyle { fixedHeight = 0 }; skin.verticalScrollbarDownButton = new GUIStyle { fixedHeight = 0 };
        }
        private static Color Parse(string text)
        { Color value; ColorUtility.TryParseHtmlString(text, out value); return value; }
        private Texture2D Pixel(string color)
        {
            Texture2D texture;
            if (!colors.TryGetValue(color, out texture))
            {
                Color value = Parse(color); texture = new Texture2D(1, 1);
                texture.SetPixel(0, 0, new Color(value.r * .55f, value.g * .55f, value.b * .55f, value.a)); texture.Apply();
                colors[color] = texture; ownedTextures.Add(texture);
            }
            return texture;
        }
        internal override void Fill(UiRect r, string color, float radius)
        { if (r.W > 0 && r.H > 0) GUI.DrawTexture(Rect(r), Pixel(color), ScaleMode.StretchToFill, true, 0, Color.white, 0, radius); }
        private GUIStyle Style(int size, string color, bool bold, int align)
        {
            string key = size + ":" + color + ":" + bold + ":" + align; GUIStyle style;
            if (!labels.TryGetValue(key, out style))
            {
                style = new GUIStyle { font = skin.font, fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                    alignment = align == 1 ? TextAnchor.MiddleCenter : align == 2 ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft,
                    wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset() };
                style.normal.textColor = Parse(color); labels[key] = style;
            }
            return style;
        }
        internal override void Text(UiRect r, string text, int size, string color, bool bold, int align)
        { GUI.Label(Rect(r), text ?? "", Style(size, color, bold, align)); }
        internal override void Line(float x1, float y1, float x2, float y2, string color, float width)
        {
            float dx=x2-x1,dy=y2-y1;
            if(Math.Abs(dy)<.001f) { Fill(new UiRect(Math.Min(x1,x2),y1-width/2,Math.Abs(dx),width),color,width/2);return; }
            if(Math.Abs(dx)<.001f) { Fill(new UiRect(x1-width/2,Math.Min(y1,y2),width,Math.Abs(dy)),color,width/2);return; }
            // GUI.Window adds a clip origin. Rotating GUI.matrix around a local pivot
            // clips line glyphs incorrectly on this Unity version; cache local pixels instead.
            string key=dx.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":"+dy.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":"+width.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":"+color;
            Texture2D texture;float padding=width/2+1;
            if(!lines.TryGetValue(key,out texture))
            {
                int w=Mathf.CeilToInt(Math.Abs(dx)+padding*2),h=Mathf.CeilToInt(Math.Abs(dy)+padding*2);
                texture=new Texture2D(w,h,TextureFormat.RGBA32,false);texture.wrapMode=TextureWrapMode.Clamp;
                float ax=padding+Math.Max(0,-dx),ay=padding+Math.Max(0,-dy),lengthSquared=dx*dx+dy*dy;
                Color ink=Parse(color);Color[] pixels=new Color[w*h];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                {
                    float px=x+.5f-ax,py=y+.5f-ay,t=Mathf.Clamp01((px*dx+py*dy)/lengthSquared);
                    float distance=Mathf.Sqrt((px-t*dx)*(px-t*dx)+(py-t*dy)*(py-t*dy));
                    pixels[(h-1-y)*w+x]=new Color(ink.r*.55f,ink.g*.55f,ink.b*.55f,ink.a*Mathf.Clamp01(width/2+.5f-distance));
                }
                texture.SetPixels(pixels);texture.Apply();lines[key]=texture;ownedTextures.Add(texture);
            }
            GUI.DrawTexture(new Rect(Math.Min(x1,x2)-padding,Math.Min(y1,y2)-padding,texture.width,texture.height),texture);
        }
        internal override void Image(UiRect r, object image)
        { Texture texture = image as Texture; if (texture != null) GUI.DrawTexture(Rect(r), texture, ScaleMode.ScaleToFit); }
        internal override bool Hit(UiRect r, string id, bool enabled)
        {
            bool old = GUI.enabled;
            try
            {
                GUI.enabled = enabled;
                EventType before=Event.current.type;bool inside=Rect(r).Contains(Event.current.mousePosition);
                bool clicked=GUI.Button(Rect(r),new GUIContent("",id),GUIStyle.none);
                if(TraceEvents&&inside&&(before==EventType.MouseDown||before==EventType.MouseUp))Debug.Log("[DawnTrainer.UI] "+id+" "+before+" clicked="+clicked+" hot="+GUIUtility.hotControl);
                return clicked;
            }
            finally { GUI.enabled = old; }
        }
        internal override bool Focused(string id) { return GUI.GetNameOfFocusedControl() == "DawnTrainer." + id; }
        internal override bool Hover(UiRect r) { return Event.current != null && Rect(r).Contains(Event.current.mousePosition); }
        internal override string Input(UiRect r, string id, string value, int maximumLength, bool centered)
        { GUI.SetNextControlName("DawnTrainer." + id); return GUI.TextField(Rect(r), value ?? "", maximumLength, centered ? inputCenter : inputLeft); }
        internal override float Slider(UiRect r, string id, float value, float min, float max)
        { return GUI.HorizontalSlider(Rect(r), value, min, max, GUIStyle.none, GUIStyle.none); }
        internal override float BeginScroll(UiRect r, float position, float contentHeight)
        { return GUI.BeginScrollView(Rect(r), new Vector2(0, position), new Rect(0, 0, r.W - 16, Math.Max(r.H, contentHeight)), false, false, GUIStyle.none, scrollTrack).y; }
        internal override void EndScroll() { GUI.EndScrollView(); }
        internal override float Measure(string text, int size, bool bold)
        { return Style(size, TrainerView.TextMain, bold, 0).CalcSize(new GUIContent(text ?? "")).x; }
    }
}
