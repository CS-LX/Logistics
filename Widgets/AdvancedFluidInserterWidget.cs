using Engine;
using Engine.Graphics;
using Game;
using Logistics.Filtering;
using SCIENEW;
using System.Xml.Linq;

namespace Logistics {
    public class AdvancedFluidInserterWidget : CanvasWidget {
        public ComponentAdvancedFluidInserter m_component;
        public GridPanelWidget m_inventoryGrid;
        public GridPanelWidget m_fluidTankGrid;
        public ButtonWidget m_inMinusButton;
        public ButtonWidget m_inPlusButton;
        public ButtonWidget m_filterModeButton;
        protected readonly ButtonWidget m_outPlusButton;
        protected readonly ButtonWidget m_outMinusButton;

        public AdvancedFluidInserterWidget(IInventory inventory, ComponentAdvancedFluidInserter component) {
            m_component = component;
            XElement node = ContentManager.Get<XElement>("Widgets/AdvancedFluidInserterWidget");
            LoadContents(this, node);
            m_inventoryGrid = Children.Find<GridPanelWidget>("InventoryGrid");
            m_fluidTankGrid = Children.Find<GridPanelWidget>("FluidTankGrid");
            m_inMinusButton = Children.Find<ButtonWidget>("InMinusButton");
            m_inPlusButton = Children.Find<ButtonWidget>("InPlusButton");
            m_outMinusButton = Children.Find<ButtonWidget>("OutMinusButton");
            m_outPlusButton = Children.Find<ButtonWidget>("OutPlusButton");
            m_filterModeButton = Children.Find<ButtonWidget>("FilterModeButton");
            int tankIndex = 0;
            for (int i = 0; i < m_fluidTankGrid.RowsCount; i++) {
                for (int j = 0; j < m_fluidTankGrid.ColumnsCount; j++) {
                    var tankWidget = new TankWidget {
                        Size = new Vector2(96f, 96f)
                    };
                    tankWidget.AssignTank(component, tankIndex++);
                    m_fluidTankGrid.Children.Add(tankWidget);
                    m_fluidTankGrid.SetWidgetCell(tankWidget, new Point2(j, i));
                }
            }
            int num = 10;
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
            LabelWidget inCount = Children.Find<LabelWidget>("InCountText");
            LabelWidget outCount = Children.Find<LabelWidget>("OutCountText");
            inCount.TextAnchor = TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter;
            outCount.TextAnchor = TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter;
            inCount.Text = string.Format(LanguageControl.GetContentWidgets("AdvancedFluidInserterWidget", 3), m_component.m_inIndex.ToString());
            outCount.Text = string.Format(LanguageControl.GetContentWidgets("AdvancedFluidInserterWidget", 4), m_component.m_outIndex.ToString());
            m_filterModeButton.Text = m_component.m_filterMode == FilterMode.Allow
                ? LanguageControl.GetContentWidgets("AdvancedFluidInserterWidget", 6)
                : LanguageControl.GetContentWidgets("AdvancedFluidInserterWidget", 7);
            if (m_filterModeButton.IsClicked) {
                m_component.m_filterMode = m_component.m_filterMode == FilterMode.Allow
                    ? FilterMode.Deny
                    : FilterMode.Allow;
            }
            if (m_inPlusButton.IsClicked) {
                m_component.m_inIndex++;
            }
            if (m_inMinusButton.IsClicked && m_component.m_inIndex > 0) {
                m_component.m_inIndex--;
            }
            if (m_outPlusButton.IsClicked) {
                m_component.m_outIndex++;
            }
            if (m_outMinusButton.IsClicked && m_component.m_outIndex > 0) {
                m_component.m_outIndex--;
            }
        }
    }
}
