using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>
    /// 储存单元：持有 <see cref="VaultGuid"/>，对外实现 <see cref="IInventory"/> 并转发到储存库。
    /// </summary>
    public class ComponentStorageUnit : Component, IInventory {
        public Guid VaultGuid { get; set; }

        ComponentBlockEntity m_componentBlockEntity;
        SubsystemStorageVaults m_subsystemStorageVaults;

        Project IInventory.Project => Project;

        public int SlotsCount => TryResolveVault(out StorageVault vault) ? vault.SlotsCount : 0;

        public int VisibleSlotsCount {
            get => SlotsCount;
            set { }
        }

        public int ActiveSlotIndex {
            get => -1;
            set { }
        }

        public bool TryResolveVault(out StorageVault vault) {
            vault = null;
            if (VaultGuid == Guid.Empty || m_subsystemStorageVaults == null) return false;
            return m_subsystemStorageVaults.TryGet(VaultGuid, out vault);
        }

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap) {
            m_subsystemStorageVaults = Project.FindSubsystem<SubsystemStorageVaults>(true);
            m_componentBlockEntity = Entity.FindComponent<ComponentBlockEntity>(true);
            VaultGuid = valuesDictionary.GetValue("VaultGuid", Guid.Empty);
            m_componentBlockEntity.m_inventoryToGatherPickable = this;
        }

        public override void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap) {
            valuesDictionary.SetValue("VaultGuid", VaultGuid);
        }

        public int GetSlotValue(int slotIndex)
            => TryResolveVault(out StorageVault vault) ? vault.GetSlotValue(slotIndex) : 0;

        public int GetSlotCount(int slotIndex)
            => TryResolveVault(out StorageVault vault) ? vault.GetSlotCount(slotIndex) : 0;

        public int GetSlotCapacity(int slotIndex, int value)
            => TryResolveVault(out StorageVault vault) ? vault.GetSlotCapacity(slotIndex, value) : 0;

        public int GetSlotProcessCapacity(int slotIndex, int value) => 0;

        public void AddSlotItems(int slotIndex, int value, int count) {
            if (!TryResolveVault(out StorageVault vault)) return;
            vault.AddSlotItems(slotIndex, value, count);
        }

        public void ProcessSlotItems(int slotIndex, int value, int count, int processCount, out int processedValue, out int processedCount) {
            processedValue = value;
            processedCount = count;
        }

        public int RemoveSlotItems(int slotIndex, int count) {
            if (!TryResolveVault(out StorageVault vault)) return 0;
            return vault.RemoveSlotItems(slotIndex, count);
        }

        /// <summary>物品在 Subsystem 储存库中；实体移除时由 Behavior 显式处理，此处不倾倒。</summary>
        public void DropAllItems(Vector3 position) { }
    }
}
