using Engine;
using Engine.Graphics;
using Engine.Input;
using Engine.Media;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>
    /// 输送带 Group 发现 / 存档 / 脏标记重建。P0：无运物；Sign 默认 +1。
    /// 调试绘制：F6 开关（对齐 SCIENEW 太阳能/LED/VoltNet 的 F3–F5 调试框）。
    /// </summary>
    public class SubsystemBeltGroups : Subsystem, IUpdateable, IDrawable {
        public const string SaveKeyGroups = "Groups";

        /// <summary>与 <see cref="SubsystemConveyerBeltBlockBehavior"/> 一致：行=水平四向，列=同层/上层/下层。</summary>
        static readonly Point3[,] NeighborOffsets = {
            { new(0, 0, -1), new(0, 1, -1), new(0, -1, -1) },
            { new(-1, 0, 0), new(-1, 1, 0), new(-1, -1, 0) },
            { new(0, 0, 1), new(0, 1, 1), new(0, -1, 1) },
            { new(1, 0, 0), new(1, 1, 0), new(1, -1, 0) }
        };

        readonly Dictionary<Guid, BeltGroup> m_groups = new();
        readonly Dictionary<Point3, Guid> m_cellToGroup = new();
        readonly HashSet<Point3> m_dirtyRebuild = new();
        readonly PrimitivesRenderer3D m_primitivesRenderer3D = new();

        FlatBatch3D m_flatBatch;
        FontBatch3D m_textBatch;
        SubsystemTerrain m_subsystemTerrain;
        int m_beltIndex;
        bool m_debugCanDraw;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public int[] DrawOrders => [1000];

        public bool TryGet(Guid id, out BeltGroup group) => m_groups.TryGetValue(id, out group);

        public bool TryGetAt(Point3 point, out BeltGroup group) {
            if (m_cellToGroup.TryGetValue(point, out Guid id) && m_groups.TryGetValue(id, out group)) {
                return true;
            }
            group = null;
            return false;
        }

        public void RequestRebuild(Point3 point) => m_dirtyRebuild.Add(point);

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
            m_beltIndex = BlocksManager.GetBlockIndex<ConveyerBeltBlock>();
            m_flatBatch = m_primitivesRenderer3D.FlatBatch(0, DepthStencilState.None);
            m_textBatch = m_primitivesRenderer3D.FontBatch(BitmapFont.DebugFont, 0, DepthStencilState.None);
            m_groups.Clear();
            m_cellToGroup.Clear();
            m_dirtyRebuild.Clear();
            ValuesDictionary groupsVd = valuesDictionary.GetValue<ValuesDictionary>(SaveKeyGroups, null);
            if (groupsVd == null) {
                return;
            }
            foreach (KeyValuePair<string, object> kv in groupsVd) {
                if (kv.Value is not ValuesDictionary groupVd) {
                    continue;
                }
                BeltGroup group = BeltGroup.Read(groupVd);
                if (group.Members.Count == 0) {
                    continue;
                }
                m_groups[group.Id] = group;
                foreach (Point3 p in group.Members) {
                    m_cellToGroup[p] = group.Id;
                }
            }
        }

        public override void Save(ValuesDictionary valuesDictionary) {
            base.Save(valuesDictionary);
            PurgeInvalidMembers();
            ValuesDictionary groupsVd = new();
            foreach (KeyValuePair<Guid, BeltGroup> kv in m_groups) {
                if (kv.Value.Members.Count == 0) {
                    continue;
                }
                ValuesDictionary groupVd = new();
                kv.Value.Write(groupVd);
                groupsVd.SetValue(kv.Key.ToString("D"), groupVd);
            }
            valuesDictionary.SetValue(SaveKeyGroups, groupsVd);
        }

        public void Update(float dt) {
            if (Keyboard.IsKeyDownOnce(Key.F2)) {
                m_debugCanDraw = !m_debugCanDraw;
            }
            if (m_dirtyRebuild.Count == 0) {
                return;
            }
            Point3[] dirty = m_dirtyRebuild.ToArray();
            m_dirtyRebuild.Clear();
            foreach (Point3 point in dirty) {
                RebuildAt(point);
            }
        }

        public void Draw(Camera camera, int drawOrder) {
            if (!m_debugCanDraw) {
                return;
            }
            int groupCount = m_groups.Count;
            foreach (BeltGroup group in m_groups.Values) {
                Color color = ColorForGuid(group.Id);
                Color lineColor = Color.Lerp(color, Color.White, 0.35f);
                for (int i = 0; i < group.Members.Count; i++) {
                    Point3 p = group.Members[i];
                    bool isController = p == group.Controller;
                    Color boxColor = isController ? Color.Yellow : color;
                    m_flatBatch.QueueBoundingBox(
                        new BoundingBox(new Vector3(p), new Vector3(p) + Vector3.One),
                        boxColor);
                    if (i + 1 < group.Members.Count) {
                        Vector3 a = new Vector3(p) + new Vector3(0.5f, 0.55f, 0.5f);
                        Vector3 b = new Vector3(group.Members[i + 1]) + new Vector3(0.5f, 0.55f, 0.5f);
                        m_flatBatch.QueueLine(a, b, lineColor, lineColor);
                    }
                }
                Vector3 textPos = new Vector3(group.Controller) + new Vector3(0.5f, 1.15f, 0.5f);
                Vector3 right = Vector3.Cross(camera.ViewDirection, Vector3.UnitY);
                if (right.LengthSquared() < 1e-6f) {
                    right = Vector3.UnitX;
                }
                right = Vector3.Normalize(right);
                Vector3 up = Vector3.Normalize(-Vector3.Cross(right, camera.ViewDirection));
                const float s = 0.006f;
                string shortId = group.Id.ToString("N")[..8];
                m_textBatch.QueueText(
                    $"{shortId}\n{group.Members.Count} cells\nSign={group.Sign}\n{groupCount} groups",
                    textPos,
                    right * s,
                    up * s,
                    Color.White,
                    TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter,
                    Vector2.Zero);
            }
            m_primitivesRenderer3D.Flush(camera.ViewProjectionMatrix);
        }

        static Color ColorForGuid(Guid id) {
            int h = id.GetHashCode();
            // 避开过暗：偏亮色相，便于区分不同组
            byte r = (byte)(96 + ((h >> 0) & 0x7F));
            byte g = (byte)(96 + ((h >> 8) & 0x7F));
            byte b = (byte)(96 + ((h >> 16) & 0x7F));
            return new Color(r, g, b);
        }

        /// <summary>以 seed 为起点，重发现其触及的连通分量并重建 Group。</summary>
        public void RebuildAt(Point3 seed) {
            var rediscoverSeeds = new HashSet<Point3> { seed };
            CollectTouchedGroupMembers(seed, rediscoverSeeds);
            foreach (Point3 n in EnumerateBeltNeighborCells(seed)) {
                rediscoverSeeds.Add(n);
                CollectTouchedGroupMembers(n, rediscoverSeeds);
            }

            var touchedGuids = new HashSet<Guid>();
            var signByGuid = new Dictionary<Guid, int>();
            var speedByGuid = new Dictionary<Guid, float>();
            var membersByGuid = new Dictionary<Guid, List<Point3>>();
            // 快照迭代，避免向 rediscoverSeeds 追加时改集合
            foreach (Point3 p in rediscoverSeeds.ToArray()) {
                if (!m_cellToGroup.TryGetValue(p, out Guid gid) || !m_groups.TryGetValue(gid, out BeltGroup g)) {
                    continue;
                }
                if (touchedGuids.Add(gid)) {
                    signByGuid[gid] = g.Sign;
                    speedByGuid[gid] = g.SpeedAbs;
                    membersByGuid[gid] = [.. g.Members];
                    foreach (Point3 m in g.Members) {
                        rediscoverSeeds.Add(m);
                    }
                }
            }

            foreach (Guid gid in touchedGuids) {
                RemoveGroup(gid);
            }

            var visited = new HashSet<Point3>();
            foreach (Point3 s in rediscoverSeeds) {
                if (!IsBeltCell(s) || !visited.Add(s)) {
                    continue;
                }
                List<Point3> cluster = CollectCluster(s);
                foreach (Point3 c in cluster) {
                    visited.Add(c);
                }
                if (cluster.Count == 0) {
                    continue;
                }
                Guid keepId = SelectKeepGuid(cluster, membersByGuid);
                int sign = 1;
                float speed = BeltGroup.DefaultSpeedAbs;
                if (keepId != Guid.Empty && signByGuid.TryGetValue(keepId, out int oldSign)) {
                    sign = oldSign;
                }
                if (keepId != Guid.Empty && speedByGuid.TryGetValue(keepId, out float oldSpeed)) {
                    speed = oldSpeed;
                }
                if (keepId == Guid.Empty) {
                    keepId = Guid.NewGuid();
                }
                else {
                    // 拆分时每个旧 Guid 只复用一次，其余分量新建
                    membersByGuid.Remove(keepId);
                }
                CreateGroup(keepId, cluster, sign, speed);
            }
        }

        void CollectTouchedGroupMembers(Point3 point, HashSet<Point3> into) {
            if (!m_cellToGroup.TryGetValue(point, out Guid gid) || !m_groups.TryGetValue(gid, out BeltGroup g)) {
                return;
            }
            foreach (Point3 m in g.Members) {
                into.Add(m);
            }
        }

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

        void CreateGroup(Guid id, List<Point3> cluster, int sign, float speedAbs) {
            Point3 controller = ElectController(cluster);
            List<Point3> ordered = OrderMembers(controller, cluster);
            var group = new BeltGroup(id) {
                Controller = controller,
                Sign = sign >= 0 ? 1 : -1,
                SpeedAbs = MathF.Max(0f, speedAbs)
            };
            group.Members.AddRange(ordered);
            m_groups[id] = group;
            foreach (Point3 p in ordered) {
                m_cellToGroup[p] = id;
            }
        }

        void RemoveGroup(Guid id) {
            if (!m_groups.TryGetValue(id, out BeltGroup group)) {
                return;
            }
            foreach (Point3 p in group.Members) {
                if (m_cellToGroup.TryGetValue(p, out Guid g) && g == id) {
                    m_cellToGroup.Remove(p);
                }
            }
            m_groups.Remove(id);
        }

        void PurgeInvalidMembers() {
            List<Guid> removeEmpty = null;
            foreach (KeyValuePair<Guid, BeltGroup> kv in m_groups) {
                BeltGroup group = kv.Value;
                for (int i = group.Members.Count - 1; i >= 0; i--) {
                    Point3 p = group.Members[i];
                    if (IsBeltCell(p)) {
                        continue;
                    }
                    group.Members.RemoveAt(i);
                    if (m_cellToGroup.TryGetValue(p, out Guid g) && g == group.Id) {
                        m_cellToGroup.Remove(p);
                    }
                }
                if (group.Members.Count == 0) {
                    removeEmpty ??= [];
                    removeEmpty.Add(group.Id);
                    continue;
                }
                if (!group.Members.Contains(group.Controller)) {
                    group.Controller = ElectController(group.Members);
                    List<Point3> ordered = OrderMembers(group.Controller, group.Members);
                    group.Members.Clear();
                    group.Members.AddRange(ordered);
                }
            }
            if (removeEmpty == null) {
                return;
            }
            foreach (Guid id in removeEmpty) {
                m_groups.Remove(id);
            }
        }

        bool IsBeltCell(Point3 p) {
            int value = m_subsystemTerrain.Terrain.GetCellValueFastChunkExists(p.X, p.Y, p.Z);
            return Terrain.ExtractContents(value) == m_beltIndex;
        }

        /// <summary>
        /// 同组邻接：沿自身朝向轴（含坡）且双向成立。
        /// 直角贴靠两侧轴不同 → 不同 Group（P3 端点交接）。
        /// </summary>
        IEnumerable<Point3> EnumerateLineNeighbors(Point3 p) {
            int value = m_subsystemTerrain.Terrain.GetCellValueFastChunkExists(p.X, p.Y, p.Z);
            int rotation = ConveyerBeltBlock.GetRotation(Terrain.ExtractData(value));
            for (int i = 0; i < 4; i++) {
                for (int k = 0; k < 3; k++) {
                    Point3 o = NeighborOffsets[i, k];
                    Point3 n = new(p.X + o.X, p.Y + o.Y, p.Z + o.Z);
                    if (!IsBeltCell(n) || !IsAlongAxisStep(p, n, rotation)) {
                        continue;
                    }
                    int nValue = m_subsystemTerrain.Terrain.GetCellValueFastChunkExists(n.X, n.Y, n.Z);
                    int nRotation = ConveyerBeltBlock.GetRotation(Terrain.ExtractData(nValue));
                    if (!IsAlongAxisStep(n, p, nRotation)) {
                        continue;
                    }
                    yield return n;
                }
            }
        }

        /// <summary>from→to 是否落在 from 朝向轴上一步（含坡 Y±1）。rotation 0/2 沿 Z，1/3 沿 X。</summary>
        static bool IsAlongAxisStep(Point3 from, Point3 to, int fromRotation) {
            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            int dz = to.Z - from.Z;
            if (Math.Abs(dy) > 1) {
                return false;
            }
            if ((fromRotation & 1) == 0) {
                return dx == 0 && Math.Abs(dz) == 1;
            }
            return dz == 0 && Math.Abs(dx) == 1;
        }

        List<Point3> CollectCluster(Point3 seed) {
            var result = new List<Point3>();
            if (!IsBeltCell(seed)) {
                return result;
            }
            var queue = new Queue<Point3>();
            var visited = new HashSet<Point3>();
            queue.Enqueue(seed);
            visited.Add(seed);
            while (queue.Count > 0) {
                Point3 p = queue.Dequeue();
                result.Add(p);
                foreach (Point3 n in EnumerateLineNeighbors(p)) {
                    if (visited.Add(n)) {
                        queue.Enqueue(n);
                    }
                }
            }
            return result;
        }

        IEnumerable<Point3> EnumerateBeltNeighborCells(Point3 p) {
            // 重建触达仍扫几何邻格（含直角旁另一组）
            for (int i = 0; i < 4; i++) {
                for (int k = 0; k < 3; k++) {
                    Point3 o = NeighborOffsets[i, k];
                    yield return new Point3(p.X + o.X, p.Y + o.Y, p.Z + o.Z);
                }
            }
        }

        Point3 ElectController(IReadOnlyList<Point3> cluster) {
            if (cluster.Count == 0) {
                return Point3.Zero;
            }
            Dictionary<Point3, int> degree = BuildDegree(cluster);
            Point3? bestEnd = null;
            Point3? bestAny = null;
            foreach (Point3 p in cluster) {
                if (bestAny == null || ComparePoint3(p, bestAny.Value) < 0) {
                    bestAny = p;
                }
                int d = degree.GetValueOrDefault(p, 0);
                if (d <= 1) {
                    if (bestEnd == null || ComparePoint3(p, bestEnd.Value) < 0) {
                        bestEnd = p;
                    }
                }
            }
            return bestEnd ?? bestAny!.Value;
        }

        List<Point3> OrderMembers(Point3 start, IReadOnlyList<Point3> cluster) {
            var clusterSet = new HashSet<Point3>(cluster);
            var ordered = new List<Point3>(cluster.Count);
            var visited = new HashSet<Point3>();
            Point3 current = start;
            while (true) {
                ordered.Add(current);
                visited.Add(current);
                Point3? next = null;
                foreach (Point3 n in EnumerateLineNeighbors(current)) {
                    if (!clusterSet.Contains(n) || visited.Contains(n)) {
                        continue;
                    }
                    if (next == null || ComparePoint3(n, next.Value) < 0) {
                        next = n;
                    }
                }
                if (next == null) {
                    break;
                }
                current = next.Value;
            }
            if (ordered.Count < cluster.Count) {
                var rest = new List<Point3>();
                foreach (Point3 p in cluster) {
                    if (!visited.Contains(p)) {
                        rest.Add(p);
                    }
                }
                rest.Sort(ComparePoint3);
                ordered.AddRange(rest);
            }
            return ordered;
        }

        Dictionary<Point3, int> BuildDegree(IReadOnlyList<Point3> cluster) {
            var set = new HashSet<Point3>(cluster);
            var degree = new Dictionary<Point3, int>();
            foreach (Point3 p in cluster) {
                int d = 0;
                foreach (Point3 n in EnumerateLineNeighbors(p)) {
                    if (set.Contains(n)) {
                        d++;
                    }
                }
                degree[p] = d;
            }
            return degree;
        }

        static int ComparePoint3(Point3 a, Point3 b) {
            int c = a.X.CompareTo(b.X);
            if (c != 0) {
                return c;
            }
            c = a.Y.CompareTo(b.Y);
            if (c != 0) {
                return c;
            }
            return a.Z.CompareTo(b.Z);
        }
    }
}
