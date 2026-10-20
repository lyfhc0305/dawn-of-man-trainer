using System;
using System.Collections.Generic;
using DawnOfMan;
using MadrugaShared;
using UnityEngine;

namespace DawnTrainer
{
    public sealed partial class Trainer
    {
        private int featureTestStage, featureTestFrame;
        private float featureTestDeadline;
        private Buildable featureTestBlueprint;
        private readonly Dictionary<string,int> featureTestBefore = new Dictionary<string,int>();
        private List<SupplyLine> featureTestExpected;
        private Human featureTestHuman;
        private void TestSelect(PhysicalEntity entity)
        { Selection s=TransientSingleton<Selection>.CurrentInstance;s.clear();if(entity!=null)s.select(entity);s.update(0); }
        private void TestGroup(string name,Action test,Action<bool,string> check)
        { try{test();}catch(Exception ex){check(false,name+": "+ex);Debug.LogError("[DawnTrainer.Test] "+name+": "+ex);} }
        internal void BeginFeatureIntegration(Action<bool,string> check)
        {
            if(!isolatedTestSession)throw new InvalidOperationException("Integration tests require an isolated opt-in session");
            featureTestHuman=manager.getPlayerHumans()[0];Vector3 pos=featureTestHuman.getPosition();
            TimeManager time=TransientSingleton<TimeManager>.CurrentInstance;
            preferences.AutoPause=false;visible=false;overlayPause.Clear();autoPauseRequested=false;
            TestGroup("native speed and pause",delegate
            {
                time.setTimeScale(8);TogglePause();check(time.getTimeScale()==0,"runtime manual pause from native 8x");TogglePause();check(time.getTimeScale()==8,"runtime manual resume restores native 8x");
                time.setTimeScale(.25f);TogglePause();TogglePause();check(time.getTimeScale()==.25f,"runtime manual pause preserves quarter speed");
                preferences.AutoPause=true;visible=true;SyncOverlayPause();check(time.getTimeScale()==0&&overlayPause.Owns,"runtime overlay pauses quarter speed");
                SetSpeed(.5f);check(time.getTimeScale()==0&&overlayPause.Desired==.5f,"runtime overlay speed selection stays paused");
                visible=false;SyncOverlayPause();check(time.getTimeScale()==.5f,"runtime overlay close restores chosen half speed");
                time.setTimeScale(0);visible=true;SyncOverlayPause();check(!overlayPause.Owns,"runtime originally paused world remains unowned");
                SetSpeed(8);check(time.getTimeScale()==0,"runtime originally paused overlay holds explicitly selected 8x");
                visible=false;SyncOverlayPause();check(time.getTimeScale()==8,"runtime originally paused overlay applies 8x only on close");
                time.realPause();visible=true;SyncOverlayPause();bool blocked=false;try{SetSpeed(2);}catch(InvalidOperationException){blocked=true;}
                visible=false;SyncOverlayPause();check(blocked&&time.isRealPaused(),"runtime speed actions do not release native real pause");time.unpause();
            },check);
            preferences.AutoPause=false;visible=false;overlayPause.Clear();autoPauseRequested=false;time.setTimeScale(0);
            TestGroup("reset and confirmation regression",delegate
            {
                time.setTimeScale(8);time.realPause();Reset();
                check(time.isRealPaused()&&overlayPause.ResetPending&&lastRunningSpeed==1,"runtime reset queues 1x while preserving the native menu pause");
                time.unpause();SyncOverlayPause();check(time.getTimeScale()==1&&!overlayPause.ResetPending,"runtime reset restores actual 1x when the native menu closes");
                preferences.AutoPause=true;visible=true;time.setTimeScale(8);SyncOverlayPause();time.realPause();Reset();
                check(time.isRealPaused()&&overlayPause.ResetPending,"runtime reset with automatic pause never releases a native menu pause");
                time.unpause();SyncOverlayPause();check(time.getTimeScale()==0&&overlayPause.Owns&&overlayPause.Desired==1,"runtime reset resumes into the panel pause at desired 1x");
                visible=false;SyncOverlayPause();check(time.getTimeScale()==1,"runtime closing the panel after reset restores 1x");
                preferences.AutoPause=false;TestSelect(featureTestHuman);CancelKillConfirmation();
                check(!HotkeyKillConfirmed(),"runtime villager kill starts with an unconfirmed first press");
                TestSelect(null);ObserveKillConfirmation();TestSelect(featureTestHuman);ObserveKillConfirmation();
                check(!HotkeyKillConfirmed(),"runtime selecting away and back invalidates the earlier kill confirmation");
                OnUiAction("page","1");check(!HotkeyKillConfirmed(),"runtime page navigation invalidates the earlier kill confirmation");
                CancelKillConfirmation();time.setTimeScale(0);
            },check);
            TestGroup("selected rescue",delegate
            {
                TestSelect(featureTestHuman);Stat[] values=BeingStats(featureTestHuman);foreach(Stat stat in values)stat.setValue(.2f);
                Human other=manager.getPlayerHumans().Count>1?manager.getPlayerHumans()[1]:null;if(other!=null)other.getHealth().setValue(.43f);
                RescueSelected(false);check(values[0].getValue()==1&&values[1].getValue()==.2f,"runtime selected health rescue leaves other survival stats unchanged");
                RescueSelected(true);bool full=true;foreach(Stat stat in values)full&=stat.getValue()==1;check(full,"runtime selected survival rescue fills all seven human stats");
                check(other==null||Mathf.Abs(other.getHealth().getValue()-.43f)<.001f,"runtime rescue does not change unselected residents");
                Animal wild=manager.createEntityOnFloor<Animal>(animals[0],new EntityParameters(pos+new Vector3(35,0,8),Quaternion.identity,Age.Adult,Gender.Male));wild.onSpawn();wild.getHealth().setValue(.31f);
                TestSelect(wild);RescueSelected(true);check(Mathf.Abs(wild.getHealth().getValue()-.31f)<.001f,"runtime rescue ignores wild or non-player animals");
            },check);
            TestGroup("repair and delivery plan",delegate
            {
                StructureType type=(StructureType)TransientSingleton<EntityTypeManager>.CurrentInstance.getTypeEntities<StructureType>().Find(t=>t.getId()=="tent");
                Buildable b=manager.createBuildable(typeof(Structure),type,GameUtil.floorPosition(pos+new Vector3(28,0,-16)),Quaternion.identity,0,0,type.getCosts().clone());b.onSpawn();TestSelect(b);CompleteSelected();
                Structure structure=(Structure)b;structure.getCondition().setValue(.2f);RepairSelected(false);
                check(structure.getCondition().getValue()==1,"runtime structure repair restores condition through native model path");
                ResourceType tool=(ResourceType)resources.Find(t=>t.getId()=="flint_axe");Resource item=manager.createResourceOnFloor(tool,pos+new Vector3(6,0,1));item.getCondition().setValue(.2f);TestSelect(item);RepairSelected(true);
                check(item.getCondition().getValue()==1,"runtime selected tool durability restores to full");
                Resource food=manager.createResourceOnFloor((ResourceType)resources.Find(t=>t.getId()=="raw_meat"),pos+new Vector3(7,0,1));food.getCondition().setValue(.22f);TestSelect(food);RepairSelected(true);
                check(Mathf.Abs(food.getCondition().getValue()-.22f)<.001f,"runtime durability action leaves non-tool food unchanged");
                featureTestBlueprint=manager.createBuildable(typeof(Structure),type,GameUtil.floorPosition(pos+new Vector3(31,0,-8)),Quaternion.identity,0,0,type.getCosts().clone());featureTestBlueprint.onSpawn();
                TestSelect(featureTestBlueprint);int before=featureTestBlueprint.getPendingConstructionMaterials().calculateTotalCount();List<SpawnJob> plan;
                List<SupplyLine> missing=MissingMaterials(SelectionSnapshot(),out plan);
                check(plan.Count>0&&missing.Count>0&&featureTestBlueprint.getPendingConstructionMaterials().calculateTotalCount()==before,"runtime missing-material preview clones without delivering or deleting materials");
                Buildable recycling=manager.createBuildable(typeof(Structure),type,GameUtil.floorPosition(pos+new Vector3(33,0,-17)),Quaternion.identity,0,0,type.getCosts().clone());recycling.onSpawn();
                List<Resource> recycled=null;recycling.increaseRecycleProgress(.1f,ref recycled);TestSelect(recycling);
                check(recycling.isBeingRecycled()&&!CanComplete(recycling)&&CompleteSelected()==0&&!recycling.isBuilt(),"runtime quick build ignores a blueprint already being recycled");
                if(ui==null)ui=new UiModel{Preferences=preferences};UpdateUiModel();
                check(ui.BuildTargets==0,"runtime target summary excludes a blueprint already being recycled");
            },check);
            TestGroup("native capture conversion",delegate
            {
                AnimalType wildType=null;foreach(EntityType entityType in animals){AnimalType candidate=(AnimalType)entityType;if(candidate.getDomesticatedVariant()!=null&&candidate.getDomesticationTech()!=null){wildType=candidate;break;}}
                if(wildType==null)throw new InvalidOperationException("No tameable animal type");
                TransientSingleton<KnowledgeManager>.CurrentInstance.unlockTech(wildType.getDomesticationTech(),UnlockTechFlags.NoKnowledgePoints|UnlockTechFlags.NoMessage);
                Animal animal=manager.createEntityOnFloor<Animal>(wildType,new EntityParameters(pos+new Vector3(40,0,14),Quaternion.identity,Age.Young,Gender.Female));animal.onSpawn();
                string reason;check(CanTame(animal,out reason),"runtime native capture eligibility accepts unlocked young "+wildType.getId());
                AnimalType domesticType=wildType.getDomesticatedVariant();int before=manager.getEntityCount(domesticType,Team.Player);TestSelect(animal);TameSelected();
                check(manager.getEntityCount(domesticType,Team.Player)==before+1&&manager.isPendingDestruction(animal),"runtime taming creates domestic variant and retires wild original");
                check(!Valid(animal)&&!SelectionSnapshot().Contains(animal)&&!CanKill(animal)&&KillSelected()==0,"runtime actions ignore the pending-destruction original after taming");
                UpdateUiModel();check(ui.KillTargets==0,"runtime target summary excludes the pending-destruction original after taming");
                Animal adult=manager.createEntityOnFloor<Animal>(wildType,new EntityParameters(pos+new Vector3(43,0,14),Quaternion.identity,Age.Adult,Gender.Male));adult.onSpawn();
                check(!CanTame(adult,out reason),"runtime native capture eligibility rejects adult wild animal");
            },check);
            TestGroup("native visitors",delegate
            {
                check(manager.hasResidentCapacity(),"runtime test settlement has housing for migrants");int before=CountVisitors<Migrant>();Invite(true);int after=CountVisitors<Migrant>();
                check(after>before,"runtime invite creates actual edge migrants");bool blocked=false;try{Invite(true);}catch(InvalidOperationException){blocked=true;}
                check(blocked&&CountVisitors<Migrant>()==after,"runtime duplicate migrant invitation is blocked");
                int traders=CountVisitors<Trader>();Invite(false);int arrived=CountVisitors<Trader>();check(arrived>traders,"runtime invite creates actual native trader party");
                blocked=false;try{Invite(false);}catch(InvalidOperationException){blocked=true;}check(blocked&&CountVisitors<Trader>()==arrived,"runtime duplicate trader invitation is blocked");
            },check);
            TestGroup("preferences and live bindings",delegate
            {
                preferences.Favorites.Add("flint");SavePreferences(true);TrainerPreferences loaded=TrainerPreferences.Load(preferencePath);
                check(loaded.Favorites.Contains("flint"),"runtime favorites persist in isolated preference file");
                check(FeaturePolicy.ShortcutError("F8",0,preferences.Keys,ReservedKeys())!=null,"runtime key validation protects actual F8 game binding");
            },check);
            if(featureTestBlueprint==null)throw new InvalidOperationException("Missing delivery test blueprint");
            TestSelect(featureTestBlueprint);List<SpawnJob> planned;featureTestExpected=MissingMaterials(SelectionSnapshot(),out planned);featureTestBefore.Clear();
            foreach(SupplyLine line in featureTestExpected)featureTestBefore[line.Id]=manager.getEntityCount(resources.Find(t=>t.getId()==line.Id));
            SupplyBuildings();featureTestStage=1;featureTestDeadline=Time.realtimeSinceStartup+20;
        }
        internal bool PollFeatureIntegration(Action<bool,string> check)
        {
            if(Time.realtimeSinceStartup>featureTestDeadline)throw new TimeoutException("Extended integration stage "+featureTestStage);
            if(jobs.Count>0||actions.Count>0)return false;
            if(featureTestStage==1)
            {
                bool exact=true;foreach(SupplyLine line in featureTestExpected)exact&=manager.getEntityCount(resources.Find(t=>t.getId()==line.Id))-featureTestBefore[line.Id]==line.Count;
                check(exact,"runtime building supply generates exact native missing-material quantities");
                check(!featureTestBlueprint.isBuilt()&&featureTestBlueprint.anyPendingConstructionMaterials(),"runtime building supply leaves blueprint and delivery tasks intact");
                TestSelect(featureTestHuman);spawnLocation=0;
                SupplyPack pack=new SupplyPack{Name="运行时快照测试"};pack.Lines.Add(new SupplyLine{Id="log",Quantity="17"});pack.Lines.Add(new SupplyLine{Id="stone",Quantity="9"});preferences.Packs.Add(pack);
                if(ui==null)ui=new UiModel{Preferences=preferences};ui.ActivePack=preferences.Packs.Count-1;
                featureTestBefore["log"]=manager.getEntityCount(resources.Find(t=>t.getId()=="log"));featureTestBefore["stone"]=manager.getEntityCount(resources.Find(t=>t.getId()=="stone"));
                FeatureUiAction("pack.execute","");pack.Lines[0].Quantity="99";
                featureTestStage=2;featureTestDeadline=Time.realtimeSinceStartup+20;return false;
            }
            if(featureTestStage==2)
            {
                check(manager.getEntityCount(resources.Find(t=>t.getId()=="log"))-featureTestBefore["log"]==17&&manager.getEntityCount(resources.Find(t=>t.getId()=="stone"))-featureTestBefore["stone"]==9,"runtime pack executes captured multi-resource snapshot after editing source");
                featureTestBefore["log"]=manager.getEntityCount(resources.Find(t=>t.getId()=="log"));
                QueuePack(new List<SupplyLine>{new SupplyLine{Id="log",Quantity="100"}},"停止测试");ProcessJobs();CancelSpawns();featureTestFrame=Time.frameCount;
                featureTestStage=3;featureTestDeadline=Time.realtimeSinceStartup+20;return false;
            }
            if(featureTestStage==3)
            {
                if(Time.frameCount-featureTestFrame<3)return false;
                check(manager.getEntityCount(resources.Find(t=>t.getId()=="log"))-featureTestBefore["log"]==12,"runtime cancel preserves generated batch and stops remaining 88 items");
                TestSelect(featureTestHuman);preferences.AutoPause=false;overlayPause.Clear();autoPauseRequested=false;TransientSingleton<TimeManager>.CurrentInstance.setTimeScale(0);
                BeginMultiplierIntegration(check);
                SavePreferences(true);featureTestStage=4;return true;
            }
            return false;
        }
    }
}
