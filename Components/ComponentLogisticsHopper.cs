using Engine;
using Game;
using GameEntitySystem;
using RecipaediaEX.ComponentsExtra.Implementation;
using SCIENEW.ProductionIO;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>
    /// 料斗逻辑：落料定时出货（大口对着输送带则直接放到带上，否则吐掉落物）；
    /// 受料吞掉落物或从大口侧输送带取货，塞进窄口贴合的 Input（无 Input 声明则整库）。
    /// 朝向/变体每帧读地形 Data。
    /// </summary>
    public class ComponentLogisticsHopper : Component, IUpdateable {
        public const float DefaultIntervalSeconds = 0.25f;
        public const float MinIntervalSeconds = 0.05f;
        public const float MaxIntervalSeconds = 2f;

        /// <summary>
        /// 从带上取货够得着的距离（弧长）。要大于默认节拍内带走过的路程
        /// （<see cref="BeltGroup.DefaultSpeedAbs"/> × <see cref="DefaultIntervalSeconds"/>），
        /// 否则物品会在两次尝试之间冲过取货点。
        /// </summary>
        public const float BeltReach = 0.4f;

        public float IntervalSeconds {
            get => m_intervalSeconds;
            set => m_intervalSeconds = ClampInterval(value);
        }

        public HopperExtractMode ExtractMode {
            get => m_extractMode;
            set => m_extractMode = value;
        }

        public bool Enabled {
            get => m_enabled;
            set => m_enabled = value;
        }

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        ComponentBlockEntity m_componentBlockEntity;
        SubsystemTerrain m_subsystemTerrain;
        SubsystemBlockEntities m_subsystemBlockEntities;
        SubsystemPickables m_subsystemPickables;
        SubsystemTime m_subsystemTime;
        SubsystemBeltGroups m_subsystemBeltGroups;
        float m_intervalSeconds = DefaultIntervalSeconds;
        HopperExtractMode m_extractMode = HopperExtractMode.OutputPreferred;
        bool m_enabled = true;
        double m_nextFireTime;
        readonly Game.Random m_random = new();

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap) {
            m_componentBlockEntity = Entity.FindComponent<ComponentBlockEntity>(true);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(true);
            m_subsystemBlockEntities = Project.FindSubsystem<SubsystemBlockEntities>(true);
            m_subsystemPickables = Project.FindSubsystem<SubsystemPickables>(true);
            m_subsystemTime = Project.FindSubsystem<SubsystemTime>(true);
            m_subsystemBeltGroups = Project.FindSubsystem<SubsystemBeltGroups>(true);
            m_intervalSeconds = ClampInterval(valuesDictionary.GetValue("IntervalSeconds", DefaultIntervalSeconds));
            m_extractMode = valuesDictionary.GetValue("ExtractMode", HopperExtractMode.OutputPreferred);
            m_enabled = valuesDictionary.GetValue("Enabled", DefaultEnabledForCurrentVariant());
            m_nextFireTime = m_subsystemTime.GameTime + m_intervalSeconds;
        }

        public override void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap) {
            valuesDictionary.SetValue("IntervalSeconds", m_intervalSeconds);
            valuesDictionary.SetValue("ExtractMode", m_extractMode);
            valuesDictionary.SetValue("Enabled", m_enabled);
        }

        public void Update(float dt) {
            if (!m_enabled) {
                return;
            }
            if (!TryReadCell(out int value, out Point3 cell)) {
                return;
            }
            double now = m_subsystemTime.GameTime;
            if (now < m_nextFireTime) {
                return;
            }
            m_nextFireTime = now + m_intervalSeconds;
            if (LogisticsHopperBlock.GetVariant(value) == LogisticsHopperVariant.Output) {
                TryDischarge(value, cell);
            }
            else {
                TryIntakeFromBelt(value, cell);
            }
        }

        /// <summary>受料：尝试吞掉落物。成功则改 worldItem。</summary>
        public bool TryAbsorb(WorldItem worldItem) {
            if (!m_enabled || worldItem == null || worldItem.ToRemove) {
                return false;
            }
            if (!TryReadCell(out int value, out Point3 cell)) {
                return false;
            }
            if (LogisticsHopperBlock.GetVariant(value) != LogisticsHopperVariant.Input) {
                return false;
            }
            if (!TryGetAttachedInventory(value, cell, out Entity destEntity, out IInventory destInventory)) {
                return false;
            }
            int itemValue = worldItem.Value;
            int count = worldItem is Pickable pickable ? pickable.Count : 1;
            if (count <= 0 || itemValue == 0) {
                return false;
            }
            if (!TryInsertIntoAttached(destEntity, destInventory, itemValue, count, out int remain)) {
                return false;
            }
            if (worldItem is Pickable p) {
                p.Count = remain;
            }
            if (remain <= 0) {
                worldItem.ToRemove = true;
            }
            return true;
        }

        /// <summary>对话框状态：贴合目标摘要。</summary>
        public string DescribeAttachedStatus() {
            if (!TryReadCell(out int value, out Point3 cell)) {
                return LanguageControl.GetContentWidgets(nameof(HopperDialog), "Gone");
            }
            if (!TryGetAttachedInventory(value, cell, out Entity entity, out IInventory inventory)) {
                return LanguageControl.GetContentWidgets(nameof(HopperDialog), "NoTarget");
            }
            IInventoryProductionSlots slots = ResolveProductionSlots(entity, inventory);
            if (slots == null) {
                return LanguageControl.GetContentWidgets(nameof(HopperDialog), "TargetChest");
            }
            bool hasInput = false;
            foreach (int _ in slots.GetSlotIndices(ProductionSlotKind.Input)) {
                hasInput = true;
                break;
            }
            return LanguageControl.GetContentWidgets(
                nameof(HopperDialog),
                hasInput ? "TargetMachine" : "TargetChest");
        }

        /// <summary>对话框状态：大口一侧是否对着输送带。</summary>
        public string DescribeMouthStatus() {
            if (!TryReadCell(out int value, out Point3 cell)) {
                return LanguageControl.GetContentWidgets(nameof(HopperDialog), "Gone");
            }
            return LanguageControl.GetContentWidgets(
                nameof(HopperDialog),
                TryGetMouthBeltCell(value, cell, out _) ? "MouthBelt" : "MouthOpen");
        }

        void TryDischarge(int value, Point3 cell) {
            if (!TryGetAttachedInventory(value, cell, out Entity sourceEntity, out IInventory sourceInventory)) {
                return;
            }
            bool ontoBelt = TryGetMouthBeltCell(value, cell, out Point3 mouthCell);
            foreach (int slotIndex in EnumerateExtractSlots(sourceEntity, sourceInventory)) {
                if (slotIndex < 0 || slotIndex >= sourceInventory.SlotsCount) {
                    continue;
                }
                int count = sourceInventory.GetSlotCount(slotIndex);
                if (count <= 0) {
                    continue;
                }
                int itemValue = sourceInventory.GetSlotValue(slotIndex);
                if (itemValue == 0) {
                    continue;
                }
                int take = GetTransferCount(sourceEntity, slotIndex, sourceInventory);
                if (take <= 0) {
                    continue;
                }
                if (ontoBelt) {
                    // 带上没位置就本次不出货：把货留在源容器里排队，而不是在带边堆掉落物
                    if (!m_subsystemBeltGroups.TryInsertItemFrom(
                            mouthCell,
                            new Vector3(cell) + new Vector3(0.5f),
                            itemValue,
                            take)) {
                        return;
                    }
                    sourceInventory.RemoveSlotItems(slotIndex, take);
                    return;
                }
                sourceInventory.RemoveSlotItems(slotIndex, take);
                EjectPickable(value, cell, itemValue, take);
                return;
            }
        }

        /// <summary>
        /// 受料：等物品滚到斗口跟前（<see cref="BeltReach"/> 以内）再取，塞进窄口贴合的容器；
        /// 塞不下就不取，物品继续跟着带走。
        /// </summary>
        void TryIntakeFromBelt(int value, Point3 cell) {
            if (!TryGetMouthBeltCell(value, cell, out Point3 mouthCell)
                || !TryGetAttachedInventory(value, cell, out Entity destEntity, out IInventory destInventory)) {
                return;
            }
            Vector3 hopperCenter = new Vector3(cell) + new Vector3(0.5f);
            if (!m_subsystemBeltGroups.TryPeekItemFrom(
                    mouthCell,
                    hopperCenter,
                    BeltReach,
                    out int itemValue,
                    out int count)) {
                return;
            }
            if (!TryInsertIntoAttached(destEntity, destInventory, itemValue, count, out int remain)) {
                return;
            }
            int moved = count - remain;
            if (moved > 0) {
                m_subsystemBeltGroups.RemoveItemFrom(mouthCell, hopperCenter, BeltReach, moved);
            }
        }

        /// <summary>大口侧邻格是否为已编组的输送带。</summary>
        bool TryGetMouthBeltCell(int value, Point3 cell, out Point3 mouthCell) {
            mouthCell = cell + CellFace.FaceToPoint3(LogisticsHopperBlock.GetFacing(value));
            return m_subsystemBeltGroups.TryGetAt(mouthCell, out _);
        }

        static int GetTransferCount(Entity sourceEntity, int slotIndex, IInventory sourceInventory) {
            var exCraftingTable = sourceEntity.FindComponent<ComponentEXCraftingTable>();
            if (exCraftingTable == null || slotIndex != exCraftingTable.ResultSlotIndex) {
                return 1;
            }
            if (exCraftingTable.m_matchedRecipe == null) {
                return 0;
            }
            return MathUtils.Min(exCraftingTable.m_matchedRecipe.ResultCount, sourceInventory.GetSlotCount(slotIndex));
        }

        IEnumerable<int> EnumerateExtractSlots(Entity sourceEntity, IInventory sourceInventory) {
            switch (m_extractMode) {
                case HopperExtractMode.OutputOnly: {
                    IInventoryProductionSlots slots = ResolveProductionSlots(sourceEntity, sourceInventory);
                    if (slots == null) {
                        yield break;
                    }
                    foreach (int slotIndex in slots.GetSlotIndices(ProductionSlotKind.Output)) {
                        yield return slotIndex;
                    }
                    yield break;
                }
                case HopperExtractMode.EntireInventory:
                    for (int i = 0; i < sourceInventory.SlotsCount; i++) {
                        yield return i;
                    }
                    yield break;
                default:
                    foreach (int slotIndex in ProductionSlotAccess.GetInventorySourceSlotOrder(
                        sourceEntity,
                        sourceInventory,
                        filterValue: 0,
                        explicitOneBasedSlot: 0
                    )) {
                        yield return slotIndex;
                    }
                    yield break;
            }
        }

        void EjectPickable(int value, Point3 cell, int itemValue, int count) {
            // facing 与贴合侧：attached = cell - FaceToPoint3(facing) → 窄口朝贴合，大口沿 facing 外抛
            int facing = LogisticsHopperBlock.GetFacing(value);
            Vector3 mouth = CellFace.FaceToVector3(facing);
            Vector3 position = new Vector3(cell) + new Vector3(0.5f) + 0.1f * mouth;
            // 朝上：按宿主 Pickable 空气阻力+重力，初速使顶点约在正上方一格中心（其余朝向保持轻推）
            Vector3 velocity = facing == 4
                ? new Vector3(0f, 4f, 0f)
                : 0.5f * (mouth + m_random.Vector3(0.12f));
            m_subsystemPickables.AddPickable(itemValue, count, position, velocity, null);
        }

        bool TryInsertIntoAttached(
            Entity destEntity,
            IInventory destInventory,
            int itemValue,
            int count,
            out int remain
        ) {
            remain = count;
            IInventoryProductionSlots slots = ResolveProductionSlots(destEntity, destInventory);
            if (slots != null) {
                bool anyInput = false;
                foreach (int slotIndex in slots.GetSlotIndices(ProductionSlotKind.Input)) {
                    anyInput = true;
                    if (slotIndex < 0 || slotIndex >= destInventory.SlotsCount) {
                        continue;
                    }
                    if (TryInsertAllIntoSlot(destInventory, slotIndex, itemValue, count)) {
                        remain = 0;
                        return true;
                    }
                }
                if (anyInput) {
                    return false;
                }
            }
            remain = ComponentInventoryBase.AcquireItems(destInventory, itemValue, count);
            return remain < count;
        }

        static bool TryInsertAllIntoSlot(IInventory inventory, int slotIndex, int itemValue, int count) {
            int existing = inventory.GetSlotCount(slotIndex);
            if (existing > 0 && inventory.GetSlotValue(slotIndex) != itemValue) {
                return false;
            }
            int space = inventory.GetSlotCapacity(slotIndex, itemValue) - existing;
            if (space < count) {
                return false;
            }
            inventory.AddSlotItems(slotIndex, itemValue, count);
            return true;
        }

        bool TryGetAttachedInventory(int value, Point3 cell, out Entity entity, out IInventory inventory) {
            entity = null;
            inventory = null;
            int facing = LogisticsHopperBlock.GetFacing(value);
            Point3 attached = cell - CellFace.FaceToPoint3(facing);
            ComponentBlockEntity blockEntity = m_subsystemBlockEntities.GetBlockEntity(attached.X, attached.Y, attached.Z);
            if (blockEntity == null) {
                return false;
            }
            entity = blockEntity.Entity;
            inventory = entity.FindComponent<IInventory>();
            return inventory != null;
        }

        static IInventoryProductionSlots ResolveProductionSlots(Entity entity, IInventory inventory) {
            if (inventory is IInventoryProductionSlots fromInventory) {
                return fromInventory;
            }
            return entity?.FindComponent<IInventoryProductionSlots>();
        }

        bool TryReadCell(out int value, out Point3 cell) {
            cell = m_componentBlockEntity.Coordinates;
            value = m_subsystemTerrain.Terrain.GetCellValue(cell.X, cell.Y, cell.Z);
            return Terrain.ExtractContents(value) == LogisticsHopperBlock.Index;
        }

        /// <summary>受料默认启用；落料默认关闭。</summary>
        bool DefaultEnabledForCurrentVariant() {
            if (!TryReadCell(out int value, out _)) {
                return true;
            }
            return LogisticsHopperBlock.GetVariant(value) != LogisticsHopperVariant.Output;
        }

        public static float ClampInterval(float seconds)
            => Math.Clamp(seconds, MinIntervalSeconds, MaxIntervalSeconds);
    }
}
