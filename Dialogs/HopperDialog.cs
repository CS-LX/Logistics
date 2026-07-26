using Engine;
using Engine.Graphics;
using Game;

namespace Logistics {
    /// <summary>
    /// 料斗透视对话框：落料可调间隔与抽槽模式；受料仅状态。风格对齐 <see cref="ConveyerBeltDirectionDialog"/>。
    /// </summary>
    public class HopperDialog : Dialog {
        readonly ComponentLogisticsHopper m_hopper;
        readonly Point3 m_cell;
        readonly LabelWidget m_titleLabel;
        readonly CheckboxWidget m_enabledCheckbox;
        readonly LabelWidget m_statusLabel;
        readonly BevelledButtonWidget m_modeButton;
        readonly SliderWidget m_intervalSlider;
        readonly LabelWidget m_intervalValueLabel;
        readonly StackPanelWidget m_dischargeControls;
        readonly BevelledButtonWidget m_closeButton;
        bool m_isDischarge;

        public HopperDialog(ComponentLogisticsHopper hopper, Point3 cell) {
            m_hopper = hopper;
            m_cell = cell;
            m_titleLabel = new LabelWidget {
                Color = Color.White,
                DropShadow = true,
                VerticalAlignment = WidgetAlignment.Center,
                TextAnchor = TextAnchor.VerticalCenter,
                Margin = new Vector2(0f, 4f)
            };
            m_enabledCheckbox = new CheckboxWidget {
                Text = LanguageControl.GetContentWidgets(nameof(HopperDialog), "Enabled"),
                CheckboxSize = new Vector2(24f, 24f),
                VerticalAlignment = WidgetAlignment.Center,
                Margin = new Vector2(12f, 4f)
            };
            var titleRow = new StackPanelWidget {
                Direction = LayoutDirection.Horizontal,
                HorizontalAlignment = WidgetAlignment.Center
            };
            titleRow.Children.Add(m_titleLabel);
            titleRow.Children.Add(m_enabledCheckbox);
            m_statusLabel = new LabelWidget {
                Color = new Color(220, 220, 220),
                DropShadow = true,
                HorizontalAlignment = WidgetAlignment.Center,
                TextAnchor = TextAnchor.HorizontalCenter,
                Margin = new Vector2(0f, 4f)
            };
            m_modeButton = new BevelledButtonWidget {
                Size = new Vector2(200f, 60f),
                Margin = new Vector2(12f, 8f),
                HorizontalAlignment = WidgetAlignment.Center
            };
            m_intervalSlider = new SliderWidget {
                MinValue = ComponentLogisticsHopper.MinIntervalSeconds,
                MaxValue = ComponentLogisticsHopper.MaxIntervalSeconds,
                Granularity = 0.05f,
                Value = hopper.IntervalSeconds,
                IsLabelVisible = false,
                Size = new Vector2(240f, 40f),
                Margin = new Vector2(8f, 4f),
                VerticalAlignment = WidgetAlignment.Center
            };
            m_intervalValueLabel = new LabelWidget {
                Color = new Color(200, 200, 200),
                DropShadow = true,
                HorizontalAlignment = WidgetAlignment.Center,
                VerticalAlignment = WidgetAlignment.Center,
                TextAnchor = TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter,
                Margin = new Vector2(4f, 2f),
                Size = new Vector2(80f, 40f)
            };
            var intervalRow = new StackPanelWidget {
                Direction = LayoutDirection.Horizontal,
                HorizontalAlignment = WidgetAlignment.Center
            };
            intervalRow.Children.Add(m_intervalSlider);
            intervalRow.Children.Add(m_intervalValueLabel);
            m_dischargeControls = new StackPanelWidget {
                Direction = LayoutDirection.Vertical,
                HorizontalAlignment = WidgetAlignment.Center
            };
            m_dischargeControls.Children.Add(m_modeButton);
            m_closeButton = new BevelledButtonWidget {
                Size = new Vector2(160f, 60f),
                Margin = new Vector2(20f, 8f),
                HorizontalAlignment = WidgetAlignment.Center,
                Text = LanguageControl.GetContentWidgets(nameof(HopperDialog), "Close")
            };
            var closeRow = new StackPanelWidget {
                Direction = LayoutDirection.Horizontal,
                HorizontalAlignment = WidgetAlignment.Center
            };
            closeRow.Children.Add(m_closeButton);
            var panel = new StackPanelWidget {
                Direction = LayoutDirection.Vertical,
                HorizontalAlignment = WidgetAlignment.Center,
                VerticalAlignment = WidgetAlignment.Far,
                Margin = new Vector2(0f, 40f)
            };
            panel.Children.Add(titleRow);
            panel.Children.Add(m_statusLabel);
            panel.Children.Add(m_dischargeControls);
            panel.Children.Add(intervalRow);
            panel.Children.Add(closeRow);
            Children.Add(new GradientBackdropWidget());
            Children.Add(panel);
            RefreshAll(forceSlider: true);
        }

        public override void Update() {
            if (DialogsManager.m_animationData.TryGetValue(this, out DialogsManager.AnimationData animationData)) {
                animationData.CoverWidget.FillColor = Color.Transparent;
            }
            if (Input.Cancel || Input.Back || m_closeButton.IsClicked) {
                DialogsManager.HideDialog(this);
                return;
            }
            if (!m_hopper.IsAddedToProject) {
                m_statusLabel.Text = LanguageControl.GetContentWidgets(nameof(HopperDialog), "Gone");
                m_modeButton.IsEnabled = false;
                m_intervalSlider.IsEnabled = false;
                m_enabledCheckbox.IsEnabled = false;
                return;
            }
            if (m_enabledCheckbox.IsClicked) {
                m_hopper.Enabled = !m_hopper.Enabled;
            }
            if (m_isDischarge && m_modeButton.IsClicked) {
                m_hopper.ExtractMode = NextMode(m_hopper.ExtractMode);
            }
            if (m_intervalSlider.IsSliding || MathF.Abs(m_intervalSlider.Value - m_hopper.IntervalSeconds) > 0.001f) {
                m_hopper.IntervalSeconds = m_intervalSlider.Value;
            }
            RefreshAll(forceSlider: false);
        }

        void RefreshAll(bool forceSlider) {
            SubsystemTerrain terrain = m_hopper.Project.FindSubsystem<SubsystemTerrain>(true);
            int value = terrain.Terrain.GetCellValue(m_cell.X, m_cell.Y, m_cell.Z);
            if (Terrain.ExtractContents(value) != LogisticsHopperBlock.Index) {
                m_statusLabel.Text = LanguageControl.GetContentWidgets(nameof(HopperDialog), "Gone");
                m_dischargeControls.IsVisible = false;
                m_modeButton.IsEnabled = false;
                m_intervalSlider.IsEnabled = false;
                m_enabledCheckbox.IsEnabled = false;
                return;
            }
            m_enabledCheckbox.IsEnabled = true;
            m_enabledCheckbox.IsChecked = m_hopper.Enabled;
            m_isDischarge = LogisticsHopperBlock.GetVariant(value) == LogisticsHopperVariant.Output;
            m_titleLabel.Text = LanguageControl.GetContentWidgets(
                nameof(HopperDialog),
                m_isDischarge ? "TitleOutput" : "TitleInput");
            m_dischargeControls.IsVisible = m_isDischarge;
            m_modeButton.IsEnabled = m_isDischarge;
            m_intervalSlider.IsEnabled = true;
            if (forceSlider || !m_intervalSlider.IsSliding) {
                m_intervalSlider.Value = m_hopper.IntervalSeconds;
            }
            m_intervalValueLabel.Text = string.Format(
                LanguageControl.GetContentWidgets(nameof(HopperDialog), "IntervalValue"),
                m_hopper.IntervalSeconds);
            if (m_isDischarge) {
                m_modeButton.Text = ModeLabel(m_hopper.ExtractMode);
            }
            string primary = m_isDischarge
                ? string.Format(
                    LanguageControl.GetContentWidgets(nameof(HopperDialog), "StatusOutput"),
                    ModeLabel(m_hopper.ExtractMode),
                    m_hopper.IntervalSeconds)
                : m_hopper.DescribeAttachedStatus();
            m_statusLabel.Text = $"{primary}\n{m_hopper.DescribeMouthStatus()}";
        }

        static HopperExtractMode NextMode(HopperExtractMode mode) => mode switch {
            HopperExtractMode.OutputPreferred => HopperExtractMode.OutputOnly,
            HopperExtractMode.OutputOnly => HopperExtractMode.EntireInventory,
            _ => HopperExtractMode.OutputPreferred
        };

        static string ModeLabel(HopperExtractMode mode) => LanguageControl.GetContentWidgets(
            nameof(HopperDialog),
            mode switch {
                HopperExtractMode.OutputOnly => "ModeOutputOnly",
                HopperExtractMode.EntireInventory => "ModeEntire",
                _ => "ModeOutputPreferred"
            });

        sealed class GradientBackdropWidget : Widget {
            const float BandFraction = 0.48f;
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
