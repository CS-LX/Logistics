using Engine;
using Game;

namespace Logistics {
    /// <summary>
    /// 单格 Segment 窗口语义：格心弧长 ±<see cref="WindowHalf"/>，供 <see cref="ComponentConveyerBeltSegment"/> 转发。
    /// </summary>
    public static class BeltSegmentInventory {
        public const float WindowHalf = 0.5f;

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
            return BeltPath.TryGetMemberCenterBeltPosition(group, cell, terrain, out center, out _);
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

        public static bool CanInsert(BeltGroup group, float center, int value) {
            return value != 0 && group.Inventory.CanInsertAt(center);
        }

        public static bool TryInsert(BeltGroup group, float center, int value, int count) {
            if (count <= 0 || !CanInsert(group, center, value)) {
                return false;
            }
            return group.Inventory.TryInsert(new TransportedItem {
                Value = value,
                Count = count,
                BeltPosition = center,
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
