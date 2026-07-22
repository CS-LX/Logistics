using Engine;
using Engine.Graphics;
using Game;
using System.Xml.Linq;

namespace Logistics {
    public class AdvancedInserterWidget : CanvasWidget {
        public ComponentAdvancedInserter m_component;
        public GridPanelWidget m_inventoryGrid;
        public GridPanelWidget m_itemGrid;
        public ButtonWidget m_inMinusButton;
        public ButtonWidget m_inPlusButton;
        protected readonly ButtonWidget m_outPlusButton;
        protected readonly ButtonWidget m_outMinusButton;

        public AdvancedInserterWidget(IInventory inventory, ComponentAdvancedInserter component) {
            m_component = component;
            XElement node = ContentManager.Get<XElement>("Widgets/AdvancedInserterWidget");
            LoadContents(this, node);
            m_inventoryGrid = Children.Find<GridPanelWidget>("InventoryGrid");
            m_itemGrid = Children.Find<GridPanelWidget>("ItemGrid");
            m_inMinusButton = Children.Find<ButtonWidget>("InMinusButton");
            m_inPlusButton = Children.Find<ButtonWidget>("InPlusButton");
            m_outMinusButton = Children.Find<ButtonWidget>("OutMinusButton");
            m_outPlusButton = Children.Find<ButtonWidget>("OutPlusButton");
            int num = 0;
            for (int i = 0; i < m_itemGrid.RowsCount; i++) {
                for (int j = 0; j < m_itemGrid.ColumnsCount; j++) {
                    var inventorySlotWidget = new InventorySlotWidget();
                    inventorySlotWidget.AssignInventorySlot(component, num++);
                    m_itemGrid.Children.Add(inventorySlotWidget);
                    m_itemGrid.SetWidgetCell(inventorySlotWidget, new Point2(j, i));
                }
            }
            num = 10;
            for (int k = 0; k < m_inventoryGrid.RowsCount; k++) {
                for (int l = 0; l < m_inventoryGrid.ColumnsCount; l++) {
                    var inventorySlotWidget2 = new InventorySlotWidget();
                    inventorySlotWidget2.AssignInventorySlot(inventory, num++);
                    m_inventoryGrid.Children.Add(inventorySlotWidget2);
                    m_inventoryGrid.SetWidgetCell(inventorySlotWidget2, new Point2(l, k));
                }
            }
        }

        public override void Update() {
            if (!m_component.IsAddedToProject) {
                ParentWidget.Children.Remove(this);
                return;
            }
            Children.Find<LabelWidget>("InCountText").TextAnchor = TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter;
            Children.Find<LabelWidget>("OutCountText").TextAnchor = TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter;
            Children.Find<LabelWidget>("InCountText").Text = string.Format(LanguageControl.GetContentWidgets("AdvancedInserterWidget", 3), m_component.m_inNum.ToString());
            Children.Find<LabelWidget>("OutCountText").Text = string.Format(LanguageControl.GetContentWidgets("AdvancedInserterWidget", 4), m_component.m_outNum.ToString());
            if (m_inPlusButton.IsClicked) {
                m_component.m_inNum++;
            }
            if (m_inMinusButton.IsClicked && m_component.m_inNum > 0) {
                m_component.m_inNum--;
            }
            if (m_outPlusButton.IsClicked) {
                m_component.m_outNum++;
            }
            if (m_outMinusButton.IsClicked && m_component.m_outNum > 0) {
                m_component.m_outNum--;
            }
        }
    }
}
