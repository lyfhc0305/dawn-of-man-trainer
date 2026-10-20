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
        internal string PackSearch = "";
    }

    internal static partial class TrainerView
    {
        private static void DrawMultipliers(UiCanvas c,UiModel m)
        {
            m.StorageMultiplier=DrawMultiplierCard(c,m,R(MainX,126,446,190),"storage","仓储容量",m.StorageMultiplier,10,
                "封闭仓储，不含露天堆场。");
            m.BuildMultiplier=DrawMultiplierCard(c,m,R(706,126,446,190),"build","建造速度",m.BuildMultiplier,20,
                "施工提速，材料与搬运照常。");
            m.CarryMultiplier=DrawMultiplierCard(c,m,R(MainX,332,446,190),"carry","人力搬运",m.CarryMultiplier,10,
                "普通货物；儿童、重物保持原有上限。");
            m.TransportMultiplier=DrawMultiplierCard(c,m,R(706,332,446,190),"transport","运输工具容量",m.TransportMultiplier,10,
                "仅拖板与拖车，犁和巨岩运输不变。");
            Card(c,R(MainX,538,MainW,70),Surface);
            Note(c,MainX+20,547,MainW-40,m.Ready?"降低容量保留现货，超额时先用掉或卸下；专用工具、衣物槽不变。":"进入地图后可调整，四项默认 1×；专用工具、衣物槽不变。");
            Note(c,MainX+20,575,MainW-40,"倍率仅作用于玩家单位；“关闭所有开关”会将这四项恢复为 1×。");
        }
        private static int DrawMultiplierCard(UiCanvas c,UiModel m,UiRect r,string key,string title,int value,int maximum,string note)
        {
            int current=Math.Max(1,Math.Min(maximum,value)),next=current;
            string id="multiplier."+key;
            Card(c,r,Surface);
            c.Text(R(r.X+20,r.Y+16,210,28),title,17,TextMain,true,0);
            Note(c,r.X+20,r.Y+49,r.W-40,note);
            if(Button(c,R(r.X+20,r.Y+86,32,32),id+".minus","−",0,m.Ready&&current>1))next=current-1;
            if(Button(c,R(r.X+r.W-52,r.Y+86,32,32),id+".plus","+",0,m.Ready&&current<maximum))next=current+1;
            UiRect slider=R(r.X+72,r.Y+86,r.W-144,32);
            float raw=current;
            if(m.Ready)raw=c.Slider(slider,id+".slider",current,1,maximum);
            else c.Hit(slider,"slider."+id+".slider",false);
            // The canvas returns a continuous value. Snap every input to a finite integer;
            // an untouched or invalid slider must not overwrite the other controls.
            if(!Single.IsNaN(raw)&&!Single.IsInfinity(raw))
            {
                int snapped=(int)Math.Round(Math.Max(1,Math.Min(maximum,raw)),MidpointRounding.AwayFromZero);
                if(snapped!=current)next=snapped;
            }
            int[] presets=maximum==20?new int[]{1,2,5,10,20}:new int[]{1,2,5,10};
            float width=(r.W-40-(presets.Length-1)*10)/presets.Length;
            for(int i=0;i<presets.Length;i++)
                if(Button(c,R(r.X+20+i*(width+10),r.Y+142,width,32),id+"."+presets[i],presets[i]+"×",current==presets[i]?1:0,m.Ready))next=presets[i];
            float trackX=slider.X,trackW=slider.W,ratio=(float)(next-1)/(maximum-1);
            c.Fill(R(trackX,r.Y+99,trackW,6),m.Ready?"#363C48":"#2C3039",3);
            if(ratio>0)c.Fill(R(trackX,r.Y+99,trackW*ratio,6),m.Ready?Accent:"#505767",3);
            c.Fill(R(trackX+trackW*ratio-8,r.Y+94,16,16),m.Ready?"#DEE5FF":"#727B8E",8);
            c.Text(R(r.X+64,r.Y+120,48,16),"1×",13,Quiet,false,0);
            c.Text(R(r.X+r.W-112,r.Y+120,48,16),maximum+"×",13,Quiet,false,2);
            c.Text(R(r.X+248,r.Y+14,r.W-268,30),"当前 "+next+"×",22,m.Ready?Accent:Quiet,true,2);
            if(next!=current)m.Emit(id,next.ToString());
            return next;
        }
        private static string KeyName(string key) { return key=="Insert"?"Ins":key=="Delete"?"Del":key=="PageUp"?"PgUp":key=="PageDown"?"PgDn":key; }
        private static void Note(UiCanvas c,float x,float y,float w,string text)
        { Label(c,x,y,w,24,text,13,Muted); }
        private static void DrawTargets(UiCanvas c,UiModel m)
        {
            if(Chip(c,R(MainX,126,136,34),"target.tab.0","人物与动物",m.TargetTab==0))m.TargetTab=0;
            if(Chip(c,R(MainX+146,126,136,34),"target.tab.1","建造与维护",m.TargetTab==1))m.TargetTab=1;
            c.Text(R(820,126,332,34),"已选择 "+m.SelectionCount+" 个目标",14,Muted,false,2);
            if(m.TargetTab==1){DrawMaintenance(c,m);return;}
            Card(c,R(MainX,180,560,374),Surface);
            c.Text(R(MainX+20,199,510,28),"选中单位的生存状态",18,TextMain,true,0);
            Note(c,MainX+20,235,510,m.RescueTargets==0?"在面板外点选玩家人物或家畜。":"显示 "+m.RescueTargets+" 个存活玩家单位的平均状态。");
            for(int i=0;i<7;i++)
            {
                float x=MainX+20+(i%2)*268,y=280+(i/2)*62;float value=m.TargetStats[i];
                Label(c,x,y,150,24,StatNames[i],15,TextMain);
                c.Text(R(x+155,y,85,24),value<0?"—":Math.Round(value*100)+"%",14,value>=0&&value<.25f?"#F0A1AB":Muted,false,2);
                c.Fill(R(x,y+32,240,5),"#353C48",2);
                if(value>=0)c.Fill(R(x,y+32,240*Math.Max(0,Math.Min(1,value)),5),value<.25f?"#E6A0A9":Accent,2);
            }
            Card(c,R(820,180,332,178),Surface);c.Text(R(840,199,292,28),"恢复健康",17,TextMain,true,0);
            Note(c,840,237,292,"只恢复选中玩家单位的健康值。");
            if(Button(c,R(840,291,292,43),"rescue.health","恢复 "+m.RescueTargets+" 个单位",1,m.RescueTargets>0))m.Emit("rescue.health","");
            Card(c,R(820,374,332,180),Surface);c.Text(R(840,393,292,28),"补满生存状态",17,TextMain,true,0);
            Note(c,840,431,292,"恢复现有属性，疾病与年龄不变。");
            if(Button(c,R(840,487,292,43),"rescue.all","补满 "+m.RescueTargets+" 个单位",0,m.RescueTargets>0))m.Emit("rescue.all","");
            Card(c,R(MainX,574,446,154),Surface);c.Text(R(MainX+20,593,300,28),"快速驯化",17,TextMain,true,0);
            Note(c,MainX+20,629,406,Short(c,m.TameTargets>0?m.TameTargets+" 只符合原生捕捉条件，将转为对应家畜。":m.TameReason,406,13));
            Note(c,MainX+20,672,226,"沿用原生转换，跳过忙碌动物");
            if(Button(c,R(MainX+266,664,160,42),"tame","驯化 "+m.TameTargets+" 只动物",1,m.TameTargets>0))m.Emit("tame","");
            TargetActionCard(c,m,R(706,574,446,154),true);
        }
        // Quick build and kill sit with the other selected-target actions, each with its hotkey switch.
        private static void TargetActionCard(UiCanvas c,UiModel m,UiRect r,bool kill)
        {
            Card(c,r,Surface);c.Text(R(r.X+20,r.Y+19,180,28),kill?"杀死选中单位":"快速完成建造",17,TextMain,true,0);
            int targets=kill?m.KillTargets:m.BuildTargets;bool villagers=kill&&targets>0&&!String.IsNullOrEmpty(m.KillVillagers);
            c.Text(R(r.X+194,r.Y+22,r.W-214,24),targets==0?"请先选择目标":villagers?Short(c,"含村民 "+m.KillVillagers,r.W-214,13):targets+" 个可操作目标",13,villagers?"#F2AAB4":Muted,false,2);
            Note(c,r.X+20,r.Y+55,r.W-40,kill?"立即死亡，结果随存档保存；含村民时需再次确认。":"无需材料立即完工，并计入原生建造奖励。");
            Label(c,r.X+20,r.Y+98,140,24,"启用 "+KeyName(m.Preferences.Keys[kill?1:0])+" 快捷键",13,Muted);
            if(kill)m.KillHotkey=Switch(c,R(r.X+164,r.Y+99,40,23),"kill.hotkey",m.KillHotkey);
            else m.BuildHotkey=Switch(c,R(r.X+164,r.Y+99,40,23),"build.hotkey",m.BuildHotkey);
            string label=targets==0?"选择目标后可用":kill&&m.ArmedAction=="kill"?"再次点击确认杀死":kill?"杀死 "+targets+" 个单位":"完成 "+targets+" 个建造";
            bool clicked=Button(c,R(r.X+r.W-180,r.Y+90,160,42),kill?"kill":"complete",label,kill?2:0,targets>0);
            if(kill?Confirmed(m,"kill",villagers,clicked):clicked)m.Emit(kill?"kill":"complete","");
        }
        private static void DrawMaintenance(UiCanvas c,UiModel m)
        {
            Card(c,R(MainX,180,560,374),Surface);c.Text(R(MainX+20,199,510,28),"建筑尚未交付的材料",18,TextMain,true,0);
            Note(c,MainX+20,235,510,"只统计玩家未完工、未回收的建造目标。");
            if(m.Materials.Count==0)
            {c.Text(R(MainX+20,332,520,32),"没有需要补给的材料",18,Muted,false,1);Note(c,MainX+74,379,440,"在面板外点选未完工建筑后显示。");}
            else
            {
                UiRect viewport=R(MainX+20,279,520,251);m.MaterialScroll=c.BeginScroll(viewport,m.MaterialScroll,m.Materials.Count*44);
                for(int i=0;i<m.Materials.Count;i++)
                {
                    SupplyLine line=m.Materials[i];UiItem item=m.Resources.Find(r=>r.Id==line.Id);float y=i*44;
                    if(item!=null&&item.Image!=null)c.Image(R(0,y+4,30,30),item.Image);
                    Label(c,44,y+4,340,30,item==null?line.Id:item.Name,15,TextMain);
                    c.Text(R(396,y+4,100,30),"× "+line.Count,15,Accent,false,2);Rule(c,0,y+42,496);
                }
                c.EndScroll();
            }
            Card(c,R(820,180,332,178),Surface);c.Text(R(840,199,292,28),"修缮建筑",17,TextMain,true,0);
            Note(c,840,237,292,"一次修复选中且已建成的玩家建筑。");
            if(Button(c,R(840,291,292,43),"repair.building","修复 "+m.RepairTargets+" 个建筑",0,m.RepairTargets>0))m.Emit("repair.building","");
            Card(c,R(820,374,332,180),Surface);c.Text(R(840,393,292,28),"恢复工具与衣物耐久",17,TextMain,true,0);
            Note(c,840,431,292,"仅处理直接选中的工具或衣物。");
            if(Button(c,R(840,487,292,43),"repair.resource","恢复 "+m.ResourceRepairTargets+" 件耐久",0,m.ResourceRepairTargets>0))m.Emit("repair.resource","");
            Card(c,R(MainX,574,446,154),Surface);c.Text(R(MainX+20,593,300,28),"按缺料清单补给",17,TextMain,true,0);
            Note(c,MainX+20,629,406,"在各建筑附近分批生成，村民继续搬运与建造。");
            Note(c,MainX+20,672,226,"含运输中材料，重复补给会多生成");
            long total=0;foreach(SupplyLine line in m.Materials)total+=line.Count;
            if(Button(c,R(MainX+266,664,160,42),"supply.building",m.Busy?"正在生成…":"补齐缺料 · "+total+" 件",1,m.Materials.Count>0&&!m.Busy&&total<=FeaturePolicy.MaximumTotal))m.Emit("supply.building","");
            TargetActionCard(c,m,R(706,574,446,154),false);
        }
        private static void DrawVisitors(UiCanvas c,UiModel m)
        {
            Card(c,R(MainX,610,446,118),Surface);c.Text(R(MainX+20,625,240,25),"邀请移民",17,TextMain,true,0);
            Note(c,MainX+20,663,236,m.MigrantCount>0?"已有 "+m.MigrantCount+" 名移民在途中":"住房余量："+m.HousingFree+" 人");
            if(Button(c,R(MainX+277,659,149,43),"invite.migrant","邀请一批移民",0,m.HousingFree>0&&m.MigrantCount==0))m.Emit("invite.migrant","");
            Card(c,R(706,610,446,118),Surface);c.Text(R(726,625,240,25),"邀请商人",17,TextMain,true,0);
            Note(c,726,663,236,m.TraderCount>0?"地图上已有商队":"从地图边缘前来");
            if(Button(c,R(983,659,149,43),"invite.trader","邀请商人",0,m.TraderCount==0))m.Emit("invite.trader","");
        }
        private static void AddPackItem(UiModel m,UiItem item,string quantity,bool fromCatalog)
        {
            if(m.Preferences.Packs.Count==0)m.Preferences.Packs.Add(new SupplyPack());
            m.ActivePack=Math.Max(0,Math.Min(m.Preferences.Packs.Count-1,m.ActivePack));SupplyPack pack=m.Preferences.Packs[m.ActivePack];
            SupplyLine existing=pack.Lines.Find(l=>l.Id==item.Id);int amount; if(!Int32.TryParse(quantity,out amount)||amount<1||amount>1000)return;
            if(existing!=null)
            {
                // Unparseable or nonpositive text is an empty amount when adding again. Check
                // the remaining capacity before addition, including malformed huge ints.
                int current=Math.Max(0,existing.Count);
                if(current>1000-amount){m.Emit("pack.notice","该物资包中的单项数量最多 1000");return;}
                existing.Quantity=(current+amount).ToString();
            }
            else if(pack.Lines.Count<64)pack.Lines.Add(new SupplyLine{Id=item.Id,Quantity=amount.ToString()});
            else{m.Emit("pack.notice","每个物资包最多 64 种资源");return;}
            m.Emit("pack.notice",fromCatalog?"已加入物资包："+pack.Name+"；到物资包标签编辑或执行":"已向「"+pack.Name+"」加入 "+item.Name+" × "+amount);
        }
        // Items for the pack editor's own search; the first three matches are offered.
        private static List<UiItem> PackMatches(UiModel m)
        {
            List<UiItem> found=new List<UiItem>();string query=(m.PackSearch??"").Trim();if(query.Length==0)return found;
            foreach(UiItem item in m.Resources)
            {
                if(item.Name.IndexOf(query,StringComparison.OrdinalIgnoreCase)<0&&item.Id.IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)continue;
                found.Add(item);if(found.Count==3)break;
            }
            return found;
        }
        private static string PackError(UiModel m,SupplyPack pack,out int total)
        {
            total=0;HashSet<string> available=new HashSet<string>();foreach(UiItem item in m.Resources)available.Add(item.Id);
            try{total=FeaturePolicy.ValidateSupply(pack.Lines,available);return null;}catch(InvalidOperationException ex){return ex.Message;}
        }
        private static void DrawPacks(UiCanvas c,UiModel m)
        {
            Card(c,R(MainX,126,274,605),Surface);c.Text(R(MainX+18,143,238,28),"我的物资包",17,TextMain,true,0);
            Note(c,MainX+18,180,238,"选择清单，编辑后自动保存。");
            m.PackListScroll=c.BeginScroll(R(MainX+14,220,246,410),m.PackListScroll,m.Preferences.Packs.Count*62);
            for(int i=0;i<m.Preferences.Packs.Count;i++)
            {
                SupplyPack entry=m.Preferences.Packs[i];UiRect r=R(0,i*62,228,54);
                c.Fill(r,i==m.ActivePack?"#2A334A":"#242830",8);Label(c,12,r.Y+5,204,24,Short(c,entry.Name,204,14),14,i==m.ActivePack?"#D4DDFF":TextMain);
                Label(c,12,r.Y+28,204,20,entry.Lines.Count+" 种资源",13,Muted);
                if(c.Hit(r,"pack.select."+i,true)){m.ActivePack=i;m.PackScroll=0;m.ArmedAction=null;m.Emit("focus.clear","");}
            }
            c.EndScroll();
            if(Button(c,R(MainX+18,648,238,32),"pack.new","新建物资包",0,m.Preferences.Packs.Count<16))
            {m.Preferences.Packs.Add(new SupplyPack{Name="新物资包 "+(m.Preferences.Packs.Count+1)});m.ActivePack=m.Preferences.Packs.Count-1;m.PackScroll=0;m.PackListScroll=10000;m.ArmedAction=null;}
            bool deleteArmed=m.ArmedAction=="pack.delete";
            if(Confirmed(m,"pack.delete",true,Button(c,R(MainX+18,691,238,25),"pack.delete",deleteArmed?"再次点击确认删除":"删除当前物资包",2,m.Preferences.Packs.Count>0)))
            {m.Preferences.Packs.RemoveAt(Math.Max(0,Math.Min(m.ActivePack,m.Preferences.Packs.Count-1)));m.ActivePack=0;}
            Card(c,R(534,126,618,605),Surface);
            if(m.Preferences.Packs.Count==0)
            {c.Text(R(554,330,578,34),"创建你的第一个物资包",20,TextMain,true,1);c.Text(R(554,379,578,26),"新建后可直接搜索物品加入，或在资源页加入。",14,Muted,false,1);return;}
            m.ActivePack=Math.Max(0,Math.Min(m.ActivePack,m.Preferences.Packs.Count-1));SupplyPack pack=m.Preferences.Packs[m.ActivePack];
            c.Fill(R(554,144,380,38),c.Focused("pack.name")?Accent:Border,8);c.Fill(R(555,145,378,36),"#181B21",7);
            pack.Name=c.Input(R(564,149,360,28),"pack.name",pack.Name,24,false);
            Note(c,954,149,178,"名称可直接编辑");
            c.Fill(R(554,190,250,32),c.Focused("pack.search")?Accent:Border,8);c.Fill(R(555,191,248,30),"#181B21",7);
            m.PackSearch=c.Input(R(562,193,234,26),"pack.search",m.PackSearch,30,false);
            if(String.IsNullOrEmpty(m.PackSearch))Label(c,566,193,226,26,"搜索物品加入本包",13,Quiet);
            List<UiItem> matches=PackMatches(m);
            if(matches.Count==0)Note(c,818,194,314,m.Resources.Count==0?"进入地图后可搜索物品":String.IsNullOrEmpty(m.PackSearch)?"输入名称或 ID，点击结果加入 10 件":"没有找到匹配项");
            for(int i=0;i<matches.Count;i++)if(Chip(c,R(818+i*106,190,100,32),"pack.pick."+matches[i].Id,"+ "+Short(c,matches[i].Name,74,14),false))AddPackItem(m,matches[i],"10",false);
            Rule(c,554,230,578);
            m.PackScroll=c.BeginScroll(R(554,242,578,330),m.PackScroll,pack.Lines.Count*50);
            for(int i=0;i<pack.Lines.Count;i++)
            {
                SupplyLine line=pack.Lines[i];UiItem item=m.Resources.Find(r=>r.Id==line.Id);float y=i*50;
                bool known=item!=null||m.Resources.Count==0,countValid=line.Count>=1&&line.Count<=1000;
                if(item!=null&&item.Image!=null)c.Image(R(0,y+7,32,32),item.Image);
                Label(c,45,y+7,300,32,Short(c,item!=null?item.Name:known?line.Id:line.Id+"（未找到）",300,15),15,known?TextMain:"#E5BD85");
                // A bad quantity is outlined on its own row, not only in the summary below.
                c.Fill(R(358,y+5,121,36),countValid?Border:"#C77883",8);c.Fill(R(359,y+6,119,34),"#15181E",7);
                line.Quantity=c.Input(R(365,y+9,107,28),"pack.quantity."+i,line.Quantity,4,true);
                if(Button(c,R(492,y+6,60,34),"pack.remove."+i,"移除",0,true)){pack.Lines.RemoveAt(i);break;}
                Rule(c,0,y+48,552);
            }
            c.EndScroll();
            if(pack.Lines.Count==0){c.Text(R(554,350,578,30),"这个物资包还是空的",16,Muted,false,1);c.Text(R(554,386,578,24),"用上方搜索加入物品，或在资源页点“加入物资包”。",13,Quiet,false,1);}
            int total;string error=PackError(m,pack,out total);bool noCatalog=m.Resources.Count==0;
            string summary=error==null?pack.Lines.Count+" 种资源 · 合计 "+total+" 件":pack.Lines.Count==0?"先加入至少一种物品":noCatalog?"进入地图后可校验并添加":error;
            Label(c,554,585,578,24,Short(c,summary,578,13),13,error==null||pack.Lines.Count==0||noCatalog?Muted:"#F2AAB4");
            if(Chip(c,R(554,624,138,32),"pack.location.target","目标附近",m.Location==0))m.Location=0;
            if(Chip(c,R(700,624,138,32),"pack.location.center","画面中心",m.Location==1))m.Location=1;
            Note(c,854,628,278,m.Location==0&&m.SelectionCount==0?"无目标，使用画面中心":"生成后由村民搬运入库");
            string execute=m.Busy?"正在生成…":error==null?"添加整包 · "+total+" 件":pack.Lines.Count==0?"请先加入物品":noCatalog?"进入地图后可添加":"请先修正清单";
            if(Button(c,R(554,674,578,40),"pack.execute",execute,1,m.Ready&&error==null&&!m.Busy))m.Emit("pack.execute","");
        }
        private static void DrawPreferences(UiCanvas c,UiModel m)
        {
            Card(c,R(MainX,126,560,394),Surface);c.Text(R(MainX+20,145,510,28),"操作快捷键",18,TextMain,true,0);
            Note(c,MainX+20,183,510,"点击按键按钮，再按新键；Esc 取消录入。");
            string[] names={"快速完成建造","杀死选中单位","重复添加资源"};
            for(int i=0;i<3;i++)
            {
                float y=235+i*82;Label(c,MainX+20,y,278,26,names[i],15,TextMain);
                Note(c,MainX+20,y+29,278,"对应功能开关每次启动默认关闭");
                if(Button(c,R(MainX+325,y+7,215,42),"key.capture."+i,m.CapturingKey==i?"请按新键…":m.Preferences.Keys[i],m.CapturingKey==i?1:0,true))m.Emit("key.capture",i.ToString());
            }
            Card(c,R(820,126,332,394),Surface);c.Text(R(840,145,292,28),"面板自动暂停",18,TextMain,true,0);
            Note(c,840,189,220,"打开面板时暂停游戏");
            m.Preferences.AutoPause=Switch(c,R(1092,189,40,23),"prefs.autopause",m.Preferences.AutoPause);
            Note(c,840,242,292,"关闭面板后恢复打开前的倍率。");
            Note(c,840,281,292,"原本已暂停时，不会自动继续。");
            Note(c,840,320,292,"支持恢复 0.25× 与 0.5× 慢速。");
            Note(c,840,359,292,"面板内选择速度会更新恢复倍率。");
            Note(c,840,406,292,m.AutoPauseActive?"当前：面板持有暂停":"当前：由游戏控制时间");
            Card(c,R(MainX,540,MainW,188),Surface);c.Text(R(MainX+20,559,MainW-40,28),"个人偏好",18,TextMain,true,0);
            Label(c,MainX+20,601,96,30,"界面大小",15,TextMain);
            for(int i=0;i<FeaturePolicy.UiScales.Length;i++)
            {int percent=FeaturePolicy.UiScales[i];if(Chip(c,R(MainX+120+i*70,601,64,30),"ui.scale."+percent,percent+"%",m.Preferences.UiScale==percent))m.Preferences.UiScale=percent;}
            Note(c,MainX+548,604,MainW-568,"100% 按分辨率自动适配，不超出屏幕");
            Note(c,MainX+20,645,MainW-40,"自动保存收藏、物资包、数量、已选物品、窗口位置、键位和界面偏好；不保存属性锁与危险操作开关。");
            if(Button(c,R(MainX+20,682,235,29),"prefs.default","恢复默认键位与界面偏好",0,true))m.Emit("prefs.default","");
            Note(c,MainX+278,685,MainW-318,"保留你的收藏和物资包。");
        }
    }
}
