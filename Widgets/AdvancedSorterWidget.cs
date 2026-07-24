using Engine;
using Game;
using Logistics.Filtering;
using System.Xml.Linq;

namespace Logistics {
    public class AdvancedSorterWidget : CanvasWidget {
        static readonly string[] TabNames = ["TabRed", "TabYellow", "TabGreen", "TabBlue"];

        // 语义色混入默认 Bevel 面板色 (181,172,154)，避免黑底/高饱和与模态脱节
        static readonly Color PanelBase = new Color(181, 172, 154);

        static readonly Color[] TabAccentColors = [
            new Color(150, 72, 64),
            new Color(150, 124, 52),
            new Color(72, 118, 72),
            new Color(72, 100, 138)
        ];

        readonly ComponentAdvancedSorter m_component;
        readonly GridPanelWidget m_inventoryGrid;
        readonly GridPanelWidget m_itemGrid;
        readonly BevelledButtonWidget m_filterModeButton;
        readonly BevelledButtonWidget[] m_tabs = new BevelledButtonWidget[ComponentAdvancedSorter.FaceCount];
        readonly InventorySlotWidget[] m_filterSlots = new InventorySlotWidget[ComponentAdvancedSorter.SlotsPerFace];

        int m_selectedFace;
        int m_boundFace = -1;

        public AdvancedSorterWidget(IInventory inventory, ComponentAdvancedSorter component) {
            m_component = component;
            XElement node = ContentManager.Get<XElement>("Widgets/AdvancedSorterWidget");
            LoadContents(this, node);
            m_inventoryGrid = Children.Find<GridPanelWidget>("InventoryGrid");
            m_itemGrid = Children.Find<GridPanelWidget>("ItemGrid");
            m_filterModeButton = Children.Find<BevelledButtonWidget>("FilterModeButton");
            for (int i = 0; i < ComponentAdvancedSorter.FaceCount; i++) {
                m_tabs[i] = Children.Find<BevelledButtonWidget>(TabNames[i]);
            }
            int slot = 0;
            for (int row = 0; row < m_itemGrid.RowsCount; row++) {
                for (int col = 0; col < m_itemGrid.ColumnsCount; col++) {
                    var inventorySlotWidget = new InventorySlotWidget();
                    m_filterSlots[slot++] = inventorySlotWidget;
                    m_itemGrid.Children.Add(inventorySlotWidget);
                    m_itemGrid.SetWidgetCell(inventorySlotWidget, new Point2(col, row));
                }
            }
            int playerSlot = 10;
            for (int row = 0; row < m_inventoryGrid.RowsCount; row++) {
                for (int col = 0; col < m_inventoryGrid.ColumnsCount; col++) {
                    var inventorySlotWidget = new InventorySlotWidget();
                    inventorySlotWidget.AssignInventorySlot(inventory, playerSlot++);
                    m_inventoryGrid.Children.Add(inventorySlotWidget);
                    m_inventoryGrid.SetWidgetCell(inventorySlotWidget, new Point2(col, row));
                }
            }
            BindFilterSlots();
            SyncTabChrome();
        }

        void BindFilterSlots() {
            if (m_boundFace == m_selectedFace) {
                return;
            }
            m_boundFace = m_selectedFace;
            int start = m_component.GetFaceSlotStart(m_selectedFace);
            for (int i = 0; i < m_filterSlots.Length; i++) {
                m_filterSlots[i].AssignInventorySlot(m_component, start + i);
            }
        }

        void SyncTabChrome() {
            for (int face = 0; face < ComponentAdvancedSorter.FaceCount; face++) {
                bool selected = face == m_selectedFace;
                m_tabs[face].IsChecked = selected;
                Color accent = TabAccentColors[face];
                if (selected) {
                    m_tabs[face].CenterColor = Color.Lerp(PanelBase, accent, 0.42f);
                    m_tabs[face].BevelColor = Color.Lerp(PanelBase, accent, 0.28f);
                    m_tabs[face].Color = Color.White;
                }
                else {
                    m_tabs[face].CenterColor = PanelBase;
                    m_tabs[face].BevelColor = PanelBase;
                    m_tabs[face].Color = Color.Lerp(accent, Color.White, 0.35f);
                }
            }
        }

        public override void Update() {
            if (!m_component.IsAddedToProject) {
                ParentWidget.Children.Remove(this);
                return;
            }
            for (int face = 0; face < ComponentAdvancedSorter.FaceCount; face++) {
                if (m_tabs[face].IsClicked) {
                    m_selectedFace = face;
                }
            }
            SyncTabChrome();
            BindFilterSlots();
            m_filterModeButton.Text = m_component.FilterModes[m_selectedFace] == FilterMode.Allow
                ? LanguageControl.GetContentWidgets("AdvancedSorterWidget", 4)
                : LanguageControl.GetContentWidgets("AdvancedSorterWidget", 5);
            if (m_filterModeButton.IsClicked) {
                m_component.FilterModes[m_selectedFace] = m_component.FilterModes[m_selectedFace] == FilterMode.Allow
                    ? FilterMode.Deny
                    : FilterMode.Allow;
            }
        }
    }
}
