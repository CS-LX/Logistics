using Engine;
using Engine.Graphics;
using Game;
using SCIENEW;

namespace Logistics {
    /// <summary>
    /// 物流存储单元：不透明立方体，六面共用 <see cref="FaceTextureSlot"/>（Logistics 图集第 5 格，0-based）。
    /// 独立 Index，不与 Transfer / 抓取机等共用 Data 变体。
    /// </summary>
    public class LogisticsStorageUnitBlock : CubeBlock {
        public const int Index = 552;

        /// <summary>Logistics.png 图集格子：青边方块面（与抓取机 debris 同格）。</summary>
        public const int FaceTextureSlot = 5;

        public override string GetCategory(int value) => IEConstants.BlockCategory.Devices;

        public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value)
            => LanguageControl.Get(nameof(LogisticsStorageUnitBlock), "DisplayName");

        public override string GetDescription(int value)
            => LanguageControl.Get(nameof(LogisticsStorageUnitBlock), "Description");

        public override string GetCraftingId(int value) => "logisticsstorageunit";

        public override int GetTextureSlotCount(int value) => 16;

        public override int GetFaceTextureSlot(int face, int value) => FaceTextureSlot;

        public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z) {
            generator.GenerateCubeVertices(
                this,
                value,
                x,
                y,
                z,
                Color.White,
                geometry.GetGeometry(LogisticsLoader.BlockTexture).OpaqueSubsetsByFace);
        }

        public override void DrawBlock(PrimitivesRenderer3D primitivesRenderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData environmentData) {
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

        public override BlockDebrisParticleSystem CreateDebrisParticleSystem(SubsystemTerrain subsystemTerrain, Vector3 position, int value, float strength) {
            return new BlockDebrisParticleSystem(
                subsystemTerrain,
                position,
                strength,
                DestructionDebrisScale,
                Color.White,
                FaceTextureSlot,
                LogisticsLoader.BlockTexture);
        }
    }
}
