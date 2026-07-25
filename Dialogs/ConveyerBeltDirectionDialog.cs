using Engine;
using Engine.Graphics;
using Game;

namespace Logistics {
    /// <summary>
    /// 输送带调向：只在屏幕下方摆状态与按钮，不铺面板底，打开时仍能看见带面滚动与在途物。
    /// </summary>
    public class ConveyerBeltDirectionDialog : Dialog {
        readonly SubsystemBeltGroups m_beltGroups;
        readonly Point3 m_cell;
        readonly LabelWidget m_statusLabel;
        readonly BevelledButtonWidget m_toggleButton;
        readonly BevelledButtonWidget m_closeButton;

        public ConveyerBeltDirectionDialog(SubsystemBeltGroups beltGroups, Point3 cell) {
            m_beltGroups = beltGroups;
            m_cell = cell;
            var titleLabel = new LabelWidget {
                Text = LanguageControl.GetContentWidgets(nameof(ConveyerBeltDirectionDialog), "Title"),
                Color = Color.White,
                DropShadow = true,
                HorizontalAlignment = WidgetAlignment.Center,
                TextAnchor = TextAnchor.HorizontalCenter,
                Margin = new Vector2(0f, 4f)
            };
            m_statusLabel = new LabelWidget {
                Color = new Color(220, 220, 220),
                DropShadow = true,
                HorizontalAlignment = WidgetAlignment.Center,
                TextAnchor = TextAnchor.HorizontalCenter,
                Margin = new Vector2(0f, 4f)
            };
            m_toggleButton = new BevelledButtonWidget {
                Size = new Vector2(160f, 60f),
                Margin = new Vector2(20f, 8f),
                Text = LanguageControl.GetContentWidgets(nameof(ConveyerBeltDirectionDialog), "Toggle")
            };
            m_closeButton = new BevelledButtonWidget {
                Size = new Vector2(160f, 60f),
                Margin = new Vector2(20f, 8f),
                Text = LanguageControl.GetContentWidgets(nameof(ConveyerBeltDirectionDialog), "Close")
            };
            var buttonsPanel = new StackPanelWidget {
                Direction = LayoutDirection.Horizontal,
                HorizontalAlignment = WidgetAlignment.Center
            };
            buttonsPanel.Children.Add(m_toggleButton);
            buttonsPanel.Children.Add(m_closeButton);
            var panel = new StackPanelWidget {
                Direction = LayoutDirection.Vertical,
                HorizontalAlignment = WidgetAlignment.Center,
                VerticalAlignment = WidgetAlignment.Far,
                Margin = new Vector2(0f, 40f)
            };
            panel.Children.Add(titleLabel);
            panel.Children.Add(m_statusLabel);
            panel.Children.Add(buttonsPanel);
            Children.Add(new GradientBackdropWidget());
            Children.Add(panel);
            RefreshStatus();
        }

        public override void Update() {
            // 宿主会铺一层近黑遮罩铺满全屏；这里换成自绘的下浓上透渐变
            if (DialogsManager.m_animationData.TryGetValue(this, out DialogsManager.AnimationData animationData)) {
                animationData.CoverWidget.FillColor = Color.Transparent;
            }
            RefreshStatus();
            if (Input.Cancel || Input.Back || m_closeButton.IsClicked) {
                DialogsManager.HideDialog(this);
                return;
            }
            if (m_toggleButton.IsClicked) {
                m_beltGroups.TryToggleSign(m_cell, out _);
            }
        }

        void RefreshStatus() {
            if (!m_beltGroups.TryGetAt(m_cell, out BeltGroup group)) {
                m_statusLabel.Text = LanguageControl.GetContentWidgets(nameof(ConveyerBeltDirectionDialog), "Gone");
                m_toggleButton.IsEnabled = false;
                return;
            }
            m_toggleButton.IsEnabled = true;
            string direction = LanguageControl.GetContentWidgets(
                nameof(ConveyerBeltDirectionDialog),
                group.Sign >= 0 ? "Forward" : "Reverse");
            string running = LanguageControl.GetContentWidgets(
                nameof(ConveyerBeltDirectionDialog),
                m_beltGroups.IsGroupRunning(group) ? "Running" : "Stopped");
            m_statusLabel.Text = string.Format(
                LanguageControl.GetContentWidgets(nameof(ConveyerBeltDirectionDialog), "Status"),
                direction,
                running,
                group.Inventory.Count,
                group.Members.Count);
        }

        /// <summary>屏幕下部渐深、上部全透：既衬出按钮与文字，又不挡住带面。</summary>
        sealed class GradientBackdropWidget : Widget {
            /// <summary>渐变带占屏幕高度的比例，其余部分不画。</summary>
            const float BandFraction = 0.42f;
            const int BottomAlpha = 176;

            public GradientBackdropWidget() {
                IsHitTestVisible = false;
                HorizontalAlignment = WidgetAlignment.Stretch;
                VerticalAlignment = WidgetAlignment.Stretch;
            }

            public override void MeasureOverride(Vector2 parentAvailableSize) {
                IsDrawRequired = true;
                DesiredSize = new Vector2(float.PositiveInfinity);
            }

            public override void Draw(DrawContext dc) {
                float bandHeight = ActualSize.Y * BandFraction;
                if (bandHeight <= 0f || ActualSize.X <= 0f) {
                    return;
                }
                Matrix m = GlobalTransform;
                float bandTop = ActualSize.Y - bandHeight;
                Vector2 topLeft = new(0f, bandTop);
                Vector2 topRight = new(ActualSize.X, bandTop);
                Vector2 bottomRight = ActualSize;
                Vector2 bottomLeft = new(0f, ActualSize.Y);
                Vector2.Transform(ref topLeft, ref m, out Vector2 p1);
                Vector2.Transform(ref topRight, ref m, out Vector2 p2);
                Vector2.Transform(ref bottomRight, ref m, out Vector2 p3);
                Vector2.Transform(ref bottomLeft, ref m, out Vector2 p4);
                Color top = new Color(0, 0, 0, 0) * GlobalColorTransform;
                Color bottom = new Color(0, 0, 0, BottomAlpha) * GlobalColorTransform;
                dc.PrimitivesRenderer2D
                    .FlatBatch(1, DepthStencilState.None)
                    .QueueQuad(p1, p2, p3, p4, 0f, top, top, bottom, bottom);
            }
        }
    }
}
