using System;
using System.Collections.Generic;
using System.Reflection;
using DawnOfMan;
using MadrugaShared;
using UnityEngine;

namespace DawnTrainer
{
    public sealed partial class Trainer
    {
        private Slot MultiplierTestCargo(PhysicalEntity owner)
        {
            for (int i = 0; i < owner.getSlotCount(); i++)
                if (owner.getSlot(i).getSlotType() == SlotType.Resource) return owner.getSlot(i);
            throw new InvalidOperationException("No native cargo slot on " + owner.getName());
        }
        private int MultiplierTestTotal(PhysicalEntity owner)
        {
            int count = 0;
            for (int i = 0; i < owner.getSlotCount(); i++)
                if (owner.getSlot(i).getSlotType() == SlotType.Resource) count += owner.getSlot(i).getCapacity();
            return count;
        }
        private Structure MultiplierTestStructure(string id, Vector3 position, float progress)
        {
            StructureType type = (StructureType)TransientSingleton<EntityTypeManager>.CurrentInstance.getTypeEntities<StructureType>().Find(t => t.getId() == id);
            Structure result = (Structure)manager.createBuildable(typeof(Structure), type, GameUtil.floorPosition(position), Quaternion.identity, 0, progress, progress < 1 ? type.getCosts().clone() : null);
            result.onSpawn(); return result;
        }
        private Human MultiplierTestHuman(Age age, Vector3 position)
        {
            Human human = manager.createEntityOnFloor<Resident>(manager.getPlayerHumans()[0].getHumanType(), new EntityParameters(position, Quaternion.identity, age, Gender.Male));
            if (human.getTeam() != Team.Player) manager.swapEntityTeam(human, Team.Player);
            human.onSpawn(); return human;
        }
        private void MultiplierTestLoadUnload(PhysicalEntity owner, Structure destination, ResourceType resource, int originalTotal, Action<bool, string> check, string label)
        {
            int count = originalTotal + 1;
            if (count < 2 || count > 256) throw new InvalidOperationException("Unexpected native inventory size: " + count);
            int before = destination.getContainedAmount(resource);
            for (int i = 0; i < count; i++)
            {
                Resource item = manager.createResourceOnFloor(resource, owner.getPosition() + Vector3.right * 2);
                if (!owner.storeItem(item, SlotType.Resource)) throw new InvalidOperationException(label + " native store rejected item " + i);
            }
            check(owner.getContainedAmount(resource) == count, "multiplier " + label + " native store exceeds original total capacity without changing native slot count");
            // Exercise the native item transfer, rather than copying counts or deleting inventory.
            int moved = 0;
            for (int i = 0; i < owner.getSlotCount(); i++)
            {
                Slot slot = owner.getSlot(i);
                if (slot.getSlotType() != SlotType.Resource) continue;
                while (slot.getAmount() > 0)
                {
                    if (!owner.transferItemTo(destination, slot.getEntity(0), SlotType.Count)) throw new InvalidOperationException(label + " native unload failed");
                    if (++moved > count) throw new InvalidOperationException("Unload loop did not remove cargo");
                }
            }
            check(moved == count && owner.getContainedAmount(resource) == 0 && destination.getContainedAmount(resource) == before + count,
                "multiplier " + label + " native unload conserves every item");
        }

        internal void BeginMultiplierIntegration(Action<bool, string> check)
        {
            if (!isolatedTestSession || !WorldReady()) throw new InvalidOperationException("Multiplier checks require the isolated ready test world");
            ResetMultipliers();
            Vector3 origin = manager.getPlayerHumans()[0].getPosition();
            try
            {
                TestGroup("multiplier native cargo and exclusions", delegate
                {
                    Structure storage = MultiplierTestStructure("storage_tent", origin + new Vector3(55, 0, 18), 1);
                    Structure visibleStorage = MultiplierTestStructure("wood_pile", origin + new Vector3(60, 0, 18), 1);
                    Structure nonStorage = MultiplierTestStructure("crafter", origin + new Vector3(65, 0, 18), 1);
                    Human adult = MultiplierTestHuman(Age.Adult, origin + new Vector3(52, 0, 20));
                    Human child = MultiplierTestHuman(Age.Young, origin + new Vector3(53, 0, 20));
                    Human old = MultiplierTestHuman(Age.Old, origin + new Vector3(54, 0, 20));
                    Slot cargo = MultiplierTestCargo(adult), oldCargo = MultiplierTestCargo(old), childCargo = MultiplierTestCargo(child);
                    SlotDefinition definition = (SlotDefinition)multiplierSlotDefinition.GetValue(cargo);
                    int adultTotal = MultiplierTestTotal(adult), originalSlots = adult.getSlotCount();
                    int originalCapacity = definition.Capacity, originalDefinitionCount = definition.Count;
                    int oldCapacity = oldCargo.getCapacity(), childCapacity = childCargo.getCapacity();
                    List<SlotDefinition> storageDefs = new List<SlotDefinition>();
                    List<int> storageCapacities = new List<int>();
                    for (int i = 0; i < storage.getSlotCount(); i++)
                    {
                        SlotDefinition d = (SlotDefinition)multiplierSlotDefinition.GetValue(storage.getSlot(i));
                        storageDefs.Add(d); storageCapacities.Add(d.Capacity);
                    }
                    List<int> visibleCapacities = new List<int>(), nonStorageCapacities = new List<int>();
                    for (int i = 0; i < visibleStorage.getSlotCount(); i++) visibleCapacities.Add(visibleStorage.getSlot(i).getCapacity());
                    for (int i = 0; i < nonStorage.getSlotCount(); i++) nonStorageCapacities.Add(nonStorage.getSlot(i).getCapacity());
                    storageMultiplier = carryMultiplier = transportMultiplier = 3;
                    check(cargo.getCapacity() == ProductionPolicy.ScaledCapacity(originalCapacity, 3) && adult.getSlotCount() == originalSlots,
                        "multiplier adult cargo scales capacity only");
                    check(oldCargo.getCapacity() == ProductionPolicy.ScaledCapacity(oldCapacity, 3) && childCargo.getCapacity() == childCapacity,
                        "multiplier elderly cargo scales and children keep native capacity");
                    check(definition.Capacity == originalCapacity && definition.Count == originalDefinitionCount && !System.Object.ReferenceEquals(multiplierSlotDefinition.GetValue(cargo), definition),
                        "multiplier per-slot clone preserves shared definition capacity and Count");
                    bool storageScaled = storage.hasAnyFlag(EntityFlags.Storage) && !storage.hasAnyFlag(EntityFlags.VisibleStorage);
                    for (int i = 0; i < storage.getSlotCount(); i++)
                    {
                        Slot slot = storage.getSlot(i); SlotDefinition d = storageDefs[i];
                        bool eligible = d.SlotType == SlotType.Resource || d.SlotType == SlotType.Food || d.SlotType == SlotType.SingleResource;
                        storageScaled &= slot.getCapacity() == (eligible ? ProductionPolicy.ScaledCapacity(storageCapacities[i], 3) : storageCapacities[i]) && d.Capacity == storageCapacities[i];
                    }
                    check(storageScaled, "multiplier built enclosed storage scales eligible native cargo slots");
                    bool excluded = true;
                    for (int i = 0; i < visibleStorage.getSlotCount(); i++) excluded &= visibleStorage.getSlot(i).getCapacity() == visibleCapacities[i];
                    for (int i = 0; i < nonStorage.getSlotCount(); i++) excluded &= nonStorage.getSlot(i).getCapacity() == nonStorageCapacities[i];
                    check(excluded && visibleStorage.hasAnyFlag(EntityFlags.VisibleStorage) && !nonStorage.hasAnyFlag(EntityFlags.Storage),
                        "multiplier visible piles and non-storage workshop retain native capacities");
                    bool equipment = true; int equipmentSlots = 0;
                    for (int i = 0; i < adult.getSlotCount(); i++)
                    {
                        Slot slot = adult.getSlot(i);
                        if (slot.getSlotType() != SlotType.Tool && slot.getSlotType() != SlotType.Clothing) continue;
                        equipmentSlots++; SlotDefinition d = (SlotDefinition)multiplierSlotDefinition.GetValue(slot);
                        equipment &= slot.getCapacity() == d.Capacity && !multiplierSlots.ContainsKey(slot);
                    }
                    check(equipment && equipmentSlots > 0, "multiplier dedicated tool and clothing slots remain native");
                    ResourceType light = (ResourceType)resources.Find(t => t.getId() == "raw_meat");
                    ResourceType heavy = (ResourceType)resources.Find(t => t.getId() == "log");
                    ResourceType veryHeavy = (ResourceType)resources.Find(t => t.getId() == "megalith");
                    cargo.canStoreEntity(heavy); check(cargo.getCapacity() == originalCapacity, "multiplier Heavy cargo probe preserves human native capacity across nested getters");
                    cargo.canStoreEntity(veryHeavy); check(cargo.getCapacity() == originalCapacity, "multiplier VeryHeavy cargo probe preserves human native capacity");
                    cargo.canStoreEntity(adult.getHumanType()); check(cargo.getCapacity() == originalCapacity, "multiplier non-resource body type probe preserves native capacity");
                    int ordinaryPlan = ResourceCarrySlots(adult, SlotType.Resource);
                    check(ordinaryPlan == adultTotal * 3 && ResourceCarrySpace(adult, SlotType.Resource), "multiplier ordinary resource planner counts actual remaining units and clears stale empty-slot probe");
                    check(CarryPlanSlots(17, child, SlotType.Resource) == 17 && CarryAnySpace(false, child, SlotType.Resource) == false && CarryPlanSlots(17, adult, SlotType.Tool) == 17,
                        "multiplier child and equipment planning leave native results untouched");
                    MultiplierTestLoadUnload(adult, storage, light, adultTotal, check, "adult");
                    // Real stored cargo remains intact when definitions and limits are restored.
                    for (int i = 0; i < adultTotal + 1; i++)
                        if (!adult.storeItem(manager.createResourceOnFloor(light, adult.getPosition() + Vector3.forward * 2), SlotType.Resource)) throw new InvalidOperationException("Reset fixture could not load cargo");
                    Slot overfull = null;
                    for (int i = 0; i < adult.getSlotCount(); i++)
                    {
                        Slot candidate = adult.getSlot(i); MultiplierSlotState state;
                        if (multiplierSlots.TryGetValue(candidate, out state) && candidate.getAmount() > state.Original.Capacity) overfull = candidate;
                    }
                    if (overfull == null) throw new InvalidOperationException("Reset fixture has no over-original-capacity slot");
                    cargo = overfull; definition = multiplierSlots[cargo].Original; originalCapacity = definition.Capacity;
                    int storedBefore = adult.getContainedAmount(light);
                    object nativeSaveState = typeof(Slot).GetField("mSaveState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cargo);
                    carryMultiplier = 1; PrepareSlot(cargo, null);
                    check(adult.getContainedAmount(light) == storedBefore && cargo.getCapacity() == originalCapacity,
                        "multiplier lowering capacity retains actual stored items");
                    check(System.Object.ReferenceEquals(nativeSaveState, typeof(Slot).GetField("mSaveState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cargo)),
                        "multiplier restoring definition does not replace native Slot.SaveState or truncate inventory");
                    check(cargo.getSpaceLeft() == 0 && cargo.isFull() && !cargo.canStoreEntity(light) && NonnegativeSpace(-10) == 0,
                        "multiplier genuinely overfull slot reports zero room and rejects additions");
                    carryMultiplier = 3;
                    manager.swapEntityTeam(adult, Team.Nature);
                    check(cargo.getCapacity() == originalCapacity && CarryPlanSlots(19, adult, SlotType.Resource) == 19, "multiplier non-player owner restores native capacity and planning");
                    manager.swapEntityTeam(adult, Team.Player);
                    foreach (string id in new string[] { "sledge", "cart" })
                    {
                        transportMultiplier = 1;
                        TransportType type = (TransportType)TransientSingleton<EntityTypeManager>.CurrentInstance.getTypeEntities<TransportType>().Find(t => t.getId() == id);
                        Transport transport = (Transport)manager.createTransport(typeof(Transport), type, GameUtil.floorPosition(origin + new Vector3(70 + (id == "cart" ? 6 : 0), 0, 20)), Quaternion.identity, 0, 1, null, null);
                        transport.onSpawn(); int baseline = MultiplierTestTotal(transport);
                        transportMultiplier = 3;
                        check(MultiplierTestTotal(transport) == baseline * 3, "multiplier " + id + " actual native cargo capacity scales");
                        MultiplierTestLoadUnload(transport, storage, light, baseline, check, id);
                    }
                    ClearMultiplierWorld();
                    check(storageMultiplier == 1 && carryMultiplier == 1 && transportMultiplier == 1 && buildMultiplier == 1 && multiplierSlots.Count == 0,
                        "multiplier world clear resets all four controls and discards recorded definitions");
                    check(adult.getContainedAmount(light) == storedBefore && System.Object.ReferenceEquals(multiplierSlotDefinition.GetValue(cargo), definition),
                        "multiplier reset restores original definition identity without losing over-capacity cargo");
                }, check);

                TestGroup("multiplier native construction operation", delegate
                {
                    ResetMultipliers();
                    Human builder = MultiplierTestHuman(Age.Adult, origin + new Vector3(80, 0, 20));
                    Structure target = MultiplierTestStructure("tent", origin + new Vector3(83, 0, 20), 0);
                    EntityAmounts materials = target.getPendingConstructionMaterials().clone();
                    int pending = materials.calculateTotalCount();
                    int knownBefore = TransientSingleton<KnowledgeManager>.CurrentInstance.getKnownEntities().getAmount(target.getBuildableType());
                    buildMultiplier = 4;
                    check(Math.Abs(ScaleBuildWork(.025f, builder, target) - .1f) < .00001f, "multiplier eligible builder scales native work amount");
                    check(target.getBuildProgress() == 0 && target.getPendingConstructionMaterials().calculateTotalCount() == pending && !target.isBuilt(),
                        "multiplier work scaling alone neither completes blueprint nor consumes or waives materials");
                    manager.swapEntityTeam(builder, Team.Nature);
                    check(ScaleBuildWork(.025f, builder, target) == .025f, "multiplier non-player builder keeps native work");
                    manager.swapEntityTeam(builder, Team.Player); manager.swapEntityTeam(target, Team.Nature);
                    check(ScaleBuildWork(.025f, builder, target) == .025f, "multiplier non-player target keeps native work");
                    manager.swapEntityTeam(target, Team.Player);
                    for (int i = 0; i < materials.getEntryCount(); i++)
                    {
                        EntityAmount entry = materials.getEntry(i);
                        for (int j = 0; j < entry.getAmount(); j++) target.addMaterial(builder, manager.createResourceOnFloor(entry.getResourceType(), target.getPosition() + Vector3.right * 3), MaterialPurpose.Construction);
                    }
                    check(!target.anyPendingConstructionMaterials(), "multiplier construction fixture delivers every native required material");
                    BeingStateBuilding operation = BeingStateBuilding.create(target);
                    typeof(BeingState).GetField("mBeing", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(operation, builder);
                    MethodInfo nativeWork = typeof(BeingStateBuilding).GetMethod("increaseBuildingOperation", BindingFlags.Instance | BindingFlags.NonPublic);
                    nativeWork.Invoke(operation, new object[] { 1f });
                    float expected = builder.getEfficiencyRatio() / target.getBuildableType().getBuildTime() * 4;
                    check(Math.Abs(target.getBuildProgress() - expected) < .0001f, "multiplier patched real native building operation applies fourfold work");
                    nativeWork.Invoke(operation, new object[] { target.getBuildableType().getBuildTime() * 2 / Math.Max(.001f, builder.getEfficiencyRatio()) });
                    int knownAfter = TransientSingleton<KnowledgeManager>.CurrentInstance.getKnownEntities().getAmount(target.getBuildableType());
                    check(target.isBuilt() && knownAfter == knownBefore + 1, "multiplier native completion still records original construction knowledge callback");
                    nativeWork.Invoke(operation, new object[] { 1f });
                    check(TransientSingleton<KnowledgeManager>.CurrentInstance.getKnownEntities().getAmount(target.getBuildableType()) == knownAfter,
                        "multiplier built target does not duplicate native completion reward");
                }, check);
            }
            finally { ResetMultipliers(); }
            // These direct native-method checks do not claim to exercise AI task dispatch or a save/reload cycle.
        }
    }
}
