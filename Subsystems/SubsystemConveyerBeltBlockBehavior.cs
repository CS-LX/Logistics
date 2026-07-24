using Engine;
using Game;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>输送带行为：邻接时自动对齐朝向与爬坡（仿铁轨，不转弯）。</summary>
    public class SubsystemConveyerBeltBlockBehavior : SubsystemBlockBehavior {
        /// <summary>与 SCIENEW 铁轨一致：行=水平四向(0=-Z,1=-X,2=+Z,3=+X)，列=同层/上层/下层。</summary>
        static readonly Point3[,] NeighborOffsets = {
            { new(0, 0, -1), new(0, 1, -1), new(0, -1, -1) },
            { new(-1, 0, 0), new(-1, 1, 0), new(-1, -1, 0) },
            { new(0, 0, 1), new(0, 1, 1), new(0, -1, 1) },
            { new(1, 0, 0), new(1, 1, 0), new(1, -1, 0) }
        };

        SubsystemTerrain m_subsystemTerrain;

        public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<ConveyerBeltBlock>()];

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
            ConveyerBeltAnimatedTexture.EnsureLoaded();
        }

        public override void OnBlockAdded(int value, int oldValue, int x, int y, int z) {
            UpdateOrientation(x, y, z, step: 1);
        }

        public override void OnNeighborBlockChanged(int x, int y, int z, int neighborX, int neighborY, int neighborZ) {
            int value = m_subsystemTerrain.Terrain.GetCellValue(x, y, z);
            if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ConveyerBeltBlock>()) {
                return;
            }
            UpdateOrientation(x, y, z, step: 1);
        }

        void UpdateOrientation(int x, int y, int z, int step) {
            int blockIndex = BlocksManager.GetBlockIndex<ConveyerBeltBlock>();
            int value = m_subsystemTerrain.Terrain.GetCellValue(x, y, z);
            if (Terrain.ExtractContents(value) != blockIndex) {
                return;
            }

            bool[] neighbors = new bool[4];
            bool[] raisedNeighbors = new bool[4];

            for (int i = 0; i < 4; i++) {
                for (int k = 0; k < 3; k++) {
                    Point3 p = NeighborOffsets[i, k];
                    int nx = x + p.X;
                    int ny = y + p.Y;
                    int nz = z + p.Z;
                    int neighborValue = m_subsystemTerrain.Terrain.GetCellValueFastChunkExists(nx, ny, nz);
                    if (Terrain.ExtractContents(neighborValue) != blockIndex) {
                        continue;
                    }
                    if (k == 1) {
                        raisedNeighbors[i] = true;
                    }
                    else {
                        neighbors[i] = true;
                    }
                    if (step > 0) {
                        UpdateOrientation(nx, ny, nz, step - 1);
                    }
                    break;
                }
            }

            int data = Terrain.ExtractData(value);
            int currentRotation = ConveyerBeltBlock.GetRotation(data);
            int shape;
            int rotation;

            if (raisedNeighbors[2]) {
                shape = 1;
                rotation = ConveyerBeltBlock.RaisedNeighborIndexToRotation(2);
            }
            else if (raisedNeighbors[0]) {
                shape = 1;
                rotation = ConveyerBeltBlock.RaisedNeighborIndexToRotation(0);
            }
            else if (raisedNeighbors[3]) {
                shape = 1;
                rotation = ConveyerBeltBlock.RaisedNeighborIndexToRotation(3);
            }
            else if (raisedNeighbors[1]) {
                shape = 1;
                rotation = ConveyerBeltBlock.RaisedNeighborIndexToRotation(1);
            }
            else {
                bool alongZ = neighbors[0] || neighbors[2];
                bool alongX = neighbors[1] || neighbors[3];
                if (alongZ && alongX) {
                    shape = 0;
                    rotation = currentRotation is 1 or 3 ? 1 : 0;
                }
                else if (alongZ) {
                    shape = 0;
                    rotation = 0;
                }
                else if (alongX) {
                    shape = 0;
                    rotation = 1;
                }
                else {
                    return;
                }
            }

            int newData = ConveyerBeltBlock.MakeData(shape, rotation);
            if (newData == data) {
                return;
            }
            m_subsystemTerrain.ChangeCell(x, y, z, Terrain.ReplaceData(value, newData));
        }
    }
}
