using Engine;
using Game;
using GameEntitySystem;
using SCIENEW;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>
    /// 输送带逻辑门面：持有 Group 表与随档存取，并按帧调度「脏重建 → 在途物推进 → 推走站立者 → 写回格状态」。
    /// 连通发现、运物、动力判定、格 Data 写入各由 <c>Belt/</c> 下的协作类负责；
    /// 滚动贴图与调试绘制在 <see cref="SubsystemConveyerBeltVisuals"/>。
    /// </summary>
    public class SubsystemBeltGroups : Subsystem, IUpdateable {
        readonly BeltGroupRegistry m_registry = new();
        readonly HashSet<Point3> m_dirtyRebuild = new();

        BeltTopology m_topology;
        BeltPowerSensor m_power;
        BeltCellDataWriter m_cellDataWriter;
        BeltTransportSimulator m_simulator;
        BeltGroupRebuilder m_rebuilder;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        /// <summary>供视觉侧只读遍历。</summary>
        public Dictionary<Guid, BeltGroup>.ValueCollection Groups => m_registry.Groups;

        public int GroupCount => m_registry.Count;

        /// <summary>刚写过格状态的若干帧内，邻接通知不应触发重建。</summary>
        public bool SuppressRebuildFromVisualSync => m_cellDataWriter.SuppressRebuild;

        public bool TryGet(Guid id, out BeltGroup group) => m_registry.TryGet(id, out group);

        public bool TryGetAt(Point3 point, out BeltGroup group) => m_registry.TryGetAt(point, out group);

        public void RequestRebuild(Point3 point) => m_dirtyRebuild.Add(point);

        public bool IsGroupRunning(BeltGroup group) => m_power.IsGroupRunning(group);

        /// <summary>是否存在任意运转中的输送带组（驱动全局滚动贴图是否前进）。</summary>
        public bool AnyGroupRunning() {
            foreach (BeltGroup group in m_registry.Groups) {
                if (m_power.IsGroupRunning(group)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>切换整组 Sign，并立刻把 reverse 写回各格 Data（UV 反向）。</summary>
        public bool TryToggleSign(Point3 cell, out int newSign) {
            newSign = 1;
            if (!m_registry.TryGetAt(cell, out BeltGroup group)) {
                return false;
            }
            group.Sign = group.Sign >= 0 ? -1 : 1;
            newSign = group.Sign;
            m_cellDataWriter.Sync(group, force: true);
            return true;
        }

        public bool TryAbsorbWorldItem(Point3 cell, WorldItem worldItem) => m_simulator.TryAbsorbWorldItem(cell, worldItem);

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            var subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
            var subsystemPickables = Project.FindSubsystem<SubsystemPickables>(throwOnError: true);
            var subsystemGameInfo = Project.FindSubsystem<SubsystemGameInfo>(throwOnError: true);
            var subsystemEnginePower = Project.FindSubsystem<SubsystemEnginePower>(throwOnError: true);
            var subsystemBodies = Project.FindSubsystem<SubsystemBodies>(throwOnError: true);
            m_topology = new BeltTopology(subsystemTerrain, BlocksManager.GetBlockIndex<ConveyerBeltBlock>());
            m_power = new BeltPowerSensor(subsystemEnginePower, m_topology);
            m_cellDataWriter = new BeltCellDataWriter(subsystemTerrain, m_topology, m_power);
            m_simulator = new BeltTransportSimulator(
                m_registry,
                m_topology,
                m_power,
                subsystemTerrain,
                subsystemPickables,
                subsystemGameInfo,
                subsystemBodies);
            m_rebuilder = new BeltGroupRebuilder(
                m_registry,
                m_topology,
                subsystemTerrain,
                subsystemPickables,
                m_cellDataWriter,
                RequestRebuild);
            m_dirtyRebuild.Clear();
            m_registry.Read(valuesDictionary);
        }

        public override void Save(ValuesDictionary valuesDictionary) {
            base.Save(valuesDictionary);
            m_registry.PurgeInvalidMembers(m_topology);
            m_registry.Write(valuesDictionary);
        }

        public void Update(float dt) {
            if (m_dirtyRebuild.Count > 0) {
                Point3[] dirty = m_dirtyRebuild.ToArray();
                m_dirtyRebuild.Clear();
                foreach (Point3 point in dirty) {
                    m_rebuilder.RebuildAt(point);
                }
            }
            m_simulator.TickInventories(dt);
            m_simulator.PushStandingBodies(dt);
            m_cellDataWriter.SyncAll(m_registry);
            m_cellDataWriter.CountDownSuppress();
        }
    }
}
