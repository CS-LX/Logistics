using Game;
using GameEntitySystem;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>
    /// 驱动输送带外观滚动贴图。
    /// 仅在 Update 合成 RT（对齐宿主水/岩浆动画）；不实现 IDrawable，避免与地形 DrawOrder 0 抢 RenderTarget。
    /// </summary>
    public class SubsystemConveyerBeltVisuals : Subsystem, IUpdateable {
        SubsystemBeltGroups m_subsystemBeltGroups;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemBeltGroups = Project.FindSubsystem<SubsystemBeltGroups>(throwOnError: true);
            ConveyerBeltAnimatedTexture.EnsureLoaded();
        }

        public void Update(float dt) {
            ConveyerBeltAnimatedTexture.Update(dt, m_subsystemBeltGroups.AnyGroupRunning());
        }
    }
}
