using Engine;
using Game;
using GameEntitySystem;
using SCIENEW.Utils;
using TemplatesDatabase;

namespace Logistics {
    public class SubsystemAdvancedInserterBlockBehavior : SubsystemBlockBehavior {
        public const string InserterEntityName = "AdvancedInserter";
        public const string FluidInserterEntityName = "AdvancedFluidInserter";

        SubsystemTerrain m_subsystemTerrain;

        public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<AdvancedInserterBlock>()];

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
        }

        static string GetEntityName(int value)
            => AdvancedInserterBlock.IsFluid(value) ? FluidInserterEntityName : InserterEntityName;

        public override void OnBlockAdded(int value, int oldValue, int x, int y, int z) {
            BlockEntityUtils.CreateBlockEntity(m_subsystemTerrain, GetEntityName(value), new Point3(x, y, z));
        }

        public override void OnBlockRemoved(int value, int newValue, int x, int y, int z) {
            BlockEntityUtils.RemoveBlockEntity(m_subsystemTerrain, new Point3(x, y, z));
        }

        public override void OnBlockGenerated(int value, int x, int y, int z, bool isLoaded) {
            if (!BlockEntityUtils.GetBlockEntity(m_subsystemTerrain, new Point3(x, y, z), out _)) {
                OnBlockAdded(value, 0, x, y, z);
            }
        }

        public override void OnBlockModified(int value, int oldValue, int x, int y, int z) {
            if (AdvancedInserterBlock.IsFluid(value) != AdvancedInserterBlock.IsFluid(oldValue)) {
                BlockEntityUtils.RemoveBlockEntity(m_subsystemTerrain, new Point3(x, y, z));
                BlockEntityUtils.CreateBlockEntity(m_subsystemTerrain, GetEntityName(value), new Point3(x, y, z));
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
            int value = raycastResult.Value;
            if (AdvancedInserterBlock.IsFluid(value)) {
                var component = blockEntity.Entity.FindComponent<ComponentAdvancedFluidInserter>(throwOnError: true);
                componentMiner.ComponentPlayer.ComponentGui.ModalPanelWidget = new AdvancedFluidInserterWidget(componentMiner.Inventory, component);
            }
            else {
                var component = blockEntity.Entity.FindComponent<ComponentAdvancedInserter>(throwOnError: true);
                componentMiner.ComponentPlayer.ComponentGui.ModalPanelWidget = new AdvancedInserterWidget(componentMiner.Inventory, component);
            }
            AudioManager.PlaySound("Audio/UI/ButtonClick", 1f, 0f, 0f);
            return true;
        }
    }
}
