using Engine;
using Game;
using GameEntitySystem;

namespace Logistics {
    /// <summary>储存单元六向连通 flood-fill 与邻格查询。</summary>
    public static class StorageClusterUtility {
        static readonly Point3[] NeighborOffsets = [
            new(0, -1, 0),
            new(0, 1, 0),
            new(-1, 0, 0),
            new(1, 0, 0),
            new(0, 0, -1),
            new(0, 0, 1)
        ];

        public static bool IsStorageUnitCell(SubsystemTerrain terrain, Point3 point) {
            int contents = terrain.Terrain.GetCellContentsFast(point.X, point.Y, point.Z);
            // 宿主惯例：Blocks[contents] is XxxBlock（兼容动态 Index，勿写死字面量）
            return BlocksManager.Blocks[contents] is LogisticsStorageUnitBlock;
        }

        public static ComponentStorageUnit GetUnit(SubsystemBlockEntities blockEntities, Point3 point) {
            ComponentBlockEntity be = blockEntities.GetBlockEntity(point.X, point.Y, point.Z);
            return be?.Entity.FindComponent<ComponentStorageUnit>();
        }

        /// <summary>自 <paramref name="start"/> 六向连通的全部储存单元格（地形 Index）。</summary>
        public static List<Point3> CollectCluster(SubsystemTerrain terrain, Point3 start) {
            var result = new List<Point3>();
            if (!IsStorageUnitCell(terrain, start)) return result;
            var visited = new HashSet<Point3>();
            var queue = new Queue<Point3>();
            queue.Enqueue(start);
            visited.Add(start);
            while (queue.Count > 0) {
                Point3 p = queue.Dequeue();
                result.Add(p);
                foreach (Point3 offset in NeighborOffsets) {
                    Point3 n = p + offset;
                    if (!visited.Add(n)) continue;
                    if (!IsStorageUnitCell(terrain, n)) continue;
                    queue.Enqueue(n);
                }
            }
            return result;
        }

        /// <summary>
        /// 以移除格的六邻为种子，收集仍属 <paramref name="vaultGuid"/> 的连通分量（地形已无该格）。
        /// </summary>
        public static List<List<Point3>> CollectRemainingComponents(
            SubsystemTerrain terrain,
            SubsystemBlockEntities blockEntities,
            Point3 removedPoint,
            Guid vaultGuid
        ) {
            var components = new List<List<Point3>>();
            var visited = new HashSet<Point3>();
            foreach (Point3 offset in NeighborOffsets) {
                Point3 seed = removedPoint + offset;
                if (!visited.Add(seed)) continue;
                if (!IsStorageUnitCell(terrain, seed)) continue;
                ComponentStorageUnit unit = GetUnit(blockEntities, seed);
                if (unit == null || unit.VaultGuid != vaultGuid) continue;

                var component = new List<Point3>();
                var queue = new Queue<Point3>();
                queue.Enqueue(seed);
                while (queue.Count > 0) {
                    Point3 p = queue.Dequeue();
                    component.Add(p);
                    foreach (Point3 no in NeighborOffsets) {
                        Point3 n = p + no;
                        if (!visited.Add(n)) continue;
                        if (!IsStorageUnitCell(terrain, n)) continue;
                        ComponentStorageUnit nu = GetUnit(blockEntities, n);
                        if (nu == null || nu.VaultGuid != vaultGuid) continue;
                        queue.Enqueue(n);
                    }
                }
                if (component.Count > 0) {
                    components.Add(component);
                }
            }
            components.Sort(CompareComponentOrder);
            return components;
        }

        static int CompareComponentOrder(List<Point3> a, List<Point3> b) {
            Point3 ma = LexMin(a);
            Point3 mb = LexMin(b);
            int c = ma.X.CompareTo(mb.X);
            if (c != 0) return c;
            c = ma.Y.CompareTo(mb.Y);
            if (c != 0) return c;
            return ma.Z.CompareTo(mb.Z);
        }

        public static Point3 LexMin(List<Point3> points) {
            Point3 best = points[0];
            for (int i = 1; i < points.Count; i++) {
                Point3 p = points[i];
                if (p.X < best.X || (p.X == best.X && p.Y < best.Y) || (p.X == best.X && p.Y == best.Y && p.Z < best.Z)) {
                    best = p;
                }
            }
            return best;
        }

        public static int CompareGuidPreferLargerVault(Guid a, Guid b, StorageVault va, StorageVault vb) {
            int bySize = vb.MemberCount.CompareTo(va.MemberCount);
            return bySize != 0 ? bySize : a.CompareTo(b);
        }
    }
}
