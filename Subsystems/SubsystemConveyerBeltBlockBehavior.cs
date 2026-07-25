using Engine;
using Game;
using GameEntitySystem;
using SCIENEW.Utils;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>输送带行为：邻接时自动对齐朝向与爬坡（仿铁轨，不转弯）；变更时请求 Group 重建；每格 Segment 实体供抓取机 IInventory。</summary>
    public class SubsystemConveyerBeltBlockBehavior : SubsystemBlockBehavior {
        public const string SegmentEntityName = "ConveyerBeltSegment";

        /// <summary>与 SCIENEW 铁轨一致：行=水平四向(0=-Z,1=-X,2=+Z,3=+X)，列=同层/上层/下层。</summary>
        static readonly Point3[,] NeighborOffsets = {
            { new(0, 0, -1), new(0, 1, -1), new(0, -1, -1) },
            { new(-1, 0, 0), new(-1, 1, 0), new(-1, -1, 0) },
            { new(0, 0, 1), new(0, 1, 1), new(0, -1, 1) },
            { new(1, 0, 0), new(1, 1, 0), new(1, -1, 0) }
        };

        SubsystemTerrain m_subsystemTerrain;
        SubsystemBeltGroups m_subsystemBeltGroups;
        SubsystemBlockEntities m_subsystemBlockEntities;

        public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<ConveyerBeltBlock>()];

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(throwOnError: true);
            m_subsystemBeltGroups = Project.FindSubsystem<SubsystemBeltGroups>(throwOnError: true);
            m_subsystemBlockEntities = Project.FindSubsystem<SubsystemBlockEntities>(throwOnError: true);
            ConveyerBeltAnimatedTexture.EnsureLoaded();
        }

        public override void OnBlockAdded(int value, int oldValue, int x, int y, int z) {
            UpdateOrientation(x, y, z, step: 1);
            EnsureSegmentEntity(new Point3(x, y, z));
            m_subsystemBeltGroups.RequestRebuild(new Point3(x, y, z));
        }

        public override void OnBlockRemoved(int value, int newValue, int x, int y, int z) {
            Point3 point = new(x, y, z);
            ComponentBlockEntity blockEntity = m_subsystemBlockEntities.GetBlockEntity(x, y, z);
            if (blockEntity != null) {
                Project.RemoveEntity(blockEntity.Entity, disposeEntity: true);
            }
            m_subsystemBeltGroups.RequestRebuild(point);
            for (int i = 0; i < 4; i++) {
                for (int k = 0; k < 3; k++) {
                    Point3 o = NeighborOffsets[i, k];
                    m_subsystemBeltGroups.RequestRebuild(new Point3(x + o.X, y + o.Y, z + o.Z));
                }
            }
        }

        public override void OnBlockGenerated(int value, int x, int y, int z, bool isLoaded) {
            EnsureSegmentEntity(new Point3(x, y, z));
            m_subsystemBeltGroups.RequestRebuild(new Point3(x, y, z));
        }

        public override void OnNeighborBlockChanged(int x, int y, int z, int neighborX, int neighborY, int neighborZ) {
            int value = m_subsystemTerrain.Terrain.GetCellValue(x, y, z);
            if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ConveyerBeltBlock>()) {
                return;
            }
            UpdateOrientation(x, y, z, step: 1);
            if (m_subsystemBeltGroups.SuppressRebuildFromVisualSync) {
                return;
            }
            m_subsystemBeltGroups.RequestRebuild(new Point3(x, y, z));
            m_subsystemBeltGroups.RequestRebuild(new Point3(neighborX, neighborY, neighborZ));
        }

        public override void OnBlockModified(int value, int oldValue, int x, int y, int z) {
            // 仅 reverse/powered 变化：地形会自行 Downgrade 重 mesh，勿再 Rebuild 编组
            if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ConveyerBeltBlock>()) {
                return;
            }
            int oldData = Terrain.ExtractData(oldValue);
            int newData = Terrain.ExtractData(value);
            if (ConveyerBeltBlock.GetRotation(oldData) == ConveyerBeltBlock.GetRotation(newData)
                && ConveyerBeltBlock.GetShape(oldData) == ConveyerBeltBlock.GetShape(newData)) {
                return;
            }
            if (m_subsystemBeltGroups.SuppressRebuildFromVisualSync) {
                return;
            }
            m_subsystemBeltGroups.RequestRebuild(new Point3(x, y, z));
        }

        /// <summary>P6：点击调向（切换整组 Sign）；附带简要状态。</summary>
        public override bool OnInteract(TerrainRaycastResult raycastResult, ComponentMiner componentMiner) {
            ComponentPlayer player = componentMiner.ComponentPlayer;
            if (player == null) {
                return false;
            }
            Point3 point = raycastResult.CellFace.Point;
            if (!m_subsystemBeltGroups.TryGetAt(point, out BeltGroup group)) {
                player.ComponentGui.DisplaySmallMessage("输送带：尚未编组（等一帧）。按 F2 开调试绘制", Color.White, blinking: true, playNotificationSound: false);
                return true;
            }
            string dirLabel = group.Sign >= 0 ? "正向" : "反向";
            string runLabel = m_subsystemBeltGroups.IsGroupRunning(group) ? "运转中" : "已停止（需侧面机械能）";
            DialogsManager.ShowDialog(
                player.GuiWidget,
                new MessageDialog(
                    "输送带方向",
                    $"当前：{dirLabel} · {runLabel}\n在途 {group.Inventory.Count} 件 · 共 {group.Members.Count} 格",
                    "切换方向",
                    "关闭",
                    button => {
                        if (button != MessageDialogButton.Button1) {
                            return;
                        }
                        if (!m_subsystemBeltGroups.TryToggleSign(point, out int newSign)) {
                            return;
                        }
                        string now = newSign >= 0 ? "正向" : "反向";
                        player.ComponentGui.DisplaySmallMessage(
                            $"输送带已改为{now}",
                            Color.White,
                            blinking: true,
                            playNotificationSound: false);
                    }));
            return true;
        }

        public override void OnHitByProjectile(CellFace cellFace, WorldItem worldItem) {
            m_subsystemBeltGroups.TryAbsorbWorldItem(cellFace.Point, worldItem);
        }

        public override void OnHitByProjectile(MovingBlock movingBlock, WorldItem worldItem) {
            if (movingBlock == null) {
                return;
            }
            m_subsystemBeltGroups.TryAbsorbWorldItem(Terrain.ToCell(movingBlock.Position), worldItem);
        }

        void EnsureSegmentEntity(Point3 point) {
            if (BlockEntityUtils.GetBlockEntity(m_subsystemTerrain, point, out _)) {
                return;
            }
            BlockEntityUtils.CreateBlockEntity(m_subsystemTerrain, SegmentEntityName, point);
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
                    int neighborValue = m_subsystemTerrain.Terrain.GetCellValueFast(nx, ny, nz);
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

            // 只改 shape/rotation；reverse（调向）与 powered（运转）由 Group 同步，铺设邻接不得抹掉
            int newData = ConveyerBeltBlock.WithGeometry(data, shape, rotation);
            if (newData == data) {
                return;
            }
            m_subsystemTerrain.ChangeCell(x, y, z, Terrain.ReplaceData(value, newData));
        }
    }
}
