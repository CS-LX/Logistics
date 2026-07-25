using Engine;
using Game;
using GameEntitySystem;
using SCIENEW.ProductionIO;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>
    /// 输送带单格门面：1 逻辑槽映射 Group 在途表「格心 ±0.5」窗口；真库存在 <see cref="SubsystemBeltGroups"/>。
    /// </summary>
    public class ComponentConveyerBeltSegment : Component, IInventory, IInventoryProductionSlots {
        ComponentBlockEntity m_componentBlockEntity;
        SubsystemBeltGroups m_subsystemBeltGroups;
        SubsystemTerrain m_subsystemTerrain;

        Project IInventory.Project => Project;

        public int SlotsCount => 1;

        public int VisibleSlotsCount {
            get => 1;
            set { }
        }

        public int ActiveSlotIndex {
            get => -1;
            set { }
        }

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap) {
            m_subsystemBeltGroups = Project.FindSubsystem<SubsystemBeltGroups>(throwOnError: true);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
            m_componentBlockEntity = Entity.FindComponent<ComponentBlockEntity>(throwOnError: true);
        }

        /// <summary>出料：窗口有物才报槽 0；进料走 AcquireItems 全槽 fallback。</summary>
        public IEnumerable<int> GetSlotIndices(ProductionSlotKind kind) {
            if (kind != ProductionSlotKind.Output) {
                yield break;
            }
            if (GetSlotCount(0) > 0) {
                yield return 0;
            }
        }

        public int GetSlotValue(int slotIndex) {
            if (slotIndex != 0 || !TryWindow(out BeltGroup group, out float center)) {
                return 0;
            }
            return BeltSegmentInventory.TryPeek(group, center, out TransportedItem item, out _) ? item.Value : 0;
        }

        public int GetSlotCount(int slotIndex) {
            if (slotIndex != 0 || !TryWindow(out BeltGroup group, out float center)) {
                return 0;
            }
            return BeltSegmentInventory.TryPeek(group, center, out TransportedItem item, out _) ? item.Count : 0;
        }

        public int GetSlotCapacity(int slotIndex, int value) {
            if (slotIndex != 0 || value == 0 || !TryWindow(out BeltGroup group, out float center)) {
                return 0;
            }
            if (BeltSegmentInventory.TryPeek(group, center, out TransportedItem existing, out _)) {
                return existing.Value == value ? existing.Count : 0;
            }
            return BeltSegmentInventory.CanInsert(group, center, value) ? 1 : 0;
        }

        public int GetSlotProcessCapacity(int slotIndex, int value) => 0;

        public void AddSlotItems(int slotIndex, int value, int count) {
            if (slotIndex != 0 || count <= 0 || !TryWindow(out BeltGroup group, out float center)) {
                return;
            }
            if (BeltSegmentInventory.TryPeek(group, center, out TransportedItem existing, out _)) {
                if (existing.Value == value) {
                    existing.Count += count;
                }
                return;
            }
            BeltSegmentInventory.TryInsert(group, center, value, count);
        }

        public void ProcessSlotItems(int slotIndex, int value, int count, int processCount, out int processedValue, out int processedCount) {
            processedValue = value;
            processedCount = count;
        }

        public int RemoveSlotItems(int slotIndex, int count) {
            if (slotIndex != 0 || count <= 0 || !TryWindow(out BeltGroup group, out float center)) {
                return 0;
            }
            return BeltSegmentInventory.TryRemove(group, center, count);
        }

        /// <summary>真物在 Group；拆除由 Behavior 卸实体，此处不倾倒。</summary>
        public void DropAllItems(Vector3 position) { }

        bool TryWindow(out BeltGroup group, out float center)
            => BeltSegmentInventory.TryResolve(
                m_subsystemBeltGroups,
                m_subsystemTerrain,
                m_componentBlockEntity.Coordinates,
                out group,
                out center);
    }
}
