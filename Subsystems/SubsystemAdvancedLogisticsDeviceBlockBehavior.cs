using Engine;
using Game;
using GameEntitySystem;
using SCIENEW.Utils;
using TemplatesDatabase;

namespace Logistics {
    public class SubsystemAdvancedLogisticsDeviceBlockBehavior : SubsystemBlockBehavior {
        SubsystemTerrain m_subsystemTerrain;
        SubsystemBlockEntities m_subsystemBlockEntities;
        SubsystemPickables m_subsystemPickables;
        SubsystemProjectiles m_subsystemProjectiles;

        public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<AdvancedLogisticsDeviceBlock>()];

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
            m_subsystemBlockEntities = Project.FindSubsystem<SubsystemBlockEntities>(throwOnError: true);
            m_subsystemPickables = Project.FindSubsystem<SubsystemPickables>(throwOnError: true);
            m_subsystemProjectiles = Project.FindSubsystem<SubsystemProjectiles>(throwOnError: true);
        }

        public override void OnBlockAdded(int value, int oldValue, int x, int y, int z) {
            string? entityName = AdvancedLogisticsDeviceBlock.GetDefinition(value).EntityName;
            if (entityName != null) {
                BlockEntityUtils.CreateBlockEntity(m_subsystemTerrain, entityName, new Point3(x, y, z));
            }
        }

        public override void OnBlockRemoved(int value, int newValue, int x, int y, int z) {
            BlockEntityUtils.RemoveBlockEntity(m_subsystemTerrain, new Point3(x, y, z));
        }

        public override void OnBlockGenerated(int value, int x, int y, int z, bool isLoaded) {
            string? entityName = AdvancedLogisticsDeviceBlock.GetDefinition(value).EntityName;
            if (entityName != null && !BlockEntityUtils.GetBlockEntity(m_subsystemTerrain, new Point3(x, y, z), out _)) {
                BlockEntityUtils.CreateBlockEntity(m_subsystemTerrain, entityName, new Point3(x, y, z));
            }
        }

        public override void OnBlockModified(int value, int oldValue, int x, int y, int z) {
            string? entityName = AdvancedLogisticsDeviceBlock.GetDefinition(value).EntityName;
            string? oldEntityName = AdvancedLogisticsDeviceBlock.GetDefinition(oldValue).EntityName;
            if (entityName == oldEntityName) {
                return;
            }
            BlockEntityUtils.RemoveBlockEntity(m_subsystemTerrain, new Point3(x, y, z));
            if (entityName != null) {
                BlockEntityUtils.CreateBlockEntity(m_subsystemTerrain, entityName, new Point3(x, y, z));
            }
        }

        public override bool OnInteract(TerrainRaycastResult raycastResult, ComponentMiner componentMiner) {
            if (componentMiner.ComponentPlayer == null) {
                return false;
            }
            Point3 point = raycastResult.CellFace.Point;
            if (!BlockEntityUtils.GetBlockEntity(m_subsystemTerrain, point, out ComponentBlockEntity blockEntity)) {
                return false;
            }
            AdvancedLogisticsDeviceDefinition definition = AdvancedLogisticsDeviceBlock.GetDefinition(raycastResult.Value);
            if (!definition.OpenWidget(componentMiner.Inventory, componentMiner, blockEntity)) {
                return false;
            }
            AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
            return true;
        }

        public override void OnHitByProjectile(CellFace cellFace, WorldItem worldItem) {
            if (worldItem.ToRemove) {
                return;
            }
            int cellValue = m_subsystemTerrain.Terrain.GetCellValue(cellFace.X, cellFace.Y, cellFace.Z);
            if (!AdvancedLogisticsDeviceBlock.IsSorter(cellValue)) {
                return;
            }
            ComponentBlockEntity blockEntity = m_subsystemBlockEntities.GetBlockEntity(cellFace.X, cellFace.Y, cellFace.Z);
            if (blockEntity == null) {
                return;
            }
            var pickable = worldItem as Pickable;
            int count = pickable?.Count ?? 1;
            if (count > 1) {
                return;
            }
            var sorter = blockEntity.Entity.FindComponent<ComponentAdvancedSorter>(throwOnError: true);
            int outputFace = sorter.FindOutputFace(worldItem.Value);
            Vector3 eject = outputFace >= 0
                ? CellFace.FaceToVector3(outputFace)
                : -CellFace.FaceToVector3(cellFace.Face);
            Vector3 position = new Vector3(cellFace.Point) + new Vector3(0.5f);
            worldItem.ToRemove = true;
            if (m_subsystemProjectiles.FireProjectile(worldItem.Value, position + 0.75f * eject, 10f * eject, Vector3.Zero, null) == null) {
                m_subsystemPickables.AddPickable(worldItem.Value, 1, position + 0.75f * eject, 1f * eject, null);
            }
        }
    }
}
