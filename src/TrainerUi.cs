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
        private Rect panelRect;
        private bool windowPositioned;

        private void InitInterface()
        {
            skin = Instantiate(GUI.skin);
            skin.font = Font.CreateDynamicFontFromOSFont(new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" }, 16);
            uiCanvas = new UnityCanvas(skin, textures);
            uiCanvas.TraceEvents = isolatedTestSession;
            ui = new UiModel { Version = Version, GameVersion = Definitions.VersionNumber.ToString(), Action = OnUiAction, Locks = Locks, Preferences = preferences };
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
            bool ready = WorldReady();
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
            ui.SelectionCount = ui.BuildTargets = ui.KillTargets = 0; ui.SelectionName = ""; ui.KillVillagers = null; selectionSignature = "";
            if (ready)
            {
                ui.Speed = TransientSingleton<TimeManager>.CurrentInstance.getTimeScale();
                ui.Knowledge = TransientSingleton<KnowledgeManager>.CurrentInstance.getKnowledgeAmount();
                ui.Weather = (int)TransientSingleton<TerrainManager>.CurrentInstance.getWeather();
                List<PhysicalEntity> selected = SelectionSnapshot(); ui.SelectionCount = selected.Count;
                if(ui.SelectionCount > 0) ui.SelectionName = selected[0].getName();
                foreach (PhysicalEntity entity in selected)
                {
                    if (CanComplete(entity)) ui.BuildTargets++;
                    if (CanKill(entity)) ui.KillTargets++;
                }
                ui.KillVillagers = SelectedVillagers(); selectionSignature = SelectionSignature();
            }
            // A pending second click expires; a kill confirmation also ends when the selection changes.
            if (ui.ArmedAction != null && (Time.realtimeSinceStartup > armedUntil || (ui.ArmedAction == "kill" && selectionSignature != armedSignature))) ui.ArmedAction = null;
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
            if (action == "close") { visible = false; CancelKillConfirmation(); inputBlockedUntilFrame = Time.frameCount + 1; GUI.FocusControl(null); return; }
            if (action == "page") { CancelKillConfirmation(); GUI.FocusControl(null); return; }
            if (action == "confirm.arm") { armedUntil = Time.realtimeSinceStartup + 4; armedSignature = selectionSignature; return; }
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
                // Hotkeys only act while the panel is hidden, so their results are shown as a short toast.
                if (visible) toastUntil = 0;
                else if (toastUntil <= Time.realtimeSinceStartup) return;
                // Layout stays in panel units; the canvas scales positions and font sizes, so text is rasterized at its final size.
                GUI.matrix = Matrix4x4.identity;
                scale = FeaturePolicy.PanelScale(Screen.width, Screen.height, TrainerView.Width, TrainerView.Height, preferences.UiScale);
                uiCanvas.SetScale(scale);
                float width = Screen.width / scale, height = Screen.height / scale;
                if (!visible) { TrainerView.RenderToast(uiCanvas, width, status, uiStatusError); return; }
                window.width = TrainerView.Width; window.height = TrainerView.PanelHeight(page);
                if (!windowPositioned) { window.x = (width - window.width) / 2; window.y = (height - window.height) / 2; windowPositioned = true; }
                window.x = FeaturePolicy.ClampPanelPosition(window.x, width, window.width);
                window.y = FeaturePolicy.ClampPanelPosition(window.y, height, window.height);
                // No full-screen dimming: the world around the panel stays visible and clickable.
                uiCanvas.Fill(new UiRect(window.x + 3, window.y + 10, window.width, window.height), "#02030677", 22);
                // If game state cannot be read this event, keep drawing the last model with the error.
                try { UpdateUiModel(); }
                catch (Exception ex) { Report(ex); ui.Status = status; ui.StatusError = true; }
                panelRect = GUI.Window(884184, uiCanvas.Scaled(new UiRect(window.x, window.y, window.width, window.height)), DrawInterfaceWindow, GUIContent.none, GUIStyle.none);
                window.x = panelRect.x / scale; window.y = panelRect.y / scale;
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
            GUI.DragWindow(uiCanvas.Scaled(new UiRect(12, 12, 970, 80)));
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
        private GUIStyle inputLeft, inputCenter, scrollTrack, scrollThumb;
        private float scale;
        internal UnityCanvas(GUISkin skin, List<Texture2D> owned) { this.skin = skin; ownedTextures = owned; SetScale(1); }
        // Panel units map to whole screen pixels; styles and stroke textures are rebuilt when the scale changes.
        internal void SetScale(float value)
        {
            if (value == scale) return;
            scale = value; labels.Clear();
            foreach (Texture2D texture in lines.Values) { ownedTextures.Remove(texture); UnityEngine.Object.Destroy(texture); }
            lines.Clear();
            inputLeft = new GUIStyle(skin.textField) { font = skin.font, fontSize = Px(14), alignment = TextAnchor.MiddleLeft, padding = new RectOffset(Px(4), Px(4), 0, 0), border = new RectOffset(), margin = new RectOffset(), clipping = TextClipping.Clip };
            inputLeft.normal.textColor = Parse("#F0F1F4"); inputLeft.focused.textColor = inputLeft.normal.textColor;
            inputLeft.normal.background = inputLeft.hover.background = inputLeft.focused.background = null;
            inputCenter = new GUIStyle(inputLeft) { alignment = TextAnchor.MiddleCenter, fontSize = Px(16) };
            scrollTrack = new GUIStyle(skin.verticalScrollbar) { fixedWidth = Px(5), margin = new RectOffset(Px(8), 0, 0, 0), border = new RectOffset() };
            scrollThumb = new GUIStyle(skin.verticalScrollbarThumb) { border = new RectOffset() };
            scrollTrack.normal.background = Pixel("#15171B"); scrollThumb.normal.background = scrollThumb.hover.background = scrollThumb.active.background = Pixel("#474E5D");
            skin.verticalScrollbar = scrollTrack; skin.verticalScrollbarThumb = scrollThumb;
            skin.verticalScrollbarUpButton = new GUIStyle { fixedHeight = 0 }; skin.verticalScrollbarDownButton = new GUIStyle { fixedHeight = 0 };
        }
        private int Px(float value) { return Mathf.Max(1, Mathf.RoundToInt(value * scale)); }
        internal Rect Scaled(UiRect r)
        {
            float x = Mathf.Round(r.X * scale), y = Mathf.Round(r.Y * scale);
            return new Rect(x, y, Mathf.Round((r.X + r.W) * scale) - x, Mathf.Round((r.Y + r.H) * scale) - y);
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
        { if (r.W > 0 && r.H > 0) Paint(Scaled(r), color, radius * scale); }
        private void Paint(Rect r, string color, float radius)
        { GUI.DrawTexture(r, Pixel(color), ScaleMode.StretchToFill, true, 0, Color.white, 0, radius); }
        private GUIStyle Style(int size, string color, bool bold, int align)
        {
            string key = size + ":" + color + ":" + bold + ":" + align; GUIStyle style;
            if (!labels.TryGetValue(key, out style))
            {
                style = new GUIStyle { font = skin.font, fontSize = Px(size), fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                    alignment = align == 1 ? TextAnchor.MiddleCenter : align == 2 ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft,
                    wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset() };
                style.normal.textColor = Parse(color); labels[key] = style;
            }
            return style;
        }
        internal override void Text(UiRect r, string text, int size, string color, bool bold, int align)
        { GUI.Label(Scaled(r), text ?? "", Style(size, color, bold, align)); }
        internal override void Line(float x1, float y1, float x2, float y2, string color, float width)
        {
            // Axis-aligned strokes snap to whole pixels and stay at least one pixel thick.
            float stroke = Mathf.Max(1, Mathf.Round(width * scale));
            if (Math.Abs(y2 - y1) < .001f)
            {
                float left = Mathf.Round(Math.Min(x1, x2) * scale), top = Mathf.Round(y1 * scale - stroke / 2);
                Paint(new Rect(left, top, Mathf.Round(Math.Max(x1, x2) * scale) - left, stroke), color, stroke / 2); return;
            }
            if (Math.Abs(x2 - x1) < .001f)
            {
                float left = Mathf.Round(x1 * scale - stroke / 2), top = Mathf.Round(Math.Min(y1, y2) * scale);
                Paint(new Rect(left, top, stroke, Mathf.Round(Math.Max(y1, y2) * scale) - top), color, stroke / 2); return;
            }
            // GUI.Window adds a clip origin. Rotating GUI.matrix around a local pivot
            // clips line glyphs incorrectly on this Unity version; cache screen-pixel strokes instead.
            x1*=scale;y1*=scale;x2*=scale;y2*=scale;
            float dx=Mathf.Round((x2-x1)*64)/64,dy=Mathf.Round((y2-y1)*64)/64;width=Mathf.Round(width*scale*64)/64;
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
        { Texture texture = image as Texture; if (texture != null) GUI.DrawTexture(Scaled(r), texture, ScaleMode.ScaleToFit); }
        internal override bool Hit(UiRect r, string id, bool enabled)
        {
            bool old = GUI.enabled;
            try
            {
                GUI.enabled = enabled; Rect area = Scaled(r);
                EventType before=Event.current.type;bool inside=area.Contains(Event.current.mousePosition);
                bool clicked=GUI.Button(area,new GUIContent("",id),GUIStyle.none);
                if(TraceEvents&&inside&&(before==EventType.MouseDown||before==EventType.MouseUp))Debug.Log("[DawnTrainer.UI] "+id+" "+before+" clicked="+clicked+" hot="+GUIUtility.hotControl);
                return clicked;
            }
            finally { GUI.enabled = old; }
        }
        internal override bool Focused(string id) { return GUI.GetNameOfFocusedControl() == "DawnTrainer." + id; }
        internal override bool Hover(UiRect r) { return Event.current != null && Scaled(r).Contains(Event.current.mousePosition); }
        internal override string Input(UiRect r, string id, string value, int maximumLength, bool centered)
        { GUI.SetNextControlName("DawnTrainer." + id); return GUI.TextField(Scaled(r), value ?? "", maximumLength, centered ? inputCenter : inputLeft); }
        internal override float Slider(UiRect r, string id, float value, float min, float max)
        { return GUI.HorizontalSlider(Scaled(r), value, min, max, GUIStyle.none, GUIStyle.none); }
        internal override float BeginScroll(UiRect r, float position, float contentHeight)
        {
            Rect view = Scaled(r), content = new Rect(0, 0, view.width - Px(16), Mathf.Max(view.height, Mathf.Round(contentHeight * scale)));
            return GUI.BeginScrollView(view, new Vector2(0, position * scale), content, false, false, GUIStyle.none, scrollTrack).y / scale;
        }
        internal override void EndScroll() { GUI.EndScrollView(); }
        internal override float Measure(string text, int size, bool bold)
        { return Style(size, TrainerView.TextMain, bold, 0).CalcSize(new GUIContent(text ?? "")).x / scale; }
    }
}
