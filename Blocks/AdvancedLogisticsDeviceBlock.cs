using Engine;
using Engine.Graphics;
using Game;
using GameEntitySystem;
using SCIENEW;

namespace Logistics {
    /// <summary>
    /// 高级物流设备：同一 Index，Data 区分机型与朝向。
    /// Data：bit0-2 朝向(0..5)，bit3-4 机型(0=抓取机，1=抓取阀，2=分拣机)。
    /// </summary>
    public class AdvancedLogisticsDeviceBlock : Block, IElectricElementBlock, IRotatableDevice {
        public const int Index = 551;

        public const int FacingMask = 0b111;
        public const int TypeShift = 3;
        public const int TypeMask = 0b11000;
        public const int FluidTypeBit = 0b01000;

        static readonly AdvancedLogisticsDeviceDefinition[] m_definitions = [
            new AdvancedItemInserterDefinition(),
            new AdvancedFluidInserterDefinition(),
            new AdvancedSorterDefinition()
        ];

        static readonly AdvancedLogisticsDeviceDefinition[] m_creativeDefinitions = [
            m_definitions[(int)AdvancedLogisticsDeviceVariant.Sorter],
            m_definitions[(int)AdvancedLogisticsDeviceVariant.ItemInserter],
            m_definitions[(int)AdvancedLogisticsDeviceVariant.FluidInserter]
        ];

        public override void Initialize() {
            base.Initialize();
            foreach (AdvancedLogisticsDeviceDefinition definition in m_definitions) {
                definition.Initialize();
            }
        }

        public static int GetFacing(int value) => Terrain.ExtractData(value) & FacingMask;

        public static AdvancedLogisticsDeviceVariant GetVariant(int value) {
            int type = (Terrain.ExtractData(value) & TypeMask) >> TypeShift;
            return type switch {
                1 => AdvancedLogisticsDeviceVariant.FluidInserter,
                2 => AdvancedLogisticsDeviceVariant.Sorter,
                _ => AdvancedLogisticsDeviceVariant.ItemInserter
            };
        }

        public static bool IsFluid(int value) => GetVariant(value) == AdvancedLogisticsDeviceVariant.FluidInserter;

        public static bool IsSorter(int value) => GetVariant(value) == AdvancedLogisticsDeviceVariant.Sorter;

        public static int MakeData(bool fluid, int facing) => (fluid ? FluidTypeBit : 0) | (facing & FacingMask);

        public static int MakeData(AdvancedLogisticsDeviceVariant variant, int facing) => ((int)variant << TypeShift) | (facing & FacingMask);

        public static int SetFacing(int value, int facing)
            => Terrain.ReplaceData(value, MakeData(GetVariant(value), facing));

        internal static AdvancedLogisticsDeviceDefinition GetDefinition(int value)
            => GetDefinition(GetVariant(value));

        internal static AdvancedLogisticsDeviceDefinition GetDefinition(AdvancedLogisticsDeviceVariant variant)
            => m_definitions[(int)variant];

        public override string GetCategory(int value) => GetDefinition(value).GetCategory(value);

        public override int GetDisplayOrder(int value) => GetDefinition(value).GetDisplayOrder(this, value);

        public override bool IsInteractive(SubsystemTerrain subsystemTerrain, int value)
            => GetDefinition(value).IsInteractive(subsystemTerrain, value);

        public override bool IsFaceTransparent(SubsystemTerrain subsystemTerrain, int face, int value)
            => GetDefinition(value).IsFaceTransparent(subsystemTerrain, face, value);

        public override bool IsPlacementTransparent_(int value)
            => GetDefinition(value).IsPlacementTransparent(value);

        public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value)
            => GetDefinition(value).GetDisplayName(subsystemTerrain, value);

        public override string GetDescription(int value)
            => GetDefinition(value).GetDescription(value);

        public override string GetCraftingId(int value)
            => GetDefinition(value).CraftingId;

        public override IEnumerable<int> GetCreativeValues() {
            foreach (AdvancedLogisticsDeviceDefinition definition in m_creativeDefinitions) {
                yield return Terrain.MakeBlockValue(BlockIndex, 0, MakeData(definition.Variant, definition.CreativeFacing));
            }
        }

        public override void GetDropValues(SubsystemTerrain subsystemTerrain, int oldValue, int newValue, int toolLevel, List<BlockDropValue> dropValues, out bool showDebris)
            => GetDefinition(oldValue).GetDropValues(this, oldValue, dropValues, out showDebris);

        public override BoundingBox[] GetCustomCollisionBoxes(SubsystemTerrain terrain, int value)
            => GetDefinition(value).GetCustomCollisionBoxes(terrain, value);

        public override int GetFaceTextureSlot(int face, int value)
            => GetDefinition(value).GetFaceTextureSlot(face, value);

        public override BlockDebrisParticleSystem CreateDebrisParticleSystem(SubsystemTerrain subsystemTerrain, Vector3 position, int value, float strength)
            => GetDefinition(value).CreateDebrisParticleSystem(subsystemTerrain, position, value, strength);

        public override BlockPlacementData GetPlacementValue(SubsystemTerrain subsystemTerrain, ComponentMiner componentMiner, int value, TerrainRaycastResult raycastResult)
            => GetDefinition(value).GetPlacementValue(this, subsystemTerrain, componentMiner, value, raycastResult);

        public int GetNextDirection(int value, bool reverse = false)
            => GetDefinition(value).GetNextDirection(value, reverse);

        public ElectricElement CreateElectricElement(SubsystemElectricity subsystemElectricity, int value, int x, int y, int z)
            => GetDefinition(value).CreateElectricElement(subsystemElectricity, value, x, y, z);

        public ElectricConnectorType? GetConnectorType(SubsystemTerrain terrain, int value, int face, int connectorFace, int x, int y, int z)
            => GetDefinition(value).GetConnectorType(terrain, value, face, connectorFace, x, y, z);

        public int GetConnectionMask(int value)
            => GetDefinition(value).GetConnectionMask(value);

        public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z)
            => GetDefinition(value).GenerateTerrainVertices(this, generator, geometry, value, x, y, z);

        public override void DrawBlock(PrimitivesRenderer3D primitivesRenderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData environmentData)
            => GetDefinition(value).DrawBlock(primitivesRenderer, value, color, size, ref matrix, environmentData);
    }

    public enum AdvancedLogisticsDeviceVariant {
        ItemInserter = 0,
        FluidInserter = 1,
        Sorter = 2
    }

    abstract class AdvancedLogisticsDeviceDefinition {
        public abstract AdvancedLogisticsDeviceVariant Variant { get; }

        public abstract string DisplayNameKey { get; }

        public abstract string DescriptionKey { get; }

        public abstract string CraftingId { get; }

        public virtual int CreativeFacing => 0;

        public virtual string? EntityName => null;

        public virtual void Initialize() {
        }

        public virtual string GetCategory(int value) => IEConstants.BlockCategory.Devices;

        public virtual int GetDisplayOrder(AdvancedLogisticsDeviceBlock block, int value) => block.DisplayOrder;

        public virtual bool IsInteractive(SubsystemTerrain subsystemTerrain, int value) => false;

        public virtual bool IsFaceTransparent(SubsystemTerrain subsystemTerrain, int face, int value) => false;

        public virtual bool IsPlacementTransparent(int value) => false;

        public virtual string GetDisplayName(SubsystemTerrain subsystemTerrain, int value)
            => LanguageControl.Get(nameof(AdvancedLogisticsDeviceBlock), DisplayNameKey);

        public virtual string GetDescription(int value)
            => LanguageControl.Get(nameof(AdvancedLogisticsDeviceBlock), DescriptionKey);

        public virtual void GetDropValues(AdvancedLogisticsDeviceBlock block, int oldValue, List<BlockDropValue> dropValues, out bool showDebris) {
            showDebris = true;
            dropValues.Add(new BlockDropValue {
                Value = Terrain.MakeBlockValue(block.BlockIndex, 0, AdvancedLogisticsDeviceBlock.MakeData(Variant, 0)),
                Count = 1
            });
        }

        public virtual BoundingBox[] GetCustomCollisionBoxes(SubsystemTerrain terrain, int value) => Block.m_defaultCollisionBoxes;

        public virtual int GetFaceTextureSlot(int face, int value) => 5;

        public virtual BlockDebrisParticleSystem CreateDebrisParticleSystem(SubsystemTerrain subsystemTerrain, Vector3 position, int value, float strength) {
            return new BlockDebrisParticleSystem(
                subsystemTerrain,
                position,
                1,
                1,
                Color.White,
                GetFaceTextureSlot(0, value),
                LogisticsLoader.BlockTexture);
        }

        public virtual BlockPlacementData GetPlacementValue(AdvancedLogisticsDeviceBlock block, SubsystemTerrain subsystemTerrain, ComponentMiner componentMiner, int value, TerrainRaycastResult raycastResult) {
            return new BlockPlacementData {
                Value = Terrain.MakeBlockValue(block.BlockIndex, 0, AdvancedLogisticsDeviceBlock.MakeData(Variant, CreativeFacing)),
                CellFace = raycastResult.CellFace
            };
        }

        public virtual int GetNextDirection(int value, bool reverse) => value;

        public virtual ElectricElement CreateElectricElement(SubsystemElectricity subsystemElectricity, int value, int x, int y, int z) => null;

        public virtual ElectricConnectorType? GetConnectorType(SubsystemTerrain terrain, int value, int face, int connectorFace, int x, int y, int z) => null;

        public virtual int GetConnectionMask(int value) => 0;

        public virtual void GenerateTerrainVertices(AdvancedLogisticsDeviceBlock block, BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z) {
            generator.GenerateCubeVertices(
                block,
                value,
                x,
                y,
                z,
                Color.White,
                geometry.GetGeometry(LogisticsLoader.BlockTexture).OpaqueSubsetsByFace);
        }

        public virtual void DrawBlock(PrimitivesRenderer3D primitivesRenderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData environmentData) {
            BlocksManager.DrawCubeBlock(
                primitivesRenderer,
                value,
                new Vector3(size),
                ref matrix,
                color,
                color,
                environmentData,
                LogisticsLoader.BlockTexture);
        }

        public virtual bool OpenWidget(IInventory inventory, ComponentMiner componentMiner, ComponentBlockEntity blockEntity) => false;
    }

    abstract class AdvancedModelInserterDefinition : AdvancedLogisticsDeviceDefinition {
        readonly string m_modelPath;
        BlockMesh[] m_meshesByFace = new BlockMesh[6];
        BoundingBox[][] m_collisionByFace = new BoundingBox[6][];
        BlockMesh m_standalone = new();

        protected AdvancedModelInserterDefinition(string modelPath) {
            m_modelPath = modelPath;
        }

        public override int CreativeFacing => 0;

        public override bool IsInteractive(SubsystemTerrain subsystemTerrain, int value) => true;

        public override bool IsFaceTransparent(SubsystemTerrain subsystemTerrain, int face, int value) => true;

        public override bool IsPlacementTransparent(int value) => true;

        public override void Initialize() {
            // PostProcessBlocksLoad 在模组加载与进世界时各跑一次；静态 definition 会复用，
            // standalone 若只 Append 会叠层错位，而地形用的 m_meshesByFace 每次 new 所以仍正常。
            m_meshesByFace = new BlockMesh[6];
            m_collisionByFace = new BoundingBox[6][];
            m_standalone = new BlockMesh();
            BuildMeshes(m_modelPath, "Inserter", m_meshesByFace, m_collisionByFace, m_standalone);
        }

        public override BoundingBox[] GetCustomCollisionBoxes(SubsystemTerrain terrain, int value)
            => m_collisionByFace[AdvancedLogisticsDeviceBlock.GetFacing(value)];

        public override BlockPlacementData GetPlacementValue(AdvancedLogisticsDeviceBlock block, SubsystemTerrain subsystemTerrain, ComponentMiner componentMiner, int value, TerrainRaycastResult raycastResult) {
            int facing = DevicesUtils.GetDirectionXYZ(componentMiner, true);
            return new BlockPlacementData {
                Value = Terrain.MakeBlockValue(block.BlockIndex, 0, AdvancedLogisticsDeviceBlock.MakeData(Variant, facing)),
                CellFace = raycastResult.CellFace
            };
        }

        public override int GetNextDirection(int value, bool reverse) {
            int facing = AdvancedLogisticsDeviceBlock.GetFacing(value);
            facing = reverse ? (facing + 5) % 6 : (facing + 1) % 6;
            return AdvancedLogisticsDeviceBlock.SetFacing(value, facing);
        }

        public override ElectricConnectorType? GetConnectorType(SubsystemTerrain terrain, int value, int face, int connectorFace, int x, int y, int z)
            => ElectricConnectorType.Input;

        public override int GetConnectionMask(int value) => int.MaxValue;

        public override void GenerateTerrainVertices(AdvancedLogisticsDeviceBlock block, BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z) {
            generator.GenerateShadedMeshVertices(
                block,
                x,
                y,
                z,
                m_meshesByFace[AdvancedLogisticsDeviceBlock.GetFacing(value)],
                Color.White,
                null,
                null,
                geometry.GetGeometry(LogisticsLoader.BlockTexture).SubsetAlphaTest);
        }

        public override void DrawBlock(PrimitivesRenderer3D primitivesRenderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData environmentData) {
            BlocksManager.DrawMeshBlock(
                primitivesRenderer,
                m_standalone,
                LogisticsLoader.BlockTexture,
                color,
                size,
                ref matrix,
                environmentData);
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
    }

    sealed class AdvancedItemInserterDefinition : AdvancedModelInserterDefinition {
        public AdvancedItemInserterDefinition() : base("Models/AdvancedInserter") {
        }

        public override AdvancedLogisticsDeviceVariant Variant => AdvancedLogisticsDeviceVariant.ItemInserter;

        public override string DisplayNameKey => "Inserter";

        public override string DescriptionKey => "DescriptionInserter";

        public override string CraftingId => "advancedinserter";

        public override string EntityName => "AdvancedInserter";

        public override ElectricElement CreateElectricElement(SubsystemElectricity subsystemElectricity, int value, int x, int y, int z)
            => new AdvancedInserterElectricElement(subsystemElectricity, new Point3(x, y, z));

        public override bool OpenWidget(IInventory inventory, ComponentMiner componentMiner, ComponentBlockEntity blockEntity) {
            var component = blockEntity.Entity.FindComponent<ComponentAdvancedInserter>(throwOnError: true);
            componentMiner.ComponentPlayer.ComponentGui.ModalPanelWidget = new AdvancedInserterWidget(inventory, component);
            return true;
        }
    }

    sealed class AdvancedFluidInserterDefinition : AdvancedModelInserterDefinition {
        public AdvancedFluidInserterDefinition() : base("Models/AdvancedFluidInserter") {
        }

        public override AdvancedLogisticsDeviceVariant Variant => AdvancedLogisticsDeviceVariant.FluidInserter;

        public override string DisplayNameKey => "FluidInserter";

        public override string DescriptionKey => "DescriptionFluidInserter";

        public override string CraftingId => "advancedfluidinserter";

        public override string EntityName => "AdvancedFluidInserter";

        public override ElectricElement CreateElectricElement(SubsystemElectricity subsystemElectricity, int value, int x, int y, int z)
            => new AdvancedFluidInserterElectricElement(subsystemElectricity, new Point3(x, y, z));

        public override void GenerateTerrainVertices(AdvancedLogisticsDeviceBlock block, BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z) {
            base.GenerateTerrainVertices(block, generator, geometry, value, x, y, z);
            for (int i = 0; i < 5; i++) {
                generator.GenerateWireVertices(value, x, y, z, i, 0, Vector2.Zero, geometry.SubsetOpaque);
            }
        }

        public override bool OpenWidget(IInventory inventory, ComponentMiner componentMiner, ComponentBlockEntity blockEntity) {
            var component = blockEntity.Entity.FindComponent<ComponentAdvancedFluidInserter>(throwOnError: true);
            componentMiner.ComponentPlayer.ComponentGui.ModalPanelWidget = new AdvancedFluidInserterWidget(inventory, component);
            return true;
        }
    }

    sealed class AdvancedSorterDefinition : AdvancedLogisticsDeviceDefinition {
        public override AdvancedLogisticsDeviceVariant Variant => AdvancedLogisticsDeviceVariant.Sorter;

        public override string DisplayNameKey => "Sorter";

        public override string DescriptionKey => "DescriptionSorter";

        public override string CraftingId => "advancedsorter";

        public override int GetDisplayOrder(AdvancedLogisticsDeviceBlock block, int value) => block.DisplayOrder - 1;

        public override int GetFaceTextureSlot(int face, int value) {
            return face switch {
                0 => 1,
                1 => 3,
                2 => 2,
                3 => 4,
                _ => 5
            };
        }
    }
}
