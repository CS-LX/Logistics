using Game;
using SCIENEW;

namespace Logistics.Filtering {
    /// <summary>
    /// 从库存或储罐槽位收集 Value 的标准名单过滤。
    /// </summary>
    public sealed class TransferFilter : ITransferFilter {
        readonly HashSet<int> m_values;

        TransferFilter(FilterMode mode, HashSet<int> values) {
            Mode = mode;
            m_values = values;
        }

        public FilterMode Mode { get; }

        public bool IsEmpty => m_values.Count == 0;

        public bool Matches(int value) => m_values.Contains(value);

        public static TransferFilter FromInventory(IInventory inventory, int startSlot, int count, FilterMode mode) {
            var values = new HashSet<int>();
            if (inventory == null || count <= 0) {
                return new TransferFilter(mode, values);
            }
            int end = Math.Min(startSlot + count, inventory.SlotsCount);
            for (int slot = startSlot; slot < end; slot++) {
                if (inventory.GetSlotCount(slot) <= 0) {
                    continue;
                }
                int slotValue = inventory.GetSlotValue(slot);
                if (slotValue != 0) {
                    values.Add(slotValue);
                }
            }
            return new TransferFilter(mode, values);
        }

        public static TransferFilter FromTank(ITank tank, int startTank, int count, FilterMode mode) {
            var values = new HashSet<int>();
            if (tank == null || count <= 0) {
                return new TransferFilter(mode, values);
            }
            int end = Math.Min(startTank + count, tank.TanksCount);
            for (int index = startTank; index < end; index++) {
                if (tank.GetTankVolume(index) <= 0f) {
                    continue;
                }
                int fluidValue = tank.GetTankValue(index);
                if (fluidValue != 0) {
                    values.Add(fluidValue);
                }
            }
            return new TransferFilter(mode, values);
        }
    }
}
