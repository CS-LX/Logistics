using Engine;
using Game;

namespace Logistics {
    /// <summary>
    /// 与宿主 <see cref="ScrollPanelWidget"/> 类似，但在库存拖拽进行中不抢 <c>Input.Drag</c>、不 <c>Input.Clear</c>，
    /// 避免拖动物品经过滚动区时被强制结束拖动并丢弃。
    /// </summary>
    public class LogisticsScrollPanelWidget : ScrollPanelWidget {
        public GameWidget m_gameWidget;
        public DragHostWidget m_dragHostWidget;

        public GameWidget GameWidget {
            get {
                if (m_gameWidget == null) {
                    for (ContainerWidget parent = ParentWidget; parent != null; parent = parent.ParentWidget) {
                        if (parent is GameWidget gameWidget) {
                            m_gameWidget = gameWidget;
                            break;
                        }
                    }
                }
                return m_gameWidget;
            }
        }

        public DragHostWidget DragHostWidget {
            get {
                if (m_dragHostWidget == null && GameWidget != null) {
                    m_dragHostWidget = GameWidget.Children.Find<DragHostWidget>(throwIfNotFound: false);
                }
                return m_dragHostWidget;
            }
        }

        public bool IsInventoryDragInProgress => DragHostWidget is { IsDragInProgress: true };

        public override void Update() {
            float overscroll = 50f;
            m_scrollAreaLength = CalculateScrollAreaLength();
            m_scrollBarAlpha = MathUtils.Max(m_scrollBarAlpha - 2f * Time.FrameDuration, 0f);

            // 库存拖拽进行中：只允许滚轮与惯性/回弹，绝不占用 Drag / Clear 输入。
            if (IsInventoryDragInProgress) {
                m_lastDragPosition = null;
                m_dragSpeed = 0f;
                UpdateWheelScroll(ref overscroll);
                UpdateInertiaAndClamp(overscroll);
                return;
            }

            if (Input.Drag.HasValue
                && !m_lastDragPosition.HasValue
                && HitTestPanel(Input.Drag.Value)) {
                m_lastDragPosition = ScreenToWidget(Input.Drag.Value);
            }

            if (m_lastDragPosition.HasValue) {
                if (Input.Press.HasValue) {
                    float speedSample;
                    Vector2 local = ScreenToWidget(Input.Press.Value);
                    Vector2 delta = local - m_lastDragPosition.Value;
                    if (Direction == LayoutDirection.Horizontal) {
                        ScrollPosition += 0f - delta.X;
                        speedSample = delta.X / Time.FrameDuration;
                    }
                    else {
                        ScrollPosition += 0f - delta.Y;
                        speedSample = delta.Y / Time.FrameDuration;
                    }
                    float blend = MathF.Abs(speedSample) < MathF.Abs(m_dragSpeed) ? 20f : 16f;
                    m_dragSpeed += MathUtils.Saturate(blend * Time.FrameDuration) * (speedSample - m_dragSpeed);
                    m_scrollBarAlpha = 4f;
                    m_lastDragPosition = local;
                    ScrollSpeed = 0f;
                }
                else {
                    ScrollSpeed = 0f - m_dragSpeed;
                    m_dragSpeed = 0f;
                    m_lastDragPosition = null;
                }
            }

            UpdateWheelScroll(ref overscroll);
            UpdateInertiaAndClamp(overscroll);

            // 仅在本控件主动接管滚动手势时清空输入，避免打断库存拖拽。
            if (m_lastDragPosition.HasValue
                && (Input.Drag.HasValue || Input.Hold.HasValue)) {
                Input.Clear();
            }
        }

        public void UpdateWheelScroll(ref float overscroll) {
            if (Input.Scroll.HasValue
                && HitTestPanel(Input.Scroll.Value.XY)) {
                ScrollPosition -= 40f * Input.Scroll.Value.Z;
                ScrollSpeed = 0f;
                overscroll = 0f;
                m_scrollBarAlpha = 3f;
            }
        }

        public void UpdateInertiaAndClamp(float overscroll) {
            if (ScrollSpeed != 0f) {
                ScrollSpeed *= MathF.Pow(0.33f, Time.FrameDuration);
                if (MathF.Abs(ScrollSpeed) < 40f) {
                    ScrollSpeed = 0f;
                }
                ScrollPosition += ScrollSpeed * Time.FrameDuration;
                m_scrollBarAlpha = 3f;
            }

            float maxScroll = MathUtils.Max(
                m_scrollAreaLength - ((Direction == LayoutDirection.Horizontal) ? ActualSize.X : ActualSize.Y),
                0f
            );
            if (ScrollPosition < 0f) {
                if (!m_lastDragPosition.HasValue) {
                    ScrollPosition = MathUtils.Min(ScrollPosition + 6f * Time.FrameDuration * (0f - ScrollPosition + 5f), 0f);
                }
                ScrollPosition = MathUtils.Max(ScrollPosition, 0f - overscroll);
                ScrollSpeed = 0f;
            }
            if (ScrollPosition > maxScroll) {
                if (!m_lastDragPosition.HasValue) {
                    ScrollPosition = MathUtils.Max(ScrollPosition + 6f * Time.FrameDuration * (maxScroll - ScrollPosition - 5f), maxScroll);
                }
                ScrollPosition = MathUtils.Min(ScrollPosition, maxScroll + overscroll);
                ScrollSpeed = 0f;
            }
        }
    }
}
