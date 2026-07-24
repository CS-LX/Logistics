using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;

namespace Logistics {
    /// <summary>
    /// 一面的无缝边角状态：上/右/下/左边与四角（左上起顺时针）。
    /// </summary>
    public struct StorageUnitFacialData : IEquatable<StorageUnitFacialData> {
        public bool[] m_points;
        public bool[] m_sides;

        public StorageUnitFacialData(bool[] points, bool[] sides) {
            if (points.Length != 4 || sides.Length != 4) {
                throw new ArgumentException("points and sides must have length 4.");
            }
            m_points = points;
            m_sides = sides;
        }

        /// <summary>编码为 0..255：低 4 位角、高 4 位边。</summary>
        public int ToSlot() {
            int slot = 0;
            for (int i = 0; i < 4; i++) {
                if (m_points[i]) {
                    slot |= 1 << i;
                }
                if (m_sides[i]) {
                    slot |= 1 << (4 + i);
                }
            }
            return slot;
        }

        public static StorageUnitFacialData FromSlot(int slot) {
            bool[] points = new bool[4];
            bool[] sides = new bool[4];
            for (int i = 0; i < 4; i++) {
                points[i] = (slot & (1 << i)) != 0;
                sides[i] = (slot & (1 << (4 + i))) != 0;
            }
            return new StorageUnitFacialData(points, sides);
        }

        public bool Equals(StorageUnitFacialData other) => ToSlot() == other.ToSlot();

        public override bool Equals(object? obj) => obj is StorageUnitFacialData other && Equals(other);

        public override int GetHashCode() => ToSlot();

        public static bool operator ==(StorageUnitFacialData left, StorageUnitFacialData right) => left.Equals(right);

        public static bool operator !=(StorageUnitFacialData left, StorageUnitFacialData right) => !left.Equals(right);
    }

    /// <summary>
    /// 存储单元无缝贴图：底图集第 8 格 + 边第 5 格（厚 2）烘烙为 16×16 变体图集，
    /// 供立方体 <see cref="BlockGeometryGenerator.GenerateCubeVertices"/> 同路径使用。
    /// </summary>
    public static class StorageUnitSeamlessTextures {
        public const int BorderedSlot = 5;
        public const int BorderlessSlot = 8;
        public const int SourceAtlasSlotCount = 16;
        public const int BorderThickness = 2;
        public const int VariantAtlasSlotCount = 16;
        public const int FullyBorderedSlot = 0xFF;

        static Texture2D? m_atlas;
        static bool m_ready;

        public static Texture2D Atlas {
            get {
                if (!m_ready) {
                    Init();
                }
                return m_atlas!;
            }
        }

        public static void Init() {
            if (m_ready) {
                return;
            }
            Image source = ContentManager.Get<Image>("Logistics");
            Image baseSlot = ExtractSlot(source, BorderlessSlot, SourceAtlasSlotCount);
            Image borderSlot = ExtractSlot(source, BorderedSlot, SourceAtlasSlotCount);
            int cell = baseSlot.Width;
            Image atlasImage = new(cell * VariantAtlasSlotCount, cell * VariantAtlasSlotCount);
            for (int slot = 0; slot < VariantAtlasSlotCount * VariantAtlasSlotCount; slot++) {
                Image baked = Bake(baseSlot, borderSlot, StorageUnitFacialData.FromSlot(slot));
                int destX = slot % VariantAtlasSlotCount * cell;
                int destY = slot / VariantAtlasSlotCount * cell;
                Blit(atlasImage, baked, destX, destY);
            }
            m_atlas = Texture2D.Load(atlasImage);
            m_ready = true;
        }

        static Image Bake(Image baseSlot, Image borderSlot, StorageUnitFacialData facialData) {
            Image image = new(baseSlot);
            int thickness = BorderThickness;
            int inner = image.Width - 2 * thickness;
            if (facialData.m_points[0]) {
                DrawRectangle(image, borderSlot, 0, 0, thickness, thickness);
            }
            if (facialData.m_points[1]) {
                DrawRectangle(image, borderSlot, image.Width - thickness, 0, thickness, thickness);
            }
            if (facialData.m_points[2]) {
                DrawRectangle(image, borderSlot, image.Width - thickness, image.Height - thickness, thickness, thickness);
            }
            if (facialData.m_points[3]) {
                DrawRectangle(image, borderSlot, 0, image.Height - thickness, thickness, thickness);
            }
            if (facialData.m_sides[0]) {
                DrawRectangle(image, borderSlot, thickness, 0, inner, thickness);
            }
            if (facialData.m_sides[1]) {
                DrawRectangle(image, borderSlot, image.Width - thickness, thickness, thickness, inner);
            }
            if (facialData.m_sides[2]) {
                DrawRectangle(image, borderSlot, thickness, image.Height - thickness, inner, thickness);
            }
            if (facialData.m_sides[3]) {
                DrawRectangle(image, borderSlot, 0, thickness, thickness, inner);
            }
            return image;
        }

        static Image ExtractSlot(Image atlas, int slot, int slotCount) {
            int slotWidth = atlas.Width / slotCount;
            int slotHeight = atlas.Height / slotCount;
            int slotX = slot % slotCount * slotWidth;
            int slotY = slot / slotCount * slotHeight;
            Image result = new(slotWidth, slotHeight);
            for (int y = 0; y < slotHeight; y++) {
                for (int x = 0; x < slotWidth; x++) {
                    result.SetPixel(x, y, atlas.GetPixel(slotX + x, slotY + y));
                }
            }
            return result;
        }

        static void Blit(Image dest, Image source, int destX, int destY) {
            for (int y = 0; y < source.Height; y++) {
                for (int x = 0; x < source.Width; x++) {
                    dest.SetPixel(destX + x, destY + y, source.GetPixel(x, y));
                }
            }
        }

        static void DrawRectangle(Image image, Image source, int x, int y, int width, int height) {
            for (int i = 0; i < width; i++) {
                for (int j = 0; j < height; j++) {
                    image.SetPixel(x + i, y + j, source.GetPixel(x + i, y + j));
                }
            }
        }
    }
}
