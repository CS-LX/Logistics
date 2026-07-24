using Game;
using GameEntitySystem;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>仅驱动输送带外观滚动贴图；无物品/电力/联网逻辑。</summary>
    public class SubsystemConveyerBeltVisuals : Subsystem, IUpdateable, IDrawable {
        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public int[] DrawOrders => [0];

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            ConveyerBeltAnimatedTexture.EnsureLoaded();
        }

        public void Update(float dt) => ConveyerBeltAnimatedTexture.Update(dt);

        public void Draw(Camera camera, int drawOrder) => ConveyerBeltAnimatedTexture.Draw();
    }
}
