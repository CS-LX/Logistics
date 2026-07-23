using System.Xml.Linq;
using Engine;
using Game;
using GameEntitySystem;

namespace Logistics {
    /// <summary>M1：单页 6×4=24 格储存库 UI（左库右背包）。分页见 M3。</summary>
    public class StorageVaultWidget : CanvasWidget {
        public const int GridColumns = 6;
        public const int GridRows = 4;

        readonly ComponentStorageUnit m_unit;
        readonly GridPanelWidget m_vaultGrid;
        readonly GridPanelWidget m_inventoryGrid;
        int m_boundVersion = -1;

        public StorageVaultWidget(IInventory playerInventory, ComponentStorageUnit unit) {
            m_unit = unit;
            XElement node = ContentManager.Get<XElement>("Widgets/StorageVaultWidget");
            LoadContents(this, node);
            m_vaultGrid = Children.Find<GridPanelWidget>("VaultGrid");
            m_inventoryGrid = Children.Find<GridPanelWidget>("InventoryGrid");
            RebuildVaultGrid();
            int num = 10;
            for (int row = 0; row < m_inventoryGrid.RowsCount; row++) {
                for (int col = 0; col < m_inventoryGrid.ColumnsCount; col++) {
                    var slot = new InventorySlotWidget();
                    slot.AssignInventorySlot(playerInventory, num++);
                    m_inventoryGrid.Children.Add(slot);
                    m_inventoryGrid.SetWidgetCell(slot, new Point2(col, row));
                }
            }
        }

        void RebuildVaultGrid() {
            m_vaultGrid.Children.Clear();
            if (!m_unit.TryResolveVault(out StorageVault vault)) return;
            m_boundVersion = vault.Version;
            int index = 0;
            for (int row = 0; row < GridRows; row++) {
                for (int col = 0; col < GridColumns; col++) {
                    var slot = new InventorySlotWidget { Size = new Vector2(48) };
                    slot.AssignInventorySlot(m_unit, index++);
                    m_vaultGrid.Children.Add(slot);
                    m_vaultGrid.SetWidgetCell(slot, new Point2(col, row));
                }
            }
        }

        public override void Update() {
            if (!m_unit.IsAddedToProject) {
                ParentWidget.Children.Remove(this);
                return;
            }
            if (m_unit.TryResolveVault(out StorageVault vault) && vault.Version != m_boundVersion) {
                RebuildVaultGrid();
            }
        }
    }
}
