using Engine;
using Game;
using GameEntitySystem;
using SCIENEW;
using SCIENEW.Utils;
using TemplatesDatabase;

namespace Logistics {
    public class SubsystemLogisticsHopperBlockBehavior : SubsystemBlockBehavior {
        public const string EntityName = "LogisticsHopper";

        SubsystemTerrain m_subsystemTerrain;
        SubsystemBlockEntities m_subsystemBlockEntities;

        public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<LogisticsHopperBlock>()];

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(true);
            m_subsystemBlockEntities = Project.FindSubsystem<SubsystemBlockEntities>(true);
        }

        public override void OnBlockAdded(int value, int oldValue, int x, int y, int z) {
            BlockEntityUtils.CreateBlockEntity(m_subsystemTerrain, EntityName, new Point3(x, y, z));
        }

        public override void OnBlockRemoved(int value, int newValue, int x, int y, int z) {
            BlockEntityUtils.RemoveBlockEntity(m_subsystemTerrain, new Point3(x, y, z));
        }

        public override void OnBlockGenerated(int value, int x, int y, int z, bool isLoaded) {
            Point3 point = new(x, y, z);
            if (!BlockEntityUtils.GetBlockEntity(m_subsystemTerrain, point, out _)) {
                OnBlockAdded(value, 0, x, y, z);
            }
        }

        public override bool OnInteract(TerrainRaycastResult raycastResult, ComponentMiner componentMiner) {
            ComponentPlayer player = componentMiner.ComponentPlayer;
            if (player == null) {
                return false;
            }
            if (BlockInterfaceResolver.ResolveFromValue<IPreferPlacement>(componentMiner.ActiveBlockValue) != null) {
                return false;
            }
            Point3 point = raycastResult.CellFace.Point;
            if (!BlockEntityUtils.GetBlockEntity(m_subsystemTerrain, point, out ComponentBlockEntity blockEntity)) {
                return false;
            }
            var hopper = blockEntity.Entity.FindComponent<ComponentLogisticsHopper>(true);
            DialogsManager.ShowDialog(player.GuiWidget, new HopperDialog(hopper, point));
            return true;
        }

        public override void OnHitByProjectile(CellFace cellFace, WorldItem worldItem) {
            if (worldItem == null || worldItem.ToRemove) {
                return;
            }
            int cellValue = m_subsystemTerrain.Terrain.GetCellValue(cellFace.X, cellFace.Y, cellFace.Z);
            // 落料斗只吐不吸；撞上也不处理
            if (LogisticsHopperBlock.GetVariant(cellValue) != LogisticsHopperVariant.Input) {
                return;
            }
            ComponentBlockEntity blockEntity = m_subsystemBlockEntities.GetBlockEntity(cellFace.X, cellFace.Y, cellFace.Z);
            blockEntity?.Entity.FindComponent<ComponentLogisticsHopper>()?.TryAbsorb(worldItem);
        }
    }
}
