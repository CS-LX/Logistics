using Engine;
using Game;
using GameEntitySystem;
using SCIENEW.Utils;
using TemplatesDatabase;

namespace Logistics {
    public class SubsystemAdvancedLogisticsDeviceBlockBehavior : SubsystemBlockBehavior {
        SubsystemTerrain m_subsystemTerrain;

        public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<AdvancedLogisticsDeviceBlock>()];

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
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
    }
}
