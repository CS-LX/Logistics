using Engine;
using Game;

namespace Logistics {
    /// <summary>
    /// 沿 Group.Members 顺序的弧长路径（单位 ≈ 方块；坡 ≈ √2）。
    /// <para>
    /// **不变量**：弧长正向恒为 <see cref="BeltGeometry.Compare"/> 递增方向（成员由
    /// <see cref="BeltTopology.OrderMembers"/> 从坐标序最小端排起）。`Sign` 是相对这根轴的符号，
    /// 格上 `reverse`、滚动 UV、铺设朝向都以此为准，单格组也不例外。
    /// </para>
    /// </summary>
    public static class BeltPath {
        public const float SurfaceHeight = 3f / 16f;
        public const float ItemDrawSize = 0.3f; // 对齐 Pickable.Draw 的 drawBlockSize
        /// <summary>绘制中心高度：贴合上表面（表面 + 半边长，避免半个模型埋进带里）。</summary>
        public static float ItemCenterHeight => SurfaceHeight + ItemDrawSize * 0.5f;

        public static float CellLength(int shape) => shape > 0 ? MathF.Sqrt(2f) : 1f;

        public static float TotalLength(BeltGroup group, SubsystemTerrain terrain) {
            float sum = 0f;
            foreach (Point3 p in group.Members) {
                sum += CellLength(GetShape(terrain, p));
            }
            return sum;
        }

        /// <summary>某成员格占据的弧长区间 [start, start + length]，供格内定位插入。</summary>
        public static bool TryGetMemberSpan(
            BeltGroup group,
            Point3 cell,
            SubsystemTerrain terrain,
            out float start,
            out float length) {
            start = 0f;
            length = 0f;
            int memberIndex = group.Members.IndexOf(cell);
            if (memberIndex < 0) {
                return false;
            }
            for (int i = 0; i < memberIndex; i++) {
                start += CellLength(GetShape(terrain, group.Members[i]));
            }
            length = CellLength(GetShape(terrain, group.Members[memberIndex]));
            return true;
        }

        /// <summary>某成员格弧长中心，供 Segment 窗口锚定。</summary>
        public static bool TryGetMemberCenterBeltPosition(
            BeltGroup group,
            Point3 cell,
            SubsystemTerrain terrain,
            out float center) {
            if (!TryGetMemberSpan(group, cell, terrain, out float start, out float length)) {
                center = 0f;
                return false;
            }
            center = start + length * 0.5f;
            return true;
        }

        public static float WorldToBeltPosition(BeltGroup group, Vector3 world, SubsystemTerrain terrain) {
            float bestPos = 0f;
            float bestDist = float.MaxValue;
            float offset = 0f;
            for (int i = 0; i < group.Members.Count; i++) {
                GetCellEnds(group, i, terrain, out Vector3 start, out Vector3 end);
                float len = CellLength(GetShape(terrain, group.Members[i]));
                Vector3 delta = end - start;
                float denom = MathF.Max(1e-6f, delta.LengthSquared());
                float t = Math.Clamp(Vector3.Dot(world - start, delta) / denom, 0f, 1f);
                Vector3 closest = start + delta * t;
                float d = Vector3.DistanceSquared(world, closest);
                if (d < bestDist) {
                    bestDist = d;
                    bestPos = offset + t * len;
                }
                offset += len;
            }
            return bestPos;
        }

        /// <summary>
        /// 在途物的绘制姿态：平直段维持轴对齐，坡道段绕带横轴倾斜，让物品贴着带面而不是竖着浮在斜坡上。
        /// </summary>
        public static Matrix CreateItemMatrix(Vector3 position, Vector3 tangent) {
            Vector3 lateral = Vector3.Cross(Vector3.UnitY, tangent);
            if (MathF.Abs(tangent.Y) < 1e-4f || lateral.LengthSquared() < 1e-6f) {
                return Matrix.CreateTranslation(position);
            }
            // 绕横轴把 up 从竖直转到带面法线；tangent 已归一，抬升量即 sin
            float pitch = -MathF.Asin(Math.Clamp(tangent.Y, -1f, 1f));
            return Matrix.CreateFromAxisAngle(Vector3.Normalize(lateral), pitch) * Matrix.CreateTranslation(position);
        }

        public static bool TryGetWorldPose(
            BeltGroup group,
            float beltPosition,
            float sideOffset,
            SubsystemTerrain terrain,
            out Vector3 position,
            out Vector3 tangent) {
            position = default;
            tangent = Vector3.UnitZ;
            if (group.Members.Count == 0) {
                return false;
            }
            float length = TotalLength(group, terrain);
            float remaining = Math.Clamp(beltPosition, 0f, MathF.Max(0f, length));
            for (int i = 0; i < group.Members.Count; i++) {
                float len = CellLength(GetShape(terrain, group.Members[i]));
                bool last = i == group.Members.Count - 1;
                if (remaining > len && !last) {
                    remaining -= len;
                    continue;
                }
                float t = len > 1e-6f ? Math.Clamp(remaining / len, 0f, 1f) : 0f;
                GetCellEnds(group, i, terrain, out Vector3 start, out Vector3 end);
                Vector3 delta = end - start;
                float dlen = delta.Length();
                tangent = dlen > 1e-6f ? delta / dlen : Vector3.UnitZ;
                Vector3 lateral = Vector3.Cross(Vector3.UnitY, tangent);
                lateral = lateral.LengthSquared() < 1e-6f ? Vector3.UnitX : Vector3.Normalize(lateral);
                position = Vector3.Lerp(start, end, t) + lateral * sideOffset;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 每格用朝向/坡道轨迹（对齐 SA MovementTrack），再按 Members 邻接调转起终点，
        /// 避免坡道低端被中心镜像算到地底以下。
        /// 没有邻居可参照时（单格组）按类注释的不变量定向。
        /// </summary>
        static void GetCellEnds(BeltGroup group, int index, SubsystemTerrain terrain, out Vector3 start, out Vector3 end) {
            Point3 cell = group.Members[index];
            int data = Terrain.ExtractData(terrain.Terrain.GetCellValueFast(cell.X, cell.Y, cell.Z));
            int shape = ConveyerBeltBlock.GetShape(data);
            GetRotationTrack(
                cell,
                ConveyerBeltBlock.GetRotation(data),
                shape,
                out start,
                out end);

            Point3? prev = index > 0 ? group.Members[index - 1] : null;
            Point3? next = index + 1 < group.Members.Count ? group.Members[index + 1] : null;
            if (next != null) {
                Vector3 target = SurfaceCenter(next.Value);
                if (Vector3.DistanceSquared(start, target) < Vector3.DistanceSquared(end, target)) {
                    (start, end) = (end, start);
                }
            }
            else if (prev != null) {
                Vector3 target = SurfaceCenter(prev.Value);
                if (Vector3.DistanceSquared(end, target) < Vector3.DistanceSquared(start, target)) {
                    (start, end) = (end, start);
                }
            }
            // 单格组：GetRotationTrack 的平直轨朝坐标减小，与多格被邻居调转后的正向相反，
            // 会让 Sign / 滚动外观 / 实际运物在「只有一格」时对不上。坡道必有同轴邻居，
            // 走不到这里，故只按水平坐标定向。
            else if (shape == 0 && end.X + end.Z < start.X + start.Z) {
                (start, end) = (end, start);
            }
        }

        static Vector3 SurfaceCenter(Point3 cell) => new(cell.X + 0.5f, cell.Y + ItemCenterHeight, cell.Z + 0.5f);

        /// <summary>与 SA MovementTrack 正向一致（无 reverse）：坡道高端 Y+1。</summary>
        static void GetRotationTrack(Point3 cell, int rotation, int shape, out Vector3 start, out Vector3 end) {
            float y = ItemCenterHeight;
            Vector3 startOffset;
            Vector3 endOffset;
            switch (rotation) {
                case 1:
                    startOffset = new Vector3(1f, y, 0.5f);
                    endOffset = new Vector3(0f, y, 0.5f);
                    break;
                case 2:
                    startOffset = new Vector3(0.5f, y, 0f);
                    endOffset = new Vector3(0.5f, y, 1f);
                    break;
                case 3:
                    startOffset = new Vector3(0f, y, 0.5f);
                    endOffset = new Vector3(1f, y, 0.5f);
                    break;
                default:
                    startOffset = new Vector3(0.5f, y, 1f);
                    endOffset = new Vector3(0.5f, y, 0f);
                    break;
            }
            if (shape > 0) {
                endOffset.Y += 1f;
            }
            Vector3 origin = new Vector3(cell);
            start = origin + startOffset;
            end = origin + endOffset;
        }

        static int GetShape(SubsystemTerrain terrain, Point3 p) {
            int value = terrain.Terrain.GetCellValueFast(p.X, p.Y, p.Z);
            return ConveyerBeltBlock.GetShape(Terrain.ExtractData(value));
        }
    }
}
