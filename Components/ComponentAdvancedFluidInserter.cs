using Engine;
using Game;
using GameEntitySystem;
using SCIENEW;
using SCIENEW.ProductionIO;
using SCIENEW.Utils;
using TemplatesDatabase;

namespace Logistics {
    public class ComponentAdvancedFluidInserter : ComponentTankBase {
        public int m_inIndex;
        public int m_outIndex;
        public SubsystemTerrain m_subsystemTerrain;
        public ComponentBlockEntity m_componentBlockEntity;
        public SubsystemFluidTransfer m_subsystemFluidTransfer;
        public float m_unitVolume = 4;

        public bool Insert() {
            if (!TryResolveTanks(out ITank fromTank, out ITank toTank)) {
                return false;
            }
            int filterValue = GetTankVolume(0) > 0f ? GetTankValue(0) : 0;
            return TryInsertBetweenTanks(fromTank, toTank, m_inIndex, m_outIndex, filterValue, m_unitVolume);
        }

        bool TryResolveTanks(out ITank fromTank, out ITank toTank) {
            fromTank = null!;
            toTank = null!;
            Point3 position = m_componentBlockEntity.Coordinates;
            int blockValue = m_subsystemTerrain.Terrain.GetCellValue(position.X, position.Y, position.Z);
            int direction = AdvancedInserterBlock.GetFacing(blockValue);
            int fromFace = CellFace.OppositeFace(direction);
            int toFace = direction;
            Point3 fromPoint = m_componentBlockEntity.Coordinates + CellFace.FaceToPoint3(fromFace);
            Point3 toPoint = m_componentBlockEntity.Coordinates + CellFace.FaceToPoint3(toFace);
            if (m_subsystemFluidTransfer.TryGetTransferrer(fromPoint, out FluidTransferrer? fromTransferrer)) {
                fromTank = fromTransferrer.GetTank();
                if (fromTank == null) return false;
            }
            else if (BlockEntityUtils.GetBlockEntity(m_subsystemTerrain, fromPoint, out ComponentBlockEntity fromEntity)) {
                fromTank = fromEntity.Entity.FindComponent<ITank>();
                if (fromTank == null) return false;
            }
            else {
                return false;
            }
            if (m_subsystemFluidTransfer.TryGetTransferrer(toPoint, out FluidTransferrer? toTransferrer)) {
                toTank = toTransferrer.GetTank();
                if (toTank == null) return false;
            }
            else if (BlockEntityUtils.GetBlockEntity(m_subsystemTerrain, toPoint, out ComponentBlockEntity toEntity)) {
                toTank = toEntity.Entity.FindComponent<ITank>();
                if (toTank == null) return false;
            }
            else {
                return false;
            }
            return true;
        }

        static bool TryInsertBetweenTanks(
            ITank fromTank,
            ITank toTank,
            int inIndex,
            int outIndex,
            int filterValue,
            float unitVolume
        ) {
            if (inIndex > fromTank.TanksCount || outIndex > toTank.TanksCount) {
                return false;
            }
            foreach (int sourceIndex in ProductionSlotAccess.GetTankSourceSlotOrder(fromTank, filterValue, inIndex)) {
                int fromValue = fromTank.GetTankValue(sourceIndex);
                if (fromValue == 0) continue;
                if (filterValue != 0 && fromValue != filterValue) continue;
                if (!ProductionSlotAccess.CanInsertFluidIntoInputTanks(toTank, fromValue, outIndex)) continue;
                float removedVolume = fromTank.RemoveTankFluid(sourceIndex, unitVolume);
                if (removedVolume <= 0f) continue;
                float overflow = ProductionSlotAccess.InsertFluidIntoInputTanks(toTank, fromValue, removedVolume, outIndex);
                fromTank.AddTankFluid(sourceIndex, fromValue, overflow, out _);
                if (overflow < removedVolume) return true;
            }
            return false;
        }

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap) {
            base.Load(valuesDictionary, idToEntityMap);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(true);
            m_componentBlockEntity = Entity.FindComponent<ComponentBlockEntity>(true);
            m_subsystemFluidTransfer = Project.FindSubsystem<SubsystemFluidTransfer>(true);
            m_inIndex = valuesDictionary.GetValue("InIndex", 0);
            m_outIndex = valuesDictionary.GetValue("OutIndex", 0);
        }

        public override void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap) {
            base.Save(valuesDictionary, entityToIdMap);
            valuesDictionary.SetValue("InIndex", m_inIndex);
            valuesDictionary.SetValue("OutIndex", m_outIndex);
        }
    }
}
