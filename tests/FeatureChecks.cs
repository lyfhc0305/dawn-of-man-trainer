using System;
using System.Collections.Generic;
using System.IO;
using DawnTrainer;

internal static class FeatureChecks
{
    internal static void Run(Action<bool,string> check)
    {
        TrainerPreferences p=TrainerPreferences.Defaults();p.Favorites.Add("flint");p.AutoPause=true;p.Keys[0]="Home";p.Quantity="123";p.WindowX=42.25f;
        p.Packs[0].Name="冬季=补给\n中文";
        TrainerPreferences copy=TrainerPreferences.Deserialize(p.Serialize());
        check(copy.Favorites.Contains("flint")&&copy.Packs[0].Name==p.Packs[0].Name&&copy.Packs[0].Lines[0].Count==20,"preferences preserve Unicode, favorites and multi-resource packs");
        check(copy.AutoPause&&copy.Keys[0]=="Home"&&copy.Quantity=="123"&&copy.WindowX==42.25f,"preferences preserve shortcuts, quantities and window placement");
        TrainerPreferences malformed=TrainerPreferences.Deserialize("Version=1\nWindowX=NaN\nWindowY=Infinity\nQuantity=9999\nAnimalQuantity=-1\nFavorite=%%%\n");
        check(malformed.WindowX==0&&malformed.WindowY==0&&malformed.Quantity=="10"&&malformed.AnimalQuantity=="1"&&malformed.Favorites.Count==0,"damaged entries and out-of-range preferences fall back safely");
        TrainerPreferences duplicate=TrainerPreferences.Deserialize("Version=1\nKey0=Home\nKey1=Home\nKey2=F10\n");
        check(duplicate.Keys[0]=="Insert"&&duplicate.Keys[1]=="Delete","duplicate saved bindings recover to distinct defaults");
        check(Throws(delegate{TrainerPreferences.Deserialize(new string('x',262145));}),"oversized preference files are rejected");
        check(Throws(delegate{TrainerPreferences.Deserialize("Version=99\n");}),"unknown preference format is rejected");
        string folder=Path.Combine(Path.GetDirectoryName(typeof(FeatureChecks).Assembly.Location),"preference-test-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(folder,"preferences.ini");p.Save(path);p.Quantity="77";p.Save(path);
        check(TrainerPreferences.Load(path).Quantity=="77"&&!File.Exists(path+".tmp"),"preference updates replace a complete staged file");
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
        OverlayPauseState clock=new OverlayPauseState();
        check(clock.Open(.25f,false)==0&&clock.Owns&&clock.Close(0,false)==.25f,"overlay pause restores quarter-speed exactly");
        check(!clock.Open(0,false).HasValue&&!clock.Close(0,false).HasValue,"opening an already paused world does not auto-resume it");
        check(!clock.Open(.5f,true).HasValue&&!clock.Owns,"overlay never acquires a native real pause");
        clock.Open(.5f,false);check(!clock.Close(0,true).HasValue&&!clock.Owns,"closing overlay cannot release a native menu pause");
        clock.Open(.5f,false);clock.Desired=2;check(clock.Close(0,false)==2,"manual speed selection updates the pending restore speed");
        clock.Open(.5f,false);clock.Desired=0;check(clock.Close(0,false)==0,"manual pause remains paused after closing overlay");
        clock.Open(.5f,false);check(!clock.Close(4,false).HasValue,"external speed changes are not overwritten on close");
        clock.Open(.5f,false);clock.Clear();check(!clock.Close(0,false).HasValue,"map changes discard pause ownership");
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
        string[] keys={"Insert","Delete","F10"};HashSet<string> reserved=new HashSet<string>{"F8","F9","F11","F12","W"};
        check(FeaturePolicy.ShortcutError("Home",0,keys,reserved)==null,"unassigned custom shortcut is accepted");
        check(FeaturePolicy.ShortcutError("Delete",0,keys,reserved)!=null,"duplicate trainer shortcuts are rejected");
        check(FeaturePolicy.ShortcutError("F8",0,keys,reserved)!=null&&FeaturePolicy.ShortcutError("W",0,keys,reserved)!=null,"reserved and game-bound keys are rejected");
        check(FeaturePolicy.ShortcutError("Mouse0",0,keys,reserved)!=null&&FeaturePolicy.ShortcutError("LeftShift",0,keys,reserved)!=null,"mouse and modifier-only bindings are rejected");
    }
    private static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}catch(InvalidDataException){return true;}}
}
