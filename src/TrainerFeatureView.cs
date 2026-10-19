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
    }

    internal static partial class TrainerView
    {
        private static string KeyName(string key) { return key=="Insert"?"Ins":key=="Delete"?"Del":key=="PageUp"?"PgUp":key=="PageDown"?"PgDn":key; }
        private static void Note(UiCanvas c,float x,float y,float w,string text)
        { Label(c,x,y,w,24,text,13,Muted); }
        private static void DrawTargets(UiCanvas c,UiModel m)
        {
            if(Chip(c,R(MainX,126,136,34),"target.tab.0","单位救助",m.TargetTab==0))m.TargetTab=0;
            if(Chip(c,R(MainX+146,126,136,34),"target.tab.1","建造与维护",m.TargetTab==1))m.TargetTab=1;
            c.Text(R(820,126,332,34),"已选择 "+m.SelectionCount+" 个目标",14,Muted,false,2);
            if(m.TargetTab==1){DrawMaintenance(c,m);return;}
            Card(c,R(MainX,180,560,374),Surface);
            c.Text(R(MainX+20,199,510,28),"选中单位的生存状态",18,TextMain,true,0);
            Note(c,MainX+20,235,510,m.RescueTargets==0?"先隐藏面板，选择玩家人物或家畜。":"显示 "+m.RescueTargets+" 个存活玩家单位的平均状态。");
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
            Card(c,R(MainX,574,MainW,154),Surface);c.Text(R(MainX+20,593,500,28),"快速驯化",17,TextMain,true,0);
            Note(c,MainX+20,629,570,Short(c,m.TameTargets>0?m.TameTargets+" 只符合原生捕捉条件，将转为对应家畜。":m.TameReason,570,13));
            Note(c,MainX+20,668,570,"保留原生物种转换流程，跳过已有任务的动物。");
            if(Button(c,R(877,651,255,43),"tame","驯化 "+m.TameTargets+" 只动物",1,m.TameTargets>0))m.Emit("tame","");
        }
        private static void DrawMaintenance(UiCanvas c,UiModel m)
        {
            Card(c,R(MainX,180,560,374),Surface);c.Text(R(MainX+20,199,510,28),"建筑尚未交付的材料",18,TextMain,true,0);
            Note(c,MainX+20,235,510,"只统计玩家未完工、未回收的建造目标。");
            if(m.Materials.Count==0)
            {c.Text(R(MainX+20,332,520,32),"没有需要补给的材料",18,Muted,false,1);Note(c,MainX+74,379,440,"先在地图选择未完工建筑，再返回查看。");}
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
            Card(c,R(MainX,574,MainW,154),Surface);c.Text(R(MainX+20,593,550,28),"按缺料清单补给",17,TextMain,true,0);
            Note(c,MainX+20,629,570,"在各建筑附近分批生成，村民继续搬运与建造。");
            Note(c,MainX+20,668,570,"清单包含运输中的材料，重复补给会额外生成。");
            long total=0;foreach(SupplyLine line in m.Materials)total+=line.Count;
            if(Button(c,R(877,651,255,43),"supply.building",m.Busy?"正在生成…":"补齐缺料 · "+total+" 件",1,m.Materials.Count>0&&!m.Busy&&total<=FeaturePolicy.MaximumTotal))m.Emit("supply.building","");
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
        private static void AddPackItem(UiModel m,string id,string quantity)
        {
            if(m.Preferences.Packs.Count==0)m.Preferences.Packs.Add(new SupplyPack());
            m.ActivePack=Math.Max(0,Math.Min(m.Preferences.Packs.Count-1,m.ActivePack));SupplyPack pack=m.Preferences.Packs[m.ActivePack];
            SupplyLine existing=pack.Lines.Find(l=>l.Id==id);int amount; if(!Int32.TryParse(quantity,out amount)||amount<1||amount>1000)return;
            if(existing!=null){if(existing.Count+amount>1000){m.Emit("pack.notice","该物资包中的单项数量最多 1000");return;}existing.Quantity=(existing.Count+amount).ToString();}
            else if(pack.Lines.Count<64)pack.Lines.Add(new SupplyLine{Id=id,Quantity=quantity});
            else{m.Emit("pack.notice","每个物资包最多 64 种资源");return;}
            m.Emit("pack.notice","已加入物资包："+pack.Name+"；到物资包标签编辑或执行");
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
                if(c.Hit(r,"pack.select."+i,true)){m.ActivePack=i;m.PackScroll=0;m.Emit("focus.clear","");}
            }
            c.EndScroll();
            if(Button(c,R(MainX+18,648,238,32),"pack.new","新建物资包",0,m.Preferences.Packs.Count<16))
            {m.Preferences.Packs.Add(new SupplyPack{Name="新物资包 "+(m.Preferences.Packs.Count+1)});m.ActivePack=m.Preferences.Packs.Count-1;m.PackScroll=0;m.PackListScroll=10000;}
            if(Button(c,R(MainX+18,691,238,25),"pack.delete","删除当前物资包",2,m.Preferences.Packs.Count>0))
            {m.Preferences.Packs.RemoveAt(Math.Max(0,Math.Min(m.ActivePack,m.Preferences.Packs.Count-1)));m.ActivePack=0;}
            Card(c,R(534,126,618,605),Surface);
            if(m.Preferences.Packs.Count==0)
            {c.Text(R(554,330,578,34),"创建你的第一个物资包",20,TextMain,true,1);c.Text(R(554,379,578,26),"到资源页选择物品，再加入物资包。",14,Muted,false,1);return;}
            m.ActivePack=Math.Max(0,Math.Min(m.ActivePack,m.Preferences.Packs.Count-1));SupplyPack pack=m.Preferences.Packs[m.ActivePack];
            c.Fill(R(554,144,380,38),c.Focused("pack.name")?Accent:Border,8);c.Fill(R(555,145,378,36),"#181B21",7);
            pack.Name=c.Input(R(564,149,360,28),"pack.name",pack.Name,24,false);
            Note(c,954,149,178,"名称可直接编辑");
            Note(c,554,196,578,"在资源页选择物品和数量，再点“加入当前物资包”。");
            Rule(c,554,230,578);
            m.PackScroll=c.BeginScroll(R(554,242,578,330),m.PackScroll,pack.Lines.Count*50);
            for(int i=0;i<pack.Lines.Count;i++)
            {
                SupplyLine line=pack.Lines[i];UiItem item=m.Resources.Find(r=>r.Id==line.Id);float y=i*50;
                if(item!=null&&item.Image!=null)c.Image(R(0,y+7,32,32),item.Image);
                Label(c,45,y+7,300,32,Short(c,item==null?line.Id:item.Name,300,15),15,item==null?"#E5BD85":TextMain);
                c.Fill(R(359,y+6,119,34),"#15181E",7);line.Quantity=c.Input(R(365,y+9,107,28),"pack.quantity."+i,line.Quantity,4,true);
                if(Button(c,R(492,y+6,60,34),"pack.remove."+i,"移除",0,true)){pack.Lines.RemoveAt(i);break;}
                Rule(c,0,y+48,552);
            }
            c.EndScroll();int total;string error=PackError(m,pack,out total);
            Note(c,554,585,578,error==null?pack.Lines.Count+" 种资源 · 合计 "+total+" 件":Short(c,error,578,13));
            if(Chip(c,R(554,624,138,32),"pack.location.target","目标附近",m.Location==0))m.Location=0;
            if(Chip(c,R(700,624,138,32),"pack.location.center","画面中心",m.Location==1))m.Location=1;
            Note(c,854,628,278,m.Location==0&&m.SelectionCount==0?"无目标，使用画面中心":"生成后由村民搬运入库");
            if(Button(c,R(554,674,578,40),"pack.execute",m.Busy?"正在生成…":"添加整包 · "+total+" 件",1,m.Ready&&error==null&&!m.Busy))m.Emit("pack.execute","");
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
            Note(c,MainX+20,601,MainW-40,"自动保存收藏、物资包、数量、已选物品、窗口位置、键位和自动暂停偏好。");
            Note(c,MainX+20,639,MainW-40,"不保存属性锁和危险操作的启用状态；自定义按键会检查游戏键位与重复占用。");
            if(Button(c,R(MainX+20,682,235,29),"prefs.default","恢复默认键位与暂停偏好",0,true))m.Emit("prefs.default","");
            Note(c,MainX+278,685,MainW-318,"保留你的收藏和物资包。");
        }
    }
}
