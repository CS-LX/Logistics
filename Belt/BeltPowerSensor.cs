using Engine;
using Game;
using SCIENEW;

namespace Logistics {
    /// <summary>
    /// 运转判定：任一段两侧（垂直于带走向的左右面，不含沿带前后与上下）接到正在输出的机械能 → 整组运转。
    /// </summary>
    public sealed class BeltPowerSensor {
        readonly SubsystemEnginePower m_subsystemEnginePower;
        readonly BeltTopology m_topology;

        public BeltPowerSensor(SubsystemEnginePower subsystemEnginePower, BeltTopology topology) {
            m_subsystemEnginePower = subsystemEnginePower;
            m_topology = topology;
        }

        public bool IsGroupRunning(BeltGroup group) {
            if (group == null || m_subsystemEnginePower == null) {
                return false;
            }
            foreach (Point3 cell in group.Members) {
                if (IsSegmentSidePowered(cell)) {
                    return true;
                }
            }
            return false;
        }

        public bool IsSegmentSidePowered(Point3 cell) {
            if (!m_topology.TryGetRotation(cell, out int rotation)) {
                return false;
            }
            (int faceA, int faceB) = BeltGeometry.SideFaces(rotation);
            return m_subsystemEnginePower.IsPowered(new CellFace(cell, faceA), out _)
                || m_subsystemEnginePower.IsPowered(new CellFace(cell, faceB), out _);
        }
    }
}
