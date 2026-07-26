using Engine;
using Game;

namespace Logistics {
    /// <summary>
    /// 单格 Segment 窗口语义：格心弧长 ±<see cref="WindowHalf"/>，供 <see cref="ComponentConveyerBeltSegment"/> 转发。
    /// </summary>
    public static class BeltSegmentInventory {
        public const float WindowHalf = 0.5f;

        /// <summary>插入位置距整组两端留边，避免刚放上去就被判为到头而弹出。</summary>
        public const float EndInset = 0.12f;

        public static bool TryResolve(
            SubsystemBeltGroups beltGroups,
            SubsystemTerrain terrain,
            Point3 cell,
            out BeltGroup group,
            out float center) {
            group = null;
            center = 0f;
            if (beltGroups == null || terrain == null || !beltGroups.TryGetAt(cell, out group)) {
                return false;
            }
            return BeltPath.TryGetMemberCenterBeltPosition(group, cell, terrain, out center);
        }

        /// <summary>取该格的弧长区间，供按格内位置（而非只在格心）插入。</summary>
        public static bool TryResolveSpan(
            SubsystemBeltGroups beltGroups,
            SubsystemTerrain terrain,
            Point3 cell,
            out BeltGroup group,
            out float spanStart,
            out float spanLength) {
            group = null;
            spanStart = 0f;
            spanLength = 0f;
            if (beltGroups == null || terrain == null || !beltGroups.TryGetAt(cell, out group)) {
                return false;
            }
            return BeltPath.TryGetMemberSpan(group, cell, terrain, out spanStart, out spanLength);
        }

        public static bool TryPeek(BeltGroup group, float center, out TransportedItem item, out int index) {
            item = null;
            index = group.Inventory.FindClosestInWindow(center, WindowHalf);
            if (index < 0) {
                return false;
            }
            item = group.Inventory.GetAt(index);
            return true;
        }

        public static bool CanInsert(BeltGroup group, float beltPosition, int value) {
            return value != 0 && group.Inventory.CanInsertAt(beltPosition);
        }

        /// <summary><paramref name="beltPosition"/> 为组内弧长，可落在格内任意处，不限格心。</summary>
        public static bool TryInsert(BeltGroup group, float beltPosition, int value, int count) {
            if (count <= 0 || !CanInsert(group, beltPosition, value)) {
                return false;
            }
            return group.Inventory.TryInsert(new TransportedItem {
                Value = value,
                Count = count,
                BeltPosition = beltPosition,
                SideOffset = 0f,
                Velocity = Vector3.Zero
            });
        }

        public static int TryRemove(BeltGroup group, float center, int count) {
            if (!TryPeek(group, center, out _, out int index)) {
                return 0;
            }
            return group.Inventory.RemoveAt(index, count);
        }
    }
}
