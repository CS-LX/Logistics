using Engine;
using Game;
using GameEntitySystem;
using Logistics.Filtering;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>
    /// 高级分拣机：四侧各一套样例格与过滤模式；撞上的物品按面过滤弹出，不吸入库存。
    /// 槽位布局：face * SlotsPerFace .. face *SlotsPerFace+SlotsPerFace-1，face 与 CellFace 0..3 一致。
    /// </summary>
    public class ComponentAdvancedSorter : ComponentInventoryBase {
        public const int FaceCount = 4;
        public const int SlotsPerFace = 9;

        public readonly FilterMode[] FilterModes = [
            FilterMode.Allow,
            FilterMode.Allow,
            FilterMode.Allow,
            FilterMode.Allow
        ];

        public int GetFaceSlotStart(int face) => face * SlotsPerFace;

        public ITransferFilter GetFaceFilter(int face)
            => TransferFilter.FromInventory(this, GetFaceSlotStart(face), SlotsPerFace, FilterModes[face]);

        /// <summary>
        /// 该面是否接此物品。空白名单不接；空黑名单作兜底全通。
        /// </summary>
        public bool FaceAccepts(int face, int value) {
            if (face < 0 || face >= FaceCount) {
                return false;
            }
            ITransferFilter filter = GetFaceFilter(face);
            if (filter.IsEmpty) {
                return FilterModes[face] == FilterMode.Deny;
            }
            return filter.Allows(value);
        }

        /// <summary>按红→黄→绿→蓝（face 0..3）找第一个接货面；无则 -1。</summary>
        public int FindOutputFace(int value) {
            for (int face = 0; face < FaceCount; face++) {
                if (FaceAccepts(face, value)) {
                    return face;
                }
            }
            return -1;
        }

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap) {
            base.Load(valuesDictionary, idToEntityMap);
            string modes = valuesDictionary.GetValue("FilterModes", string.Empty);
            if (!string.IsNullOrEmpty(modes)) {
                string[] parts = modes.Split(',');
                for (int i = 0; i < FaceCount && i < parts.Length; i++) {
                    FilterModes[i] = int.TryParse(parts[i], out int mode) && mode == (int)FilterMode.Deny
                        ? FilterMode.Deny
                        : FilterMode.Allow;
                }
            }
            else {
                for (int i = 0; i < FaceCount; i++) {
                    FilterModes[i] = valuesDictionary.GetValue($"FilterMode{i}", 0) == (int)FilterMode.Deny
                        ? FilterMode.Deny
                        : FilterMode.Allow;
                }
            }
        }

        public override void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap) {
            base.Save(valuesDictionary, entityToIdMap);
            valuesDictionary.SetValue(
                "FilterModes",
                string.Join(",", FilterModes.Select(m => ((int)m).ToString())));
        }
    }
}
