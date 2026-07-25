using Engine;
using Game;

namespace Logistics {
    /// <summary>
    /// 依地形读取输送带连通关系：同组邻接、簇发现、控制端选举、成员排序、由格 reverse 决定 Sign。
    /// 未加载区块一律返回「读不到」，由调用方决定延后，禁止当成「非带」。
    /// </summary>
    public sealed class BeltTopology {
        readonly SubsystemTerrain m_subsystemTerrain;
        readonly int m_beltIndex;

        public BeltTopology(SubsystemTerrain subsystemTerrain, int beltIndex) {
            m_subsystemTerrain = subsystemTerrain;
            m_beltIndex = beltIndex;
        }

        public int BeltIndex => m_beltIndex;

        /// <summary>区块未加载或 Y 越界时返回 false（勿调用 FastChunkExists）。</summary>
        public bool TryGetCellValue(Point3 p, out int value) {
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

        public bool IsBeltCell(Point3 p)
            => TryGetCellValue(p, out int value) && Terrain.ExtractContents(value) == m_beltIndex;

        public bool TryGetRotation(Point3 p, out int rotation) {
            rotation = 0;
            if (!TryGetCellValue(p, out int value) || Terrain.ExtractContents(value) != m_beltIndex) {
                return false;
            }
            rotation = ConveyerBeltBlock.GetRotation(Terrain.ExtractData(value));
            return true;
        }

        /// <summary>
        /// 同组邻接：沿自身朝向轴（含坡）且双向成立。
        /// 直角贴靠两侧轴不同 → 不同 Group（端点交接）。
        /// </summary>
        public IEnumerable<Point3> EnumerateLineNeighbors(Point3 p) {
            if (!TryGetCellValue(p, out int value)) {
                yield break;
            }
            int rotation = ConveyerBeltBlock.GetRotation(Terrain.ExtractData(value));
            foreach (Point3 n in BeltGeometry.EnumerateNeighborCells(p)) {
                if (!IsBeltCell(n) || !BeltGeometry.IsAlongAxisStep(p, n, rotation)) {
                    continue;
                }
                if (!TryGetCellValue(n, out int nValue)) {
                    continue;
                }
                int nRotation = ConveyerBeltBlock.GetRotation(Terrain.ExtractData(nValue));
                if (!BeltGeometry.IsAlongAxisStep(n, p, nRotation)) {
                    continue;
                }
                yield return n;
            }
        }

        public List<Point3> CollectCluster(Point3 seed) {
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

        public Point3 ElectController(IReadOnlyList<Point3> cluster) {
            if (cluster.Count == 0) {
                return Point3.Zero;
            }
            Dictionary<Point3, int> degree = BuildDegree(cluster);
            Point3? bestEnd = null;
            Point3? bestAny = null;
            foreach (Point3 p in cluster) {
                if (bestAny == null || BeltGeometry.Compare(p, bestAny.Value) < 0) {
                    bestAny = p;
                }
                int d = degree.GetValueOrDefault(p, 0);
                if (d <= 1) {
                    if (bestEnd == null || BeltGeometry.Compare(p, bestEnd.Value) < 0) {
                        bestEnd = p;
                    }
                }
            }
            return bestEnd ?? bestAny!.Value;
        }

        public List<Point3> OrderMembers(Point3 start, IReadOnlyList<Point3> cluster) {
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
                    if (next == null || BeltGeometry.Compare(n, next.Value) < 0) {
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
                rest.Sort(BeltGeometry.Compare);
                ordered.AddRange(rest);
            }
            return ordered;
        }

        /// <summary>新组 Sign：格 reverse 多数决；平局或无法读取时 +1。</summary>
        public int ResolveSignFromCells(IReadOnlyList<Point3> cluster) {
            int positive = 0;
            int negative = 0;
            foreach (Point3 p in cluster) {
                if (!TryGetCellValue(p, out int value) || Terrain.ExtractContents(value) != m_beltIndex) {
                    continue;
                }
                if (ConveyerBeltBlock.GetReverse(Terrain.ExtractData(value)) != 0) {
                    negative++;
                }
                else {
                    positive++;
                }
            }
            if (positive == 0 && negative == 0) {
                return 1;
            }
            return positive >= negative ? 1 : -1;
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
    }
}
