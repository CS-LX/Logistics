using Engine;
using Game;
using SCIENEW.Utils;

namespace Logistics {
    /// <summary>
    /// 以某格为种子重新发现连通分量并重建 Group：保留旧 Id / 方向 / 速度，整表迁移在途物，
    /// 落不进任何组的在途物才弹出。触及的组只要还有成员在未加载区块，整次重建延后。
    /// </summary>
    public sealed class BeltGroupRebuilder {
        readonly BeltGroupRegistry m_registry;
        readonly BeltTopology m_topology;
        readonly SubsystemTerrain m_subsystemTerrain;
        readonly SubsystemPickables m_subsystemPickables;
        readonly BeltCellDataWriter m_cellDataWriter;
        readonly Action<Point3> m_deferRebuild;
        readonly List<LooseBeltItem> m_looseItems = [];

        public BeltGroupRebuilder(
            BeltGroupRegistry registry,
            BeltTopology topology,
            SubsystemTerrain subsystemTerrain,
            SubsystemPickables subsystemPickables,
            BeltCellDataWriter cellDataWriter,
            Action<Point3> deferRebuild) {
            m_registry = registry;
            m_topology = topology;
            m_subsystemTerrain = subsystemTerrain;
            m_subsystemPickables = subsystemPickables;
            m_cellDataWriter = cellDataWriter;
            m_deferRebuild = deferRebuild;
        }

        public void RebuildAt(Point3 seed) {
            // 种子区块未就绪：延后，避免拆组后只扫到半截
            if (!m_topology.TryGetCellValue(seed, out _)) {
                m_deferRebuild(seed);
                return;
            }

            var rediscoverSeeds = new HashSet<Point3> { seed };
            CollectTouchedGroupMembers(seed, rediscoverSeeds);
            foreach (Point3 n in BeltGeometry.EnumerateNeighborCells(seed)) {
                rediscoverSeeds.Add(n);
                CollectTouchedGroupMembers(n, rediscoverSeeds);
            }

            var touchedGuids = new HashSet<Guid>();
            var signByGuid = new Dictionary<Guid, int>();
            var speedByGuid = new Dictionary<Guid, float>();
            var membersByGuid = new Dictionary<Guid, List<Point3>>();
            m_looseItems.Clear();
            foreach (Point3 p in rediscoverSeeds.ToArray()) {
                if (!m_registry.TryGetAt(p, out BeltGroup g)) {
                    continue;
                }
                if (touchedGuids.Add(g.Id)) {
                    signByGuid[g.Id] = g.Sign;
                    speedByGuid[g.Id] = g.SpeedAbs;
                    membersByGuid[g.Id] = [.. g.Members];
                    foreach (Point3 m in g.Members) {
                        rediscoverSeeds.Add(m);
                    }
                }
            }

            // 触及组仍有成员在未加载区块：整次 Rebuild 延后，禁止拆成残缺组
            foreach (List<Point3> members in membersByGuid.Values) {
                foreach (Point3 m in members) {
                    if (m_topology.TryGetCellValue(m, out _)) {
                        continue;
                    }
                    m_deferRebuild(seed);
                    foreach (Point3 p in rediscoverSeeds) {
                        m_deferRebuild(p);
                    }
                    m_looseItems.Clear();
                    return;
                }
            }

            foreach (Guid gid in touchedGuids) {
                if (!m_registry.TryGet(gid, out BeltGroup g)) {
                    continue;
                }
                foreach (TransportedItem item in g.Inventory.Items) {
                    if (BeltPath.TryGetWorldPose(g, item.BeltPosition, item.SideOffset, m_subsystemTerrain, out Vector3 world, out Vector3 tangent)) {
                        Vector3 travel = g.Sign >= 0 ? tangent : -tangent;
                        m_looseItems.Add(new LooseBeltItem {
                            Value = item.Value,
                            Count = item.Count,
                            Position = world,
                            Velocity = travel * g.SpeedAbs,
                            SideOffset = item.SideOffset
                        });
                    }
                    else {
                        m_looseItems.Add(new LooseBeltItem {
                            Value = item.Value,
                            Count = item.Count,
                            Position = new Vector3(g.Controller) + new Vector3(0.5f),
                            Velocity = item.Velocity,
                            SideOffset = item.SideOffset
                        });
                    }
                }
            }

            foreach (Guid gid in touchedGuids) {
                m_registry.Remove(gid);
                m_cellDataWriter.Forget(gid);
            }

            var visited = new HashSet<Point3>();
            foreach (Point3 s in rediscoverSeeds) {
                if (!m_topology.IsBeltCell(s) || !visited.Add(s)) {
                    continue;
                }
                List<Point3> cluster = m_topology.CollectCluster(s);
                foreach (Point3 c in cluster) {
                    visited.Add(c);
                }
                if (cluster.Count == 0) {
                    continue;
                }
                Guid keepId = SelectKeepGuid(cluster, membersByGuid);
                int sign;
                float speed = BeltGroup.DefaultSpeedAbs;
                if (keepId != Guid.Empty && signByGuid.TryGetValue(keepId, out int oldSign)) {
                    sign = oldSign;
                }
                else {
                    // 新组：从格上 reverse 取 Sign（铺设朝向 / 旧档）；同组成员多数决，平局偏 +1
                    sign = m_topology.ResolveSignFromCells(cluster);
                }
                if (keepId != Guid.Empty && speedByGuid.TryGetValue(keepId, out float oldSpeed)) {
                    speed = oldSpeed;
                }
                if (keepId == Guid.Empty) {
                    keepId = Guid.NewGuid();
                }
                else {
                    membersByGuid.Remove(keepId);
                }
                CreateGroup(keepId, cluster, sign, speed);
            }

            // 按世界坐标迁回在途物；无法落入任何组则弹出
            for (int i = 0; i < m_looseItems.Count; i++) {
                LooseBeltItem loose = m_looseItems[i];
                if (TryInsertLooseItem(loose)) {
                    continue;
                }
                m_subsystemPickables.AddPickable(loose.Value, loose.Count, loose.Position, loose.Velocity, null);
            }
            m_looseItems.Clear();
        }

        void CreateGroup(Guid id, List<Point3> cluster, int sign, float speedAbs) {
            Point3 controller = m_topology.ElectController(cluster);
            List<Point3> ordered = m_topology.OrderMembers(controller, cluster);
            var group = new BeltGroup(id) {
                Controller = controller,
                Sign = sign >= 0 ? 1 : -1,
                SpeedAbs = MathF.Max(0f, speedAbs)
            };
            group.Members.AddRange(ordered);
            m_registry.Add(group);
            foreach (Point3 p in ordered) {
                EnsureSegmentEntity(p);
            }
            // reverse/powered 在 Update 末尾统一写格；此处不 ChangeCell，避免 Rebuild 中再 dirty
        }

        void EnsureSegmentEntity(Point3 point) {
            if (!m_topology.IsBeltCell(point) || BlockEntityUtils.GetBlockEntity(m_subsystemTerrain, point, out _)) {
                return;
            }
            BlockEntityUtils.CreateBlockEntity(
                m_subsystemTerrain,
                SubsystemConveyerBeltBlockBehavior.SegmentEntityName,
                point);
        }

        void CollectTouchedGroupMembers(Point3 point, HashSet<Point3> into) {
            if (!m_registry.TryGetAt(point, out BeltGroup g)) {
                return;
            }
            foreach (Point3 m in g.Members) {
                into.Add(m);
            }
        }

        bool TryInsertLooseItem(LooseBeltItem loose) {
            Point3 cell = Terrain.ToCell(loose.Position);
            if (TryInsertAtCell(cell, loose)) {
                return true;
            }
            foreach (Point3 n in BeltGeometry.EnumerateNeighborCells(cell)) {
                if (TryInsertAtCell(n, loose)) {
                    return true;
                }
            }
            return false;
        }

        bool TryInsertAtCell(Point3 cell, LooseBeltItem loose) {
            if (!m_registry.TryGetAt(cell, out BeltGroup group)) {
                return false;
            }
            float beltPos = BeltPath.WorldToBeltPosition(group, loose.Position, m_subsystemTerrain);
            return group.Inventory.TryInsert(new TransportedItem {
                Value = loose.Value,
                Count = loose.Count,
                BeltPosition = beltPos,
                SideOffset = loose.SideOffset,
                Velocity = loose.Velocity
            });
        }

        /// <summary>与新簇重叠最多的旧组 Id 继续沿用；重叠相同取字典序小者，保证重建稳定。</summary>
        static Guid SelectKeepGuid(List<Point3> cluster, Dictionary<Guid, List<Point3>> membersByGuid) {
            if (membersByGuid.Count == 0) {
                return Guid.Empty;
            }
            var clusterSet = new HashSet<Point3>(cluster);
            Guid best = Guid.Empty;
            int bestOverlap = -1;
            foreach ((Guid gid, List<Point3> members) in membersByGuid) {
                int overlap = 0;
                foreach (Point3 m in members) {
                    if (clusterSet.Contains(m)) {
                        overlap++;
                    }
                }
                if (overlap == 0) {
                    continue;
                }
                if (overlap > bestOverlap || (overlap == bestOverlap && (best == Guid.Empty || gid.CompareTo(best) < 0))) {
                    best = gid;
                    bestOverlap = overlap;
                }
            }
            return best;
        }

        struct LooseBeltItem {
            public int Value;
            public int Count;
            public Vector3 Position;
            public Vector3 Velocity;
            public float SideOffset;
        }
    }
}
