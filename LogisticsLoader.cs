using Engine;
using Game;

namespace Logistics {
    /// <summary>工业时代2：物流 — 模组入口。</summary>
    public class LogisticsLoader : ModLoader {
        public override void __ModInitialize() {
            base.__ModInitialize();
            Log.Information("[Logistics] 工业时代2：物流 initialized.");
        }
    }
}
