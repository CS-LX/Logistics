using Engine;
using Engine.Graphics;
using Game;
using SCIENEW;

namespace Logistics {
    /// <summary>
    /// 物流料斗：同一 Index，Data 区分机型与朝向。
    /// Data：bit0-2 朝向(0..5)，bit3 机型(0=受料斗 Input，1=落料斗 Output)。
    /// 模型 <c>Models/Hopper</c>：mesh <c>Input</c> / <c>Output</c>。
    /// </summary>
    public class LogisticsHopperBlock : Block, IRotatableDevice, IElectricDrillRemovable, IPreferPlacement {
        public static int Index = 554;
        public override bool IsIndexDynamic => false;

        public const int FacingMask = 0b111;
        public const int TypeShift = 3;
        public const int TypeMask = 0b01000;

        const string ModelPath = "Models/Hopper";

        static readonly BlockMesh[][] m_meshesByVariantFace = [new BlockMesh[6], new BlockMesh[6]];
        static readonly BoundingBox[][][] m_collisionByVariantFace = [new BoundingBox[6][], new BoundingBox[6][]];
        static readonly BlockMesh[] m_standaloneByVariant = [new BlockMesh(), new BlockMesh()];

        public override void Initialize() {
            base.Initialize();
            // PostProcessBlocksLoad 可能跑两次；每次重建，避免 standalone Append 叠层。
            m_meshesByVariantFace[0] = new BlockMesh[6];
            m_meshesByVariantFace[1] = new BlockMesh[6];
            m_collisionByVariantFace[0] = new BoundingBox[6][];
            m_collisionByVariantFace[1] = new BoundingBox[6][];
            m_standaloneByVariant[0] = new BlockMesh();
            m_standaloneByVariant[1] = new BlockMesh();
            BuildMeshes(LogisticsHopperVariant.Input, "Input");
            BuildMeshes(LogisticsHopperVariant.Output, "Output");
        }

        public static int GetFacing(int value) => Terrain.ExtractData(value) & FacingMask;

        public static LogisticsHopperVariant GetVariant(int value)
            => ((Terrain.ExtractData(value) & TypeMask) >> TypeShift) == 1
                ? LogisticsHopperVariant.Output
                : LogisticsHopperVariant.Input;

        public static int MakeData(LogisticsHopperVariant variant, int facing)
            => ((int)variant << TypeShift) | (facing & FacingMask);

        public static int SetFacing(int value, int facing)
            => Terrain.ReplaceData(value, MakeData(GetVariant(value), facing));

        public override string GetCategory(int value) => IEConstants.BlockCategory.Devices;

        public override int GetDisplayOrder(int value)
            => GetVariant(value) == LogisticsHopperVariant.Input ? DisplayOrder : DisplayOrder + 1;

        public override bool IsFaceTransparent(SubsystemTerrain subsystemTerrain, int face, int value) => true;

        public override bool IsInteractive(SubsystemTerrain subsystemTerrain, int value) => true;

        public override bool IsPlacementTransparent_(int value) => true;

        public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value)
            => LanguageControl.Get(nameof(LogisticsHopperBlock), GetVariant(value) == LogisticsHopperVariant.Input ? "Input" : "Output");

        public override string GetDescription(int value)
            => LanguageControl.Get(
                nameof(LogisticsHopperBlock),
                GetVariant(value) == LogisticsHopperVariant.Input ? "DescriptionInput" : "DescriptionOutput");

        public override string GetCraftingId(int value)
            => GetVariant(value) == LogisticsHopperVariant.Input ? "logisticshopperinput" : "logisticshopperoutput";

        public override IEnumerable<int> GetCreativeValues() {
            // facing 须与掉落 / .ier Result Data 一致，否则图鉴 BlockItem.Match 对不上配方。
            yield return Terrain.MakeBlockValue(BlockIndex, 0, MakeData(LogisticsHopperVariant.Input, 0));
            yield return Terrain.MakeBlockValue(BlockIndex, 0, MakeData(LogisticsHopperVariant.Output, 0));
        }

        public override void GetDropValues(
            SubsystemTerrain subsystemTerrain,
            int oldValue,
            int newValue,
            int toolLevel,
            List<BlockDropValue> dropValues,
            out bool showDebris
        ) {
            showDebris = true;
            dropValues.Add(new BlockDropValue {
                Value = Terrain.MakeBlockValue(BlockIndex, 0, MakeData(GetVariant(oldValue), 0)),
                Count = 1
            });
        }

        public override BoundingBox[] GetCustomCollisionBoxes(SubsystemTerrain terrain, int value)
            => m_collisionByVariantFace[(int)GetVariant(value)][GetFacing(value)];

        public override int GetFaceTextureSlot(int face, int value) => 5;

        public override BlockDebrisParticleSystem CreateDebrisParticleSystem(
            SubsystemTerrain subsystemTerrain,
            Vector3 position,
            int value,
            float strength
        ) {
            return new BlockDebrisParticleSystem(
                subsystemTerrain,
                position,
                1,
                1,
                Color.White,
                GetFaceTextureSlot(0, value),
                LogisticsLoader.BlockTexture);
        }

        public override BlockPlacementData GetPlacementValue(
            SubsystemTerrain subsystemTerrain,
            ComponentMiner componentMiner,
            int value,
            TerrainRaycastResult raycastResult
        ) {
            // 朝向 = 所点 CellFace：大口贴点击面外侧、窄口朝向被贴方块（与 Camera/Lens 同）。
            int facing = raycastResult.CellFace.Face;
            return new BlockPlacementData {
                Value = Terrain.MakeBlockValue(BlockIndex, 0, MakeData(GetVariant(value), facing)),
                CellFace = raycastResult.CellFace
            };
        }

        public int GetNextDirection(int value, bool reverse = false) {
            int facing = GetFacing(value);
            facing = reverse ? (facing + 5) % 6 : (facing + 1) % 6;
            return SetFacing(value, facing);
        }

        public override void GenerateTerrainVertices(
            BlockGeometryGenerator generator,
            TerrainGeometry geometry,
            int value,
            int x,
            int y,
            int z
        ) {
            generator.GenerateShadedMeshVertices(
                this,
                x,
                y,
                z,
                m_meshesByVariantFace[(int)GetVariant(value)][GetFacing(value)],
                Color.White,
                null,
                null,
                geometry.GetGeometry(LogisticsLoader.BlockTexture).SubsetAlphaTest);
        }

        public override void DrawBlock(
            PrimitivesRenderer3D primitivesRenderer,
            int value,
            Color color,
            float size,
            ref Matrix matrix,
            DrawBlockEnvironmentData environmentData
        ) {
            BlocksManager.DrawMeshBlock(
                primitivesRenderer,
                m_standaloneByVariant[(int)GetVariant(value)],
                LogisticsLoader.BlockTexture,
                color,
                size,
                ref matrix,
                environmentData);
        }

        static void BuildMeshes(LogisticsHopperVariant variant, string meshName) {
            int variantIndex = (int)variant;
            Model model = ContentManager.Get<Model>(ModelPath);
            ModelMesh modelMesh = model.FindMesh(meshName);
            Matrix boneAbsoluteTransform = BlockMesh.GetBoneAbsoluteTransform(modelMesh.ParentBone);
            BlockMesh[] meshesByFace = m_meshesByVariantFace[variantIndex];
            BoundingBox[][] collisionByFace = m_collisionByVariantFace[variantIndex];
            for (int i = 0; i < 6; i++) {
                Matrix matrix = i >= 4
                    ? i != 4
                        ? Matrix.CreateRotationX((float)Math.PI) * Matrix.CreateTranslation(0.5f, 1f, 0.5f)
                        : Matrix.CreateTranslation(0.5f, 0f, 0.5f)
                    : Matrix.CreateRotationX((float)Math.PI / 2f)
                        * Matrix.CreateTranslation(0f, 0f, -0.5f)
                        * Matrix.CreateRotationY(i * (float)Math.PI / 2f)
                        * Matrix.CreateTranslation(0.5f, 0.5f, 0.5f);
                meshesByFace[i] = new BlockMesh();
                meshesByFace[i].AppendModelMeshPart(
                    modelMesh.MeshParts[0],
                    boneAbsoluteTransform * matrix,
                    makeEmissive: false,
                    flipWindingOrder: false,
                    doubleSided: false,
                    flipNormals: false,
                    Color.White);
                collisionByFace[i] = [meshesByFace[i].CalculateBoundingBox()];
            }
            // 创造栏 / 手持：面朝下（模型默认大口朝上）更易辨认。
            m_standaloneByVariant[variantIndex].AppendBlockMesh(meshesByFace[4]);
            m_standaloneByVariant[variantIndex].TransformPositions(Matrix.CreateTranslation(-0.5f, -0.5f, -0.5f));
        }
    }

    public enum LogisticsHopperVariant {
        /// <summary>受料斗（模型 mesh Input）。</summary>
        Input = 0,
        /// <summary>落料斗（模型 mesh Output）。</summary>
        Output = 1
    }
}
