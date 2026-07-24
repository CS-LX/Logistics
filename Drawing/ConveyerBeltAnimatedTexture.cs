using Engine;
using Engine.Graphics;
using Game;

namespace Logistics {
    /// <summary>
    /// 输送带滚动贴图：底图 + 皮带条合成到固定 RenderTarget。
    /// RT 在首次取用时即创建，保证地形几何始终引用同一纹理对象，进世界即可滚动。
    /// </summary>
    public static class ConveyerBeltAnimatedTexture {
        public const string BaseTexturePath = "Textures/ConveyerBelt/ConveyerBelt";
        public const string BeltTexturePath = "Textures/ConveyerBelt/Belt";

        static Texture2D? m_base;
        static Texture2D? m_beltStrip;
        static RenderTarget2D? m_animated;
        static readonly PrimitivesRenderer2D m_primitives = new();
        static float m_offset;
        static bool m_loaded;

        public static Texture2D BaseTexture {
            get {
                EnsureLoaded();
                return m_base!;
            }
        }

        /// <summary>地形与世界渲染统一用此 RT（内容每帧更新）。</summary>
        public static Texture2D Texture {
            get {
                EnsureLoaded();
                EnsureAnimatedTarget();
                return m_animated!;
            }
        }

        public static void EnsureLoaded() {
            if (m_loaded) {
                return;
            }
            m_base = ContentManager.Get<Texture2D>(BaseTexturePath);
            m_beltStrip = ContentManager.Get<Texture2D>(BeltTexturePath);
            m_loaded = true;
            EnsureAnimatedTarget();
        }

        public static void Update(float dt) {
            EnsureLoaded();
            m_offset += dt;
            if (m_offset >= 1f) {
                m_offset = 0f;
            }
        }

        public static void Draw() {
            EnsureLoaded();
            EnsureAnimatedTarget();
            int width = m_base!.Width;
            int height = m_base.Height;

            RenderTarget2D previous = Display.RenderTarget;
            try {
                Display.RenderTarget = m_animated;
                Display.Clear(new Vector4(Color.Transparent));
                TexturedBatch2D empty = m_primitives.TexturedBatch(
                    m_base,
                    useAlphaTest: false,
                    layer: 0,
                    DepthStencilState.None,
                    rasterizerState: null,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp);
                TexturedBatch2D belt = m_primitives.TexturedBatch(
                    m_beltStrip!,
                    useAlphaTest: false,
                    layer: 1,
                    DepthStencilState.None,
                    rasterizerState: null,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp);

                empty.QueueQuad(Vector2.Zero, new Vector2(width, height), 0, Vector2.Zero, Vector2.One, Color.White);

                float beltTop = height * m_offset * 0.5f;
                belt.QueueQuad(
                    new Vector2(width * 0.5f, beltTop + height * 0.25f),
                    new Vector2(width, height * 0.75f),
                    0,
                    Vector2.Zero,
                    new Vector2(1f, 1f - m_offset),
                    Color.White);
                if (beltTop != 0f) {
                    belt.QueueQuad(
                        new Vector2(width * 0.5f, height * 0.25f),
                        new Vector2(width, beltTop + height * 0.25f),
                        0,
                        new Vector2(0f, 1f - m_offset),
                        Vector2.One,
                        Color.White);
                }
                m_primitives.Flush();
            }
            finally {
                Display.RenderTarget = previous;
            }
        }

        static void EnsureAnimatedTarget() {
            if (m_animated != null || m_base == null) {
                return;
            }
            m_animated = new RenderTarget2D(m_base.Width, m_base.Height, 1, ColorFormat.Rgba8888, DepthFormat.None);
        }
    }
}
