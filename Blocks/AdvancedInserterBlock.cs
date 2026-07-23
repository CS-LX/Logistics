using Engine;
using Engine.Graphics;
using Game;
using SCIENEW;

namespace Logistics {
    /// <summary>
    /// 高级抓取机 / 高级抓取阀：同一 Index，Data 区分机型与朝向。
    /// Data：bit0–2 朝向(0..5)，bit3 机型(0=抓取机，1=抓取阀)。
    /// </summary>
    public class AdvancedInserterBlock : Block, IElectricElementBlock, IRotatableDevice {
        public const int Index = 551;

        public const int FacingMask = 0b111;
        public const int FluidTypeBit = 0b1000;

        BlockMesh[] m_inserterMeshesByFace = new BlockMesh[6];
        BlockMesh[] m_fluidMeshesByFace = new BlockMesh[6];
        BoundingBox[][] m_inserterCollisionByFace = new BoundingBox[6][];
        BoundingBox[][] m_fluidCollisionByFace = new BoundingBox[6][];
        BlockMesh m_inserterStandalone = new();
        BlockMesh m_fluidStandalone = new();
        Texture2D m_texture;

        public override void Initialize() {
            base.Initialize();
            m_texture = ContentManager.Get<Texture2D>("Logistics");
            BuildMeshes("Models/AdvancedInserter", "Inserter", m_inserterMeshesByFace, m_inserterCollisionByFace, m_inserterStandalone);
            BuildMeshes("Models/AdvancedFluidInserter", "Inserter", m_fluidMeshesByFace, m_fluidCollisionByFace, m_fluidStandalone);
        }

        static void BuildMeshes(string modelPath, string meshName, BlockMesh[] meshesByFace, BoundingBox[][] collisionByFace, BlockMesh standalone) {
            Model model = ContentManager.Get<Model>(modelPath);
            Matrix boneAbsoluteTransform = BlockMesh.GetBoneAbsoluteTransform(model.FindMesh(meshName).ParentBone);
            for (int i = 0; i < 6; i++) {
                Matrix matrix = (i >= 4)
                    ? ((i != 4)
                        ? (Matrix.CreateRotationX((float)Math.PI) * Matrix.CreateTranslation(0.5f, 1f, 0.5f))
                        : Matrix.CreateTranslation(0.5f, 0f, 0.5f))
                    : (Matrix.CreateRotationX((float)Math.PI / 2f)
                        * Matrix.CreateTranslation(0f, 0f, -0.5f)
                        * Matrix.CreateRotationY(i * (float)Math.PI / 2f)
                        * Matrix.CreateTranslation(0.5f, 0.5f, 0.5f));
                meshesByFace[i] = new BlockMesh();
                meshesByFace[i].AppendModelMeshPart(
                    model.FindMesh(meshName).MeshParts[0],
                    boneAbsoluteTransform * matrix,
                    makeEmissive: false,
                    flipWindingOrder: false,
                    doubleSided: false,
                    flipNormals: false,
                    Color.White);
                collisionByFace[i] = [meshesByFace[i].CalculateBoundingBox()];
            }
            // 与 IE2 Inserter/FluidInserter（useStandaloneMesh: false）一致：物品栏用 face0 水平朝向。
            standalone.AppendBlockMesh(meshesByFace[0]);
            standalone.TransformPositions(Matrix.CreateTranslation(-0.5f, -0.5f, -0.5f));
        }

        public static int GetFacing(int value) => Terrain.ExtractData(value) & FacingMask;

        public static bool IsFluid(int value) => (Terrain.ExtractData(value) & FluidTypeBit) != 0;

        public static int MakeData(bool fluid, int facing) => (fluid ? FluidTypeBit : 0) | (facing & FacingMask);

        public static int SetFacing(int value, int facing)
            => Terrain.ReplaceData(value, MakeData(IsFluid(value), facing));

        public override string GetCategory(int value) => IEConstants.BlockCategory.Devices;

        public override bool IsInteractive(SubsystemTerrain subsystemTerrain, int value) => true;

        public override bool IsFaceTransparent(SubsystemTerrain subsystemTerrain, int face, int value) => true;

        public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value)
            => LanguageControl.Get(nameof(AdvancedInserterBlock), IsFluid(value) ? "FluidInserter" : "Inserter");

        public override string GetDescription(int value)
            => LanguageControl.Get(nameof(AdvancedInserterBlock), IsFluid(value) ? "DescriptionFluidInserter" : "DescriptionInserter");

        public override string GetCraftingId(int value)
            => IsFluid(value) ? "advancedfluidinserter" : "advancedinserter";

        public override IEnumerable<int> GetCreativeValues() {
            yield return Terrain.MakeBlockValue(BlockIndex, 0, MakeData(fluid: false, facing: 0));
            yield return Terrain.MakeBlockValue(BlockIndex, 0, MakeData(fluid: true, facing: 0));
        }

        public override void GetDropValues(SubsystemTerrain subsystemTerrain, int oldValue, int newValue, int toolLevel, List<BlockDropValue> dropValues, out bool showDebris) {
            showDebris = true;
            dropValues.Add(new BlockDropValue {
                Value = Terrain.MakeBlockValue(BlockIndex, 0, MakeData(IsFluid(oldValue), 0)),
                Count = 1
            });
        }

        public override BoundingBox[] GetCustomCollisionBoxes(SubsystemTerrain terrain, int value) {
            int facing = GetFacing(value);
            return IsFluid(value) ? m_fluidCollisionByFace[facing] : m_inserterCollisionByFace[facing];
        }

        public override int GetFaceTextureSlot(int face, int value) => 5;

        public override BlockDebrisParticleSystem CreateDebrisParticleSystem(SubsystemTerrain subsystemTerrain, Vector3 position, int value, float strength) {
            return new BlockDebrisParticleSystem(
                subsystemTerrain,
                position,
                1,
                1,
                Color.White,
                GetFaceTextureSlot(0, value),
                m_texture);
        }

        public override BlockPlacementData GetPlacementValue(SubsystemTerrain subsystemTerrain, ComponentMiner componentMiner, int value, TerrainRaycastResult raycastResult) {
            int facing = DevicesUtils.GetDirectionXYZ(componentMiner, true);
            return new BlockPlacementData {
                Value = Terrain.MakeBlockValue(BlockIndex, 0, MakeData(IsFluid(value), facing)),
                CellFace = raycastResult.CellFace
            };
        }

        public int GetNextDirection(int value, bool reverse = false) {
            int facing = GetFacing(value);
            facing = reverse ? (facing + 5) % 6 : (facing + 1) % 6;
            return SetFacing(value, facing);
        }

        public ElectricElement CreateElectricElement(SubsystemElectricity subsystemElectricity, int value, int x, int y, int z) {
            var point = new Point3(x, y, z);
            return IsFluid(value)
                ? new AdvancedFluidInserterElectricElement(subsystemElectricity, point)
                : new AdvancedInserterElectricElement(subsystemElectricity, point);
        }

        public ElectricConnectorType? GetConnectorType(SubsystemTerrain terrain, int value, int face, int connectorFace, int x, int y, int z)
            => ElectricConnectorType.Input;

        public int GetConnectionMask(int value) => int.MaxValue;

        public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z) {
            BlockMesh mesh = IsFluid(value) ? m_fluidMeshesByFace[GetFacing(value)] : m_inserterMeshesByFace[GetFacing(value)];
            generator.GenerateShadedMeshVertices(
                this,
                x,
                y,
                z,
                mesh,
                Color.White,
                null,
                null,
                geometry.GetGeometry(m_texture).SubsetAlphaTest);
            if (IsFluid(value)) {
                for (int i = 0; i < 5; i++) {
                    generator.GenerateWireVertices(value, x, y, z, i, 0, Vector2.Zero, geometry.SubsetOpaque);
                }
            }
        }

        public override void DrawBlock(PrimitivesRenderer3D primitivesRenderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData environmentData) {
            BlockMesh mesh = IsFluid(value) ? m_fluidStandalone : m_inserterStandalone;
            BlocksManager.DrawMeshBlock(
                primitivesRenderer,
                mesh,
                m_texture,
                color,
                size,
                ref matrix,
                environmentData);
        }
    }
}
