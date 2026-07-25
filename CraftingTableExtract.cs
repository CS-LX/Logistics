using Engine;
using Game;
using GameEntitySystem;
using RecipaediaEX.ComponentsExtra.Implementation;

namespace Logistics {
    /// <summary>
    /// 合成台结果格「一组产出」抽取数量：与宿主 <c>ComponentInserter</c> / 高级抓取机一致，避免只抽 1 件打乱配方结算。
    /// </summary>
    public static class CraftingTableExtract {
        /// <summary>
        /// 普通槽返回 1；结果格在无匹配配方时返回 0（不抽）；有配方时返回 min(ResultCount, 槽内数量)。
        /// </summary>
        public static int GetCount(Entity sourceEntity, int slotIndex, IInventory sourceInventory) {
            var exCraftingTable = sourceEntity.FindComponent<ComponentEXCraftingTable>();
            if (exCraftingTable == null || slotIndex != exCraftingTable.ResultSlotIndex) {
                return 1;
            }
            if (exCraftingTable.m_matchedRecipe == null) {
                return 0;
            }
            return MathUtils.Min(exCraftingTable.m_matchedRecipe.ResultCount, sourceInventory.GetSlotCount(slotIndex));
        }
    }
}
