using Engine;
using Engine.Graphics;
using Engine.Input;
using Engine.Media;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>
    /// 输送带视觉侧：滚动贴图合成、在途物绘制、编组调试绘制。只读 <see cref="SubsystemBeltGroups"/> 的状态，不改仿真。
    /// 滚动贴图**只在 Update 合成**（对齐宿主水/岩浆动画）；Draw 里绝不碰 RenderTarget，否则会与地形 DrawOrder 0 抢绑定。
    /// </summary>
    public class SubsystemConveyerBeltVisuals : Subsystem, IUpdateable, IDrawable {
        readonly PrimitivesRenderer3D m_primitivesRenderer3D = new();
        readonly PrimitivesRenderer3D m_itemPrimitivesRenderer3D = new();
        readonly DrawBlockEnvironmentData m_drawBlockEnvironmentData = new();

        SubsystemBeltGroups m_subsystemBeltGroups;
        SubsystemTerrain m_subsystemTerrain;
        FlatBatch3D m_flatBatch;
        FontBatch3D m_textBatch;
        bool m_debugCanDraw;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public int[] DrawOrders => [10, 1000];

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemBeltGroups = Project.FindSubsystem<SubsystemBeltGroups>(throwOnError: true);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
            m_drawBlockEnvironmentData.SubsystemTerrain = m_subsystemTerrain;
            m_flatBatch = m_primitivesRenderer3D.FlatBatch(0, DepthStencilState.None);
            m_textBatch = m_primitivesRenderer3D.FontBatch(BitmapFont.DebugFont, 0, DepthStencilState.None);
            ConveyerBeltAnimatedTexture.EnsureLoaded();
        }

        public void Update(float dt) {
            // 开发者调试绘制：与 IE2 其它子系统的 F3–F5 同一套习惯
            if (Keyboard.IsKeyDownOnce(Key.F2)) {
                m_debugCanDraw = !m_debugCanDraw;
            }
            ConveyerBeltAnimatedTexture.Update(dt, m_subsystemBeltGroups.AnyGroupRunning());
        }

        public void Draw(Camera camera, int drawOrder) {
            if (drawOrder == 10) {
                DrawItems(camera);
                return;
            }
            if (drawOrder == 1000 && m_debugCanDraw) {
                DrawGroupDebug(camera);
            }
        }

        void DrawItems(Camera camera) {
            float visibility = SettingsManager.VisibilityRange;
            foreach (BeltGroup group in m_subsystemBeltGroups.Groups) {
                foreach (TransportedItem item in group.Inventory.Items) {
                    if (!BeltPath.TryGetWorldPose(group, item.BeltPosition, item.SideOffset, m_subsystemTerrain, out Vector3 pos, out _)) {
                        continue;
                    }
                    if (Vector3.Distance(pos, camera.ViewPosition) > visibility) {
                        continue;
                    }
                    Point3 cell = Terrain.ToCell(pos);
                    TerrainChunk chunk = m_subsystemTerrain.Terrain.GetChunkAtCell(cell.X, cell.Z);
                    if (chunk is { State: >= TerrainChunkState.InvalidVertices1 } && cell.Y is >= 0 and < 255) {
                        m_drawBlockEnvironmentData.Humidity = m_subsystemTerrain.Terrain.GetHumidity(cell.X, cell.Z);
                        m_drawBlockEnvironmentData.Temperature = m_subsystemTerrain.Terrain.GetTemperature(cell.X, cell.Z);
                        m_drawBlockEnvironmentData.Light = m_subsystemTerrain.Terrain.GetCellLightFast(cell.X, cell.Y, cell.Z);
                    }
                    m_drawBlockEnvironmentData.BillboardDirection = camera.ViewDirection;
                    var matrix = Matrix.CreateTranslation(pos);
                    Block block = BlocksManager.Blocks[Terrain.ExtractContents(item.Value)];
                    block.DrawBlock(
                        m_itemPrimitivesRenderer3D,
                        item.Value,
                        Color.White,
                        BeltPath.ItemDrawSize,
                        ref matrix,
                        m_drawBlockEnvironmentData);
                }
            }
            m_itemPrimitivesRenderer3D.Flush(camera.ViewProjectionMatrix);
        }

        void DrawGroupDebug(Camera camera) {
            int groupCount = m_subsystemBeltGroups.GroupCount;
            foreach (BeltGroup group in m_subsystemBeltGroups.Groups) {
                Color color = ColorForGuid(group.Id);
                Color lineColor = Color.Lerp(color, Color.White, 0.35f);
                for (int i = 0; i < group.Members.Count; i++) {
                    Point3 p = group.Members[i];
                    bool isController = p == group.Controller;
                    Color boxColor = isController ? Color.Yellow : color;
                    m_flatBatch.QueueBoundingBox(
                        new BoundingBox(new Vector3(p), new Vector3(p) + Vector3.One),
                        boxColor);
                    if (i + 1 < group.Members.Count) {
                        Vector3 a = new Vector3(p) + new Vector3(0.5f, 0.55f, 0.5f);
                        Vector3 b = new Vector3(group.Members[i + 1]) + new Vector3(0.5f, 0.55f, 0.5f);
                        m_flatBatch.QueueLine(a, b, lineColor, lineColor);
                    }
                }
                Vector3 textPos = new Vector3(group.Controller) + new Vector3(0.5f, 1.15f, 0.5f);
                Vector3 right = Vector3.Cross(camera.ViewDirection, Vector3.UnitY);
                if (right.LengthSquared() < 1e-6f) {
                    right = Vector3.UnitX;
                }
                right = Vector3.Normalize(right);
                Vector3 up = Vector3.Normalize(-Vector3.Cross(right, camera.ViewDirection));
                const float s = 0.006f;
                string shortId = group.Id.ToString("N")[..8];
                bool running = m_subsystemBeltGroups.IsGroupRunning(group);
                m_textBatch.QueueText(
                    $"{shortId}\n{group.Members.Count} cells\nSign={group.Sign} run={(running ? 1 : 0)}\ninv={group.Inventory.Count}\n{groupCount} groups",
                    textPos,
                    right * s,
                    up * s,
                    Color.White,
                    TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter,
                    Vector2.Zero);
            }
            m_primitivesRenderer3D.Flush(camera.ViewProjectionMatrix);
        }

        static Color ColorForGuid(Guid id) {
            int h = id.GetHashCode();
            byte r = (byte)(96 + ((h >> 0) & 0x7F));
            byte g = (byte)(96 + ((h >> 8) & 0x7F));
            byte b = (byte)(96 + ((h >> 16) & 0x7F));
            return new Color(r, g, b);
        }
    }
}
