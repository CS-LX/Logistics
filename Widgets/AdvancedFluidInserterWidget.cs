using Engine;
using Engine.Graphics;
using Game;
using SCIENEW;
using System.Xml.Linq;

namespace Logistics {
    public class AdvancedFluidInserterWidget : CanvasWidget {
        public ComponentAdvancedFluidInserter m_component;
        public GridPanelWidget m_inventoryGrid;
        public TankWidget m_fluidTank;
        public ButtonWidget m_inMinusButton;
        public ButtonWidget m_inPlusButton;
        protected readonly ButtonWidget m_outPlusButton;
        protected readonly ButtonWidget m_outMinusButton;

        public AdvancedFluidInserterWidget(IInventory inventory, ComponentAdvancedFluidInserter component) {
            m_component = component;
            XElement node = ContentManager.Get<XElement>("Widgets/AdvancedFluidInserterWidget");
            LoadContents(this, node);
            m_inventoryGrid = Children.Find<GridPanelWidget>("InventoryGrid");
            m_fluidTank = Children.Find<TankWidget>("FluidTank");
            m_inMinusButton = Children.Find<ButtonWidget>("InMinusButton");
            m_inPlusButton = Children.Find<ButtonWidget>("InPlusButton");
            m_outMinusButton = Children.Find<ButtonWidget>("OutMinusButton");
            m_outPlusButton = Children.Find<ButtonWidget>("OutPlusButton");
            int num = 10;
            for (int k = 0; k < m_inventoryGrid.RowsCount; k++) {
                for (int l = 0; l < m_inventoryGrid.ColumnsCount; l++) {
                    var inventorySlotWidget2 = new InventorySlotWidget();
                    inventorySlotWidget2.AssignInventorySlot(inventory, num++);
                    m_inventoryGrid.Children.Add(inventorySlotWidget2);
                    m_inventoryGrid.SetWidgetCell(inventorySlotWidget2, new Point2(l, k));
                }
            }
            m_fluidTank.AssignTank(component, 0);
        }

        public override void Update() {
            if (!m_component.IsAddedToProject) {
                ParentWidget.Children.Remove(this);
                return;
            }
            Children.Find<LabelWidget>("InCountText").TextAnchor = TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter;
            Children.Find<LabelWidget>("OutCountText").TextAnchor = TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter;
            Children.Find<LabelWidget>("InCountText").Text = string.Format(LanguageControl.GetContentWidgets("AdvancedFluidInserterWidget", 3), m_component.m_inIndex.ToString());
            Children.Find<LabelWidget>("OutCountText").Text = string.Format(LanguageControl.GetContentWidgets("AdvancedFluidInserterWidget", 4), m_component.m_outIndex.ToString());
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
