using Engine;
using Game;
using GameEntitySystem;
using SCIENEW.Utils;
using TemplatesDatabase;

namespace Logistics {
    public class SubsystemStorageUnitBlockBehavior : SubsystemBlockBehavior {
        public const string EntityName = "LogisticsStorageUnit";

        SubsystemTerrain m_subsystemTerrain;
        SubsystemStorageVaults m_subsystemStorageVaults;
        SubsystemBlockEntities m_subsystemBlockEntities;

        public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<LogisticsStorageUnitBlock>()];

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(true);
            m_subsystemStorageVaults = Project.FindSubsystem<SubsystemStorageVaults>(true);
            m_subsystemBlockEntities = Project.FindSubsystem<SubsystemBlockEntities>(true);
        }

        public override void OnBlockAdded(int value, int oldValue, int x, int y, int z) {
            Point3 point = new(x, y, z);
            Entity entity = BlockEntityUtils.CreateBlockEntity(m_subsystemTerrain, EntityName, point);
            var unit = entity.FindComponent<ComponentStorageUnit>(true);
            Guid id = Guid.NewGuid();
            m_subsystemStorageVaults.Create(id, memberCount: 1);
            unit.VaultGuid = id;
        }

        public override void OnBlockRemoved(int value, int newValue, int x, int y, int z) {
            Point3 point = new(x, y, z);
            ComponentBlockEntity blockEntity = m_subsystemBlockEntities.GetBlockEntity(x, y, z);
            if (blockEntity == null) return;

            var unit = blockEntity.Entity.FindComponent<ComponentStorageUnit>();
            Vector3 dropPos = new Vector3(point) + new Vector3(0.5f);
            if (unit != null && unit.VaultGuid != Guid.Empty
                && m_subsystemStorageVaults.TryGet(unit.VaultGuid, out StorageVault vault)) {
                vault.DropAllItems(Project, dropPos);
                m_subsystemStorageVaults.Remove(unit.VaultGuid);
            }

            // 不走 BlockEntityUtils.RemoveBlockEntity：避免与储存库 Drop 重复；Unit.DropAllItems 本为 no-op。
            Project.RemoveEntity(blockEntity.Entity, disposeEntity: true);
        }

        public override void OnBlockGenerated(int value, int x, int y, int z, bool isLoaded) {
            Point3 point = new(x, y, z);
            if (!BlockEntityUtils.GetBlockEntity(m_subsystemTerrain, point, out ComponentBlockEntity blockEntity)) {
                OnBlockAdded(value, 0, x, y, z);
                return;
            }
            var unit = blockEntity.Entity.FindComponent<ComponentStorageUnit>();
            if (unit == null) return;
            if (unit.VaultGuid == Guid.Empty || !m_subsystemStorageVaults.TryGet(unit.VaultGuid, out _)) {
                Guid id = unit.VaultGuid != Guid.Empty ? unit.VaultGuid : Guid.NewGuid();
                if (!m_subsystemStorageVaults.TryGet(id, out _)) {
                    m_subsystemStorageVaults.Create(id, memberCount: 1);
                }
                unit.VaultGuid = id;
            }
        }

        public override bool OnInteract(TerrainRaycastResult raycastResult, ComponentMiner componentMiner) {
            if (componentMiner.ComponentPlayer == null) return false;
            Point3 point = raycastResult.CellFace.Point;
            if (!BlockEntityUtils.GetBlockEntity(m_subsystemTerrain, point, out ComponentBlockEntity blockEntity)) {
                return false;
            }
            var unit = blockEntity.Entity.FindComponent<ComponentStorageUnit>(true);
            componentMiner.ComponentPlayer.ComponentGui.ModalPanelWidget = new StorageVaultWidget(componentMiner.Inventory, unit);
            AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
            return true;
        }

        public override void OnHitByProjectile(CellFace cellFace, WorldItem worldItem) {
            if (worldItem.ToRemove) return;
            ComponentBlockEntity blockEntity = m_subsystemBlockEntities.GetBlockEntity(cellFace.X, cellFace.Y, cellFace.Z);
            blockEntity?.GatherPickable(worldItem);
        }
    }
}
