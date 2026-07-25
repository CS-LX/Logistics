using Engine;

namespace Logistics {
    /// <summary>输送带格间纯几何：邻接偏移、朝向轴判定、两侧面、稳定排序。不读地形。</summary>
    public static class BeltGeometry {
        /// <summary>与铺设朝向一致：行=水平四向(0=-Z,1=-X,2=+Z,3=+X)，列=同层/上层/下层。</summary>
        public static readonly Point3[,] NeighborOffsets = {
            { new(0, 0, -1), new(0, 1, -1), new(0, -1, -1) },
            { new(-1, 0, 0), new(-1, 1, 0), new(-1, -1, 0) },
            { new(0, 0, 1), new(0, 1, 1), new(0, -1, 1) },
            { new(1, 0, 0), new(1, 1, 0), new(1, -1, 0) }
        };

        /// <summary>几何上可能相邻的 12 格（含直角旁另一组）。</summary>
        public static IEnumerable<Point3> EnumerateNeighborCells(Point3 p) {
            for (int i = 0; i < 4; i++) {
                for (int k = 0; k < 3; k++) {
                    Point3 o = NeighborOffsets[i, k];
                    yield return new Point3(p.X + o.X, p.Y + o.Y, p.Z + o.Z);
                }
            }
        }

        /// <summary>from→to 是否落在 from 朝向轴上一步（含坡 Y±1）。rotation 0/2 沿 Z，1/3 沿 X。</summary>
        public static bool IsAlongAxisStep(Point3 from, Point3 to, int fromRotation) {
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

        /// <summary>沿带走向为 Z 时取 ±X 面，为 X 时取 ±Z 面。Face：0=+Z, 1=+X, 2=-Z, 3=-X。</summary>
        public static (int FaceA, int FaceB) SideFaces(int rotation) => (rotation & 1) == 0 ? (1, 3) : (0, 2);

        /// <summary>成员排序与控制端选举用的稳定顺序。</summary>
        public static int Compare(Point3 a, Point3 b) {
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
