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

        SubsystemTerrain m_subsystemTerrain;
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

        /// <summary>
        /// 把物品放到某带格上。<paramref name="cellFraction"/> 为格内位置，可取 0..1 之间任意值：
        /// 0 = 该格弧长小端、0.5 = 格心、1 = 大端；整组两端会留边。间距不足则失败。
        /// </summary>
        public bool TryInsertItem(Point3 cell, int value, int count, float cellFraction = 0.5f) {
            if (!BeltSegmentInventory.TryResolveSpan(
                    this,
                    m_subsystemTerrain,
                    cell,
                    out BeltGroup group,
                    out float spanStart,
                    out float spanLength)) {
                return false;
            }
            return TryInsertInSpan(group, spanStart, spanLength, cellFraction, value, count);
        }

        /// <summary>
        /// 按来料方向放到某带格上：与带走向同轴（正对端口）时落在靠来料那一端，
        /// 从侧面或上下方送来时落在格心。
        /// </summary>
        public bool TryInsertItemFrom(Point3 cell, Vector3 sourceCenter, int value, int count) {
            if (!BeltSegmentInventory.TryResolveSpan(
                    this,
                    m_subsystemTerrain,
                    cell,
                    out BeltGroup group,
                    out float spanStart,
                    out float spanLength)) {
                return false;
            }
            return TryInsertInSpan(
                group,
                spanStart,
                spanLength,
                ResolveCellFraction(group, cell, spanStart, spanLength, sourceCenter),
                value,
                count);
        }

        /// <summary>读某带格窗口内最近的一件在途物，不取走。</summary>
        public bool TryPeekItem(Point3 cell, out int value, out int count) {
            value = 0;
            count = 0;
            if (!BeltSegmentInventory.TryResolve(this, m_subsystemTerrain, cell, out BeltGroup group, out float center)
                || !BeltSegmentInventory.TryPeek(group, center, out TransportedItem item, out _)) {
                return false;
            }
            value = item.Value;
            count = item.Count;
            return true;
        }

        /// <returns>实际取走的数量。</returns>
        public int RemoveItem(Point3 cell, int count) {
            if (!BeltSegmentInventory.TryResolve(this, m_subsystemTerrain, cell, out BeltGroup group, out float center)) {
                return 0;
            }
            return BeltSegmentInventory.TryRemove(group, center, count);
        }

        bool TryInsertInSpan(BeltGroup group, float spanStart, float spanLength, float cellFraction, int value, int count) {
            float total = BeltPath.TotalLength(group, m_subsystemTerrain);
            float far = MathF.Max(BeltSegmentInventory.EndInset, total - BeltSegmentInventory.EndInset);
            float near = MathF.Min(BeltSegmentInventory.EndInset, far);
            float beltPosition = Math.Clamp(spanStart + spanLength * Math.Clamp(cellFraction, 0f, 1f), near, far);
            return BeltSegmentInventory.TryInsert(group, beltPosition, value, count);
        }

        /// <summary>来料方向与该格带走向夹角决定落点：近乎同轴取入口端，明显横向取格心。</summary>
        float ResolveCellFraction(BeltGroup group, Point3 cell, float spanStart, float spanLength, Vector3 sourceCenter) {
            const float axisAlignedDot = 0.7f;
            if (!BeltPath.TryGetWorldPose(group, spanStart, 0f, m_subsystemTerrain, out Vector3 low, out _)
                || !BeltPath.TryGetWorldPose(group, spanStart + spanLength, 0f, m_subsystemTerrain, out Vector3 high, out _)) {
                return 0.5f;
            }
            Vector3 axis = high - low;
            Vector3 approach = new Vector3(cell) + new Vector3(0.5f) - sourceCenter;
            if (axis.LengthSquared() < 1e-6f || approach.LengthSquared() < 1e-6f) {
                return 0.5f;
            }
            float alignment = Vector3.Dot(Vector3.Normalize(axis), Vector3.Normalize(approach));
            if (MathF.Abs(alignment) < axisAlignedDot) {
                return 0.5f;
            }
            return alignment > 0f ? 0f : 1f;
        }

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
            var subsystemPickables = Project.FindSubsystem<SubsystemPickables>(throwOnError: true);
            var subsystemGameInfo = Project.FindSubsystem<SubsystemGameInfo>(throwOnError: true);
            var subsystemEnginePower = Project.FindSubsystem<SubsystemEnginePower>(throwOnError: true);
            var subsystemBodies = Project.FindSubsystem<SubsystemBodies>(throwOnError: true);
            m_topology = new BeltTopology(m_subsystemTerrain, BlocksManager.GetBlockIndex<ConveyerBeltBlock>());
            m_power = new BeltPowerSensor(subsystemEnginePower, m_topology);
            m_cellDataWriter = new BeltCellDataWriter(m_subsystemTerrain, m_topology, m_power);
            m_simulator = new BeltTransportSimulator(
                m_registry,
                m_topology,
                m_power,
                m_subsystemTerrain,
                subsystemPickables,
                subsystemGameInfo,
                subsystemBodies);
            m_rebuilder = new BeltGroupRebuilder(
                m_registry,
                m_topology,
                m_subsystemTerrain,
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
