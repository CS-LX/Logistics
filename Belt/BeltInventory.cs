using Engine;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>挂在 Group 上的连续物流表（按 BeltPosition 有序）。</summary>
    public sealed class BeltInventory {
        public const float Spacing = 0.25f;

        readonly List<TransportedItem> m_items = [];

        public int Count => m_items.Count;

        public IReadOnlyList<TransportedItem> Items => m_items;

        public void Clear() => m_items.Clear();

        public void Write(ValuesDictionary vd) {
            ValuesDictionary itemsVd = new();
            for (int i = 0; i < m_items.Count; i++) {
                ValuesDictionary itemVd = new();
                m_items[i].Write(itemVd);
                itemsVd.SetValue(i.ToString(), itemVd);
            }
            vd.SetValue("Items", itemsVd);
        }

        public void Read(ValuesDictionary vd) {
            m_items.Clear();
            ValuesDictionary itemsVd = vd.GetValue<ValuesDictionary>("Items", null);
            if (itemsVd == null) {
                return;
            }
            var indexed = new List<(int Index, TransportedItem Item)>();
            foreach (KeyValuePair<string, object> kv in itemsVd) {
                if (kv.Value is not ValuesDictionary itemVd) {
                    continue;
                }
                if (!int.TryParse(kv.Key, out int index)) {
                    index = indexed.Count;
                }
                indexed.Add((index, TransportedItem.Read(itemVd)));
            }
            indexed.Sort((a, b) => a.Index.CompareTo(b.Index));
            foreach ((_, TransportedItem item) in indexed) {
                m_items.Add(item);
            }
            SortInPlace();
        }

        /// <summary>若与已有物品间距足够则插入并保持有序。</summary>
        public bool TryInsert(TransportedItem item) {
            if (item == null || item.Count <= 0 || item.Value == 0) {
                return false;
            }
            foreach (TransportedItem existing in m_items) {
                if (MathF.Abs(existing.BeltPosition - item.BeltPosition) < Spacing * 0.5f) {
                    return false;
                }
            }
            m_items.Add(item);
            SortInPlace();
            return true;
        }

        /// <summary>
        /// 推进并间距限速；越界物品移入 <paramref name="ejected"/>。
        /// Sign&gt;0 往弧长增大方向；Sign&lt;0 往减小方向。
        /// </summary>
        public void Tick(int sign, float speedAbs, float length, float dt, List<TransportedItem> ejected) {
            if (m_items.Count == 0 || speedAbs <= 0f || sign == 0) {
                return;
            }
            float movement = sign * speedAbs * dt;
            SortInPlace();

            if (sign > 0) {
                for (int i = m_items.Count - 1; i >= 0; i--) {
                    TransportedItem item = m_items[i];
                    float maxPos = i + 1 < m_items.Count
                        ? m_items[i + 1].BeltPosition - Spacing
                        : length;
                    item.BeltPosition = MathF.Min(item.BeltPosition + movement, maxPos);
                    item.SideOffset = MathUtils.Lerp(item.SideOffset, 0f, MathF.Min(1f, dt * 4f));
                    if (item.BeltPosition >= length - 1e-3f) {
                        ejected.Add(item);
                        m_items.RemoveAt(i);
                    }
                }
            }
            else {
                for (int i = 0; i < m_items.Count; i++) {
                    TransportedItem item = m_items[i];
                    float minPos = i > 0
                        ? m_items[i - 1].BeltPosition + Spacing
                        : 0f;
                    item.BeltPosition = MathF.Max(item.BeltPosition + movement, minPos);
                    item.SideOffset = MathUtils.Lerp(item.SideOffset, 0f, MathF.Min(1f, dt * 4f));
                }
                for (int i = m_items.Count - 1; i >= 0; i--) {
                    if (m_items[i].BeltPosition > 1e-3f) {
                        continue;
                    }
                    ejected.Add(m_items[i]);
                    m_items.RemoveAt(i);
                }
            }
        }

        void SortInPlace() => m_items.Sort((a, b) => a.BeltPosition.CompareTo(b.BeltPosition));
    }
}
