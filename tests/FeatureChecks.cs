using System;
using System.Collections.Generic;
using System.IO;
using DawnTrainer;

internal static class FeatureChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(ProductionPolicy.ValidMultiplier(1,10)&&ProductionPolicy.ValidMultiplier(10,10)&&ProductionPolicy.ValidMultiplier(20,20),"multipliers accept integral minimum and maximum values");
        check(!ProductionPolicy.ValidMultiplier(0,10)&&!ProductionPolicy.ValidMultiplier(-1,10)&&!ProductionPolicy.ValidMultiplier(11,10),"multipliers reject values below 1 or above their supported range");
        check(ProductionPolicy.ScaledCapacity(3,1)==3&&ProductionPolicy.ScaledCapacity(3,10)==30,"capacity multiplier preserves 1x and scales actual per-slot capacity");
        check(ProductionPolicy.ScaledCapacity(Int32.MaxValue,10)==Int32.MaxValue,"capacity calculation saturates without integer overflow");
        check(ProductionPolicy.ScaledCapacity(0,10)==0&&ProductionPolicy.ScaledCapacity(-1,10)==-1,"unavailable capacity is never made positive by a multiplier");
        check(ProductionPolicy.ScaledCapacity(3,0)==3&&ProductionPolicy.ScaledCapacity(3,11)==3,"invalid capacity factors fall back to the native value");
        check(Near(ProductionPolicy.ScaleBuildWork(.25f,1),.25f)&&Near(ProductionPolicy.ScaleBuildWork(.25f,20),5),"construction work scales without changing elapsed game time");
        check(ProductionPolicy.ScaleBuildWork(Single.NaN,5)==0&&ProductionPolicy.ScaleBuildWork(Single.PositiveInfinity,5)==0&&ProductionPolicy.ScaleBuildWork(-1,5)==0,"invalid construction work cannot corrupt a build progress value");
        check(ProductionPolicy.ScaleBuildWork(Single.MaxValue,20)==Single.MaxValue&&ProductionPolicy.ScaleBuildWork(1,0)==1,"construction work remains finite and invalid factors preserve 1x");
        TrainerPreferences p=TrainerPreferences.Defaults();p.Favorites.Add("flint");p.AutoPause=true;p.Keys[0]="Home";p.Quantity="123";p.WindowX=42.25f;p.UiScale=125;p.Dock=2;p.Collapsed=true;
        p.Packs[0].Name="冬季=补给\n中文";
        TrainerPreferences copy=TrainerPreferences.Deserialize(p.Serialize());
        check(copy.Favorites.Contains("flint")&&copy.Packs[0].Name==p.Packs[0].Name&&copy.Packs[0].Lines[0].Count==20,"preferences preserve Unicode, favorites and multi-resource packs");
        check(copy.AutoPause&&copy.Keys[0]=="Home"&&copy.Quantity=="123"&&copy.WindowX==42.25f,"preferences preserve shortcuts, quantities and window placement");
        check(copy.UiScale==125&&TrainerPreferences.Deserialize("Version=1\nUiScale=999\n").UiScale==100,"interface size persists and unknown sizes fall back to automatic");
        check(copy.Dock==2&&copy.Collapsed&&TrainerPreferences.Defaults().Dock==0&&!TrainerPreferences.Defaults().Collapsed,"dock side and collapsed rail persist; defaults dock right and expanded");
        TrainerPreferences badDock=TrainerPreferences.Deserialize("Version=1\nDock=7\nCollapsed=maybe\n");
        check(badDock.Dock==0&&!badDock.Collapsed,"unknown dock sides and damaged collapse flags fall back to the right-docked drawer");
        float s1080=FeaturePolicy.DrawerScale(1920,1080,100);
        check(Near(s1080,1)&&Near(FeaturePolicy.DrawerScale(2560,1440,100),1.3f)&&Near(FeaturePolicy.DrawerScale(3840,2160,100),2),"automatic size keeps the drawer near 58% of screen height");
        check(Near(FeaturePolicy.DrawerScale(1280,720,100),1)&&Near(FeaturePolicy.DrawerScale(1366,768,100),1),"small screens never shrink the drawer below 1:1");
        check(FeaturePolicy.DrawerScale(1920,1080,125)>s1080&&FeaturePolicy.DrawerScale(1920,1080,80)<s1080,"interface size preference scales the drawer");
        bool fits=true;
        foreach(int[] screen in new int[][]{new[]{1280,720},new[]{1366,768},new[]{1920,1080},new[]{2560,1080},new[]{2560,1440},new[]{3840,2160}})
        foreach(int percent in FeaturePolicy.UiScales){float s=FeaturePolicy.DrawerScale(screen[0],screen[1],percent);fits&=FeaturePolicy.DrawerWidth*s<=screen[0]-24&&FeaturePolicy.DrawerHeight*s<=screen[1]-24;}
        check(fits,"every interface size fits common resolutions");
        float dx,dy;FeaturePolicy.DockPosition(0,1920,1080,400,620,out dx,out dy);
        check(Near(dx,1508)&&Near(dy,108),"right dock keeps a 12-unit edge and starts below the top-right status bar");
        check(dy+620<=1080*.72f,"on 16:9 screens the right dock ends above the game's selected-unit card");
        FeaturePolicy.DockPosition(1,1920,1080,400,620,out dx,out dy);
        check(Near(dx,12)&&Near(dy,1080*.28f),"left dock starts below the game's resource bar");
        FeaturePolicy.DockPosition(0,1280,720,400,620,out dx,out dy);
        check(Near(dy,72)&&dy+620<=720-12,"short screens keep the docked drawer fully visible");
        FeaturePolicy.DockPosition(0,1920,1080,56,520,out dx,out dy);
        check(Near(dx,1920-56-12),"the collapsed rail stays anchored to the docked edge");
        FeaturePolicy.DockPosition(0,Single.NaN,Single.NaN,400,620,out dx,out dy);
        check(Near(dx,12)&&Near(dy,12),"invalid screen sizes fall back to a visible dock position");
        check(FeaturePolicy.ClampPanelPosition(-100,1920,1180)==12&&FeaturePolicy.ClampPanelPosition(2000,1920,1180)==728,"dragging beyond left and right edges clamps without recentering");
        check(FeaturePolicy.ClampPanelPosition(0,1080,800)==12&&FeaturePolicy.ClampPanelPosition(400,1080,800)==268&&FeaturePolicy.ClampPanelPosition(80,1080,800)==80,"top and bottom edges clamp while valid window positions stay fixed");
        check(FeaturePolicy.ClampPanelPosition(100,1180,1180)==0&&FeaturePolicy.ClampPanelPosition(100,1180,1200)==0,"window clamp handles a screen with no spare space");
        check(FeaturePolicy.ClampPanelPosition(Single.NaN,1920,1180)==370&&FeaturePolicy.ClampPanelPosition(Single.PositiveInfinity,1920,1180)==370,"invalid window coordinates recover to screen center");
        TrainerPreferences malformed=TrainerPreferences.Deserialize("Version=1\nWindowX=NaN\nWindowY=Infinity\nQuantity=9999\nAnimalQuantity=-1\nFavorite=%%%\n");
        check(malformed.WindowX==0&&malformed.WindowY==0&&malformed.Quantity=="10"&&malformed.AnimalQuantity=="1"&&malformed.Favorites.Count==0,"damaged entries and out-of-range preferences fall back safely");
        TrainerPreferences duplicate=TrainerPreferences.Deserialize("Version=1\nKey0=Home\nKey1=Home\nKey2=F10\n");
        check(duplicate.Keys[0]=="Insert"&&duplicate.Keys[1]=="Delete","duplicate saved bindings recover to distinct defaults");
        check(Throws(delegate{TrainerPreferences.Deserialize(new string('x',262145));}),"oversized preference files are rejected");
        check(Throws(delegate{TrainerPreferences.Deserialize("Version=99\n");}),"unknown preference format is rejected");
        string folder=Path.Combine(Path.GetDirectoryName(typeof(FeatureChecks).Assembly.Location),"preference-test-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(folder,"preferences.ini");p.Save(path);p.Quantity="77";p.Save(path);
        check(TrainerPreferences.Load(path).Quantity=="77"&&!File.Exists(path+".tmp"),"preference updates replace a complete staged file");
        string unreadable=Path.Combine(folder,"unreadable.ini");File.WriteAllText(unreadable,"Version=99\n");bool writable;string problem;
        TrainerPreferences recovered=TrainerPreferences.LoadSafely(unreadable,out writable,out problem);
        check(writable&&problem!=null&&recovered.Packs.Count==2&&Directory.GetFiles(folder,"unreadable.ini.unreadable-*").Length==1&&File.ReadAllText(unreadable)=="Version=99\n","unreadable preferences are backed up before defaults can be saved");
        using(FileStream locked=new FileStream(unreadable,FileMode.Open,FileAccess.ReadWrite,FileShare.None))recovered=TrainerPreferences.LoadSafely(unreadable,out writable,out problem);
        check(!writable&&problem!=null&&recovered.Packs.Count==2,"locked preferences are not overwritten this session");
        TrainerPreferences empty=TrainerPreferences.Deserialize("Version=1\n");check(empty.Packs.Count==0,"deleting all packs remains empty after reload");
        HashSet<string> available=new HashSet<string>{"log","stone"};List<SupplyLine> lines=new List<SupplyLine>{new SupplyLine{Id="log",Quantity="20"},new SupplyLine{Id="stone",Quantity="6"}};
        check(FeaturePolicy.ValidateSupply(lines,available)==26,"multi-resource plan validates all quantities before enqueue");
        lines.Add(new SupplyLine{Id="missing",Quantity="1"});check(Throws(delegate{FeaturePolicy.ValidateSupply(lines,available);}),"unknown resource rejects the whole supply plan");lines.RemoveAt(2);
        foreach(string bad in new string[]{"0","-1","1001","abc",""})
        {lines[0].Quantity=bad;check(Throws(delegate{FeaturePolicy.ValidateSupply(lines,available);}),"invalid supply amount rejects the whole plan: "+bad);}
        lines.Clear();for(int i=0;i<5;i++)lines.Add(new SupplyLine{Id="log",Quantity="1000"});
        check(FeaturePolicy.ValidateSupply(lines,available)==5000,"batch limit accepts exactly 5000 items");
        lines.Add(new SupplyLine{Id="log",Quantity="1"});check(Throws(delegate{FeaturePolicy.ValidateSupply(lines,available);}),"batch limit rejects excess before generating anything");
        lines.Clear();for(int i=0;i<129;i++)lines.Add(new SupplyLine{Id="log",Quantity="1"});
        check(Throws(delegate{FeaturePolicy.ValidateSupply(lines,available);}),"oversized job lists cannot enter the queue");
        check(FeaturePolicy.ValidateSupply(lines,available,FeaturePolicy.MaximumTotal)==129,"building supply plans may exceed 128 per-building jobs within the 5000 item limit");
        OverlayPauseState clock=new OverlayPauseState();
        check(clock.Open(.25f,false)==0&&clock.Owns&&clock.Close(0,false)==.25f,"overlay pause restores quarter-speed exactly");
        check(!clock.Open(0,false).HasValue&&!clock.Close(0,false).HasValue,"opening an already paused world does not auto-resume it");
        check(!clock.Open(.5f,true).HasValue&&!clock.Owns,"overlay never acquires a native real pause");
        clock.Open(.5f,false);check(!clock.Close(0,true).HasValue&&!clock.Owns,"closing overlay cannot release a native menu pause");
        clock.Open(.5f,false);clock.Desired=2;check(clock.Close(0,false)==2,"manual speed selection updates the pending restore speed");
        clock.Open(.5f,false);clock.Desired=0;check(clock.Close(0,false)==0,"manual pause remains paused after closing overlay");
        clock.Open(.5f,false);check(!clock.Close(4,false).HasValue,"external speed changes are not overwritten on close");
        clock.Open(.5f,false);clock.Clear();check(!clock.Close(0,false).HasValue,"map changes discard pause ownership");
        clock.Open(.5f,false);clock.Observe(0);bool held=clock.Owns;clock.Observe(2);
        check(held&&!clock.Owns&&!clock.Close(2,false).HasValue,"a speed set with the game's own keys ends the panel pause hold");
        float last=1;float running=8;running=FeaturePolicy.NextPauseSpeed(running,ref last);running=FeaturePolicy.NextPauseSpeed(running,ref last);
        check(running==8,"manual pause and resume preserve an externally selected 8x speed");
        last=1;running=.25f;running=FeaturePolicy.NextPauseSpeed(running,ref last);running=FeaturePolicy.NextPauseSpeed(running,ref last);
        check(running==.25f,"manual pause and resume preserve externally selected slow speed");
        clock.Clear();clock.Open(8,false);last=1;float selected=FeaturePolicy.NextPauseSpeed(clock.Desired,ref last);
        float actual=clock.RequestSpeed(selected,true);selected=FeaturePolicy.NextPauseSpeed(clock.Desired,ref last);actual=clock.RequestSpeed(selected,true);
        check(actual==0&&clock.Close(actual,false)==8,"manual pause and continue during automatic pause keep world stopped and restore 8x");
        clock.Clear();clock.Open(0,false);actual=clock.RequestSpeed(8,true);
        check(actual==0&&clock.Owns&&clock.Close(actual,false)==8,"explicit speed selection in an originally paused overlay waits until close");
        clock.Clear();clock.Open(0,false);check(!clock.Close(0,false).HasValue,"original pause without explicit speed selection remains untouched");
        clock.Open(8,false);
        check(!clock.ResetSpeed(true,false).HasValue&&clock.ResetPending&&!clock.Owns&&clock.Desired==1&&!clock.ResumeReset(true,false).HasValue,"reset during a native menu pause queues 1x without changing the game clock");
        check(clock.ResumeReset(false,false)==1&&!clock.ResetPending&&!clock.ResumeReset(false,false).HasValue,"pending reset applies 1x exactly once after the native menu closes");
        clock.ResetSpeed(true,true);actual=clock.ResumeReset(false,true).Value;
        check(actual==0&&clock.Owns&&clock.Desired==1&&clock.Close(actual,false)==1,"pending reset honors an open automatically paused panel and restores 1x on close");
        clock.ResetSpeed(true,true);clock.Close(0,true);
        check(clock.ResumeReset(false,false)==1,"closing the panel before the menu does not discard the pending reset");
        clock.ResetSpeed(true,false);clock.Clear();
        check(!clock.ResetPending&&!clock.ResumeReset(false,false).HasValue,"map changes discard pending speed resets");
        check(clock.ResetSpeed(false,false)==1&&clock.ResetSpeed(false,true)==0&&clock.Close(0,false)==1,"ordinary reset restores 1x and overlay reset waits until the panel closes");
        SelectionConfirmation confirmation=new SelectionConfirmation();
        check(!confirmation.Confirm("villager-a",10)&&confirmation.Confirm("villager-a",13.99f)&&!confirmation.Confirm("villager-a",14),"kill confirmation requires a second press within four seconds and is consumed once");
        confirmation.Clear();confirmation.Confirm("villager-a",10);
        check(!confirmation.Confirm("villager-a",14),"kill confirmation expires exactly at its deadline");
        confirmation.Clear();confirmation.Confirm("villager-a",10);confirmation.Observe("animal-b",11);confirmation.Observe("villager-a",12);
        check(!confirmation.Confirm("villager-a",13),"switching away and back cannot reuse the original kill confirmation");
        confirmation.Clear();confirmation.Confirm("villager-a",10);confirmation.Clear();
        check(!confirmation.Confirm("villager-a",11),"explicit page or visibility cancellation requires a fresh kill confirmation");
        string[] keys={"Insert","Delete","F10"};HashSet<string> reserved=new HashSet<string>{"F8","F9","F11","F12","W"};
        check(FeaturePolicy.ShortcutError("Home",0,keys,reserved)==null,"unassigned custom shortcut is accepted");
        check(FeaturePolicy.ShortcutError("Delete",0,keys,reserved)!=null,"duplicate trainer shortcuts are rejected");
        check(FeaturePolicy.ShortcutError("F8",0,keys,reserved)!=null&&FeaturePolicy.ShortcutError("W",0,keys,reserved)!=null,"reserved and game-bound keys are rejected");
        check(FeaturePolicy.ShortcutError("Mouse0",0,keys,reserved)!=null&&FeaturePolicy.ShortcutError("LeftShift",0,keys,reserved)!=null,"mouse and modifier-only bindings are rejected");
    }
    private static bool Near(float a,float b){return Math.Abs(a-b)<.001f;}
    private static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}catch(InvalidDataException){return true;}}
}
