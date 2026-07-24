using Engine;
using Engine.Graphics;
using Game;
using SCIENEW;

namespace Logistics {
    /// <summary>
    /// 输送带方块。Data：bit0-1 朝向 rotation(0..3)，bit2 形态 shape(0 平直 / 1 坡道)。
    /// 邻接自动对齐由 <see cref="SubsystemConveyerBeltBlockBehavior"/> 处理。
    /// </summary>
    public class ConveyerBeltBlock : Block {
        public const int Index = 553;
        public const int MeshCount = 8;

        public override bool IsIndexDynamic => false;

        static readonly BlockMesh[] m_blockMeshes = new BlockMesh[MeshCount];
        static readonly BoundingBox[][] m_collisionBoxes = new BoundingBox[MeshCount][];
        static BlockMesh m_standalone = new();
        static Texture2D? m_staticTexture;
        static bool m_meshesReady;

        public override void Initialize() {
            base.Initialize();
            BuildMeshes();
        }

        public override string GetCategory(int value) => IEConstants.BlockCategory.Devices;

        public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value)
            => LanguageControl.Get(nameof(ConveyerBeltBlock), "DisplayName");

        public override string GetDescription(int value)
            => LanguageControl.Get(nameof(ConveyerBeltBlock), "Description");

        public override string GetCraftingId(int value) => "conveyerbelt";

        public override bool IsFaceTransparent(SubsystemTerrain subsystemTerrain, int face, int value) => true;

        public override bool IsPlacementTransparent_(int value) => true;

        public override IEnumerable<int> GetCreativeValues() {
            yield return Terrain.MakeBlockValue(BlockIndex, 0, MakeData(shape: 0, rotation: 0));
        }

        public override void GetDropValues(
            SubsystemTerrain subsystemTerrain,
            int oldValue,
            int newValue,
            int toolLevel,
            List<BlockDropValue> dropValues,
            out bool showDebris) {
            showDebris = true;
            dropValues.Add(new BlockDropValue {
                Value = Terrain.MakeBlockValue(BlockIndex, 0, 0),
                Count = 1
            });
        }

        public override BoundingBox[] GetCustomCollisionBoxes(SubsystemTerrain terrain, int value) {
            int data = MeshIndex(Terrain.ExtractData(value));
            return m_collisionBoxes[data] ?? Block.m_defaultCollisionBoxes;
        }

        public override BoundingBox[] GetCustomInteractionBoxes(SubsystemTerrain terrain, int value)
            => GetCustomCollisionBoxes(terrain, value);

        public override BlockPlacementData GetPlacementValue(
            SubsystemTerrain subsystemTerrain,
            ComponentMiner componentMiner,
            int value,
            TerrainRaycastResult raycastResult) {
            Vector3 forward = componentMiner.ComponentCreature.ComponentCreatureModel.EyeRotation.GetForwardVector();
            int rotation = MathUtils.Abs(forward.X) > MathUtils.Abs(forward.Z) ? 1 : 0;
            return new BlockPlacementData {
                Value = Terrain.MakeBlockValue(BlockIndex, 0, MakeData(0, rotation)),
                CellFace = raycastResult.CellFace
            };
        }

        public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z) {
            int data = MeshIndex(Terrain.ExtractData(value));
            BlockMesh mesh = m_blockMeshes[data];
            if (mesh == null) {
                return;
            }
            generator.GenerateShadedMeshVertices(
                this,
                x,
                y,
                z,
                mesh,
                Color.White,
                null,
                null,
                geometry.GetGeometry(ConveyerBeltAnimatedTexture.Texture).SubsetAlphaTest);
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

        public static int GetRotation(int data) => data & 0b11;

        public static int GetShape(int data) => (data >> 2) & 1;

        public static int SetRotation(int data, int rotation) => (data & ~0b11) | (rotation & 0b11);

        public static int SetShape(int data, int shape) => (data & ~0b100) | ((shape & 1) << 2);

        public static int MakeData(int shape, int rotation) => SetShape(SetRotation(0, rotation), shape);

        public static int MeshIndex(int data) => MathUtils.Clamp(GetShape(data) * 4 + GetRotation(data), 0, MeshCount - 1);

        public static int RaisedNeighborIndexToRotation(int neighborIndex) => neighborIndex & 3;

        static void BuildMeshes() {
            if (m_meshesReady) {
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

            for (int shape = 0; shape < 2; shape++) {
                for (int rotation = 0; rotation < 4; rotation++) {
                    int index = shape * 4 + rotation;
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
                    // rotation 2/3（+Z / +X）相对模型默认 UV 差 180°，不翻则滚动与平直带相反
                    if ((rotation & 2) != 0) {
                        mesh.TransformTextureCoordinates(
                            Matrix.CreateTranslation(-0.25f, -0.5f, 0f)
                            * Matrix.CreateRotationZ(MathUtils.DegToRad(180f))
                            * Matrix.CreateTranslation(0.25f, 0.5f, 0f));
                    }
                    // 采样图集右半区（滚动合成区）
                    mesh.TransformTextureCoordinates(Matrix.CreateTranslation(0.5f, 0f, 0f));
                    m_blockMeshes[index] = mesh;
                    m_collisionBoxes[index] = shape == 0
                        ? [mesh.CalculateBoundingBox()]
                        : RiseCollisionBoxes(rotation);
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
    }
}
