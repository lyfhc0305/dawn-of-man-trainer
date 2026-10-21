using System;
using System.Collections.Generic;
using System.Reflection;
using DawnOfMan;
using MadrugaShared;

namespace DawnTrainer
{
    public sealed partial class Trainer
    {
        private int storageMultiplier = 1, buildMultiplier = 1, carryMultiplier = 1, transportMultiplier = 1;
        private int craftMultiplier = 1, dryMultiplier = 1, gatherMultiplier = 1, farmMultiplier = 1;
        private static readonly FieldInfo multiplierSlotDefinition = typeof(Slot).GetField("mSlotDefinition", BindingFlags.Instance | BindingFlags.NonPublic);
        private sealed class MultiplierSlotState
        {
            internal SlotDefinition Original, Local;
            // A canStoreEntity probe calls isFull before storing anything. Keep its type across
            // those nested null-type probes so a body/heavy item never sees the expanded limit.
            internal PhysicalEntityType Incoming;
        }
        private readonly Dictionary<Slot, MultiplierSlotState> multiplierSlots = new Dictionary<Slot, MultiplierSlotState>();

        private bool MultiplierOwner(PhysicalEntity owner)
        { return WorldReady() && owner != null && Valid(owner) && owner.getTeam() == Team.Player; }

        private int CargoMultiplier(PhysicalEntity owner)
        {
            if (!MultiplierOwner(owner)) return 1;
            Human human = owner as Human;
            if (human != null) return human.isAlive() && (human.getAge() == Age.Adult || human.getAge() == Age.Old) ? carryMultiplier : 1;
            Transport transport = owner as Transport;
            if (transport == null || !transport.isBuilt() || transport.isBeingRecycled()) return 1;
            string id = transport.getTransportType().getId();
            return id == "sledge" || id == "cart" ? transportMultiplier : 1;
        }

        private int SlotMultiplier(Slot slot)
        {
            PhysicalEntity owner = slot.getParent();
            if (!MultiplierOwner(owner)) return 1;
            SlotType kind = slot.getSlotType();
            if (kind == SlotType.Resource)
            {
                int cargo = CargoMultiplier(owner);
                if (owner is Human || owner is Transport) return cargo;
            }
            Structure storage = owner as Structure;
            if (storage == null || !storage.isBuilt() || storage.isBeingRecycled() ||
                !storage.hasAnyFlag(EntityFlags.Storage) || storage.hasAnyFlag(EntityFlags.VisibleStorage)) return 1;
            return kind == SlotType.Resource || kind == SlotType.Food || kind == SlotType.SingleResource ? storageMultiplier : 1;
        }

        private static bool OrdinaryMultiplierResource(PhysicalEntityType type, bool human)
        {
            ResourceType resource = type as ResourceType;
            // Tool/clothing resources are legitimate cargo. Their dedicated equipment slots
            // are excluded by SlotMultiplier; a tool transported as cargo is not equipment.
            return resource != null && (!human || !resource.hasAnyFlag(EntityFlags.Heavy | EntityFlags.VeryHeavy));
        }

        public static void PrepareSlot(Slot slot, PhysicalEntityType incoming)
        {
            Trainer trainer = instance;
            if (trainer == null || slot == null || multiplierSlotDefinition == null) return;
            if (trainer.multiplierSlots.Count == 0 && trainer.storageMultiplier == 1 && trainer.carryMultiplier == 1 && trainer.transportMultiplier == 1) return;
            MultiplierSlotState state;
            SlotDefinition current = (SlotDefinition)multiplierSlotDefinition.GetValue(slot);
            if (current == null) return;
            if (trainer.multiplierSlots.TryGetValue(slot, out state) && !Object.ReferenceEquals(current, state.Local) && !Object.ReferenceEquals(current, state.Original))
            { trainer.multiplierSlots.Remove(slot); state = null; }
            int factor = trainer.SlotMultiplier(slot);
            if (state == null && factor <= 1) return;
            if (state == null)
            {
                state = new MultiplierSlotState { Original = current, Local = new SlotDefinition {
                    Count = current.Count, Capacity = current.Capacity, SlotType = current.SlotType, ResourceType = current.ResourceType } };
                trainer.multiplierSlots.Add(slot, state);
            }
            if (incoming != null) state.Incoming = incoming;
            PhysicalEntityType stored = slot.getEntityType();
            PhysicalEntityType candidate = incoming ?? stored ?? state.Incoming;
            bool human = slot.getParent() is Human;
            if ((stored != null && !OrdinaryMultiplierResource(stored, human)) ||
                (candidate != null && !OrdinaryMultiplierResource(candidate, human))) factor = 1;
            ResourceType fixedResource = state.Original.SlotType == SlotType.SingleResource ? slot.getSingleResourceType() : null;
            if (fixedResource != null && !OrdinaryMultiplierResource(fixedResource, human)) factor = 1;
            if (factor <= 1)
            {
                if (Object.ReferenceEquals(current, state.Local)) multiplierSlotDefinition.SetValue(slot, state.Original);
                return;
            }
            state.Local.Capacity = ProductionPolicy.ScaledCapacity(state.Original.Capacity, factor);
            // Never multiply Count (the number of native slots), and never mutate the shared type definition.
            if (!Object.ReferenceEquals(current, state.Local)) multiplierSlotDefinition.SetValue(slot, state.Local);
        }

        public static int CarryPlanSlots(int original, PhysicalEntity owner, SlotType type)
        {
            Trainer trainer = instance;
            if (trainer == null || type != SlotType.Resource || trainer.CargoMultiplier(owner) <= 1) return original;
            long available = 0;
            for (int i = 0; i < owner.getSlotCount(); i++)
            {
                Slot slot = owner.getSlot(i);
                if (slot.getSlotType() != SlotType.Resource) continue;
                PhysicalEntityType stored = slot.getEntityType();
                if (stored != null && !OrdinaryMultiplierResource(stored, owner is Human)) continue;
                // These helpers are scoped to ordinary-resource AI call sites. An empty slot
                // must not retain a previous rejected Heavy/body probe indefinitely.
                MultiplierSlotState state;
                if (stored == null && trainer.multiplierSlots.TryGetValue(slot, out state)) state.Incoming = null;
                PrepareSlot(slot, null);
                available += Math.Max(0, slot.getSpaceLeft());
                if (available >= Int32.MaxValue) return Int32.MaxValue;
            }
            return (int)available;
        }

        public static bool CarryAnySpace(bool original, PhysicalEntity owner, SlotType type)
        {
            Trainer trainer = instance;
            if (trainer == null || type != SlotType.Resource || trainer.CargoMultiplier(owner) <= 1) return original;
            return CarryPlanSlots(0, owner, type) > 0;
        }

        // Installer substitutes only ordinary resource AI call sites. Do not use these for
        // corpse/equipment AI, and do not postfix the general PhysicalEntity methods.
        public static int ResourceCarrySlots(PhysicalEntity owner, SlotType type)
        { return owner == null ? 0 : CarryPlanSlots(owner.countEmptySlots(type), owner, type); }
        public static bool ResourceCarrySpace(PhysicalEntity owner, SlotType type)
        { return owner != null && CarryAnySpace(owner.anyEmptySlots(type), owner, type); }
        public static int NonnegativeSpace(int original) { return Math.Max(0, original); }

        public static float ScaleBuildWork(float work, PhysicalEntity builder, Buildable target)
        {
            Trainer trainer = instance;
            Human human = builder as Human;
            if (trainer == null || trainer.buildMultiplier <= 1 || human == null || !trainer.MultiplierOwner(human) ||
                !human.isAlive() || !trainer.MultiplierOwner(target) || !trainer.CanComplete(target)) return work;
            return ProductionPolicy.ScaleBuildWork(work, trainer.buildMultiplier);
        }

        // The multiplier for a hooked native work state; harvesting is a tree action but farm work.
        private int WorkMultiplier(BeingState state)
        {
            if (state is BeingStateRepairing || state is BeingStateRecycling) return buildMultiplier;
            if (state is BeingStateCrafting) return craftMultiplier;
            if (state is BeingStateHarvesting || state is BeingStatePlanting || state is BeingStatePlowing || state is BeingStateMilking ||
                state is BeingStateShearing || state is BeingStateTaming || state is BeingStateButchering || state is BeingStateSlaughtering) return farmMultiplier;
            if (state is BeingStateTreeAction || state is BeingStateMining || state is BeingStateMiningUnderground || state is BeingStateGatheringProp ||
                state is BeingStateGatheringDetail || state is BeingStateExtractingMud || state is BeingStateExtractingWater || state is BeingStateFishing) return gatherMultiplier;
            return 1;
        }

        // Installer passes the timeStep feeding one native progress value. Only the progress runs
        // faster: each finished unit still consumes, produces, wears tools and rewards natively.
        public static float ScaleWork(float work, BeingState state, Being being)
        {
            Trainer trainer = instance;
            int factor = trainer == null ? 1 : trainer.WorkMultiplier(state);
            Human human = being as Human;
            if (factor <= 1 || human == null || !trainer.MultiplierOwner(human) || !human.isAlive()) return work;
            return ProductionPolicy.ScaleBuildWork(work, factor);
        }

        // Laborless recipes (cured meat, dried fish, dried skin) progress in their rack's own tick.
        public static float ScaleDryWork(float work, Structure rack)
        {
            Trainer trainer = instance;
            if (trainer == null || trainer.dryMultiplier <= 1 || !trainer.MultiplierOwner(rack) || !rack.isBuilt()) return work;
            return ProductionPolicy.ScaleBuildWork(work, trainer.dryMultiplier);
        }

        private void RestoreMultiplierSlots()
        {
            if (multiplierSlotDefinition != null)
                foreach (KeyValuePair<Slot, MultiplierSlotState> pair in multiplierSlots)
                    if (Object.ReferenceEquals(multiplierSlotDefinition.GetValue(pair.Key), pair.Value.Local))
                        multiplierSlotDefinition.SetValue(pair.Key, pair.Value.Original);
            multiplierSlots.Clear();
        }

        private void ClearMultiplierWorld() { ResetMultipliers(); }
        private void ResetMultipliers()
        {
            storageMultiplier = buildMultiplier = carryMultiplier = transportMultiplier = 1;
            craftMultiplier = dryMultiplier = gatherMultiplier = farmMultiplier = 1;
            RestoreMultiplierSlots(); UpdateMultiplierModel();
        }

        private void SetMultiplier(string kind, int value)
        {
            int maximum = kind == "storage" || kind == "carry" || kind == "transport" ? 10 : 20;
            if (value < 1 || value > maximum) throw new ArgumentOutOfRangeException("value", "倍率超出允许范围");
            string label;
            switch (kind)
            {
                case "storage": storageMultiplier = value; label = "仓储容量"; break;
                case "build": buildMultiplier = value; label = "建造速度"; break;
                case "craft": craftMultiplier = value; label = "制作速度"; break;
                case "dry": dryMultiplier = value; label = "晾晒速度"; break;
                case "gather": gatherMultiplier = value; label = "采集速度"; break;
                case "farm": farmMultiplier = value; label = "农牧速度"; break;
                case "carry": carryMultiplier = value; label = "人力搬运"; break;
                case "transport": transportMultiplier = value; label = "运输工具容量"; break;
                default: throw new ArgumentException("未知倍率项目", "kind");
            }
            // Reapply from the original capacities on demand, including immediately restoring disabled limits.
            List<Slot> slots = new List<Slot>(multiplierSlots.Keys);
            foreach (Slot slot in slots) PrepareSlot(slot, null);
            if (storageMultiplier == 1 && carryMultiplier == 1 && transportMultiplier == 1) RestoreMultiplierSlots();
            UpdateMultiplierModel();
            if (WorldReady()) TransientSingleton<GameGuiManager>.CurrentInstance.requestRefresh();
            // The scope notes live on the efficiency page; the notice only confirms the new value.
            Notify(label + "已设为 " + value + "×");
        }

        private void UpdateMultiplierModel()
        {
            if (ui == null) return;
            ui.StorageMultiplier = storageMultiplier; ui.BuildMultiplier = buildMultiplier;
            ui.CarryMultiplier = carryMultiplier; ui.TransportMultiplier = transportMultiplier;
            ui.CraftMultiplier = craftMultiplier; ui.DryMultiplier = dryMultiplier;
            ui.GatherMultiplier = gatherMultiplier; ui.FarmMultiplier = farmMultiplier;
        }
    }
}
