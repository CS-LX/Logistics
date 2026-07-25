using Engine;
using Engine.Graphics;
using Game;

namespace Logistics {
    /// <summary>
    /// 输送带调向：只在屏幕下方摆状态与按钮，不铺面板底，打开时仍能看见带面滚动与在途物。
    /// </summary>
    public class ConveyerBeltDirectionDialog : Dialog {
        /// <summary>宿主对话框会铺一层近黑遮罩；调向要对照带面，故压到几乎透明。</summary>
        static readonly Color CoverColor = new(0, 0, 0, 32);

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
            Children.Add(panel);
            RefreshStatus();
        }

        public override void Update() {
            if (DialogsManager.m_animationData.TryGetValue(this, out DialogsManager.AnimationData animationData)) {
                animationData.CoverWidget.FillColor = CoverColor;
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
    }
}
