using System;
using System.Collections.Generic;
using System.IO;
using DawnOfMan;
using MadrugaShared;
using UnityEngine;

namespace DawnTrainer
{
    internal static class IntegrationChecks
    {
        public static bool Finished;
        private static int passed, failed;
        private static int queueStage, queueBefore;
        private static EntityType queueType;
        private static float queueDeadline;
        internal static string ArtifactDirectory()
        {
            foreach(string arg in Environment.GetCommandLineArgs())
                if(arg.StartsWith("--trainer-test-dir=")) return Path.GetFullPath(arg.Substring("--trainer-test-dir=".Length));
            return Path.Combine(Path.GetTempPath(), "DawnTrainer-self-test");
        }
        private static void Check(bool condition, string name)
        {
            if (condition) { passed++; Debug.Log("[DawnTrainer.Test] PASS " + name); }
            else { failed++; Debug.LogError("[DawnTrainer.Test] FAIL " + name); }
        }
        public static void Run(Trainer trainer, EntityManager manager)
        {
            try
            {
                TimeManager time = TransientSingleton<TimeManager>.CurrentInstance;
                time.fakePause();
                EntityTypeManager types = TransientSingleton<EntityTypeManager>.CurrentInstance;
                List<EntityType> resources = types.getTypeEntities<ResourceType>();
                List<EntityType> animals = types.getTypeEntities<AnimalType>();
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--trainer-export-catalog") >= 0) ExportCatalog(resources, animals);
                Check(resources != null && resources.Count > 70, "resource catalog available: " + resources.Count);
                Check(animals != null && animals.Count >= 27, "animal catalog available: " + animals.Count);
                Human human = manager.getPlayerHumans()[0];
                Vector3 position = human.getPosition();
                Stat[] stats = { human.getHealth(), human.getNutrition(), human.getHydration(), human.getTemperature(), human.getStamina(), human.getRest(), human.getMorale() };
                for (int i = 0; i < 7; i++) Trainer.Locks[i] = true;
                trainer.RefreshLocks();
                for (int i = 0; i < stats.Length; i++)
                {
                    bool min = stats[i].decay(2f);
                    Check(!min && stats[i].getValue() == 1f, "locked " + stats[i].getStatType());
                }
                Stat unrelated = new Stat(StatType.Health);
                Check(unrelated.decay(2f) && unrelated.getValue() == 0, "unrelated stats remain normal");
                Array.Clear(Trainer.Locks, 0, 7); trainer.RefreshLocks();
                stats[4].setValue(1); stats[4].decay(.25f);
                Check(Mathf.Abs(stats[4].getValue() - .75f) < .001f, "disabling lock restores decay"); stats[4].setValue(1);
                int beforeKnowledge = TransientSingleton<KnowledgeManager>.CurrentInstance.getKnowledgeAmount();
                trainer.AddKnowledge(10);
                Check(TransientSingleton<KnowledgeManager>.CurrentInstance.getKnowledgeAmount() == beforeKnowledge + 10, "knowledge +10 exact delta");
                foreach (int speed in new int[] { 1, 2, 4, 8, 15 }) { time.setTimeScale(speed); Check(time.getTimeScale() == speed, "speed " + speed); }
                time.fakePause(); Check(time.getTimeScale() == 0, "pause"); time.unpause(); Check(time.getTimeScale() == 15, "resume previous speed"); time.fakePause();
                int resourcesOK = 0;
                foreach (EntityType type in resources)
                {
                    try
                    {
                        int before = manager.getEntityCount(type);
                        Resource item = manager.createResourceOnFloor((ResourceType)type, position + new Vector3(3, 0, 3));
                        TransientSingleton<KnowledgeManager>.CurrentInstance.onResourceAcquired((ResourceType)type, item.getPosition());
                        if (manager.getEntityCount(type) == before + 1 && item.getResourceType() == type && type.getIcon() != null) resourcesOK++;
                    }
                    catch (Exception ex) { Debug.LogError("[DawnTrainer.Test] Resource " + type.getId() + ": " + ex); }
                }
                Check(resourcesOK == resources.Count, "spawn all resource types with icons: " + resourcesOK + "/" + resources.Count);
                Animal victim = null;
                int animalsOK = 0;
                foreach (EntityType type in animals)
                {
                    try
                    {
                        int before = manager.getEntityCount(type);
                        Animal animal = manager.createEntityOnFloor<Animal>(type, new EntityParameters(position + new Vector3(12, 0, 12), Quaternion.identity, Age.Adult, Gender.Female));
                        if (animal.getTeam() == Team.Player) TransientSingleton<KnowledgeManager>.CurrentInstance.onNewResidentAnimal(animal);
                        animal.onSpawn();
                        if (animal.isAlive() && manager.getEntityCount(type) == before + 1 && animal.getAge() == Age.Adult && animal.getGender() == Gender.Female) animalsOK++;
                        victim = animal;
                    }
                    catch (Exception ex) { Debug.LogError("[DawnTrainer.Test] Animal " + type.getId() + ": " + ex); }
                }
                Check(animalsOK == animals.Count, "spawn all animal types / age / gender: " + animalsOK + "/" + animals.Count);
                Selection selection = TransientSingleton<Selection>.CurrentInstance;
                selection.clear(); selection.select(victim); selection.update(0);
                Check(trainer.KillSelected() == 1 && victim.isDead(), "kill selected animal");
                Check(trainer.KillSelected() == 0, "dead selection is ignored");
                selection.clear();
                StructureType structureType = null;
                foreach (EntityType type in types.getTypeEntities<StructureType>()) if (type.getId() == "tent") structureType = (StructureType)type;
                if (structureType == null) structureType = (StructureType)manager.getFirstEntity<Structure>().getEntityType();
                Buildable blueprint = manager.createBuildable(typeof(Structure), structureType, GameUtil.floorPosition(position + new Vector3(18, 0, -12)), Quaternion.identity, 0, 0, structureType.getCosts().clone());
                blueprint.onSpawn(); selection.select(blueprint); selection.update(0);
                KnowledgeManager knowledge = TransientSingleton<KnowledgeManager>.CurrentInstance;
                knowledge.getKnownEntities().setAmount(structureType, 0);
                int pointsBeforeBuild = knowledge.getKnowledgeAmount();
                Check(!blueprint.isBuilt() && trainer.CompleteSelected() == 1 && blueprint.isBuilt() && !blueprint.anyPendingConstructionMaterials(), "instant construction without materials");
                Check(knowledge.getKnownEntities().getAmount(structureType) == 1, "instant construction records first build count");
                int firstReward = knowledge.isKnowledgeEnabled() && structureType.isRelevantCount(1) ? structureType.getKnowledge() : 0;
                Check(knowledge.getKnowledgeAmount() == pointsBeforeBuild + firstReward, "instant construction awards first build knowledge");
                int pointsAfterBuild = knowledge.getKnowledgeAmount();
                Check(trainer.CompleteSelected() == 0 && knowledge.getKnownEntities().getAmount(structureType) == 1
                    && knowledge.getKnowledgeAmount() == pointsAfterBuild, "repeated construction gives no duplicate count or reward"); selection.clear();
                Check(trainer.KillSelected() == 0 && trainer.CompleteSelected() == 0, "empty selection safe");
                foreach (Weather weather in new Weather[] { Weather.Rainy, Weather.Snowy, Weather.Storm, Weather.Blizzard, Weather.Sunny })
                { TransientSingleton<SeasonManager>.CurrentInstance.setWeather(weather); Check(TransientSingleton<TerrainManager>.CurrentInstance.getWeather() == weather, "weather " + weather); }
                queueType = resources[0]; queueBefore = manager.getEntityCount(queueType); queueStage = 1; queueDeadline = Time.realtimeSinceStartup + 10;
                trainer.QueueSpawn(queueType, 37, position, Age.Count, Gender.Count);
            }
            catch (Exception ex) { failed++; Debug.LogError("[DawnTrainer.Test] FATAL " + ex); End(); }
        }
        public static void Poll(Trainer trainer, EntityManager manager)
        {
            if (Finished || queueStage == 0 || manager == null) return;
            if (queueStage == 3)
            {
                try { if (trainer.PollFeatureIntegration(Check)) End(); }
                catch(Exception ex) { failed++; Debug.LogError("[DawnTrainer.Test] EXTENDED FATAL " + ex); End(); }
                return;
            }
            int expected = queueStage == 1 ? 37 : 3;
            int actual = manager.getEntityCount(queueType) - queueBefore;
            if (actual < expected && Time.realtimeSinceStartup < queueDeadline) return;
            Check(actual == expected, queueStage == 1 ? "batched resource spawn exact quantity 37" : "batched animal spawn exact quantity 3");
            if (queueStage == 1)
            {
                queueType = TransientSingleton<EntityTypeManager>.CurrentInstance.getTypeEntities<AnimalType>()[0];
                queueBefore = manager.getEntityCount(queueType); queueStage = 2; queueDeadline = Time.realtimeSinceStartup + 10;
                trainer.QueueSpawn(queueType, 3, manager.getPlayerHumans()[0].getPosition(), Age.Adult, Gender.Male);
            }
            else { queueStage = 3; trainer.BeginFeatureIntegration(Check); }
        }
        private static void End()
        {
            Debug.Log("[DawnTrainer.Test] RESULT passed=" + passed + " failed=" + failed);
            Directory.CreateDirectory(ArtifactDirectory());
            File.WriteAllText(Path.Combine(ArtifactDirectory(), "result.txt"), "passed="+passed+" failed="+failed);
            Finished = true;
        }
        private static void ExportCatalog(List<EntityType> resources, List<EntityType> animals)
        {
            string folder=ArtifactDirectory(),icons=Path.Combine(folder,"icons");Directory.CreateDirectory(icons);
            System.Text.StringBuilder catalog=new System.Text.StringBuilder();
            foreach(List<EntityType> group in new[]{resources,animals}) foreach(EntityType type in group)
            {
                Texture2D icon=type.getIcon();string path=Path.Combine(icons,type.getId()+".png");
                if(icon!=null)
                {
                    RenderTexture previous=RenderTexture.active,render=RenderTexture.GetTemporary(icon.width,icon.height,0);
                    Texture2D copy=null;
                    try { Graphics.Blit(icon,render);RenderTexture.active=render;copy=new Texture2D(icon.width,icon.height,TextureFormat.RGBA32,false);copy.ReadPixels(new Rect(0,0,icon.width,icon.height),0,0);copy.Apply();File.WriteAllBytes(path,ImageConversion.EncodeToPNG(copy)); }
                    finally { RenderTexture.active=previous;RenderTexture.ReleaseTemporary(render);if(copy!=null)UnityEngine.Object.Destroy(copy); }
                }
                int mask=0;if(type.hasAnyFlag(EntityFlags.Food|EntityFlags.Drink))mask|=1;if(type.hasAnyFlag(EntityFlags.Material))mask|=2;if(type.hasAnyFlag(EntityFlags.Tool))mask|=4;if(type.hasAnyFlag(EntityFlags.Clothing))mask|=8;
                catalog.Append(group==resources?"resource":"animal").Append('\t').Append(type.getId()).Append('\t').Append(type.getName()).Append('\t').Append(mask).Append('\t').Append(icon==null?"":path).Append('\n');
            }
            File.WriteAllText(Path.Combine(folder,"catalog.tsv"),catalog.ToString(),System.Text.Encoding.UTF8);
        }
    }
}
