using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>
    /// 世界级储存库表：Guid → <see cref="StorageVault"/>。
    /// 帧末 Compact；簇并入 / 缩容爆出 / 挖断 Q1 切分；防抖 Rebuild 与孤儿 GC。
    /// </summary>
    public class SubsystemStorageVaults : Subsystem, IUpdateable {
        public const string SaveKeyVaults = "Vaults";

        static readonly Point3[] NeighborOffsets = [
            new(0, -1, 0), new(0, 1, 0),
            new(-1, 0, 0), new(1, 0, 0),
            new(0, 0, -1), new(0, 0, 1)
        ];

        readonly Dictionary<Guid, StorageVault> m_vaults = new();
        readonly Dictionary<Point3, Guid> m_cellToGuid = new();
        readonly HashSet<Point3> m_dirtyRebuild = new();

        SubsystemTerrain m_subsystemTerrain;
        SubsystemBlockEntities m_subsystemBlockEntities;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public bool TryGet(Guid id, out StorageVault vault) => m_vaults.TryGetValue(id, out vault);

        public StorageVault Get(Guid id) {
            if (!m_vaults.TryGetValue(id, out StorageVault vault)) {
                throw new InvalidOperationException($"Storage vault {id} not found.");
            }
            return vault;
        }

        public StorageVault Create(Guid id, int memberCount) {
            if (m_vaults.ContainsKey(id)) {
                throw new InvalidOperationException($"Storage vault {id} already exists.");
            }
            var vault = new StorageVault(id, memberCount);
            m_vaults[id] = vault;
            return vault;
        }

        public bool Remove(Guid id) => m_vaults.Remove(id);

        public void RememberCell(Point3 point, Guid vaultGuid) {
            if (vaultGuid == Guid.Empty) {
                m_cellToGuid.Remove(point);
            }
            else {
                m_cellToGuid[point] = vaultGuid;
            }
        }

        public void ForgetCell(Point3 point) => m_cellToGuid.Remove(point);

        /// <summary>同帧多次邻接变化合并为一次 Rebuild。</summary>
        public void RequestRebuild(Point3 point) => m_dirtyRebuild.Add(point);

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(true);
            m_subsystemBlockEntities = Project.FindSubsystem<SubsystemBlockEntities>(true);
            m_vaults.Clear();
            m_cellToGuid.Clear();
            m_dirtyRebuild.Clear();
            ValuesDictionary vaultsVd = valuesDictionary.GetValue<ValuesDictionary>(SaveKeyVaults, null);
            if (vaultsVd == null) return;
            foreach (KeyValuePair<string, object> kv in vaultsVd) {
                if (kv.Value is not ValuesDictionary vaultVd) continue;
                StorageVault vault = StorageVault.Read(vaultVd);
                m_vaults[vault.Id] = vault;
            }
        }

        public override void Save(ValuesDictionary valuesDictionary) {
            base.Save(valuesDictionary);
            PurgeZeroMemberVaults();
            ValuesDictionary vaultsVd = new();
            foreach (KeyValuePair<Guid, StorageVault> kv in m_vaults) {
                ValuesDictionary vaultVd = new();
                kv.Value.Write(vaultVd);
                vaultsVd.SetValue(kv.Key.ToString("D"), vaultVd);
            }
            valuesDictionary.SetValue(SaveKeyVaults, vaultsVd);
        }

        public void Update(float dt) {
            foreach (StorageVault vault in m_vaults.Values) {
                vault.CompactIfNeeded();
            }
            if (m_dirtyRebuild.Count > 0) {
                Point3[] dirty = m_dirtyRebuild.ToArray();
                m_dirtyRebuild.Clear();
                foreach (Point3 point in dirty) {
                    RebuildAt(point);
                }
            }
        }

        /// <summary>放置新单元后：并入邻簇或新建 Guid（软上限 64）。</summary>
        public void IntegrateNewUnit(ComponentStorageUnit newUnit, Point3 point) {
            var neighborSeeds = new Dictionary<Guid, Point3>();
            foreach (Point3 offset in NeighborOffsets) {
                Point3 np = point + offset;
                ComponentStorageUnit neighbor = StorageClusterUtility.GetUnit(m_subsystemBlockEntities, np);
                if (neighbor == null || neighbor == newUnit || neighbor.VaultGuid == Guid.Empty) continue;
                neighborSeeds.TryAdd(neighbor.VaultGuid, np);
                RememberCell(np, neighbor.VaultGuid);
            }

            if (neighborSeeds.Count == 0) {
                Guid id = Guid.NewGuid();
                Create(id, 1);
                newUnit.VaultGuid = id;
                RememberCell(point, id);
                return;
            }

            int totalMembers = 1;
            foreach (Guid g in neighborSeeds.Keys) {
                if (TryGet(g, out StorageVault v)) {
                    totalMembers += v.MemberCount;
                }
            }

            if (totalMembers > StorageVault.MaxUnitsPerCluster) {
                Guid id = Guid.NewGuid();
                Create(id, 1);
                newUnit.VaultGuid = id;
                RememberCell(point, id);
                return;
            }

            if (neighborSeeds.Count == 1) {
                Guid g = neighborSeeds.Keys.First();
                StorageVault vault = Get(g);
                vault.ExpandToMemberCount(vault.MemberCount + 1);
                newUnit.VaultGuid = g;
                RememberCell(point, g);
                return;
            }

            Guid keepId = SelectKeepGuid(neighborSeeds.Keys);
            StorageVault keep = Get(keepId);
            foreach ((Guid g, Point3 seed) in neighborSeeds) {
                if (g == keepId || !TryGet(g, out StorageVault other)) continue;
                keep.ExpandToMemberCount(keep.MemberCount + other.MemberCount);
                keep.AppendContentsFrom(other);
                ReassignGuidInGeometricCluster(seed, g, keepId);
                Remove(g);
            }

            keep.ExpandToMemberCount(keep.MemberCount + 1);
            newUnit.VaultGuid = keepId;
            RememberCell(point, keepId);
        }

        /// <summary>拆除单元：缩容爆出；挖断则按 Q1 切分。</summary>
        public void DisintegrateRemovedUnit(Guid vaultGuid, Point3 removedPoint, Vector3 ejectPosition) {
            ForgetCell(removedPoint);
            if (vaultGuid == Guid.Empty || !TryGet(vaultGuid, out StorageVault vault)) {
                return;
            }

            List<List<Point3>> components = StorageClusterUtility.CollectRemainingComponents(
                m_subsystemTerrain,
                m_subsystemBlockEntities,
                removedPoint,
                vaultGuid
            );

            if (components.Count == 0) {
                vault.DropAllItems(Project, ejectPosition);
                Remove(vaultGuid);
                return;
            }

            if (components.Count == 1) {
                List<Point3> only = components[0];
                vault.ShrinkToMemberCount(Project, only.Count, ejectPosition);
                foreach (Point3 p in only) {
                    ComponentStorageUnit u = StorageClusterUtility.GetUnit(m_subsystemBlockEntities, p);
                    if (u != null) {
                        u.VaultGuid = vaultGuid;
                        RememberCell(p, vaultGuid);
                    }
                }
                return;
            }

            vault.CompactIfNeeded();
            vault.Compact();

            int[] caps = new int[components.Count];
            int[] starts = new int[components.Count];
            int offset = 0;
            for (int i = 0; i < components.Count; i++) {
                caps[i] = components[i].Count * StorageVault.SlotsPerUnit;
                starts[i] = offset;
                offset += caps[i];
            }

            for (int i = components.Count - 1; i >= 1; i--) {
                Guid newId = Guid.NewGuid();
                StorageVault part = Create(newId, components[i].Count);
                vault.MoveWindowTo(part, starts[i], caps[i]);
                foreach (Point3 p in components[i]) {
                    ComponentStorageUnit u = StorageClusterUtility.GetUnit(m_subsystemBlockEntities, p);
                    if (u != null) {
                        u.VaultGuid = newId;
                        RememberCell(p, newId);
                    }
                }
            }

            vault.Compact();
            vault.ShrinkToMemberCount(Project, components[0].Count, ejectPosition);
            foreach (Point3 p in components[0]) {
                ComponentStorageUnit u = StorageClusterUtility.GetUnit(m_subsystemBlockEntities, p);
                if (u != null) {
                    u.VaultGuid = vaultGuid;
                    RememberCell(p, vaultGuid);
                }
            }
        }

        /// <summary>
        /// 已加载几何簇一致性：跨区块邻接若多 Guid 且未超软上限则合并；
        /// 已加载成员数大于 <see cref="StorageVault.MemberCount"/> 时只扩容不缩（未加载格仍算成员）。
        /// </summary>
        public void RebuildAt(Point3 seed) {
            if (!StorageClusterUtility.IsStorageUnitCell(m_subsystemTerrain, seed)) {
                ForgetCell(seed);
                return;
            }

            List<Point3> cluster = StorageClusterUtility.CollectCluster(m_subsystemTerrain, seed);
            if (cluster.Count == 0) return;

            var guidToSeed = new Dictionary<Guid, Point3>();
            var loadedCountByGuid = new Dictionary<Guid, int>();
            foreach (Point3 p in cluster) {
                ComponentStorageUnit u = StorageClusterUtility.GetUnit(m_subsystemBlockEntities, p);
                if (u == null || u.VaultGuid == Guid.Empty) continue;
                if (!TryGet(u.VaultGuid, out _)) continue;
                guidToSeed.TryAdd(u.VaultGuid, p);
                loadedCountByGuid[u.VaultGuid] = loadedCountByGuid.GetValueOrDefault(u.VaultGuid) + 1;
                RememberCell(p, u.VaultGuid);
            }

            if (guidToSeed.Count == 0) {
                ComponentStorageUnit unit = StorageClusterUtility.GetUnit(m_subsystemBlockEntities, seed);
                if (unit != null) {
                    IntegrateNewUnit(unit, seed);
                }
                return;
            }

            if (guidToSeed.Count > 1) {
                int totalMembers = 0;
                foreach (Guid g in guidToSeed.Keys) {
                    if (TryGet(g, out StorageVault v)) {
                        totalMembers += v.MemberCount;
                    }
                }
                if (totalMembers <= StorageVault.MaxUnitsPerCluster) {
                    Guid keepId = SelectKeepGuid(guidToSeed.Keys);
                    if (keepId == Guid.Empty || !TryGet(keepId, out StorageVault keep)) {
                        return;
                    }
                    foreach ((Guid g, Point3 gSeed) in guidToSeed) {
                        if (g == keepId || !TryGet(g, out StorageVault other)) continue;
                        keep.ExpandToMemberCount(keep.MemberCount + other.MemberCount);
                        keep.AppendContentsFrom(other);
                        ReassignGuidInGeometricCluster(gSeed, g, keepId);
                        Remove(g);
                    }
                    foreach (Point3 p in cluster) {
                        RememberCell(p, keepId);
                    }
                    int loadedKeep = 0;
                    foreach (Point3 p in cluster) {
                        ComponentStorageUnit u = StorageClusterUtility.GetUnit(m_subsystemBlockEntities, p);
                        if (u != null && u.VaultGuid == keepId) {
                            loadedKeep++;
                        }
                    }
                    loadedCountByGuid.Clear();
                    loadedCountByGuid[keepId] = loadedKeep;
                }
            }

            foreach ((Guid g, int loaded) in loadedCountByGuid) {
                if (!TryGet(g, out StorageVault vault)) continue;
                if (loaded > vault.MemberCount) {
                    vault.ExpandToMemberCount(loaded);
                }
            }
        }

        Guid SelectKeepGuid(IEnumerable<Guid> candidates) {
            Guid keepId = default;
            StorageVault keep = null;
            foreach (Guid g in candidates) {
                if (!TryGet(g, out StorageVault v)) continue;
                if (keep == null
                    || StorageClusterUtility.CompareGuidPreferLargerVault(g, keepId, v, keep) < 0) {
                    keepId = g;
                    keep = v;
                }
            }
            return keepId;
        }

        void ReassignGuidInGeometricCluster(Point3 seed, Guid from, Guid to) {
            foreach (Point3 p in StorageClusterUtility.CollectCluster(m_subsystemTerrain, seed)) {
                ComponentStorageUnit u = StorageClusterUtility.GetUnit(m_subsystemBlockEntities, p);
                if (u != null && u.VaultGuid == from) {
                    u.VaultGuid = to;
                    RememberCell(p, to);
                }
            }
        }

        /// <summary>
        /// 仅清除 MemberCount==0 的空壳。有成员的库即使当前区块未加载也不删（跨区块簇）。
        /// </summary>
        void PurgeZeroMemberVaults() {
            List<Guid> remove = null;
            foreach (KeyValuePair<Guid, StorageVault> kv in m_vaults) {
                if (kv.Value.MemberCount > 0) continue;
                remove ??= [];
                remove.Add(kv.Key);
            }
            if (remove == null) return;
            foreach (Guid id in remove) {
                m_vaults.Remove(id);
            }
        }
    }
}
