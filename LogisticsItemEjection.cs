using Engine;
using Game;
using GameEntitySystem;
using SCIENEW.ProductionIO;

namespace Logistics {
    /// <summary>
    /// 高级抓取机 / 分拣机向外吐货的共用语义：
    /// 传送带可接则入带；接不下则宿主发射器「吐出」；非传送带则宿主分拣机「投射」（非发射）。
    /// </summary>
    public static class LogisticsItemEjection {
        /// <summary>宿主 <see cref="ComponentDispenser"/> Dispense（吐出）：Pickable + 1.8 初速。</summary>
        public static void DispenseThrow(SubsystemPickables pickables, Game.Random random, int value, int count, Vector3 cellCenter, Vector3 faceVector) {
            Vector3 position = cellCenter + 0.6f * faceVector;
            pickables.AddPickable(
                value,
                count,
                position,
                1.8f * (faceVector + random.Vector3(0.2f)),
                null
            );
        }

        /// <summary>
        /// 宿主分拣机投射：<see cref="SubsystemProjectiles.FireProjectile"/> 速度 10；
        /// 失败时退回 Pickable（速度 1）。不是发射器「发射」(~40)。
        /// </summary>
        public static void ProjectThrow(SubsystemProjectiles projectiles, SubsystemPickables pickables, int value, int count, Vector3 cellCenter, Vector3 faceVector) {
            Vector3 position = cellCenter + 0.75f * faceVector;
            if (count == 1
                && projectiles.FireProjectile(
                    value,
                    position,
                    10f * faceVector,
                    Vector3.Zero,
                    null
                )
                != null) {
                return;
            }
            float speed = count == 1 ? 1f : 10f;
            pickables.AddPickable(
                value,
                count,
                position,
                speed * faceVector,
                null
            );
        }

        public static bool IsConveyerBeltDest(ComponentBlockEntity destBlockEntity, SubsystemTerrain terrain, Point3 destCoords) {
            if (destBlockEntity?.Entity.FindComponent<ComponentConveyerBeltSegment>() != null) {
                return true;
            }
            int contents = Terrain.ExtractContents(terrain.Terrain.GetCellValue(destCoords.X, destCoords.Y, destCoords.Z));
            return contents == BlocksManager.GetBlockIndex<ConveyerBeltBlock>();
        }

        /// <summary>
        /// 向邻格输出：传送带优先插入，满则吐出；其它库存走插入；无库存则分拣投射。
        /// </summary>
        public static bool TryOutput(SubsystemBlockEntities blockEntities, SubsystemTerrain terrain, SubsystemPickables pickables, SubsystemProjectiles projectiles, Game.Random random, Point3 destCoords, Vector3 deviceCenter, Vector3 faceVector, int itemValue, int itemCount, int outSlotOneBased, bool allowWorldEject) {
            var destBlockEntity = blockEntities.GetBlockEntity(destCoords.X, destCoords.Y, destCoords.Z);
            var destInventory = destBlockEntity?.Entity.FindComponent<IInventory>();
            bool isBelt = IsConveyerBeltDest(destBlockEntity, terrain, destCoords);
            if (isBelt) {
                if (destInventory != null
                    && ProductionSlotAccess.TryInsertIntoInputSlots(
                        destBlockEntity.Entity,
                        destInventory,
                        itemValue,
                        itemCount,
                        outSlotOneBased
                    )) {
                    return true;
                }
                if (!allowWorldEject) {
                    return false;
                }
                DispenseThrow(
                    pickables,
                    random,
                    itemValue,
                    itemCount,
                    deviceCenter,
                    faceVector
                );
                return true;
            }
            if (destInventory != null) {
                return ProductionSlotAccess.TryInsertIntoInputSlots(
                    destBlockEntity.Entity,
                    destInventory,
                    itemValue,
                    itemCount,
                    outSlotOneBased
                );
            }
            if (!allowWorldEject) {
                return false;
            }
            ProjectThrow(
                projectiles,
                pickables,
                itemValue,
                itemCount,
                deviceCenter,
                faceVector
            );
            return true;
        }
    }
}