using Engine;
using Engine.Graphics;
using Game;

namespace Logistics {
    /// <summary>
    /// 输送带滚动贴图：底图 + 皮带条合成到固定 RenderTarget。
    /// 必须在 Update 阶段写入 RT（对齐宿主 SubsystemAnimatedTextures），禁止在 DrawOrder 0 与地形同帧抢 RT。
    /// </summary>
    public static class ConveyerBeltAnimatedTexture {
        public const string BaseTexturePath = "Textures/ConveyerBelt/ConveyerBelt";
        public const string BeltTexturePath = "Textures/ConveyerBelt/Belt";

        /// <summary>
        /// 贴图 UV 滚动极性。与 reverse=0（Sign=+1）mesh 搭配，使视觉与弧长增大同向。
        /// </summary>
        public const float ScrollPolarity = -1f;

        static Texture2D? m_base;
        static Texture2D? m_beltStrip;
        static RenderTarget2D? m_animated;
        static readonly PrimitivesRenderer2D m_primitives = new();
        static float m_offset;
        static bool m_loaded;
        static bool m_deviceResetHooked;

        public static Texture2D BaseTexture {
            get {
                EnsureLoaded();
                return m_base!;
            }
        }

        /// <summary>地形采样用：优先 RT；未就绪时回退底图，避免 GetGeometry(null) 崩 chunk。</summary>
        public static Texture2D Texture {
            get {
                EnsureLoaded();
                EnsureAnimatedTarget();
                return m_animated ?? m_base!;
            }
        }

        public static void EnsureLoaded() {
            if (m_loaded) {
                return;
            }
            m_base = ContentManager.Get<Texture2D>(BaseTexturePath);
            m_beltStrip = ContentManager.Get<Texture2D>(BeltTexturePath);
            m_loaded = true;
            HookDeviceReset();
            EnsureAnimatedTarget();
        }

        /// <param name="running">任一输送带运转则滚；否则冻结相位（powered=0 格已采静区）。</param>
        public static void Update(float dt, bool running) {
            EnsureLoaded();
            if (dt > 0f && running) {
                m_offset += dt * ScrollPolarity;
                while (m_offset >= 1f) {
                    m_offset -= 1f;
                }
                while (m_offset < 0f) {
                    m_offset += 1f;
                }
            }
            // 在 Update 合成 RT，供随后地形 Draw 采样（勿放到 IDrawable.Draw）
            Compose();
        }

        static void Compose() {
            EnsureAnimatedTarget();
            if (m_animated == null || m_base == null || m_beltStrip == null) {
                return;
            }
            int width = m_base.Width;
            int height = m_base.Height;
            RenderTarget2D previous = Display.RenderTarget;
            Rectangle scissor = Display.ScissorRectangle;
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
                    m_beltStrip,
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
                Display.ScissorRectangle = scissor;
            }
        }

        static void EnsureAnimatedTarget() {
            if (m_animated != null || m_base == null) {
                return;
            }
            m_animated = new RenderTarget2D(m_base.Width, m_base.Height, 1, ColorFormat.Rgba8888, DepthFormat.None);
        }

        static void HookDeviceReset() {
            if (m_deviceResetHooked) {
                return;
            }
            Display.DeviceReset += () => {
                Utilities.Dispose(ref m_animated);
                m_animated = null;
            };
            m_deviceResetHooked = true;
        }
    }
}
