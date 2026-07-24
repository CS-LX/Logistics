using System.Xml.Linq;
using Engine;
using Game;
using GameEntitySystem;
using SCIENEW;

namespace Logistics {
    /// <summary>储存库 UI：左库右背包；中间翻页（箭头 / 滑条 / 悬停滚轮）；记住每库上次页码。</summary>
    public class StorageVaultWidget : CanvasWidget, IRemovedActionWidget {
        public const int PageColumns = 6;
        public const int PageRows = 4;
        public const int PageSize = PageColumns * PageRows;

        readonly ComponentStorageUnit m_unit;
        readonly GridPanelWidget m_vaultGrid;
        readonly GridPanelWidget m_inventoryGrid;
        readonly ContainerWidget m_pageControls;
        readonly ButtonWidget m_previousPageButton;
        readonly ButtonWidget m_nextPageButton;
        readonly LabelWidget m_pageLabel;
        readonly SliderWidget m_pageSlider;

        int m_pageIndex;
        int m_boundVersion = -1;
        int m_boundSlots = -1;
        Guid m_boundVaultId = Guid.Empty;
        Guid m_boundUnitGuid = Guid.Empty;
        int m_displayedPage = -1;

        public StorageVaultWidget(IInventory playerInventory, ComponentStorageUnit unit) {
            m_unit = unit;
            XElement node = ContentManager.Get<XElement>("Widgets/StorageVaultWidget");
            LoadContents(this, node);
            m_vaultGrid = Children.Find<GridPanelWidget>("VaultGrid");
            m_inventoryGrid = Children.Find<GridPanelWidget>("InventoryGrid");
            m_pageControls = Children.Find<ContainerWidget>("PageControls");
            m_previousPageButton = Children.Find<ButtonWidget>("PreviousPageButton");
            m_nextPageButton = Children.Find<ButtonWidget>("NextPageButton");
            m_pageLabel = Children.Find<LabelWidget>("PageLabel");
            m_pageSlider = Children.Find<SliderWidget>("PageSlider");
            m_pageSlider.Children.Find<RectangleWidget>("Slider.Rectangle").Size = new Vector2(6f, float.PositiveInfinity);
            if (m_unit.TryResolveVault(out StorageVault vault)) {
                m_pageIndex = ClampPageIndex(vault.LastUiPageIndex, vault.SlotsCount);
            }
            RebuildPage();
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

        static int PageCount(int slotsCount) => Math.Max(1, (Math.Max(0, slotsCount) + PageSize - 1) / PageSize);

        static int ClampPageIndex(int pageIndex, int slotsCount)
            => Math.Clamp(pageIndex, 0, PageCount(slotsCount) - 1);

        void ClampPage(int slotsCount) {
            m_pageIndex = ClampPageIndex(m_pageIndex, slotsCount);
        }

        void RememberPage(StorageVault vault) {
            vault.LastUiPageIndex = ClampPageIndex(m_pageIndex, vault.SlotsCount);
        }

        void SyncPageChrome(int pageCount) {
            m_pageLabel.Text = string.Format(
                LanguageControl.GetContentWidgets(nameof(StorageVaultWidget), 3),
                m_pageIndex + 1,
                pageCount
            );
            m_previousPageButton.IsEnabled = m_pageIndex > 0;
            m_nextPageButton.IsEnabled = m_pageIndex < pageCount - 1;
            m_pageSlider.MinValue = 1f;
            m_pageSlider.MaxValue = pageCount;
            m_pageSlider.Granularity = 1f;
            m_pageSlider.IsEnabled = pageCount > 1;
            if (!m_pageSlider.IsSliding) {
                m_pageSlider.Value = m_pageIndex + 1;
            }
        }

        void RebuildPage() {
            m_vaultGrid.Children.Clear();
            if (!m_unit.TryResolveVault(out StorageVault vault)) {
                m_boundVersion = -1;
                m_boundSlots = -1;
                m_boundVaultId = Guid.Empty;
                m_boundUnitGuid = Guid.Empty;
                m_displayedPage = -1;
                SyncPageChrome(1);
                m_pageLabel.Text = string.Format(LanguageControl.GetContentWidgets(nameof(StorageVaultWidget), 3), 0, 0);
                m_previousPageButton.IsEnabled = false;
                m_nextPageButton.IsEnabled = false;
                m_pageSlider.IsEnabled = false;
                return;
            }

            // 簇变动（容量/Guid）：页码 = min(上次页, 新末页)
            if (vault.Id != m_boundVaultId || vault.SlotsCount != m_boundSlots) {
                if (vault.Id != m_boundVaultId) {
                    m_pageIndex = vault.LastUiPageIndex;
                }
                ClampPage(vault.SlotsCount);
                RememberPage(vault);
            }
            else {
                ClampPage(vault.SlotsCount);
            }

            int pageCount = PageCount(vault.SlotsCount);
            int baseIndex = m_pageIndex * PageSize;
            int index = 0;
            for (int row = 0; row < PageRows; row++) {
                for (int col = 0; col < PageColumns; col++) {
                    var slot = new InventorySlotWidget();
                    slot.AssignInventorySlot(m_unit, baseIndex + index);
                    m_vaultGrid.Children.Add(slot);
                    m_vaultGrid.SetWidgetCell(slot, new Point2(col, row));
                    index++;
                }
            }

            m_boundVersion = vault.Version;
            m_boundSlots = vault.SlotsCount;
            m_boundVaultId = vault.Id;
            m_boundUnitGuid = m_unit.VaultGuid;
            m_displayedPage = m_pageIndex;
            RememberPage(vault);
            SyncPageChrome(pageCount);
        }

        void Close() {
            if (m_unit.TryResolveVault(out StorageVault vault)) {
                RememberPage(vault);
            }
            ParentWidget?.Children.Remove(this);
        }

        public void OnRemoved() {
            if (m_unit.TryResolveVault(out StorageVault vault)) {
                RememberPage(vault);
            }
        }

        public override void Update() {
            if (!m_unit.IsAddedToProject) {
                Close();
                return;
            }
            if (!m_unit.TryResolveVault(out StorageVault vault)) {
                Close();
                return;
            }

            // 悬停翻页区滚轮翻页（对齐创造背包 / 家具面板：Scroll.Z）
            if (Input.Scroll.HasValue) {
                Widget hit = HitTestGlobal(Input.Scroll.Value.XY);
                if (hit != null && (hit == m_pageControls || hit.IsChildWidgetOf(m_pageControls))) {
                    m_pageIndex -= (int)Input.Scroll.Value.Z;
                }
            }
            if (m_previousPageButton.IsClicked) {
                m_pageIndex--;
            }
            if (m_nextPageButton.IsClicked) {
                m_pageIndex++;
            }
            if (m_pageSlider.IsSliding) {
                m_pageIndex = (int)MathF.Round(m_pageSlider.Value) - 1;
            }

            ClampPage(vault.SlotsCount);
            bool topologyOrContentChanged = vault.Version != m_boundVersion
                || vault.SlotsCount != m_boundSlots
                || vault.Id != m_boundVaultId
                || m_unit.VaultGuid != m_boundUnitGuid;
            if (topologyOrContentChanged || m_pageIndex != m_displayedPage) {
                RebuildPage();
            }
            else {
                RememberPage(vault);
                SyncPageChrome(PageCount(vault.SlotsCount));
            }
        }
    }
}
