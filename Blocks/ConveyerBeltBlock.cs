using Engine;
using Engine.Graphics;
using Game;
using SCIENEW;

namespace Logistics {
    /// <summary>
    /// 输送带方块。Data：
    /// bit0-1 rotation(0..3)，bit2 shape(0 平直 / 1 坡道)，
    /// bit3 reverse(0=Sign+1 / 1=Sign-1)，bit4 powered(0 静贴图左半 / 1 滚动右半)。
    /// 邻接自动对齐只改 shape/rotation，保留 reverse/powered。
    /// 外观（网格与世界几何）在 <see cref="ConveyerBeltDrawingManager"/>，本类只管数据契约、碰撞与放置。
    /// </summary>
    public class ConveyerBeltBlock : Block, IPreferPlacement {
        public const int Index = 553;
        /// <summary>data 变体数（powered × reverse × shape × rotation）；网格与碰撞盒按此索引缓存。</summary>
        public const int DataVariantCount = 32;

        public override bool IsIndexDynamic => false;

        static readonly BoundingBox[][] m_collisionBoxes = new BoundingBox[DataVariantCount][];

        public override void Initialize() {
            base.Initialize();
            ConveyerBeltDrawingManager.Initialize();
            BuildCollisionBoxes();
        }

        public override string GetCategory(int value) => IEConstants.BlockCategory.Devices;

        public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value)
            => LanguageControl.Get(nameof(ConveyerBeltBlock), "DisplayName");

        public override string GetDescription(int value)
            => LanguageControl.Get(nameof(ConveyerBeltBlock), "Description");

        public override string GetCraftingId(int value) => "conveyerbelt";

        public override bool IsFaceTransparent(SubsystemTerrain subsystemTerrain, int face, int value) => true;

        public override bool IsPlacementTransparent_(int value) => true;

        /// <summary>允许瞄准交互：弹出调向对话框。</summary>
        public override bool IsInteractive(SubsystemTerrain subsystemTerrain, int value) => true;

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
            // 轴：|X| 大 → 沿 X(rotation 1)；否则沿 Z(rotation 0)。reverse 对齐视线，使落地带物流朝向玩家面前。
            int rotation;
            int reverse;
            if (MathUtils.Abs(forward.X) > MathUtils.Abs(forward.Z)) {
                rotation = 1;
                reverse = forward.X > 0f ? 1 : 0;
            }
            else {
                rotation = 0;
                reverse = forward.Z > 0f ? 1 : 0;
            }
            return new BlockPlacementData {
                Value = Terrain.MakeBlockValue(BlockIndex, 0, MakeData(0, rotation, reverse, powered: 0)),
                CellFace = raycastResult.CellFace
            };
        }

        public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z)
            => ConveyerBeltDrawingManager.GenerateBeltVertices(generator, geometry, x, y, z, Terrain.ExtractData(value));

        public override void DrawBlock(
            PrimitivesRenderer3D primitivesRenderer,
            int value,
            Color color,
            float size,
            ref Matrix matrix,
            DrawBlockEnvironmentData environmentData)
            => ConveyerBeltDrawingManager.DrawBelt(primitivesRenderer, color, size, ref matrix, environmentData);

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

        public static int GetReverse(int data) => (data >> 3) & 1;

        public static int GetPowered(int data) => (data >> 4) & 1;

        public static int SetRotation(int data, int rotation) => (data & ~0b11) | (rotation & 0b11);

        public static int SetShape(int data, int shape) => (data & ~0b100) | ((shape & 1) << 2);

        public static int SetReverse(int data, int reverse) => (data & ~0b1000) | ((reverse & 1) << 3);

        public static int SetPowered(int data, int powered) => (data & ~0b10000) | ((powered & 1) << 4);

        public static int MakeData(int shape, int rotation, int reverse = 0, int powered = 0)
            => SetPowered(SetReverse(SetShape(SetRotation(0, rotation), shape), reverse), powered);

        /// <summary>仅改 shape/rotation，保留 reverse/powered（铺设自动朝向用）。</summary>
        public static int WithGeometry(int data, int shape, int rotation)
            => SetShape(SetRotation(data, rotation), shape);

        public static int MeshIndex(int data) {
            int index = (GetPowered(data) << 4) | (GetReverse(data) << 3) | (GetShape(data) << 2) | GetRotation(data);
            return MathUtils.Clamp(index, 0, DataVariantCount - 1);
        }

        public static int RaisedNeighborIndexToRotation(int neighborIndex) => neighborIndex & 3;

        public static int SignToReverse(int sign) => sign >= 0 ? 0 : 1;

        public static int ReverseToSign(int reverse) => reverse != 0 ? -1 : 1;

        /// <summary>碰撞盒随外观网格走：平直段取网格包围盒，坡道段用两段台阶近似。</summary>
        static void BuildCollisionBoxes() {
            for (int powered = 0; powered < 2; powered++) {
                for (int reverse = 0; reverse < 2; reverse++) {
                    for (int shape = 0; shape < 2; shape++) {
                        for (int rotation = 0; rotation < 4; rotation++) {
                            int data = MakeData(shape, rotation, reverse, powered);
                            m_collisionBoxes[MeshIndex(data)] = shape == 0
                                ? [ConveyerBeltDrawingManager.GetMesh(data).CalculateBoundingBox()]
                                : RiseCollisionBoxes(rotation);
                        }
                    }
                }
            }
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
