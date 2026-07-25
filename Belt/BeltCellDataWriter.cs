using Engine;
using Game;

namespace Logistics {
    /// <summary>
    /// 把 Group 的方向与运转状态写回各格 Data（reverse + powered），供地形 mesh 取用。
    /// 真相源仍是 Group；仅在状态变化时 ChangeCell，并抑制紧随其后的邻格重建。
    /// </summary>
    public sealed class BeltCellDataWriter {
        readonly SubsystemTerrain m_subsystemTerrain;
        readonly BeltTopology m_topology;
        readonly BeltPowerSensor m_power;
        readonly Dictionary<Guid, long> m_syncedKey = new();

        int m_suppressRebuildRemaining;

        public BeltCellDataWriter(SubsystemTerrain subsystemTerrain, BeltTopology topology, BeltPowerSensor power) {
            m_subsystemTerrain = subsystemTerrain;
            m_topology = topology;
            m_power = power;
        }

        /// <summary>写格之后若干帧内禁止邻格请求重建（ChangeCell 的邻接通知是延迟的）。</summary>
        public bool SuppressRebuild => m_suppressRebuildRemaining > 0;

        public void CountDownSuppress() {
            if (m_suppressRebuildRemaining > 0) {
                m_suppressRebuildRemaining--;
            }
        }

        /// <summary>组被拆除或重建时丢掉缓存键，避免新成员沿用旧判断而漏写。</summary>
        public void Forget(Guid id) => m_syncedKey.Remove(id);

        public void Sync(BeltGroup group, bool force = false) {
            if (group == null) {
                return;
            }
            int reverse = ConveyerBeltBlock.SignToReverse(group.Sign);
            int powered = m_power.IsGroupRunning(group) ? 1 : 0;
            long key = ((long)reverse << 1) | (uint)powered;
            if (!force && m_syncedKey.TryGetValue(group.Id, out long prev) && prev == key) {
                return;
            }
            m_syncedKey[group.Id] = key;
            bool anyChanged = false;
            foreach (Point3 cell in group.Members) {
                if (!m_topology.TryGetCellValue(cell, out int value)
                    || Terrain.ExtractContents(value) != m_topology.BeltIndex) {
                    continue;
                }
                int data = Terrain.ExtractData(value);
                int newData = ConveyerBeltBlock.SetPowered(ConveyerBeltBlock.SetReverse(data, reverse), powered);
                if (newData == data) {
                    continue;
                }
                anyChanged = true;
                m_subsystemTerrain.ChangeCell(cell.X, cell.Y, cell.Z, Terrain.ReplaceData(value, newData));
            }
            // ChangeCell → ProcessModifiedCells 的 OnNeighbor 在后续帧；多留 2 帧抑制 Rebuild
            if (anyChanged) {
                m_suppressRebuildRemaining = Math.Max(m_suppressRebuildRemaining, 2);
            }
        }

        public void SyncAll(BeltGroupRegistry registry) {
            foreach (BeltGroup group in registry.Groups) {
                Sync(group);
            }
            if (m_syncedKey.Count <= registry.Count) {
                return;
            }
            List<Guid> stale = null;
            foreach (Guid id in m_syncedKey.Keys) {
                if (!registry.Contains(id)) {
                    stale ??= [];
                    stale.Add(id);
                }
            }
            if (stale == null) {
                return;
            }
            foreach (Guid id in stale) {
                m_syncedKey.Remove(id);
            }
        }
    }
}
