using System;
using System.Collections.Generic;

namespace DawnTrainer
{
    // One layout is used by Unity and by the offline PNG preview exporter.
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
        internal string Version = "1.4.0", Search = "", Quantity = "10", AnimalQuantity = "1", Status = "准备就绪";
        internal string GameVersion = "";
        internal bool Ready, Domestic, BuildHotkey, KillHotkey, GiveHotkey, Busy, StatusError;
        internal bool[] Locks = new bool[7];
        internal int Page, Category, Location, Age = 1, Gender = 2, Knowledge, SelectionCount, BuildTargets, KillTargets;
        internal int ResourceCount, AnimalCount, Done, Total, Weather;
        internal int StorageMultiplier = 1, BuildMultiplier = 1, CarryMultiplier = 1, TransportMultiplier = 1;
        internal float Speed = 1, ResourceScroll, AnimalScroll;
        internal string SelectedResource, SelectedAnimal;
        internal string ResourceSearch = "", AnimalSearch = "", SelectionName = "", TaskName = "", CompletedName = "";
        internal int CompletedCount;
        internal bool ShowSuccess;
        internal void Navigate(int next)
        {
            if(next==Page)return;
            if(Page==1)ResourceSearch=Search;else if(Page==2)AnimalSearch=Search;
            Page=next;Search=next==1?ResourceSearch:next==2?AnimalSearch:"";ArmedAction=null;
            Emit("page",next.ToString());
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

    internal static partial class TrainerView
    {
        internal const float Width = 1180, Height = 800, Sidebar = 216;
        internal static int PanelHeight(int page) { return page==8?640:800; }
        internal const string Background = "#17181B", Side = "#121316", Surface = "#1D1F23", Border = "#303238";
        internal const string TextMain = "#F0F1F4", Muted = "#B1B8C5", Quiet = "#969FAD", Accent = "#A8B7FF";
        private const float MainX = 244, MainW = 908;
        private static readonly string[] Pages = { "基础功能", "添加资源", "产生动物", "世界控制", "使用说明", "选中目标", "物资包", "偏好设置", "效率倍率" };
        private static readonly string[] Descriptions = {
            "管理人物状态、游戏速度与选中目标。", "找到所需物品，按数量添加到地图。", "选择物种、年龄与性别，在地图中生成动物。",
            "调整流速、天气，邀请移民与商人。", "快捷键、添加位置与常用操作。",
            "管理选中的人物、动物和建筑，可直接在面板外点选。", "保存常用清单，一次添加多种资源。", "快捷键、自动暂停和个人偏好。",
            "分别调整仓储、施工和搬运效率，按整数设置倍率。"
        };
        private static readonly string[] StatNames = { "健康", "饱食", "口渴", "温度", "体力", "休息", "精神" };

        private static UiRect R(float x, float y, float w, float h) { return new UiRect(x, y, w, h); }
        private static void Label(UiCanvas c, float x, float y, float w, float h, string text, int size, string color)
        { c.Text(R(x, y, w, h), text, Math.Max(13,size), color, false, 0); }
        private static void Card(UiCanvas c, UiRect r, string fill)
        { c.Fill(r, Border, 13); c.Fill(r.Inset(1), fill, 12); }
        private static string Short(UiCanvas c, string text, float width, int size)
        {
            if (String.IsNullOrEmpty(text) || c.Measure(text, size, false) <= width) return text ?? "";
            while (text.Length > 0 && c.Measure(text + "…", size, false) > width) text = text.Substring(0, text.Length - 1);
            return text + "…";
        }
        private static void Rule(UiCanvas c, float x, float y, float w) { c.Line(x, y, x + w, y, Border, 1); }
        private static void Key(UiCanvas c, UiRect r, string text)
        { c.Fill(r, "#30333B", 5); c.Text(r, text, 12, Muted, false, 1); }
        private static void Pill(UiCanvas c, UiRect r, string text, bool active)
        { c.Fill(r, active ? "#29334C" : "#27292F", r.H / 2); c.Text(r, text, 12, active ? Accent : Muted, false, 1); }

        private static bool Button(UiCanvas c, UiRect r, string id, string label, int style, bool enabled)
        {
            bool hover = enabled && c.Hover(r);
            string fill = style == 1 ? "#DFE5FF" : style == 2 ? "#382429" : "#292C33";
            string ink = style == 1 ? "#1A2341" : style == 2 ? "#F2A3AA" : TextMain;
            if (!enabled) { fill = "#24262C"; ink = Quiet; }
            else if (hover) fill = style == 1 ? "#F0F3FF" : style == 2 ? "#4A2B33" : "#373B44";
            c.Fill(r, fill, 8);
            c.Text(r, label, 14, ink, style == 1, 1);
            return c.Hit(r, id, enabled);
        }
        // A destructive button that needs confirmation runs on its second click; the runtime expires the armed state.
        private static bool Confirmed(UiModel m, string id, bool needed, bool clicked)
        {
            if (!clicked || !needed) return clicked;
            if (m.ArmedAction == id) { m.ArmedAction = null; return true; }
            m.ArmedAction = id; m.Emit("confirm.arm", id); return false;
        }
        private static bool Switch(UiCanvas c, UiRect r, string id, bool value, UiRect? hitArea = null)
        {
            c.Fill(r, value ? "#A8B7FF" : c.Hover(r) ? "#454B58" : "#383D47", r.H / 2);
            c.Fill(R(r.X + (value ? r.W - r.H + 3 : 3), r.Y + 3, r.H - 6, r.H - 6), value ? "#18213B" : "#B5BAC5", (r.H - 6) / 2);
            return c.Hit(hitArea ?? r, id, true) ? !value : value;
        }
        private static bool Chip(UiCanvas c, UiRect r, string id, string text, bool selected)
        {
            c.Fill(r, selected ? "#303950" : c.Hover(r) ? "#292D35" : "#21242A", 8);
            c.Text(r, text, 14, selected ? "#C7D1FF" : Muted, false, 1);
            return c.Hit(r, id, true);
        }
        private static void Dot(UiCanvas c, float x, float y, string color) { c.Fill(R(x, y, 6, 6), color, 3); }
        private static void Glyph(UiCanvas c, string name, float x, float y, float size, string color)
        {
            float s = size / 24f;
            Action<float, float, float, float> l = (a, b, d, e) => c.Line(x + a * s, y + b * s, x + d * s, y + e * s, color, 1.5f);
            Action<float, float, float> circle = (a, b, radius) => {
                c.Fill(R(x + (a - radius) * s, y + (b - radius) * s, radius * 2 * s, radius * 2 * s), color, radius * s);
                c.Fill(R(x + (a - radius) * s + 1.5f, y + (b - radius) * s + 1.5f, radius * 2 * s - 3, radius * 2 * s - 3), Surface, Math.Max(0, radius * s - 1.5f));
            };
            if (name == "basic")
            {
                foreach (int a in new int[] { 3, 14 }) foreach (int b in new int[] { 3, 14 })
                { l(a, b, a + 7, b); l(a + 7, b, a + 7, b + 7); l(a + 7, b + 7, a, b + 7); l(a, b + 7, a, b); }
            }
            else if (name == "box") { l(3, 7, 12, 3); l(12, 3, 21, 7); l(21, 7, 12, 11); l(12, 11, 3, 7); l(3, 7, 3, 17); l(3, 17, 12, 21); l(12, 21, 21, 17); l(21, 17, 21, 7); l(12, 11, 12, 21); }
            else if (name == "paw") { c.Fill(R(x + 7*s,y + 12*s,10*s,8*s),color,4*s); foreach (float a in new float[]{3,8,14,19}) c.Fill(R(x+a*s,y+(a==3||a==19?7:3)*s,4*s,6*s),color,2*s); }
            else if (name == "search") { circle(10, 10, 6); l(15, 15, 21, 21); }
            else if (name == "close") { l(6, 6, 18, 18); l(6, 18, 18, 6); }
            else if (name == "check") { l(5, 12, 10, 17); l(10, 17, 20, 6); }
            else if (name == "plus") { l(5, 12, 19, 12); l(12, 5, 12, 19); }
            else if (name == "help") { circle(12,12,9); l(12,10,12,17); c.Fill(R(x+11*s,y+6*s,2*s,2*s),color,s); }
            else if (name == "sun") { circle(12,12,4); foreach(int a in new int[]{0,1,2,3,4,5,6,7}) {double t=a*Math.PI/4; l(12+(float)Math.Cos(t)*8,12+(float)Math.Sin(t)*8,12+(float)Math.Cos(t)*11,12+(float)Math.Sin(t)*11);} }
            else if (name == "rain" || name == "snow" || name == "storm" || name == "blizzard")
            {
                l(4,13,3,10); l(3,10,5,7); l(5,7,9,7); l(9,7,12,3); l(12,3,17,4); l(17,4,19,8); l(19,8,22,10); l(22,10,21,13); l(21,13,4,13);
                if(name=="storm") {l(13,14,9,19);l(9,19,14,19);l(14,19,11,23);}
                else { l(6,16,4,20); l(13,16,11,20);l(20,16,18,20); if(name!="rain"){l(3,18,7,18);l(10,18,14,18);l(17,18,21,18);}if(name=="blizzard"){l(0,4,7,4);l(-2,10,2,10);l(15,0,23,0);} }
            }
            else if (name == "arrow") { l(5,12,19,12);l(14,7,19,12);l(19,12,14,17); }
            else if (name == "target") { l(12,2,12,8);l(12,16,12,22);l(2,12,8,12);l(16,12,22,12);l(9,9,15,9);l(15,9,15,15);l(15,15,9,15);l(9,15,9,9); }
            else if (name == "pack") { l(8,3,16,3);l(8,3,8,12);l(16,3,16,12);l(3,12,21,12);l(3,21,21,21);l(3,12,3,21);l(12,12,12,21);l(21,12,21,21); }
            else if (name == "sliders")
            {
                l(3,6,21,6);l(3,12,21,12);l(3,18,21,18);
                foreach (float[] knob in new float[][] { new float[] { 8, 6 }, new float[] { 16, 12 }, new float[] { 10, 18 } })
                    c.Fill(R(x+(knob[0]-2.5f)*s,y+(knob[1]-2.5f)*s,5*s,5*s),color,2.5f*s);
            }
        }

        internal static void Render(UiCanvas c, UiModel m)
        {
            int height=PanelHeight(m.Page);
            c.Fill(R(0,0,Width,height), "#363941", 20);
            c.Fill(R(1,1,Width-2,height-2), Background, 19);
            c.Fill(R(1,1,Sidebar-1,height-2), Side, 19);
            c.Fill(R(Sidebar-20,1,20,height-2), Side, 0);
            c.Line(Sidebar,1,Sidebar,height-1,"#292B31",1);
            DrawSidebar(c,m);
            DrawHeader(c,m);
            if (!m.Ready && m.Page!=4 && m.Page!=6 && m.Page!=7 && m.Page!=8) DrawEmptyWorld(c);
            else if (m.Page == 0) DrawOverview(c,m);
            else if (m.Page == 1 || m.Page == 2) DrawCatalog(c,m,m.Page==2);
            else if (m.Page == 3) DrawWorld(c,m);
            else if (m.Page == 5) DrawTargets(c,m);
            else if (m.Page == 6) DrawPacks(c,m);
            else if (m.Page == 7) DrawPreferences(c,m);
            else if (m.Page == 8) DrawMultipliers(c,m);
            else DrawHelp(c,m);
            DrawStatus(c,m);
        }

        private static void DrawSidebar(UiCanvas c, UiModel m)
        {
            bool compact=m.Page==8;
            c.Fill(R(20,26,38,38), "#29324C", 11);
            c.Text(R(20,26,38,38),"D",23,Accent,true,1);
            c.Text(R(70,25,132,25),"人类黎明",17,TextMain,true,0);
            Label(c,70,51,132,19,"游戏内控制台",12,Quiet);
            Label(c,24,107,150,20,"工作空间",11,Quiet);
            string[] icons={"basic","box","paw","sun","help","target","pack","sliders","sliders"};
            int[] order={0,5,1,2,6,3,8,7,4};
            for(int row=0;row<order.Length;row++)
            {
                int i=order[row]; UiRect r=R(12,138+row*(compact?42:48),192,compact?38:44);
                bool selected=m.Page==i;
                if(selected||c.Hover(r)) c.Fill(r,selected?"#262D40":"#202329",9);
                Glyph(c,icons[i],25,r.Y+(compact?9:12),20,selected?Accent:Muted);
                Label(c,58,r.Y+(compact?7:10),116,24,Pages[i],14,selected?"#DAE1FF":Muted);
                if(i==1 && m.ResourceCount>0) Pill(c,R(164,r.Y+(compact?10:13),28,19),m.ResourceCount.ToString(),selected);
                if(c.Hit(r,"nav."+i,true)) m.Navigate(i);
            }
            if(compact)
            {
                Card(c,R(16,544,184,56),"#191C22");
                Dot(c,30,555,m.Ready?"#8ECDB4":"#D3B77E");
                Label(c,44,548,143,21,m.Ready?"已连接当前地图":"等待进入地图",13,TextMain);
                Label(c,30,575,155,19,"Dawn of Man  " + m.GameVersion,11,Quiet);
                Key(c,R(20,610,30,22),"F11");Label(c,55,610,65,22,"显示/隐藏",12,Muted);
                Key(c,R(125,610,30,22),"Esc");Label(c,160,610,40,22,"关闭",12,Muted);
                return;
            }
            Card(c,R(16,Height-178,184,84),"#191C22");
            Dot(c,30,Height-157,m.Ready?"#8ECDB4":"#D3B77E");
            Label(c,44,Height-164,143,22,m.Ready?"已连接当前地图":"等待进入地图",13,TextMain);
            Label(c,30,Height-135,155,21,"Dawn of Man  " + m.GameVersion,11,Quiet);
            Key(c,R(24,Height-73,35,23),"F11"); Label(c,69,Height-73,130,23,"显示 / 隐藏",12,Muted);
            Key(c,R(24,Height-42,35,22),"Esc"); Label(c,69,Height-42,130,22,"关闭面板",12,Muted);
        }

        private static void DrawHeader(UiCanvas c, UiModel m)
        {
            c.Text(R(MainX,26,550,34),Pages[m.Page],25,TextMain,true,0);
            Label(c,MainX,66,620,22,Descriptions[m.Page],13,Muted);
            if(Button(c,R(972,34,134,34),"reset","关闭所有开关",0,m.Ready)) m.Emit("reset","");
            UiRect close=R(1118,34,34,34); if(c.Hover(close)) c.Fill(close,"#2D3038",8);
            Glyph(c,"close",1125,41,20,Muted); if(c.Hit(close,"close",true)) m.Emit("close","");
            if(m.Busy&&Button(c,R(816,34,142,34),"spawn.cancel","停止后续生成",2,true))m.Emit("spawn.cancel","");
            Rule(c,MainX,108,MainW);
            if(c.Hover(R(972,34,134,34)))
            {c.Fill(R(845,78,307,30),"#303644",7);c.Text(R(853,78,291,30),"关闭持续开关，速度与效率均恢复 1×",13,TextMain,false,1);}
        }
        private static void DrawStatus(UiCanvas c, UiModel m)
        {
            int height=PanelHeight(m.Page);bool compact=m.Page==8;
            Rule(c,MainX,height-(compact?25:51),MainW);
            Dot(c,MainX,height-(compact?15:28),m.StatusError?"#EE9CA6":m.Busy?Accent:"#8ECDB4");
            Label(c,MainX+15,height-(compact?24:39),MainW-160,compact?20:28,Short(c,m.Status,MainW-172,13),13,m.StatusError?"#EEA5AD":Muted);
            c.Text(R(1047,height-(compact?24:38),105,compact?20:26),"v"+m.Version,11,Quiet,false,2);
        }
        internal static void RenderToast(UiCanvas c, float screenWidth, string text, bool error)
        {
            float w=Math.Min(640,c.Measure(text,14,false)+60),x=(screenWidth-w)/2;
            c.Fill(R(x,80,w,44),"#363941",12);c.Fill(R(x+1,81,w-2,42),Background,11);
            Dot(c,x+20,99,error?"#EE9CA6":"#8ECDB4");
            c.Text(R(x+36,88,w-52,28),Short(c,text,w-52,14),14,error?"#EEA5AD":TextMain,false,0);
        }
        private static void DrawEmptyWorld(UiCanvas c)
        {
            Glyph(c,"basic",672,294,42,Quiet);
            c.Text(R(MainX,355,MainW,40),"进入地图后开始使用",22,TextMain,true,1);
            c.Text(R(MainX,403,MainW,30),"新建游戏或载入存档，即可管理人物、资源和世界。",14,Muted,false,1);
            c.Text(R(MainX,444,MainW,26),"F11 隐藏此面板，继续游戏。",12,Quiet,false,1);
        }
        private static int Enabled(UiModel m) { int n=0; foreach(bool v in m.Locks) if(v)n++; if(m.Domestic)n++;return n; }
        private static void Summary(UiCanvas c, UiRect r, string title, string value, string note)
        {
            Card(c,r,Surface); Label(c,r.X+16,r.Y+13,120,18,title,12,Muted);
            c.Text(R(r.X+16,r.Y+32,r.W-32,24),value,20,TextMain,true,0);
            c.Text(R(r.X+125,r.Y+34,r.W-141,22),note,13,Quiet,false,2);
        }

        private static void DrawOverview(UiCanvas c, UiModel m)
        {
            Summary(c,R(MainX,126,292,66),"人物保护",Enabled(m)+" 项启用",m.Domestic?"含玩家家畜":"玩家人物");
            Summary(c,R(MainX+308,126,292,66),"时间流速",m.AutoPauseActive?"面板暂停":m.Speed==0?"已暂停":m.Speed.ToString("0.##")+"×",
                m.AutoPauseActive?(m.Speed==0?"关闭后保持暂停":"关闭后 "+m.Speed.ToString("0.##")+"×"):"最高 15 倍");
            Summary(c,R(MainX+616,126,292,66),"当前选择",m.SelectionCount+" 个目标",m.SelectionCount==0?"在地图中选择":"支持多选");

            UiRect attr=R(MainX,208,560,328); Card(c,attr,Surface);
            c.Text(R(MainX+20,224,200,26),"人物状态",16,TextMain,true,0);
            int enabled=0;foreach(bool v in m.Locks)if(v)enabled++;
            if(Button(c,R(MainX+437,221,104,30),"stats.all",enabled==7?"关闭全部":"开启全部",0,true))
            {for(int i=0;i<7;i++)m.Locks[i]=enabled!=7;}
            Label(c,MainX+20,254,510,20,"保持选定属性满值，关闭后恢复自然消耗。",12,Muted);
            for(int i=0;i<8;i++)
            {
                int row=i/2,col=i%2;float x=MainX+20+col*268,y=291+row*57;
                string name=i<7?StatNames[i]:"同时保护家畜";
                string note=i<7?(m.Locks[i]?"保持满值":"自然消耗"):"玩家所属动物";
                Label(c,x,y,182,23,name,15,TextMain); Label(c,x,y+24,182,18,note,13,i<7&&m.Locks[i]?Accent:Quiet);
                if(i<7)m.Locks[i]=Switch(c,R(x+204,y+7,40,23),"lock."+i,m.Locks[i],R(x,y,244,49));
                else m.Domestic=Switch(c,R(x+204,y+7,40,23),"domestic",m.Domestic,R(x,y,244,49));
                if(row<3)Rule(c,x,y+49,244);
            }

            DrawSpeedCard(c,m,R(820,208,332,174),false);
            Card(c,R(820,398,332,138),Surface);
            c.Text(R(840,414,130,24),"知识点",16,TextMain,true,0);
            c.Text(R(974,411,156,30),m.Knowledge.ToString("N0"),23,TextMain,true,2);
            Label(c,840,448,292,18,"添加后可用于解锁科技。",12,Muted);
            int[] amounts={10,100,1000};
            for(int i=0;i<3;i++)if(Button(c,R(840+i*99,482,92,34),"knowledge."+amounts[i],"+"+amounts[i],0,true))m.Emit("knowledge",amounts[i].ToString());

            // All selected-target actions live on their own page; the overview summarizes and links there.
            Card(c,R(MainX,552,MainW,176),Surface);
            c.Text(R(MainX+20,568,200,25),"选中目标",16,TextMain,true,0);
            Label(c,MainX+210,570,500,22,"可直接在面板外点选地图上的目标。",12,Muted);
            if(Button(c,R(MainX+MainW-170,563,150,34),"targets.open","前往选中目标",0,true))m.Navigate(5);
            string[] kinds={"可救助","可驯化","可杀死","未完工建筑","可修缮"};
            int[] counts={m.RescueTargets,m.TameTargets,m.KillTargets,m.BuildTargets,m.RepairTargets+m.ResourceRepairTargets};
            for(int i=0;i<kinds.Length;i++)
            {
                UiRect tile=R(MainX+20+i*176,610,164,96);c.Fill(tile,"#23262D",9);
                Label(c,tile.X+14,tile.Y+12,tile.W-28,20,i==2&&!String.IsNullOrEmpty(m.KillVillagers)?"可杀死 · 含村民":kinds[i],13,i==2&&!String.IsNullOrEmpty(m.KillVillagers)?"#F2AAB4":Muted);
                c.Text(R(tile.X+14,tile.Y+40,tile.W-28,34),counts[i].ToString(),26,counts[i]>0?TextMain:Quiet,true,0);
            }
        }

        // The slider is logarithmic: every doubling of speed gets the same width, so 0.25×–1× is as easy to hit as 4×–15×.
        internal static float SpeedTrack(float speed) { return (float)(Math.Log(Math.Max(.25f,Math.Min(15,speed))/.25)/Math.Log(60)); }
        private static float TrackSpeed(float track)
        {
            float speed=(float)(.25*Math.Pow(60,Math.Max(0,Math.Min(1,track))));
            return speed<1?(float)Math.Round(speed*4)/4:(float)Math.Round(speed);
        }
        private static void DrawSpeedCard(UiCanvas c, UiModel m, UiRect r, bool wide)
        {
            Card(c,r,Surface);
            c.Text(R(r.X+20,r.Y+16,160,24),"游戏速度",16,TextMain,true,0);
            Pill(c,R(r.X+r.W-98,r.Y+17,78,24),m.AutoPauseActive?"面板暂停":m.Speed==0?"暂停":m.Speed.ToString("0.##")+"×",true);
            float sliderY=r.Y+66,sliderW=r.W-44;
            float current=Math.Max(.25f,Math.Min(15,m.Speed)),track=SpeedTrack(current);
            float raw=c.Slider(R(r.X+22,sliderY-8,sliderW,28),"speed.slider",track,0,1);
            c.Fill(R(r.X+22,sliderY+4,sliderW,4),"#383D49",2);
            float knob=track*sliderW;c.Fill(R(r.X+22,sliderY+4,knob,4),Accent,2);
            c.Fill(R(r.X+17+knob,sliderY,12,12),"#DDE5FF",6);
            // Only an actual drag dispatches, so a speed between the slider steps is never rewritten on its own.
            float next=TrackSpeed(raw);
            if(Math.Abs(raw-track)>.0001f&&next!=current)m.Emit("speed",next.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // While the panel holds the pause, a highlighted speed is the one restored on close; say so.
            if(m.AutoPauseActive)Label(c,r.X+22,r.Y+84,sliderW,22,m.Speed==0?"关闭面板后保持暂停":"关闭面板后恢复 "+m.Speed.ToString("0.##")+"×",13,Accent);
            float[] presets=wide?new float[]{.25f,.5f,1,2,4,8,15}:new float[]{.5f,1,2,4};float presetW=(r.W-40-2*presets.Length)/(presets.Length+1);
            for(int i=0;i<presets.Length;i++)if(Chip(c,R(r.X+20+i*(presetW+2),r.Y+111,presetW,30),"speed."+presets[i].ToString(System.Globalization.CultureInfo.InvariantCulture),presets[i]+"×",m.Speed==presets[i]))m.Emit("speed",presets[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
            if(Chip(c,R(r.X+20+presets.Length*(presetW+2),r.Y+111,presetW,30),"speed.pause",m.Speed==0?"继续":"暂停",m.Speed==0))m.Emit("pause","");
        }

        private static List<UiItem> Filter(UiModel m,bool animal)
        {
            List<UiItem> found=new List<UiItem>();
            string query=(m.Search??"").Trim();
            foreach(UiItem item in animal?m.Animals:m.Resources)
            {
                if(query.Length>0 && item.Name.IndexOf(query,StringComparison.OrdinalIgnoreCase)<0 && item.Id.IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)continue;
                if(!animal && m.Category==5 && !m.Preferences.Favorites.Contains(item.Id))continue;
                if(!animal && m.Category>0 && m.Category<5 && (item.Categories & (1<<(m.Category-1)))==0)continue;
                found.Add(item);
            }
            return found;
        }
        private static UiItem Selected(UiModel m,bool animal)
        {string id=animal?m.SelectedAnimal:m.SelectedResource;return (animal?m.Animals:m.Resources).Find(i=>i.Id==id);}

        private static void DrawCatalog(UiCanvas c,UiModel m,bool animal)
        {
            const float gridW=588,inspectorX=852,inspectorW=300;
            UiRect search=R(MainX,126,gridW,44);
            c.Fill(search,c.Focused("search")?Accent:Border,13);c.Fill(search.Inset(1),"#202329",12);
            Glyph(c,"search",MainX+14,139,18,Muted);
            string next=c.Input(R(MainX+43,134,gridW-88,28),"search",m.Search,60,false);
            if(next!=m.Search){m.Search=next;if(animal)m.AnimalScroll=0;else m.ResourceScroll=0;}
            if(String.IsNullOrEmpty(m.Search))Label(c,MainX+49,137,gridW-100,24,animal?"搜索动物名称或 ID":"搜索物品名称或 ID",13,Quiet);
            UiRect clear=R(MainX+gridW-36,134,28,28);
            if(!String.IsNullOrEmpty(m.Search)){Glyph(c,"close",clear.X+5,clear.Y+5,18,Muted);if(c.Hit(clear,"search.clear",true)){m.Search="";if(animal)m.AnimalScroll=0;else m.ResourceScroll=0;}}
            if(!animal)
            {
                string[] categories={"全部","食物 / 饮品","材料","工具 / 武器","衣物","收藏"};
                float x=MainX;
                for(int i=0;i<categories.Length;i++)
                {
                    float w=i==1||i==3?108:62;
                    if(Chip(c,R(x,184,w,30),"category."+i,categories[i],m.Category==i)){m.Category=i;m.ResourceScroll=0;}
                    x+=w+8;
                }
            }
            else Label(c,MainX,184,500,30,"全部物种 · 保留物种原有阵营",12,Muted);
            List<UiItem> found=Filter(m,animal);
            float viewportY=230,viewportH=484;
            UiRect viewport=R(MainX,viewportY,gridW,viewportH);
            if(found.Count==0)
            {
                Glyph(c,"search",MainX+278,352,32,Quiet);
                c.Text(R(MainX,408,gridW,30),"没有找到匹配项",18,TextMain,true,1);
                c.Text(R(MainX,444,gridW,26),"试试其他名称，或清空搜索与分类。",13,Muted,false,1);
                if(Button(c,R(MainX+230,490,128,36),"filters.clear","清空筛选",0,true)){m.Search="";m.Category=0;if(animal)m.AnimalScroll=0;else m.ResourceScroll=0;}
            }
            else
            {
                int columns=4; const float gap=12,cellH=112;float cellW=(gridW-16-gap*(columns-1))/columns;
                int rows=(found.Count+columns-1)/columns;
                float scroll=c.BeginScroll(viewport,animal?m.AnimalScroll:m.ResourceScroll,rows*(cellH+gap)-gap);
                if(animal)m.AnimalScroll=scroll;else m.ResourceScroll=scroll;
                int first=Math.Max(0,(int)(scroll/(cellH+gap))),last=Math.Min(rows-1,(int)((scroll+viewportH)/(cellH+gap)));
                UiItem selected=Selected(m,animal);
                for(int row=first;row<=last;row++)for(int col=0;col<columns;col++)
                {
                    int index=row*columns+col;if(index>=found.Count)break;UiItem item=found[index];
                    UiRect r=R(col*(cellW+gap),row*(cellH+gap),cellW,cellH);bool active=selected!=null&&selected.Id==item.Id;
                    c.Fill(r,active?"#8192DC":c.Hover(r)?"#4A505E":Border,11);
                    c.Fill(r.Inset(1),active?"#242D43":c.Hover(r)?"#272C35":Surface,10);
                    if(item.Image!=null)c.Image(R(r.X+(cellW-48)/2,r.Y+14,48,48),item.Image);
                    else Glyph(c,animal?"paw":"box",r.X+(cellW-36)/2,r.Y+20,36,Quiet);
                    c.Text(R(r.X+9,r.Y+76,cellW-18,25),Short(c,item.Name,cellW-18,15),15,TextMain,false,1);
                    if(active){c.Fill(R(r.X+cellW-22,r.Y+9,14,14),"#B5C1FA",7);Glyph(c,"check",r.X+cellW-21,r.Y+10,12,"#202B4B");}
                    if(c.Hit(r,"item."+item.Id,true)){if(animal)m.SelectedAnimal=item.Id;else m.SelectedResource=item.Id;}
                }
                c.EndScroll();
            }
            Label(c,MainX,719,gridW,25,found.Count>16?"共 "+found.Count+" 项 · 滚动浏览更多":"共 "+found.Count+" 项 · 点击卡片选择",13,Quiet);
            DrawInspector(c,m,animal,found,R(inspectorX,126,inspectorW,Height-195));
        }

        private static void DrawInspector(UiCanvas c,UiModel m,bool animal,List<UiItem> filtered,UiRect r)
        {
            Card(c,r,Surface);UiItem item=Selected(m,animal);float x=r.X+20,w=r.W-40;
            Label(c,x,r.Y+15,w-90,22,animal?"已选动物":"已选物品",13,Muted);
            if(!animal&&item!=null&&Chip(c,R(x+w-80,r.Y+13,80,26),"favorite",m.Preferences.Favorites.Contains(item.Id)?"已收藏":"收藏",m.Preferences.Favorites.Contains(item.Id)))
            {if(!m.Preferences.Favorites.Remove(item.Id))m.Preferences.Favorites.Add(item.Id);}
            c.Fill(R(x,r.Y+48,w,84),"#252B37",10);
            if(item!=null&&item.Image!=null)c.Image(R(x+12,r.Y+61,58,58),item.Image);
            else Glyph(c,animal?"paw":"box",x+21,r.Y+70,40,Quiet);
            c.Text(R(x+84,r.Y+62,w-96,27),Short(c,item==null?"请选择一项":item.Name,w-96,17),17,TextMain,true,0);
            Label(c,x+84,r.Y+94,w-96,22,item==null?"从左侧选择":Short(c,item.Id,w-96,13),13,Muted);
            bool hidden=item!=null&&!filtered.Exists(i=>i.Id==item.Id);
            Label(c,x,r.Y+138,w,24,hidden?"已选项不在当前筛选中":animal?"保留物种原有阵营":"物品在地面生成，由村民搬运。",13,hidden?"#E2C78C":Muted);
            Label(c,x,r.Y+171,80,23,"添加数量",14,TextMain);
            c.Text(R(x+90,r.Y+171,w-90,23),animal?"1–50 只":"1–1000 件",13,Muted,false,2);
            string quantity=animal?m.AnimalQuantity:m.Quantity;int maximum=animal?50:1000,count;
            bool initialValid=Int32.TryParse(quantity,out count)&&count>=1&&count<=maximum;
            UiRect field=R(x+46,r.Y+199,w-92,40);
            if(Button(c,R(x,r.Y+199,38,40),"quantity.minus","−",0,true)&&Int32.TryParse(quantity,out count))quantity=Math.Max(1,Math.Min(maximum,count-1)).ToString();
            c.Fill(field,!initialValid?"#C77883":c.Focused("quantity")?Accent:"#454C59",8);c.Fill(field.Inset(1),"#15181E",7);
            quantity=c.Input(field.Inset(4),"quantity",quantity,4,true);
            if(Button(c,R(x+w-38,r.Y+199,38,40),"quantity.plus","+",0,true)&&Int32.TryParse(quantity,out count))quantity=Math.Max(1,Math.Min(maximum,count+1)).ToString();
            int[] amounts=animal?new int[]{1,5,10,50}:new int[]{1,10,50,100};float amountW=(w-24)/4;
            for(int i=0;i<amounts.Length;i++)if(Chip(c,R(x+i*(amountW+8),r.Y+250,amountW,28),"quantity."+amounts[i],amounts[i].ToString(),quantity==amounts[i].ToString()))quantity=amounts[i].ToString();
            if(animal)m.AnimalQuantity=quantity;else m.Quantity=quantity;
            bool valid=Int32.TryParse(quantity,out count)&&count>=1&&count<=maximum;
            string feedback=!valid?"请输入 1–"+maximum+" 的整数。":m.Busy?"正在添加 "+Short(c,m.TaskName,100,13)+" · "+m.Done+"/"+m.Total:m.ShowSuccess?"已添加 "+Short(c,m.CompletedName,108,13)+" × "+m.CompletedCount:"";
            Label(c,x,r.Y+284,w,24,feedback,13,!valid?"#F2AAB4":m.Busy?"#C5CFFF":"#9CD8BD");
            if(m.Busy)
            {c.Fill(R(x,r.Y+312,w,3),"#383E4A",1);c.Fill(R(x,r.Y+312,w*Math.Max(0,Math.Min(1,(float)m.Done/Math.Max(1,m.Total))),3),Accent,1);}
            if(animal)
            {
                Label(c,x,r.Y+320,w,21,"年龄",13,Muted);string[] ages={"幼年","成年","老年"};float cw=(w-16)/3;
                for(int i=0;i<3;i++)if(Chip(c,R(x+i*(cw+8),r.Y+346,cw,30),"age."+i,ages[i],m.Age==i))m.Age=i;
                Label(c,x,r.Y+382,w,21,"性别",13,Muted);string[] genders={"雄性","雌性","随机"};
                for(int i=0;i<3;i++)if(Chip(c,R(x+i*(cw+8),r.Y+408,cw,30),"gender."+i,genders[i],m.Gender==i))m.Gender=i;
            }
            float locationY=animal?r.Y+444:r.Y+338;
            Label(c,x,locationY,w,22,"生成位置",14,TextMain);
            float lw=(w-8)/2;
            if(Chip(c,R(x,locationY+30,lw,32),"location.target","目标附近",m.Location==0))m.Location=0;
            if(Chip(c,R(x+lw+8,locationY+30,lw,32),"location.center","画面中心",m.Location==1))m.Location=1;
            string position=m.Location==1?"画面中心地面":m.SelectionCount==0?"当前将使用画面中心":"目标："+Short(c,m.SelectionName,w-80,13)+" 附近";
            Label(c,x,locationY+68,w,23,position,13,Muted);
            if(!animal)
            {
                c.Fill(R(x,r.Y+444,w,46),"#232731",9);
                Label(c,x+12,r.Y+455,w-70,24,"启用 "+m.Preferences.Keys[2]+" 快捷添加",13,Muted);
                m.GiveHotkey=Switch(c,R(x+w-52,r.Y+456,40,23),"give.hotkey",m.GiveHotkey);
                // Name the pack that receives the item, and keep a clear gap above the primary button.
                int packIndex=Math.Max(0,Math.Min(m.ActivePack,m.Preferences.Packs.Count-1));
                string packLabel=m.Preferences.Packs.Count==0?"加入新物资包":"加入物资包「"+Short(c,m.Preferences.Packs[packIndex].Name,w-130,14)+"」";
                if(Button(c,R(x,r.Y+498,w,34),"pack.add",packLabel,0,item!=null&&valid))AddPackItem(m,item,quantity,true);
            }
            string noun=item==null?"":Short(c,item.Name,w-116,14);
            string primary=m.Busy?"正在添加 · "+(m.Total>0?Math.Round(100f*m.Done/m.Total):0)+"%":item==null?"请先选择一项":(animal?"生成 ":"添加 ")+noun+" × "+(valid?count.ToString():"—");
            if(Button(c,R(x,r.Y+r.H-60,w,42),"spawn",primary,1,item!=null&&valid&&!m.Busy))m.Emit(animal?"spawn.animal":"spawn.resource","");
        }

        private static void DrawWorld(UiCanvas c,UiModel m)
        {
            DrawSpeedCard(c,m,R(MainX,126,MainW,164),true);
            c.Text(R(MainX,321,600,28),"天气",18,TextMain,true,0);
            Label(c,MainX,358,MainW,24,"切换后按游戏规则逐渐过渡，后续仍会自然变化。",13,Muted);
            string[] names={"晴朗","降雨","降雪","雷暴","暴风雪"};string[] icons={"sun","rain","snow","storm","blizzard"};
            for(int i=0;i<5;i++)
            {
                UiRect r=R(MainX+i*184,402,172,168);bool selected=m.Weather==i;
                c.Fill(r,selected?"#788AD3":Border,12);c.Fill(r.Inset(1),selected?"#252D42":c.Hover(r)?"#272C35":Surface,11);
                Glyph(c,icons[i],r.X+67,r.Y+30,38,selected?"#B9C6FF":Muted);
                c.Text(R(r.X,r.Y+89,r.W,26),names[i],16,TextMain,false,1);
                c.Text(R(r.X,r.Y+124,r.W,20),selected?"当前天气":"点击切换",11,selected?Accent:Quiet,false,1);
                if(c.Hit(r,"weather."+i,true))m.Emit("weather",i.ToString());
            }
            Label(c,MainX,577,MainW,23,"天气自然过渡，保留原有季节与灾害影响。",13,Muted);
            DrawVisitors(c,m);
        }

        private static void DrawHelp(UiCanvas c,UiModel m)
        {
            Card(c,R(MainX,126,438,328),Surface);c.Text(R(MainX+20,146,390,28),"键盘操作",17,TextMain,true,0);
            string[] keys={"F11","Esc",m.Preferences.Keys[0],m.Preferences.Keys[1],m.Preferences.Keys[2]};
            string[] labels={"显示或隐藏修改器","关闭面板","完成建造 · 在选中目标页启用","杀死单位 · 选中目标页启用，村民按两次","重复添加 · 在资源页启用"};
            for(int i=0;i<5;i++){Key(c,R(MainX+20,195+i*46,74,26),KeyName(keys[i]));Label(c,MainX+110,194+i*46,305,29,labels[i],14,TextMain);}
            Card(c,R(698,126,454,328),Surface);c.Text(R(718,146,414,28),"添加资源",17,TextMain,true,0);
            string[] steps={"搜索名称或英文 ID，点击物品卡片。","填写数量，选择生成位置。","点击添加，再由村民搬运入库。"};
            for(int i=0;i<3;i++){Pill(c,R(718,197+i*60,26,26),(i+1).ToString(),true);Label(c,758,193+i*60,370,36,steps[i],14,TextMain);}
            Label(c,718,391,412,22,"动物使用相同流程，可额外选择年龄与性别。",12,Muted);
            Card(c,R(MainX,474,MainW,242),Surface);c.Text(R(MainX+20,494,MainW-40,28),"使用提示",17,TextMain,true,0);
            string[] tips={
                "开关每次启动默认关闭；人物保护默认仅作用于玩家人物。",
                "面板打开时可直接点选面板外的目标；操作快捷键在面板、菜单和输入框中停用。",
                "添加物品、知识、建造和死亡结果会随游戏存档保存。",
                "健康锁保持健康值；寿命结束、雷击等直接死亡机制不在保护范围。",
                "可在偏好设置开启面板自动暂停；收藏、物资包和键位会自动保存。"
            };
            for(int i=0;i<tips.Length;i++){Dot(c,MainX+21,543+i*31,Quiet);Label(c,MainX+39,534+i*31,MainW-66,26,tips[i],14,Muted);}
        }
    }
}
