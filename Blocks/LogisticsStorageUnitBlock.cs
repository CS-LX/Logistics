using Engine;
using Engine.Graphics;
using Game;
using SCIENEW;

namespace Logistics {
    /// <summary>
    /// 物流存储单元：不透明立方体；地形按面无缝 CT（底图集第 8 格 + 边第 5 格）。
    /// Index 写法对齐 SCIENEW <c>BaseNormalBlock</c>；不与 Transfer / 抓取机等共用 Data 变体。
    /// </summary>
    public class LogisticsStorageUnitBlock : CubeBlock {
        public static int Index = 552;
        public override bool IsIndexDynamic => false;

        /// <summary>源图集有边格（debris）；地形用无缝变体图集。</summary>
        public const int FaceTextureSlot = 5;

        public override string GetCategory(int value) => IEConstants.BlockCategory.Devices;

        public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value)
            => LanguageControl.Get(nameof(LogisticsStorageUnitBlock), "DisplayName");

        public override string GetDescription(int value)
            => LanguageControl.Get(nameof(LogisticsStorageUnitBlock), "Description");

        public override bool IsInteractive(SubsystemTerrain subsystemTerrain, int value) => true;

        public override string GetCraftingId(int value) => "logisticsstorageunit";

        public override int GetTextureSlotCount(int value) => StorageUnitSeamlessTextures.VariantAtlasSlotCount;

        public override int GetFaceTextureSlot(int face, int value) => StorageUnitSeamlessTextures.FullyBorderedSlot;

        public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z) {
            Terrain terrain = generator.Terrain;
            int blockIndex = BlockIndex;
            Point3 origin = new(x, y, z);
            TerrainGeometrySubset[] subsets = geometry.GetGeometry(StorageUnitSeamlessTextures.Atlas).OpaqueSubsetsByFace;

            for (int face = 0; face < 6; face++) {
                Point3 n = CellFace.FaceToPoint3(face);
                int neighborValue = terrain.GetCellValueFast(x + n.X, y + n.Y, z + n.Z);
                if (!GenerateFacesForSameNeighbors && Terrain.ExtractContents(neighborValue) == blockIndex) {
                    continue;
                }
                if (!ShouldGenerateFace(generator.SubsystemTerrain, face, value, neighborValue, x, y, z)) {
                    continue;
                }

                int slot = GetFacialData(origin, face, generator.SubsystemTerrain).ToSlot();
                EmitCubeFace(generator, face, x, y, z, Color.White, slot, subsets[face]);
            }
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
                StorageUnitSeamlessTextures.Atlas);
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

        /// <summary>
        /// 面内四邻 + 四角：邻格为存储单元则不画边；角规则对齐 LED/太阳能板。
        /// </summary>
        public static StorageUnitFacialData GetFacialData(Point3 point, int face, SubsystemTerrain terrain) {
            DevicesUtils.FaceToAxesAndConner(face, out Point3[] axes, out Point3[] corners);
            bool[] sides = new bool[4];
            bool[] points = new bool[4];
            for (int i = 0; i < 4; i++) {
                Point3 sidePos = point + axes[i];
                Point3 prevPos = point + axes[(i + 3) % 4];
                Point3 cornerPos = point + corners[i];
                sides[i] = NeedsBorder(terrain, sidePos);
                points[i] = NeedsBorder(terrain, cornerPos)
                    || NeedsBorder(terrain, sidePos)
                    || NeedsBorder(terrain, prevPos);
            }
            return new StorageUnitFacialData(points, sides);
        }

        static bool NeedsBorder(SubsystemTerrain terrain, Point3 position) {
            int contents = terrain.Terrain.GetCellContentsFast(position.X, position.Y, position.Z);
            return contents != Index;
        }

        static void EmitCubeFace(BlockGeometryGenerator generator, int face, int x, int y, int z, Color color, int textureSlot, TerrainGeometrySubset subset) {
            const int slotCount = StorageUnitSeamlessTextures.VariantAtlasSlotCount;
            DynamicArray<TerrainVertex> vertices = subset.Vertices;
            TerrainGeometryDynamicArray<int> indices = subset.Indices;
            int vi = vertices.Count;
            vertices.Count += 4;

            switch (face) {
                case 0:
                    generator.SetupCubeVertexFace0(x, y, z + 1, 1f, 0, textureSlot, slotCount, color, ref vertices.Array[vi]);
                    generator.SetupCubeVertexFace0(x + 1, y, z + 1, 1f, 1, textureSlot, slotCount, color, ref vertices.Array[vi + 1]);
                    generator.SetupCubeVertexFace0(x + 1, y + 1, z + 1, 1f, 2, textureSlot, slotCount, color, ref vertices.Array[vi + 2]);
                    generator.SetupCubeVertexFace0(x, y + 1, z + 1, 1f, 3, textureSlot, slotCount, color, ref vertices.Array[vi + 3]);
                    AddQuadIndices(indices, vi, flipped: false);
                    break;
                case 1:
                    generator.SetupCubeVertexFace1(x + 1, y, z, 1f, 1, textureSlot, slotCount, color, ref vertices.Array[vi]);
                    generator.SetupCubeVertexFace1(x + 1, y + 1, z, 1f, 2, textureSlot, slotCount, color, ref vertices.Array[vi + 1]);
                    generator.SetupCubeVertexFace1(x + 1, y + 1, z + 1, 1f, 3, textureSlot, slotCount, color, ref vertices.Array[vi + 2]);
                    generator.SetupCubeVertexFace1(x + 1, y, z + 1, 1f, 0, textureSlot, slotCount, color, ref vertices.Array[vi + 3]);
                    AddQuadIndices(indices, vi, flipped: false);
                    break;
                case 2:
                    generator.SetupCubeVertexFace2(x, y, z, 1f, 1, textureSlot, slotCount, color, ref vertices.Array[vi]);
                    generator.SetupCubeVertexFace2(x + 1, y, z, 1f, 0, textureSlot, slotCount, color, ref vertices.Array[vi + 1]);
                    generator.SetupCubeVertexFace2(x + 1, y + 1, z, 1f, 3, textureSlot, slotCount, color, ref vertices.Array[vi + 2]);
                    generator.SetupCubeVertexFace2(x, y + 1, z, 1f, 2, textureSlot, slotCount, color, ref vertices.Array[vi + 3]);
                    AddQuadIndices(indices, vi, flipped: true);
                    break;
                case 3:
                    generator.SetupCubeVertexFace3(x, y, z, 1f, 0, textureSlot, slotCount, color, ref vertices.Array[vi]);
                    generator.SetupCubeVertexFace3(x, y + 1, z, 1f, 3, textureSlot, slotCount, color, ref vertices.Array[vi + 1]);
                    generator.SetupCubeVertexFace3(x, y + 1, z + 1, 1f, 2, textureSlot, slotCount, color, ref vertices.Array[vi + 2]);
                    generator.SetupCubeVertexFace3(x, y, z + 1, 1f, 1, textureSlot, slotCount, color, ref vertices.Array[vi + 3]);
                    AddQuadIndices(indices, vi, flipped: true);
                    break;
                case 4:
                    generator.SetupCubeVertexFace4(x, y + 1, z, 1f, 3, textureSlot, slotCount, color, ref vertices.Array[vi]);
                    generator.SetupCubeVertexFace4(x + 1, y + 1, z, 1f, 2, textureSlot, slotCount, color, ref vertices.Array[vi + 1]);
                    generator.SetupCubeVertexFace4(x + 1, y + 1, z + 1, 1f, 1, textureSlot, slotCount, color, ref vertices.Array[vi + 2]);
                    generator.SetupCubeVertexFace4(x, y + 1, z + 1, 1f, 0, textureSlot, slotCount, color, ref vertices.Array[vi + 3]);
                    AddQuadIndices(indices, vi, flipped: true);
                    break;
                default:
                    generator.SetupCubeVertexFace5(x, y, z, 1f, 0, textureSlot, slotCount, color, ref vertices.Array[vi]);
                    generator.SetupCubeVertexFace5(x + 1, y, z, 1f, 1, textureSlot, slotCount, color, ref vertices.Array[vi + 1]);
                    generator.SetupCubeVertexFace5(x + 1, y, z + 1, 1f, 2, textureSlot, slotCount, color, ref vertices.Array[vi + 2]);
                    generator.SetupCubeVertexFace5(x, y, z + 1, 1f, 3, textureSlot, slotCount, color, ref vertices.Array[vi + 3]);
                    AddQuadIndices(indices, vi, flipped: false);
                    break;
            }
        }

        static void AddQuadIndices(TerrainGeometryDynamicArray<int> indices, int vi, bool flipped) {
            int ii = indices.Count;
            indices.Count += 6;
            if (flipped) {
                indices.Array[ii] = vi;
                indices.Array[ii + 1] = vi + 1;
                indices.Array[ii + 2] = vi + 2;
                indices.Array[ii + 3] = vi + 2;
                indices.Array[ii + 4] = vi + 3;
                indices.Array[ii + 5] = vi;
            }
            else {
                indices.Array[ii] = vi;
                indices.Array[ii + 1] = vi + 2;
                indices.Array[ii + 2] = vi + 1;
                indices.Array[ii + 3] = vi + 2;
                indices.Array[ii + 4] = vi;
                indices.Array[ii + 5] = vi + 3;
            }
        }
    }
}
