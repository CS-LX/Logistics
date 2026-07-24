using Engine;
using Engine.Graphics;
using Game;

namespace Logistics {
    /// <summary>工业时代2：物流 — 模组入口。</summary>
    public class LogisticsLoader : ModLoader {
        static Texture2D? m_blockTexture;

        /// <summary>方块图集 <c>Assets/Logistics.png</c>。</summary>
        public static Texture2D BlockTexture => m_blockTexture ?? ContentManager.Get<Texture2D>("Logistics");

        public override void __ModInitialize() {
            base.__ModInitialize();
            ModsManager.RegisterHook("OnLoadingFinished", this);
            Log.Information("[Logistics] 工业时代2：物流 initialized.");
        }

        public override void OnLoadingFinished(List<Action> actions) {
            m_blockTexture = ContentManager.Get<Texture2D>("Logistics");
            StorageUnitSeamlessTextures.Init();
        }
    }
}
