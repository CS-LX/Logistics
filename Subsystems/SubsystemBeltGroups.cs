using Engine;
using Engine.Graphics;
using Engine.Input;
using Engine.Media;
using Game;
using GameEntitySystem;
using SCIENEW.Utils;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>
    /// 输送带 Group 发现 / 存档 / 连续运物仿真。
    /// 调试绘制：F2 开关（对齐 SCIENEW F3–F5 调试框）。
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
        readonly List<TransportedItem> m_ejectBuffer = [];
        readonly List<LooseBeltItem> m_rebuildLooseItems = [];
        readonly PrimitivesRenderer3D m_primitivesRenderer3D = new();
        readonly PrimitivesRenderer3D m_itemPrimitivesRenderer3D = new();
        readonly DrawBlockEnvironmentData m_drawBlockEnvironmentData = new();

        FlatBatch3D m_flatBatch;
        FontBatch3D m_textBatch;
        SubsystemTerrain m_subsystemTerrain;
        SubsystemPickables m_subsystemPickables;
        SubsystemGameInfo m_subsystemGameInfo;
        int m_beltIndex;
        bool m_debugCanDraw;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public int[] DrawOrders => [10, 1000];

        public bool TryGet(Guid id, out BeltGroup group) => m_groups.TryGetValue(id, out group);

        public bool TryGetAt(Point3 point, out BeltGroup group) {
            if (m_cellToGroup.TryGetValue(point, out Guid id) && m_groups.TryGetValue(id, out group)) {
                return true;
            }
            group = null;
            return false;
        }

        public void RequestRebuild(Point3 point) => m_dirtyRebuild.Add(point);

        /// <summary>将掉落物吸入对应 Group；成功则标记 ToRemove。</summary>
        public bool TryAbsorbWorldItem(Point3 cell, WorldItem worldItem) {
            if (worldItem == null || worldItem.ToRemove || !TryGetAt(cell, out BeltGroup group)) {
                return false;
            }
            // 与玩家自动拾取相同的等待期，避免刚弹出的掉落物立刻被吸回
            if (worldItem is Pickable ageCheck) {
                double age = m_subsystemGameInfo.TotalElapsedGameTime - ageCheck.CreationTime;
                if (age < ageCheck.TimeWaitToAutoPick) {
                    return false;
                }
            }
            int count = worldItem is Pickable pickable ? pickable.Count : 1;
            if (count <= 0) {
                return false;
            }
            float beltPos = BeltPath.WorldToBeltPosition(group, worldItem.Position, m_subsystemTerrain);
            var item = new TransportedItem {
                Value = worldItem.Value,
                Count = count,
                BeltPosition = beltPos,
                SideOffset = 0f,
                Velocity = worldItem.Velocity
            };
            if (!group.Inventory.TryInsert(item)) {
                return false;
            }
            if (worldItem is Pickable p) {
                p.Count = 0;
            }
            worldItem.ToRemove = true;
            return true;
        }

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
            m_subsystemPickables = Project.FindSubsystem<SubsystemPickables>(throwOnError: true);
            m_subsystemGameInfo = Project.FindSubsystem<SubsystemGameInfo>(throwOnError: true);
            m_beltIndex = BlocksManager.GetBlockIndex<ConveyerBeltBlock>();
            m_drawBlockEnvironmentData.SubsystemTerrain = m_subsystemTerrain;
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
            if (m_dirtyRebuild.Count > 0) {
                Point3[] dirty = m_dirtyRebuild.ToArray();
                m_dirtyRebuild.Clear();
                foreach (Point3 point in dirty) {
                    RebuildAt(point);
                }
            }
            TickInventories(dt);
        }

        public void Draw(Camera camera, int drawOrder) {
            if (drawOrder == 10) {
                DrawItems(camera);
                return;
            }
            if (drawOrder != 1000 || !m_debugCanDraw) {
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
                    $"{shortId}\n{group.Members.Count} cells\nSign={group.Sign}\ninv={group.Inventory.Count}\n{groupCount} groups",
                    textPos,
                    right * s,
                    up * s,
                    Color.White,
                    TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter,
                    Vector2.Zero);
            }
            m_primitivesRenderer3D.Flush(camera.ViewProjectionMatrix);
        }

        void DrawItems(Camera camera) {
            float visibility = SettingsManager.VisibilityRange;
            foreach (BeltGroup group in m_groups.Values) {
                foreach (TransportedItem item in group.Inventory.Items) {
                    if (!BeltPath.TryGetWorldPose(group, item.BeltPosition, item.SideOffset, m_subsystemTerrain, out Vector3 pos, out _)) {
                        continue;
                    }
                    if (Vector3.Distance(pos, camera.ViewPosition) > visibility) {
                        continue;
                    }
                    Point3 cell = Terrain.ToCell(pos);
                    TerrainChunk chunk = m_subsystemTerrain.Terrain.GetChunkAtCell(cell.X, cell.Z);
                    if (chunk is { State: >= TerrainChunkState.InvalidVertices1 } && cell.Y is >= 0 and < 255) {
                        m_drawBlockEnvironmentData.Humidity = m_subsystemTerrain.Terrain.GetHumidity(cell.X, cell.Z);
                        m_drawBlockEnvironmentData.Temperature = m_subsystemTerrain.Terrain.GetTemperature(cell.X, cell.Z);
                        m_drawBlockEnvironmentData.Light = m_subsystemTerrain.Terrain.GetCellLightFast(cell.X, cell.Y, cell.Z);
                    }
                    m_drawBlockEnvironmentData.BillboardDirection = camera.ViewDirection;
                    var matrix = Matrix.CreateTranslation(pos);
                    Block block = BlocksManager.Blocks[Terrain.ExtractContents(item.Value)];
                    block.DrawBlock(
                        m_itemPrimitivesRenderer3D,
                        item.Value,
                        Color.White,
                        BeltPath.ItemDrawSize,
                        ref matrix,
                        m_drawBlockEnvironmentData);
                }
            }
            m_itemPrimitivesRenderer3D.Flush(camera.ViewProjectionMatrix);
        }

        void TickInventories(float dt) {
            foreach (BeltGroup group in m_groups.Values) {
                if (group.Inventory.Count == 0) {
                    continue;
                }
                float length = BeltPath.TotalLength(group, m_subsystemTerrain);
                foreach (TransportedItem item in group.Inventory.Items) {
                    if (!BeltPath.TryGetWorldPose(group, item.BeltPosition, item.SideOffset, m_subsystemTerrain, out _, out Vector3 tangent)) {
                        continue;
                    }
                    Vector3 travel = group.Sign >= 0 ? tangent : -tangent;
                    Vector3 desired = travel * group.SpeedAbs;
                    // P3：跨带后世界速度向本带切向靠拢
                    item.Velocity = Vector3.Lerp(item.Velocity, desired, MathF.Min(1f, dt * 8f));
                }
                m_ejectBuffer.Clear();
                group.Inventory.Tick(group.Sign, group.SpeedAbs, length, dt, m_ejectBuffer);
                foreach (TransportedItem item in m_ejectBuffer) {
                    HandleBeltEnd(group, item);
                }
            }
        }

        /// <summary>末端：优先正交滑入邻组，否则弹出 Pickable。</summary>
        void HandleBeltEnd(BeltGroup group, TransportedItem item) {
            if (TryHandoffToOrthogonal(group, item)) {
                return;
            }
            EjectAsPickable(group, item);
        }

        /// <summary>P3：末端直角邻接另一 Group 时滑入，继承速度并带 SideOffset。</summary>
        bool TryHandoffToOrthogonal(BeltGroup source, TransportedItem item) {
            if (source.Members.Count == 0) {
                return false;
            }
            Point3 exitCell = source.Sign >= 0 ? source.Members[^1] : source.Members[0];
            if (!TryGetCellValue(exitCell, out int exitValue)) {
                return false;
            }
            int exitRotation = ConveyerBeltBlock.GetRotation(Terrain.ExtractData(exitValue));
            bool exitAlongZ = (exitRotation & 1) == 0;

            float sourceLength = BeltPath.TotalLength(source, m_subsystemTerrain);
            float posePos = source.Sign >= 0
                ? MathF.Min(item.BeltPosition, sourceLength)
                : MathF.Max(item.BeltPosition, 0f);
            if (!BeltPath.TryGetWorldPose(source, posePos, item.SideOffset, m_subsystemTerrain, out Vector3 exitPos, out Vector3 exitTangent)) {
                return false;
            }
            Vector3 sourceTravel = source.Sign >= 0 ? exitTangent : -exitTangent;
            Vector3 inheritVelocity = item.Velocity.LengthSquared() > 1e-4f
                ? item.Velocity
                : sourceTravel * source.SpeedAbs;

            BeltGroup bestTarget = null;
            float bestEntryPos = 0f;
            float bestSide = 0f;
            Vector3 bestTravel = default;
            float bestScore = float.MaxValue;

            foreach (Point3 n in EnumerateBeltNeighborCells(exitCell)) {
                if (!TryGetAt(n, out BeltGroup target) || target.Id == source.Id) {
                    continue;
                }
                if (!TryGetCellValue(n, out int nValue)) {
                    continue;
                }
                int nRotation = ConveyerBeltBlock.GetRotation(Terrain.ExtractData(nValue));
                bool nAlongZ = (nRotation & 1) == 0;
                // 直角：轴正交（同轴应已在同组）
                if (exitAlongZ == nAlongZ) {
                    continue;
                }
                if (!BeltPath.TryGetMemberCenterBeltPosition(target, n, m_subsystemTerrain, out float entryCenter, out _)) {
                    continue;
                }
                float targetLength = BeltPath.TotalLength(target, m_subsystemTerrain);
                const float inset = 0.12f;
                float entryPos = target.Sign >= 0
                    ? MathF.Min(entryCenter, MathF.Max(0f, targetLength - inset))
                    : MathF.Max(entryCenter, MathF.Min(targetLength, inset));

                if (!BeltPath.TryGetWorldPose(target, entryPos, 0f, m_subsystemTerrain, out Vector3 entryWorld, out Vector3 entryTangent)) {
                    continue;
                }
                Vector3 targetTravel = target.Sign >= 0 ? entryTangent : -entryTangent;
                // 目标推进应大致离开出口（避免立刻顶回）
                Vector3 leave = entryWorld - exitPos;
                if (leave.LengthSquared() > 1e-6f && Vector3.Dot(Vector3.Normalize(leave), targetTravel) < -0.25f) {
                    continue;
                }

                Vector3 lateral = Vector3.Cross(Vector3.UnitY, targetTravel);
                if (lateral.LengthSquared() < 1e-6f) {
                    lateral = Vector3.UnitX;
                }
                else {
                    lateral = Vector3.Normalize(lateral);
                }
                float side = Math.Clamp(Vector3.Dot(exitPos - entryWorld, lateral), -0.45f, 0.45f);
                float score = Vector3.DistanceSquared(exitPos, entryWorld);
                if (score >= bestScore) {
                    continue;
                }
                bestScore = score;
                bestTarget = target;
                bestEntryPos = entryPos;
                bestSide = side;
                bestTravel = targetTravel;
            }

            if (bestTarget == null) {
                return false;
            }

            item.BeltPosition = bestEntryPos;
            item.SideOffset = bestSide;
            item.Velocity = Vector3.Lerp(inheritVelocity, bestTravel * bestTarget.SpeedAbs, 0.35f);
            if (bestTarget.Inventory.TryInsert(item)) {
                return true;
            }
            // 间距占满：塞回源末端排队；仍失败则交给弹出
            float sourceLengthClamp = BeltPath.TotalLength(source, m_subsystemTerrain);
            item.BeltPosition = source.Sign >= 0
                ? MathF.Max(0f, sourceLengthClamp - 0.05f)
                : MathF.Min(sourceLengthClamp, 0.05f);
            item.SideOffset = 0f;
            item.Velocity = inheritVelocity;
            return source.Inventory.TryInsert(item);
        }

        /// <summary>对齐抓取机吐出思路，但缩小偏置/初速，减少带上图标→掉落物的断层感。</summary>
        void EjectAsPickable(BeltGroup group, TransportedItem item) {
            float length = BeltPath.TotalLength(group, m_subsystemTerrain);
            float posePos = group.Sign >= 0
                ? MathF.Min(item.BeltPosition, length)
                : MathF.Max(item.BeltPosition, 0f);
            if (!BeltPath.TryGetWorldPose(group, posePos, item.SideOffset, m_subsystemTerrain, out Vector3 pos, out Vector3 tangent)) {
                pos = new Vector3(group.Members[^1]) + new Vector3(0.5f, BeltPath.ItemCenterHeight, 0.5f);
                tangent = Vector3.UnitZ;
            }
            Vector3 travel = group.Sign >= 0 ? tangent : -tangent;
            Vector3 spawn = pos + travel * 0.2f;
            Vector3 velocity = item.Velocity.LengthSquared() > 1e-4f
                ? item.Velocity + Vector3.UnitY * 0.04f
                : travel * MathF.Max(group.SpeedAbs, 0.8f) + Vector3.UnitY * 0.04f;
            m_subsystemPickables.AddPickable(item.Value, item.Count, spawn, velocity, null);
        }

        static Color ColorForGuid(Guid id) {
            int h = id.GetHashCode();
            byte r = (byte)(96 + ((h >> 0) & 0x7F));
            byte g = (byte)(96 + ((h >> 8) & 0x7F));
            byte b = (byte)(96 + ((h >> 16) & 0x7F));
            return new Color(r, g, b);
        }

        struct LooseBeltItem {
            public int Value;
            public int Count;
            public Vector3 Position;
            public Vector3 Velocity;
            public float SideOffset;
        }

        /// <summary>以 seed 为起点，重发现其触及的连通分量并重建 Group。</summary>
        public void RebuildAt(Point3 seed) {
            // 种子区块未就绪：延后，避免拆组后只扫到半截
            if (!TryGetCellValue(seed, out _)) {
                m_dirtyRebuild.Add(seed);
                return;
            }

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
            m_rebuildLooseItems.Clear();
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

            // 触及组仍有成员在未加载区块：整次 Rebuild 延后，禁止拆成残缺组
            foreach (List<Point3> members in membersByGuid.Values) {
                foreach (Point3 m in members) {
                    if (TryGetCellValue(m, out _)) {
                        continue;
                    }
                    m_dirtyRebuild.Add(seed);
                    foreach (Point3 p in rediscoverSeeds) {
                        m_dirtyRebuild.Add(p);
                    }
                    m_rebuildLooseItems.Clear();
                    return;
                }
            }

            foreach (Guid gid in touchedGuids) {
                if (!m_groups.TryGetValue(gid, out BeltGroup g)) {
                    continue;
                }
                foreach (TransportedItem item in g.Inventory.Items) {
                    if (BeltPath.TryGetWorldPose(g, item.BeltPosition, item.SideOffset, m_subsystemTerrain, out Vector3 world, out Vector3 tangent)) {
                        Vector3 travel = g.Sign >= 0 ? tangent : -tangent;
                        m_rebuildLooseItems.Add(new LooseBeltItem {
                            Value = item.Value,
                            Count = item.Count,
                            Position = world,
                            Velocity = travel * g.SpeedAbs,
                            SideOffset = item.SideOffset
                        });
                    }
                    else {
                        m_rebuildLooseItems.Add(new LooseBeltItem {
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
                    membersByGuid.Remove(keepId);
                }
                CreateGroup(keepId, cluster, sign, speed);
            }

            // 按世界坐标迁回在途物；无法落入任何组则弹出
            for (int i = 0; i < m_rebuildLooseItems.Count; i++) {
                LooseBeltItem loose = m_rebuildLooseItems[i];
                if (TryInsertLooseItem(loose)) {
                    continue;
                }
                m_subsystemPickables.AddPickable(loose.Value, loose.Count, loose.Position, loose.Velocity, null);
            }
            m_rebuildLooseItems.Clear();
        }

        bool TryInsertLooseItem(LooseBeltItem loose) {
            Point3 cell = Terrain.ToCell(loose.Position);
            if (TryInsertAtCell(cell, loose)) {
                return true;
            }
            for (int i = 0; i < 4; i++) {
                for (int k = 0; k < 3; k++) {
                    Point3 o = NeighborOffsets[i, k];
                    if (TryInsertAtCell(new Point3(cell.X + o.X, cell.Y + o.Y, cell.Z + o.Z), loose)) {
                        return true;
                    }
                }
            }
            return false;
        }

        bool TryInsertAtCell(Point3 cell, LooseBeltItem loose) {
            if (!TryGetAt(cell, out BeltGroup group)) {
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
                EnsureSegmentEntity(p);
            }
        }

        void EnsureSegmentEntity(Point3 point) {
            if (!IsBeltCell(point) || BlockEntityUtils.GetBlockEntity(m_subsystemTerrain, point, out _)) {
                return;
            }
            BlockEntityUtils.CreateBlockEntity(
                m_subsystemTerrain,
                SubsystemConveyerBeltBlockBehavior.SegmentEntityName,
                point);
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
                    // 未加载：保留成员，禁止当成「非带」清掉
                    if (!TryGetCellValue(p, out int value)) {
                        continue;
                    }
                    if (Terrain.ExtractContents(value) == m_beltIndex) {
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

        /// <summary>区块未加载或 Y 越界时返回 false（勿调用 FastChunkExists）。</summary>
        bool TryGetCellValue(Point3 p, out int value) {
            value = 0;
            if (p.Y is < 0 or >= TerrainChunk.Height) {
                return false;
            }
            TerrainChunk chunk = m_subsystemTerrain.Terrain.GetChunkAtCell(p.X, p.Z);
            if (chunk == null) {
                return false;
            }
            value = chunk.GetCellValueFast(p.X & 0xF, p.Y, p.Z & 0xF);
            return true;
        }

        bool IsBeltCell(Point3 p)
            => TryGetCellValue(p, out int value) && Terrain.ExtractContents(value) == m_beltIndex;

        /// <summary>
        /// 同组邻接：沿自身朝向轴（含坡）且双向成立。
        /// 直角贴靠两侧轴不同 → 不同 Group（P3 端点交接）。
        /// </summary>
        IEnumerable<Point3> EnumerateLineNeighbors(Point3 p) {
            if (!TryGetCellValue(p, out int value)) {
                yield break;
            }
            int rotation = ConveyerBeltBlock.GetRotation(Terrain.ExtractData(value));
            for (int i = 0; i < 4; i++) {
                for (int k = 0; k < 3; k++) {
                    Point3 o = NeighborOffsets[i, k];
                    Point3 n = new(p.X + o.X, p.Y + o.Y, p.Z + o.Z);
                    if (!IsBeltCell(n) || !IsAlongAxisStep(p, n, rotation)) {
                        continue;
                    }
                    if (!TryGetCellValue(n, out int nValue)) {
                        continue;
                    }
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
