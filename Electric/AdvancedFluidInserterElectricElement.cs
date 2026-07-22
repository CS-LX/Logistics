using Engine;
using Game;

namespace Logistics {
    public class AdvancedFluidInserterElectricElement : ElectricElement {
        public double? m_lastDispenseTime;
        public SubsystemBlockEntities m_subsystemBlockEntities;

        public AdvancedFluidInserterElectricElement(SubsystemElectricity subsystemElectricity, Point3 point) : base(
            subsystemElectricity,
            new List<CellFace> {
                new(point.X, point.Y, point.Z, 0),
                new(point.X, point.Y, point.Z, 1),
                new(point.X, point.Y, point.Z, 2),
                new(point.X, point.Y, point.Z, 3),
                new(point.X, point.Y, point.Z, 4),
                new(point.X, point.Y, point.Z, 5)
            }
        ) {
            m_subsystemBlockEntities = SubsystemElectricity.Project.FindSubsystem<SubsystemBlockEntities>(throwOnError: true);
        }

        public override bool Simulate() {
            if (CalculateHighInputsCount() > 0
                && (!m_lastDispenseTime.HasValue || SubsystemElectricity.SubsystemTime.GameTime - m_lastDispenseTime > 0.1)) {
                var blockEntity = m_subsystemBlockEntities.GetBlockEntity(CellFaces[0].Point.X, CellFaces[0].Point.Y, CellFaces[0].Point.Z);
                bool placed = false;
                if (blockEntity != null) {
                    var component = blockEntity.Entity.FindComponent<ComponentAdvancedFluidInserter>();
                    if (component != null) {
                        placed = component.Insert();
                    }
                }
                if (placed) {
                    m_lastDispenseTime = SubsystemElectricity.SubsystemTime.GameTime;
                }
            }
            return false;
        }
    }
}
