using System.Globalization;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>
    /// 储存库：稠密槽表，不实现 <see cref="IInventory"/>。由 <see cref="SubsystemStorageVaults"/> 按 Guid 持有。
    /// Compact 默认延后到帧末，避免宿主拖拽 Remove+Add 半途压实。
    /// </summary>
    public sealed class StorageVault {
        public const int SlotsPerUnit = 24;
        public const int MaxUnitsPerCluster = 64;

        public Guid Id { get; }
        public int MemberCount { get; private set; }
        public int Version { get; private set; }
        public bool NeedsCompact { get; private set; }

        /// <summary>玩家上次打开 UI 时的页码（0-based）；簇缩容后由 UI 夹到合法页。</summary>
        public int LastUiPageIndex { get; set; }

        readonly List<ComponentInventoryBase.Slot> m_slots = [];
        readonly Engine.Random m_random = new();

        public int Capacity => MemberCount * SlotsPerUnit;
        public int SlotsCount => m_slots.Count;

        public StorageVault(Guid id, int memberCount) {
            Id = id;
            SetMemberCount(memberCount);
        }

        public void SetMemberCount(int memberCount) {
            MemberCount = Math.Max(0, memberCount);
            EnsureSlotCount(Capacity);
            Version++;
        }

        /// <summary>扩容到新成员数（仅增大）。</summary>
        public void ExpandToMemberCount(int memberCount) {
            if (memberCount < MemberCount) {
                throw new InvalidOperationException("Use ShrinkToMemberCount to reduce capacity.");
            }
            SetMemberCount(memberCount);
        }

        /// <summary>
        /// 缩容：先 Compact，再将超出新容量的尾部槽在 <paramref name="ejectPosition"/> 爆出。
        /// </summary>
        public void ShrinkToMemberCount(Project project, int memberCount, Vector3 ejectPosition) {
            CompactIfNeeded();
            Compact();
            int newCapacity = Math.Max(0, memberCount) * SlotsPerUnit;
            SubsystemPickables pickables = project.FindSubsystem<SubsystemPickables>(true);
            for (int i = m_slots.Count - 1; i >= newCapacity; i--) {
                int count = GetSlotCount(i);
                if (count > 0) {
                    int value = GetSlotValue(i);
                    m_slots[i].Count = 0;
                    m_slots[i].Value = 0;
                    Vector3 velocity = m_random.Float(5f, 10f)
                        * Vector3.Normalize(new Vector3(m_random.Float(-1f, 1f), m_random.Float(1f, 2f), m_random.Float(-1f, 1f)));
                    pickables.AddPickable(value, count, ejectPosition, velocity, null);
                }
            }
            SetMemberCount(memberCount);
            Compact();
        }

        /// <summary>把 <paramref name="other"/> 的占用槽追加到末尾（调用前双方应已按成员数设好 Capacity）。</summary>
        public void AppendContentsFrom(StorageVault other) {
            other.CompactIfNeeded();
            other.Compact();
            CompactIfNeeded();
            Compact();
            int write = CountOccupiedSlots();
            int occupied = other.CountOccupiedSlots();
            for (int i = 0; i < occupied; i++) {
                if (write >= Capacity) break;
                m_slots[write].Value = other.GetSlotValue(i);
                m_slots[write].Count = other.GetSlotCount(i);
                write++;
            }
            Version++;
            Compact();
        }

        /// <summary>
        /// 将本库槽窗口 <paramref name="srcStart"/> 起 <paramref name="length"/> 格中的非空项迁到 <paramref name="destination"/>。
        /// 不压缩本库（挖断多窗切分时从高到低调用，需保持源下标稳定）；调用方最后自行 Compact。
        /// </summary>
        public void MoveWindowTo(StorageVault destination, int srcStart, int length) {
            destination.CompactIfNeeded();
            destination.Compact();
            int destWrite = 0;
            for (int j = 0; j < length; j++) {
                int src = srcStart + j;
                if (src < 0 || src >= m_slots.Count) break;
                int count = m_slots[src].Count;
                if (count <= 0) continue;
                if (destWrite >= destination.Capacity) break;
                destination.m_slots[destWrite].Value = m_slots[src].Value;
                destination.m_slots[destWrite].Count = count;
                m_slots[src].Value = 0;
                m_slots[src].Count = 0;
                destWrite++;
            }
            destination.Compact();
            Version++;
        }

        void EnsureSlotCount(int count) {
            while (m_slots.Count < count) {
                m_slots.Add(new ComponentInventoryBase.Slot());
            }
            while (m_slots.Count > count) {
                m_slots.RemoveAt(m_slots.Count - 1);
            }
        }

        public int GetSlotValue(int slotIndex) {
            if (slotIndex < 0 || slotIndex >= m_slots.Count) return 0;
            ComponentInventoryBase.Slot slot = m_slots[slotIndex];
            return slot.Count <= 0 ? 0 : slot.Value;
        }

        public int GetSlotCount(int slotIndex) {
            if (slotIndex < 0 || slotIndex >= m_slots.Count) return 0;
            return m_slots[slotIndex].Count;
        }

        public int GetSlotCapacity(int slotIndex, int value) {
            if (slotIndex < 0 || slotIndex >= m_slots.Count) return 0;
            return BlocksManager.Blocks[Terrain.ExtractContents(value)].GetMaxStacking(value);
        }

        public void AddSlotItems(int slotIndex, int value, int count) {
            if (count <= 0 || slotIndex < 0 || slotIndex >= m_slots.Count) return;
            ComponentInventoryBase.Slot slot = m_slots[slotIndex];
            int slotValue = GetSlotValue(slotIndex);
            int slotCount = GetSlotCount(slotIndex);
            int slotCapacity = GetSlotCapacity(slotIndex, value);
            if (slotCount != 0 && slotValue != value) {
                throw new InvalidOperationException($"Cannot add slot items because items are different. Slot {slotIndex}");
            }
            if (slotCount + count > slotCapacity) {
                throw new InvalidOperationException($"Cannot add slot items because it exceeded capacity. Slot {slotIndex}");
            }
            slot.Value = value;
            slot.Count += count;
            Version++;
            RequestCompact();
        }

        public int RemoveSlotItems(int slotIndex, int count) {
            if (slotIndex < 0 || slotIndex >= m_slots.Count) return 0;
            count = MathUtils.Min(count, GetSlotCount(slotIndex));
            m_slots[slotIndex].Count -= count;
            Version++;
            RequestCompact();
            return count;
        }

        public void RequestCompact() => NeedsCompact = true;

        /// <summary>若有挂起请求则立刻压实。拓扑/存档/爆出等路径应调用此方法。</summary>
        public void CompactIfNeeded() {
            if (NeedsCompact) Compact();
        }

        /// <summary>紧密堆叠：非空槽前移，去掉中间空洞；不改变已占用槽的相对顺序。</summary>
        public void Compact() {
            NeedsCompact = false;
            int write = 0;
            for (int read = 0; read < m_slots.Count; read++) {
                if (m_slots[read].Count <= 0) continue;
                if (write != read) {
                    m_slots[write].Value = m_slots[read].Value;
                    m_slots[write].Count = m_slots[read].Count;
                    m_slots[read].Value = 0;
                    m_slots[read].Count = 0;
                }
                write++;
            }
            for (int i = write; i < m_slots.Count; i++) {
                m_slots[i].Value = 0;
                m_slots[i].Count = 0;
            }
            Version++;
        }

        public int CountOccupiedSlots() {
            int n = 0;
            for (int i = 0; i < m_slots.Count; i++) {
                if (m_slots[i].Count > 0) n++;
            }
            return n;
        }

        public void DropAllItems(Project project, Vector3 position) {
            SubsystemPickables pickables = project.FindSubsystem<SubsystemPickables>(true);
            for (int i = 0; i < m_slots.Count; i++) {
                int count = GetSlotCount(i);
                if (count <= 0) continue;
                int value = GetSlotValue(i);
                m_slots[i].Count = 0;
                m_slots[i].Value = 0;
                Vector3 velocity = m_random.Float(5f, 10f)
                    * Vector3.Normalize(new Vector3(m_random.Float(-1f, 1f), m_random.Float(1f, 2f), m_random.Float(-1f, 1f)));
                pickables.AddPickable(value, count, position, velocity, null);
            }
            Compact();
        }

        public void Write(ValuesDictionary valuesDictionary) {
            CompactIfNeeded();
            valuesDictionary.SetValue("Id", Id);
            valuesDictionary.SetValue("MemberCount", MemberCount);
            valuesDictionary.SetValue("LastUiPageIndex", LastUiPageIndex);
            valuesDictionary.SetValue("SlotsCount", m_slots.Count);
            ValuesDictionary slots = new();
            valuesDictionary.SetValue("Slots", slots);
            for (int i = 0; i < m_slots.Count; i++) {
                ComponentInventoryBase.Slot slot = m_slots[i];
                if (slot.Count <= 0) continue;
                ValuesDictionary slotVd = new();
                slots.SetValue($"Slot{i.ToString(CultureInfo.InvariantCulture)}", slotVd);
                slotVd.SetValue("Contents", slot.Value);
                slotVd.SetValue("Count", slot.Count);
            }
        }

        public static StorageVault Read(ValuesDictionary valuesDictionary) {
            Guid id = valuesDictionary.GetValue<Guid>("Id");
            int memberCount = valuesDictionary.GetValue("MemberCount", 1);
            var vault = new StorageVault(id, memberCount);
            vault.LastUiPageIndex = Math.Max(0, valuesDictionary.GetValue("LastUiPageIndex", 0));
            int slotsCount = valuesDictionary.GetValue("SlotsCount", vault.Capacity);
            vault.EnsureSlotCount(Math.Max(slotsCount, vault.Capacity));
            ValuesDictionary slots = valuesDictionary.GetValue<ValuesDictionary>("Slots", null);
            if (slots != null) {
                for (int i = 0; i < vault.m_slots.Count; i++) {
                    ValuesDictionary slotVd = slots.GetValue<ValuesDictionary>($"Slot{i.ToString(CultureInfo.InvariantCulture)}", null);
                    if (slotVd == null) continue;
                    vault.m_slots[i].Value = slotVd.GetValue<int>("Contents");
                    vault.m_slots[i].Count = slotVd.GetValue<int>("Count");
                }
            }
            vault.Compact();
            return vault;
        }
    }
}
