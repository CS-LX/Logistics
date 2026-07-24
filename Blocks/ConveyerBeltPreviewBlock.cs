using Engine;
using Engine.Graphics;
using Game;
using SCIENEW;

namespace Logistics {
    /// <summary>
    /// 输送带外观预览方块：模型 + 滚动贴图，无运物/电力/交互业务。
    /// Data：bit0 reverse，bit1-2 rotation，bit4 shape（平直/坡道）；始终采样「通电」UV 半区以显示滚动。
    /// </summary>
    public class ConveyerBeltPreviewBlock : Block {
        public const int Index = 553;
        public const int MeshCount = 32;

        public override bool IsIndexDynamic => false;

        static readonly BlockMesh[] m_blockMeshes = new BlockMesh[MeshCount];
        static readonly BoundingBox[][] m_collisionBoxes = new BoundingBox[MeshCount][];
        static BlockMesh m_standalone = new();
        static Texture2D? m_staticTexture;
        static bool m_meshesReady;

        public static readonly string[] ShapeName = ["平直", "坡道"];
        public static readonly string[] ReverseName = ["正向", "反向"];
        public static readonly string[] RotationName = ["+0°", "+90°", "+180°", "+270°"];

        public override void Initialize() {
            base.Initialize();
            BuildMeshes();
        }

        public override string GetCategory(int value) => IEConstants.BlockCategory.Devices;

        public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value) {
            int data = Terrain.ExtractData(value);
            return LanguageControl.Get(nameof(ConveyerBeltPreviewBlock), "DisplayName")
                + $" ({ShapeName[GetShape(data)]},{ReverseName[GetReverse(data)]},{RotationName[GetRotation(data)]})";
        }

        public override string GetDescription(int value)
            => LanguageControl.Get(nameof(ConveyerBeltPreviewBlock), "Description");

        public override string GetCraftingId(int value) => "conveyerbeltpreview";

        public override bool IsFaceTransparent(SubsystemTerrain subsystemTerrain, int face, int value) => true;

        public override bool IsPlacementTransparent_(int value) => true;

        public override IEnumerable<int> GetCreativeValues() {
            // 与地形 mesh 索引一致：始终带「滚动 UV」半区（原 SA 的 power/enable 位）。
            yield return Terrain.MakeBlockValue(BlockIndex, 0, SetShape(SetPower(0, 1), 0));
            yield return Terrain.MakeBlockValue(BlockIndex, 0, SetShape(SetPower(0, 1), 1));
        }

        public override BoundingBox[] GetCustomCollisionBoxes(SubsystemTerrain terrain, int value) {
            int data = MeshData(Terrain.ExtractData(value));
            return m_collisionBoxes[data] ?? Block.m_defaultCollisionBoxes;
        }

        public override BoundingBox[] GetCustomInteractionBoxes(SubsystemTerrain terrain, int value)
            => GetCustomCollisionBoxes(terrain, value);

        public override BlockPlacementData GetPlacementValue(
            SubsystemTerrain subsystemTerrain,
            ComponentMiner componentMiner,
            int value,
            TerrainRaycastResult raycastResult) {
            int data = Terrain.ExtractData(value);
            int shape = GetShape(data);
            Vector3 forward = Matrix.CreateFromQuaternion(componentMiner.ComponentCreature.ComponentCreatureModel.EyeRotation).Forward;
            float angleX = MathUtils.Abs(GetAngle(forward, Vector3.UnitX));
            float angleXN = MathUtils.Abs(GetAngle(forward, -Vector3.UnitX));
            float angleZ = MathUtils.Abs(GetAngle(forward, Vector3.UnitZ));
            float angleZN = MathUtils.Abs(GetAngle(forward, -Vector3.UnitZ));
            float min = MathUtils.Min(angleX, angleXN, angleZ, angleZN);
            int rotation = 0;
            int reverse = 0;
            if (angleX == min) {
                rotation = 1;
                reverse = 1;
            }
            else if (angleXN == min) {
                rotation = 1;
            }
            else if (angleZ == min) {
                reverse = 1;
            }
            int newData = SetShape(SetRotation(SetReverse(SetPower(0, 1), reverse), rotation), shape);
            return new BlockPlacementData {
                Value = Terrain.MakeBlockValue(BlockIndex, 0, newData),
                CellFace = raycastResult.CellFace
            };
        }

        public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z) {
            int data = MeshData(Terrain.ExtractData(value));
            BlockMesh mesh = m_blockMeshes[data];
            if (mesh == null) {
                return;
            }
            Texture2D texture = ConveyerBeltAnimatedTexture.Texture;
            // 透明方块走 AlphaTest 子集，与高级抓取机自定义 mesh 一致。
            generator.GenerateShadedMeshVertices(
                this,
                x,
                y,
                z,
                mesh,
                Color.White,
                null,
                null,
                geometry.GetGeometry(texture).SubsetAlphaTest);
        }

        public override void DrawBlock(
            PrimitivesRenderer3D primitivesRenderer,
            int value,
            Color color,
            float size,
            ref Matrix matrix,
            DrawBlockEnvironmentData environmentData) {
            m_staticTexture ??= ConveyerBeltAnimatedTexture.BaseTexture;
            BlocksManager.DrawMeshBlock(primitivesRenderer, m_standalone, m_staticTexture, color, size, ref matrix, environmentData);
        }

        public override BlockDebrisParticleSystem CreateDebrisParticleSystem(
            SubsystemTerrain subsystemTerrain,
            Vector3 position,
            int value,
            float strength) {
            return new BlockDebrisParticleSystem(
                subsystemTerrain,
                position,
                strength,
                DestructionDebrisScale,
                Color.White,
                0,
                ConveyerBeltAnimatedTexture.BaseTexture);
        }

        static void BuildMeshes() {
            if (m_meshesReady) {
                // PostProcessBlocksLoad 可能二次进入：重建 standalone，避免叠层。
                for (int i = 0; i < MeshCount; i++) {
                    m_blockMeshes[i] = null!;
                    m_collisionBoxes[i] = null!;
                }
                m_standalone = new BlockMesh();
            }

            Model model = ContentManager.Get<Model>("Models/ConveyerBelt");
            Matrix boneAbsoluteTransform = BlockMesh.GetBoneAbsoluteTransform(model.FindMesh("Flat").ParentBone);
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

            // 始终写入 enable=1 的 UV（右半区滚动），预览不依赖电力位。
            const int enable = 1;
            for (int shape = 0; shape < 2; shape++) {
                for (int rotation = 0; rotation < 4; rotation++) {
                    for (int reverse = 0; reverse < 2; reverse++) {
                        int data = SetShape(SetRotation(SetReverse(SetPower(0, enable), reverse), rotation), shape);
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
                        if (reverse > 0) {
                            mesh.TransformTextureCoordinates(
                                Matrix.CreateTranslation(-0.25f, -0.5f, 0f)
                                * Matrix.CreateRotationZ(MathUtils.DegToRad(180f))
                                * Matrix.CreateTranslation(0.25f, 0.5f, 0f));
                        }
                        mesh.TransformTextureCoordinates(Matrix.CreateTranslation(0.5f, 0f, 0f));
                        m_blockMeshes[data] = mesh;
                        m_collisionBoxes[data] = shape == 0
                            ? [mesh.CalculateBoundingBox()]
                            : RiseCollisionBoxes(rotation);
                    }
                }
            }
            m_meshesReady = true;
            m_staticTexture = ContentManager.Get<Texture2D>(ConveyerBeltAnimatedTexture.BaseTexturePath);
        }

        static BoundingBox[] RiseCollisionBoxes(int rotation) => rotation switch {
            1 => [
                new BoundingBox(new Vector3(0.5f, 0f, 0f), new Vector3(1f, 0.5f, 1f)),
                new BoundingBox(new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 1f, 1f))
            ],
            2 => [
                new BoundingBox(new Vector3(0f, 0f, 0f), new Vector3(1f, 0.5f, 0.5f)),
                new BoundingBox(new Vector3(0f, 0.5f, 0.5f), new Vector3(1f, 1f, 1f))
            ],
            3 => [
                new BoundingBox(new Vector3(0f, 0f, 0f), new Vector3(0.5f, 0.5f, 1f)),
                new BoundingBox(new Vector3(0.5f, 0.5f, 0f), new Vector3(1f, 1f, 1f))
            ],
            _ => [
                new BoundingBox(new Vector3(0f, 0f, 0.5f), new Vector3(1f, 0.5f, 1f)),
                new BoundingBox(new Vector3(0f, 0.5f, 0f), new Vector3(1f, 1f, 0.5f))
            ]
        };

        static float GetAngle(Vector3 a, Vector3 b)
            => MathUtils.Acos(MathUtils.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f));

        /// <summary>mesh 只烘焙在 enable=1 槽位；旧存档/创造物 power=0 时也映射过去。</summary>
        static int MeshData(int data) => MathUtils.Clamp(SetPower(data, 1), 0, MeshCount - 1);

        static int Pow2(int n) => 1 << n;

        // data 布局与 SA 一致：shape | enable | rotation(2) | reverse
        public static int GetShape(int data) => (data - data % Pow2(4)) / Pow2(4);

        public static int GetPower(int data) => (data % Pow2(4) - data % Pow2(3)) / Pow2(3);

        public static int GetRotation(int data) => (data % Pow2(3) - data % 2) / 2;

        public static int GetReverse(int data) => data % 2;

        public static int SetShape(int data, int shape) => shape * Pow2(4) + data % Pow2(4);

        public static int SetPower(int data, int enable) => data - data % Pow2(4) + enable * Pow2(3) + data % Pow2(3);

        public static int SetRotation(int data, int rotation) => data - data % Pow2(3) + rotation * 2 + data % 2;

        public static int SetReverse(int data, int reverse) => data - data % 2 + reverse;
    }
}
