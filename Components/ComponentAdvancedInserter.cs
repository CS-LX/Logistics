using Engine;
using Game;
using GameEntitySystem;
using RecipaediaEX.ComponentsExtra.Implementation;
using SCIENEW.ProductionIO;
using TemplatesDatabase;

namespace Logistics {
    public class ComponentAdvancedInserter : ComponentInventoryBase {
        protected ComponentBlockEntity m_componentBlockEntity;
        public SubsystemTerrain m_subsystemTerrain;
        public SubsystemBlockEntities m_subsystemBlockEntities;
        public SubsystemPickables m_subsystemPickables;
        public int m_inNum;
        public int m_outNum;
        public bool m_dispenseItem = true;

        public bool Place() {
            Point3 coordinates = m_componentBlockEntity.Coordinates;
            int cellValue = m_subsystemTerrain.Terrain.GetCellValue(coordinates.X, coordinates.Y, coordinates.Z);
            int filterValue = GetSlotCount(0) > 0 ? GetSlotValue(0) : 0;
            int face = AdvancedInserterBlock.GetFacing(cellValue);
            Vector3 faceVector = CellFace.FaceToVector3(face);
            Vector3 center = new Vector3(coordinates) + new Vector3(0.5f);
            Vector3 dropPosition = center + 0.6f * faceVector;
            Point3 sourceCoords = coordinates - new Point3((int)faceVector.X, (int)faceVector.Y, (int)faceVector.Z);
            Point3 destCoords = coordinates + new Point3((int)faceVector.X, (int)faceVector.Y, (int)faceVector.Z);
            var sourceBlockEntity = m_subsystemBlockEntities.GetBlockEntity(sourceCoords.X, sourceCoords.Y, sourceCoords.Z);
            var sourceInventory = sourceBlockEntity?.Entity.FindComponent<IInventory>();
            if (sourceInventory == null) return false;
            foreach (int slotIndex in ProductionSlotAccess.GetInventorySourceSlotOrder(
                sourceBlockEntity.Entity,
                sourceInventory,
                filterValue,
                m_inNum
            )) {
                if (sourceInventory.GetSlotCount(slotIndex) <= 0) continue;
                int itemCount = GetTransferCount(sourceBlockEntity.Entity, slotIndex, sourceInventory);
                if (itemCount <= 0) continue;
                int itemValue = sourceInventory.GetSlotValue(slotIndex);
                if (filterValue != 0 && itemValue != filterValue) continue;
                if (TryTransferFromSlot(
                    sourceBlockEntity.Entity,
                    sourceInventory,
                    slotIndex,
                    itemValue,
                    itemCount,
                    destCoords,
                    dropPosition,
                    faceVector
                ))
                    return true;
            }
            return false;
        }

        static int GetTransferCount(Entity sourceEntity, int slotIndex, IInventory sourceInventory) {
            var exCraftingTable = sourceEntity.FindComponent<ComponentEXCraftingTable>();
            if (exCraftingTable == null || slotIndex != exCraftingTable.ResultSlotIndex) return 1;
            if (exCraftingTable.m_matchedRecipe == null) return 0;
            return MathUtils.Min(exCraftingTable.m_matchedRecipe.ResultCount, sourceInventory.GetSlotCount(slotIndex));
        }

        bool TryTransferFromSlot(
            Entity sourceEntity,
            IInventory sourceInventory,
            int slotIndex,
            int itemValue,
            int itemCount,
            Point3 destCoords,
            Vector3 dropPosition,
            Vector3 faceVector
        ) {
            var destBlockEntity = m_subsystemBlockEntities.GetBlockEntity(destCoords.X, destCoords.Y, destCoords.Z);
            var destInventory = destBlockEntity?.Entity.FindComponent<IInventory>();
            if (destInventory != null) {
                if (ProductionSlotAccess.TryInsertIntoInputSlots(
                    destBlockEntity.Entity,
                    destInventory,
                    itemValue,
                    itemCount,
                    m_outNum
                )) {
                    sourceInventory.RemoveSlotItems(slotIndex, itemCount);
                    return true;
                }
            }
            else if (m_dispenseItem) {
                m_subsystemPickables.AddPickable(
                    itemValue,
                    itemCount,
                    dropPosition,
                    1.8f * (faceVector + m_random.Vector3(0.2f)),
                    null
                );
                sourceInventory.RemoveSlotItems(slotIndex, itemCount);
                return true;
            }
            return false;
        }

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap) {
            base.Load(valuesDictionary, idToEntityMap);
            m_componentBlockEntity = Entity.FindComponent<ComponentBlockEntity>(true);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(true);
            m_subsystemBlockEntities = Project.FindSubsystem<SubsystemBlockEntities>(true);
            m_subsystemPickables = Project.FindSubsystem<SubsystemPickables>(true);
            m_inNum = valuesDictionary.GetValue("Innum", 0);
            m_outNum = valuesDictionary.GetValue("Outnum", 0);
        }

        public override void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap) {
            base.Save(valuesDictionary, entityToIdMap);
            valuesDictionary.SetValue("Innum", m_inNum);
            valuesDictionary.SetValue("Outnum", m_outNum);
        }
    }
}
