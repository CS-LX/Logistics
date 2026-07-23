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
