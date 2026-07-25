using Engine;
using Engine.Graphics;
using Game;

namespace Logistics {
    /// <summary>
    /// 输送带外观的唯一来源：按 data 变体缓存的网格 + 世界几何 / 手持绘制入口。
    /// 其它方块想在外观上嵌入一段带子，只需在 GenerateTerrainVertices 里调
    /// <see cref="GenerateBeltVertices"/> 并传入 data（可选格内变换），不必自己加载模型，
    /// 也不必复刻 reverse/powered 的 UV 约定。
    /// </summary>
    public static class ConveyerBeltDrawingManager {
        public const string ModelPath = "Models/ConveyerBelt";

        static readonly BlockMesh[] m_meshes = new BlockMesh[ConveyerBeltBlock.DataVariantCount];
        static BlockMesh m_standalone = new();
        static bool m_ready;

        /// <summary>强制重建（模组重载 / 方块 Initialize）；常规调用走各入口的惰性构建即可。</summary>
        public static void Initialize() {
            m_ready = false;
            EnsureInitialized();
        }

        /// <summary>取某 data 变体的网格（碰撞盒等需要读几何时用）。</summary>
        public static BlockMesh GetMesh(int data) {
            EnsureInitialized();
            return m_meshes[ConveyerBeltBlock.MeshIndex(data)];
        }

        /// <summary>手持 / 物品栏用的整块网格（平直段，原点居中）。</summary>
        public static BlockMesh StandaloneMesh {
            get {
                EnsureInitialized();
                return m_standalone;
            }
        }

        /// <summary>
        /// 把一段输送带写进世界几何（滚动贴图子集，随全局相位动）。
        /// </summary>
        /// <param name="data">带子 data：rotation / shape / reverse / powered，构造见 <see cref="ConveyerBeltBlock.MakeData"/></param>
        /// <param name="transform">格内附加变换（嵌入时下沉、缩放、偏移等）；null 为与独立输送带同位</param>
        /// <param name="color">附加着色；null 为不染色</param>
        public static void GenerateBeltVertices(
            BlockGeometryGenerator generator,
            TerrainGeometry geometry,
            int x,
            int y,
            int z,
            int data,
            Matrix? transform = null,
            Color? color = null) {
            BlockMesh mesh = GetMesh(data);
            if (mesh == null) {
                return;
            }
            generator.GenerateShadedMeshVertices(
                BlocksManager.Blocks[ConveyerBeltBlock.Index],
                x,
                y,
                z,
                mesh,
                color ?? Color.White,
                transform,
                null,
                geometry.GetGeometry(ConveyerBeltAnimatedTexture.Texture).SubsetAlphaTest);
        }

        /// <summary>非地形绘制（手持、物品栏、图鉴）：用静态底图，不参与滚动。</summary>
        public static void DrawBelt(
            PrimitivesRenderer3D primitivesRenderer,
            Color color,
            float size,
            ref Matrix matrix,
            DrawBlockEnvironmentData environmentData) {
            BlocksManager.DrawMeshBlock(
                primitivesRenderer,
                StandaloneMesh,
                ConveyerBeltAnimatedTexture.BaseTexture,
                color,
                size,
                ref matrix,
                environmentData);
        }

        static void EnsureInitialized() {
            if (m_ready) {
                return;
            }
            BuildMeshes();
        }

        static void BuildMeshes() {
            Model model = ContentManager.Get<Model>(ModelPath);
            Matrix boneAbsoluteTransform = BlockMesh.GetBoneAbsoluteTransform(model.FindMesh("Flat").ParentBone);

            m_standalone = new BlockMesh();
            m_standalone.AppendModelMeshPart(
                model.FindMesh("Flat").MeshParts[0],
                boneAbsoluteTransform * Matrix.CreateTranslation(0f, -0.5f, 0f),
                makeEmissive: false,
                flipWindingOrder: false,
                doubleSided: false,
                flipNormals: false,
                Color.White);
            m_standalone.AppendModelMeshPart(
                model.FindMesh("Belt_Flat").MeshParts[0],
                boneAbsoluteTransform * Matrix.CreateTranslation(0f, -0.5f, 0f),
                makeEmissive: false,
                flipWindingOrder: false,
                doubleSided: false,
                flipNormals: false,
                Color.White);

            for (int powered = 0; powered < 2; powered++) {
                for (int reverse = 0; reverse < 2; reverse++) {
                    for (int shape = 0; shape < 2; shape++) {
                        for (int rotation = 0; rotation < 4; rotation++) {
                            int index = ConveyerBeltBlock.MeshIndex(ConveyerBeltBlock.MakeData(shape, rotation, reverse, powered));
                            var mesh = new BlockMesh();
                            string shapeName = shape == 0 ? "Flat" : "Rise";
                            Matrix transform = boneAbsoluteTransform
                                * Matrix.CreateRotationY(MathUtils.DegToRad(90f) * rotation)
                                * Matrix.CreateTranslation(0.5f, 0f, 0.5f);
                            mesh.AppendModelMeshPart(
                                model.FindMesh(shapeName).MeshParts[0],
                                transform,
                                makeEmissive: false,
                                flipWindingOrder: false,
                                doubleSided: false,
                                flipNormals: false,
                                Color.White);
                            mesh.AppendModelMeshPart(
                                model.FindMesh("Belt_" + shapeName).MeshParts[0],
                                transform,
                                makeEmissive: false,
                                flipWindingOrder: false,
                                doubleSided: false,
                                flipNormals: false,
                                Color.White);
                            // rotation 2/3 与 reverse 各需 180° UV；两者同时则抵消（对齐 SA + 本模组朝向修正）
                            bool flipUv = ((rotation & 2) != 0) ^ (reverse != 0);
                            if (flipUv) {
                                mesh.TransformTextureCoordinates(
                                    Matrix.CreateTranslation(-0.25f, -0.5f, 0f)
                                    * Matrix.CreateRotationZ(MathUtils.DegToRad(180f))
                                    * Matrix.CreateTranslation(0.25f, 0.5f, 0f));
                            }
                            if (powered != 0) {
                                mesh.TransformTextureCoordinates(Matrix.CreateTranslation(0.5f, 0f, 0f));
                            }
                            m_meshes[index] = mesh;
                        }
                    }
                }
            }
            m_ready = true;
        }
    }
}
