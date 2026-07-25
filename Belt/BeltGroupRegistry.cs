using Engine;
using Game;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>
    /// Group 表与「格 → Group」索引的唯一真相源，保证两个索引一致；同时负责随世界存档读写。
    /// </summary>
    public sealed class BeltGroupRegistry {
        const string SaveKeyGroups = "Groups";

        readonly Dictionary<Guid, BeltGroup> m_groups = new();
        readonly Dictionary<Point3, Guid> m_cellToGroup = new();

        public int Count => m_groups.Count;

        public Dictionary<Guid, BeltGroup>.ValueCollection Groups => m_groups.Values;

        public bool Contains(Guid id) => m_groups.ContainsKey(id);

        public bool TryGet(Guid id, out BeltGroup group) => m_groups.TryGetValue(id, out group);

        public bool TryGetAt(Point3 point, out BeltGroup group) {
            if (m_cellToGroup.TryGetValue(point, out Guid id) && m_groups.TryGetValue(id, out group)) {
                return true;
            }
            group = null;
            return false;
        }

        public void Add(BeltGroup group) {
            m_groups[group.Id] = group;
            foreach (Point3 p in group.Members) {
                m_cellToGroup[p] = group.Id;
            }
        }

        public void Remove(Guid id) {
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

        public void Clear() {
            m_groups.Clear();
            m_cellToGroup.Clear();
        }

        public void Read(ValuesDictionary valuesDictionary) {
            Clear();
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
                Add(group);
            }
        }

        public void Write(ValuesDictionary valuesDictionary) {
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

        /// <summary>丢掉已经不是输送带的成员；未加载区块的成员一律保留。</summary>
        public void PurgeInvalidMembers(BeltTopology topology) {
            List<Guid> removeEmpty = null;
            foreach (KeyValuePair<Guid, BeltGroup> kv in m_groups) {
                BeltGroup group = kv.Value;
                for (int i = group.Members.Count - 1; i >= 0; i--) {
                    Point3 p = group.Members[i];
                    if (!topology.TryGetCellValue(p, out int value)) {
                        continue;
                    }
                    if (Terrain.ExtractContents(value) == topology.BeltIndex) {
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
                    group.Controller = topology.ElectController(group.Members);
                    List<Point3> ordered = topology.OrderMembers(group.Controller, group.Members);
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
    }
}
