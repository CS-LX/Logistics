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
            m_subsystemStorageVaults.IntegrateNewUnit(unit, point);
        }

        public override void OnBlockRemoved(int value, int newValue, int x, int y, int z) {
            Point3 point = new(x, y, z);
            ComponentBlockEntity blockEntity = m_subsystemBlockEntities.GetBlockEntity(x, y, z);
            if (blockEntity == null) return;

            var unit = blockEntity.Entity.FindComponent<ComponentStorageUnit>();
            Guid vaultGuid = unit?.VaultGuid ?? Guid.Empty;
            Vector3 ejectPos = new Vector3(point) + new Vector3(0.5f);

            // 先卸实体，再按地形剩余分量切分（Collect 依赖方块已不在该格）
            Project.RemoveEntity(blockEntity.Entity, disposeEntity: true);
            m_subsystemStorageVaults.DisintegrateRemovedUnit(vaultGuid, point, ejectPos);
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
                // 读档缺库时恢复：并入邻簇或新建，避免孤立空 Guid
                m_subsystemStorageVaults.IntegrateNewUnit(unit, point);
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
