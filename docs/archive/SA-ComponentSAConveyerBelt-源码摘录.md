# SA 输送带源码摘录（ComponentSAConveyerBelt 及附属逻辑）

> **用途**：供 Logistics Create 连续物流开发时对照；嵌入正文避免依赖外部仓库路径。  
> **来源仓库**：`sc-guns-siyahakrep-mod` / 项目 `sc-gunsmod`（SiyahAkrep）  
> **摘录日期**：2026-07-25  
> **性质**：**仅供参考**；**禁止**一比一复刻其玩法、源码或逻辑。  
> **权威**：玩法 / 源码 / 逻辑一律以 [输送带实现计划.md](../输送带实现计划.md) 为准；冲突时以计划为准、不以本文或外部 SA 仓库为准。

---

## 0. 给后续 Agent 的速查

### 0.1 SA 模型一句话

每格一个 `ComponentSAConveyerBelt`（`SlotsCount=1`），物品在**本格槽位**里用 `Progress`（0→1）滑动；到头 `DeliverBeltItem`：优先塞进前方相邻输送带，否则弹出 `Pickable`。通电位写在方块 Data；邻接同 Data（忽略 power）连锁充能。站立生物按 `MovementTrack.Direction` 被推动。滚动贴图是**全局单 RT**。

### 0.2 与 Logistics 计划的差异（勿照搬）

| SA | Logistics 计划 |
| --- | --- |
| 每格独立库存 + Progress | Group + Controller 连续 `beltPosition` |
| Data 含 reverse / power | Data 仅 rotation+shape；Sign 在 Group |
| 邻接充能连锁 | 定速视觉；无 SA 电力语义 |
| 点击手取/手放在途物 | 不被动截胡；臂走 IInventory 门面 |
| 全局 BeltOffset 贴图 | 已有 `ConveyerBeltAnimatedTexture`（同源算法） |

### 0.3 源文件索引

| 内容 | 路径（相对 sc-gunsmod） | 约略行号 |
| --- | --- | --- |
| 方块 `SAConveyerBelt` | `GunsCode/SACode/Machine/Machines/Connections.cs` | 155–362 |
| 组件 `ComponentSAConveyerBelt` | `GunsCode/SACode/Machine/MachineComponents.cs` | 3509–3775 |
| 充能/邻接 | `GunsCode/SACode/Machine/MachineManager.cs` | 704–825 |
| 编辑/交互/拆除充能 | `GunsCode/SACode/-行为.cs` | 600–643, 792–817, 1008–1026 |
| 踩带推动 | `GunsCode/-Component.cs` | 4897–4914 |
| 滚动贴图 | `GunsCode/-Subsystem.cs` | 1452–1463, 1476–1477, 1673–1748 |
| 编辑对话框 | `GunsCode/Widgets/EditConveyerBeltDialog.cs` | 全文 |
| 实体模板 | `SiyahAkrep/SiyahAkrep.xdb` | `SAConveyerBeltEntity` |

### 0.4 Data 位布局（SA）

```
bit:  shape(高) | power/enable | rotation(2) | reverse(低)
```

- `GetShape` / `SetShape`：平直 0 / 坡道 1  
- `GetPower` / `SetPower`：是否运转（兼 UV 右半区）  
- `GetRotation` / `SetRotation`：0..3  
- `GetReverse` / `SetReverse`：正向/反向  

### 0.5 关键可抄点（实现 Logistics 时）

1. **`MovementTrack`**：由 rotation/shape/reverse 算起点与方向；坡道 `endOffset.Y += 1`；reverse 交换起终点。  
2. **`ItemPositions`**：`track.Position + Direction * (spacing*i + Progress*length) + Y*0.25`；坡长 `Sqrt(2)`。  
3. **`DeliverBeltItem`**：目标格 = 当前格中心 + Direction；前方是带则 `AcquireItems`，否则 `AddPickable` 带初速。  
4. **实体推动**：非潜行 + power>0 → `Velocity += dt * 10 * track.Direction`。  
5. **滚动贴图**：底图全幅 + `Belt.png` 在右半边按 `BeltOffset` 卷动（Logistics 已移植）。  
6. **`GetBeltConnectableFaces`**：平直沿轴 ±1；坡道为对角邻接（含 Y±1），可对照自动爬坡邻接。

---

## 1. 实体模板（xdb）

路径：`SiyahAkrep/SiyahAkrep.xdb`

```xml
    <EntityTemplate Name="SAConveyerBeltEntity" Guid="112233aa-0009-0000-0000-000000000001">
      <MemberComponentTemplate Name="BlockEntity" Guid="112233aa-0009-0000-0000-000000000002" InheritanceParent="09a85cba-d94e-41b8-9497-f20ed942c17e" />
      <MemberComponentTemplate Name="SAConveyerBelt" Guid="112233aa-0009-0000-0000-000000000003" InheritanceParent="81a44c6a-c30a-4f53-8d64-0c30aabab8f9">
        <Parameter Name="Class" Value="Game.ComponentSAConveyerBelt" Type="string" />
        <Parameter Name="Name" Value="输送带" Type="string" />
        <Parameter Name="Point" Value="0,0,0" Type="Point3" />
        <Parameter Name="SlotsCount" Value="1" Type="int" />
        <Parameter Name="PowerConsumption" Value="0" Type="float" />
      </MemberComponentTemplate>
    </EntityTemplate>
```

---

## 2. 方块 `SAConveyerBelt`

路径：`GunsCode/SACode/Machine/Machines/Connections.cs`

```csharp
    public class SAConveyerBelt : AlphaTestCubeBlock
    {
        public const int Index = 552;
        public const int Count = 32;//2的5次方
        public BlockMesh[] m_blockMeshes = new BlockMesh[Count];
        public BlockMesh m_standaloneBlockMesh = new BlockMesh();
        public Texture2D m_texture;
        public BoundingBox[][] m_collisionBoxes = new BoundingBox[Count][];

        public override BoundingBox[] GetCustomCollisionBoxes(SubsystemTerrain terrain, int value)
        {
            int data = MathUtils.Clamp(Terrain.ExtractData(value), 0, m_collisionBoxes.Length - 1);
            return m_collisionBoxes[data];
        }
        public override BoundingBox[] GetCustomInteractionBoxes(SubsystemTerrain terrain, int value)
        {
            return GetCustomCollisionBoxes(terrain, value);
        }
        public override void Initialize()
        {
            try
            {
                m_texture = ContentManager.Get<Texture2D>("Textures/Items/ConveyerBelt/ConveyerBelt");
                Model model = ContentManager.Get<Model>("Models/SAMachine/ConveyerBelt");
                Matrix boneAbsoluteTransform = BlockMesh.GetBoneAbsoluteTransform(model.FindMesh("Flat").ParentBone);
                m_standaloneBlockMesh.AppendModelMeshPart(model.FindMesh("Flat").MeshParts[0], boneAbsoluteTransform * Matrix.CreateTranslation(0f, -0.5f, 0f), false, false, false, false, Color.White);
                m_standaloneBlockMesh.AppendModelMeshPart(model.FindMesh("Belt_Flat").MeshParts[0], boneAbsoluteTransform * Matrix.CreateTranslation(0f, -0.5f, 0f), false, false, false, false, Color.White);
                for (int shape = 0; shape < 2; shape++)
                {
                    for (int rotation = 0; rotation < 4; rotation++)
					{
                        for (int reverse = 0; reverse < 2; reverse++)
                        {
                            for (int enable = 0; enable < 2; enable++)
							{
                                int data = SetShape(SetRotation(SetReverse(SetPower(0, enable), reverse), rotation), shape);
                                m_blockMeshes[data] = new BlockMesh();
                                string shapeName = shape == 0 ? "Flat" : "Rise";
                                m_blockMeshes[data].AppendModelMeshPart(model.FindMesh(shapeName).MeshParts[0], boneAbsoluteTransform * Matrix.CreateRotationY(MathUtils.DegToRad(90f) * rotation) * Matrix.CreateTranslation(0.5f, 0f, 0.5f), false, false, false, false, Color.White);
                                m_blockMeshes[data].AppendModelMeshPart(model.FindMesh("Belt_" + shapeName).MeshParts[0], boneAbsoluteTransform * Matrix.CreateRotationY(MathUtils.DegToRad(90f) * rotation) * Matrix.CreateTranslation(0.5f, 0f, 0.5f), false, false, false, false, Color.White);
                                if (reverse > 0)
                                    m_blockMeshes[data].TransformTextureCoordinates(Matrix.CreateTranslation(-0.25f, -0.5f, 0) * Matrix.CreateRotationZ(MathUtils.DegToRad(180f)) * Matrix.CreateTranslation(0.25f, 0.5f, 0));
                                if(enable > 0)
                                    m_blockMeshes[data].TransformTextureCoordinates(Matrix.CreateTranslation(0.5f, 0f, 0f));
                                if (shape == 0)
                                    m_collisionBoxes[data] = new BoundingBox[1] { m_blockMeshes[data].CalculateBoundingBox() };
								else
								{
                                    BoundingBox[] boundingBoxes;
									switch (rotation)
									{
                                        case 1:boundingBoxes = new BoundingBox[2]
                                        {
                                            new BoundingBox(new Vector3(0.5f, 0, 0), new Vector3(1f, 0.5f, 1f)),
                                            new BoundingBox(new Vector3(0, 0.5f, 0), new Vector3(0.5f, 1f, 1f))
                                        }; break;
                                        case 2:boundingBoxes = new BoundingBox[2]
                                        {
                                            new BoundingBox(new Vector3(0, 0, 0), new Vector3(1, 0.5f, 0.5f)),
                                            new BoundingBox(new Vector3(0, 0.5f, 0.5f), new Vector3(1f, 1f, 1f))
                                        }; break;
                                        case 3:boundingBoxes = new BoundingBox[2]
                                        {
                                            new BoundingBox(new Vector3(0, 0, 0), new Vector3(0.5f, 0.5f, 1f)),
                                            new BoundingBox(new Vector3(0.5f, 0.5f, 0), new Vector3(1f, 1f, 1f))
                                        }; break;
                                        default: boundingBoxes = new BoundingBox[2]
                                        {
                                            new BoundingBox(new Vector3(0, 0, 0.5f), new Vector3(1f, 0.5f, 1f)),
                                            new BoundingBox(new Vector3(0, 0.5f, 0), new Vector3(1f, 1f, 0.5f))
                                        }; break;
                                    }
                                    m_collisionBoxes[data] = boundingBoxes;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                m_texture = ContentManager.Get<Texture2D>("Textures/Blocks");
                Model model = ContentManager.Get<Model>("Models/CraftingTable");
                Matrix boneAbsoluteTransform = BlockMesh.GetBoneAbsoluteTransform(model.FindMesh("CraftingTable").ParentBone);
                m_standaloneBlockMesh.AppendModelMeshPart(model.FindMesh("CraftingTable").MeshParts[0], boneAbsoluteTransform * Matrix.CreateTranslation(0f, -0.5f, 0f), false, false, false, false, Color.White);
                for (int i = 0; i < m_blockMeshes.Length; i++)
                {
                    m_blockMeshes[i] = new BlockMesh();
                    m_blockMeshes[i].AppendModelMeshPart(model.FindMesh("CraftingTable").MeshParts[0], boneAbsoluteTransform * Matrix.CreateRotationY(MathUtils.DegToRad(90f) * i) * Matrix.CreateTranslation(0.5f, 0f, 0.5f), false, false, false, false, Color.White);
                    m_collisionBoxes[i] = new BoundingBox[1] { m_blockMeshes[i].CalculateBoundingBox() };
                }
            }
            base.Initialize();
        }

        public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z)
        {
            int data = MathUtils.Clamp(Terrain.ExtractData(value), 0, m_blockMeshes.Length - 1);
			generator.GenerateShadedMeshVertices(this, x, y, z, m_blockMeshes[data], Color.White, null, null, geometry.GetGeometry(generator.SubsystemTerrain.Project.FindSubsystem<SubsystemGMItemsBlockBehavior>().ConveyerBeltTexture).SubsetOpaque);
		}

        public override void DrawBlock(PrimitivesRenderer3D primitivesRenderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData environmentData)
        {
            BlocksManager.DrawMeshBlock(primitivesRenderer, m_standaloneBlockMesh, m_texture, color, size, ref matrix, environmentData);
        }

        public override IEnumerable<int> GetCreativeValues()
        {
            if (GMSettingManager.SALoaded)
                yield return Terrain.ReplaceContents(Terrain.ReplaceData(0, DefaultCreativeData), BlockIndex);
        }
        public static int PowOf2(int num)
        {//2的n次方
            return (int)MathUtils.Pow(2, num);
        }

        //2进制  1 0 01 0
        //shape enable rotation reverse
        public static int GetShape(int data)
        {//第1位(2)
            return (data - data % PowOf2(4)) / PowOf2(4);
        }
        public static int GetPower(int data)
        {//第2位(2)
            return (data % PowOf2(4) - data % PowOf2(3)) / PowOf2(3);
        }
        public static int GetRotation(int data)
        {//第3-4位(2^2=4)
            return (data % PowOf2(3) - data % 2) / 2;
        }
        public static int GetReverse(int data)
        {//第5位(2)
            return data % 2;
        }
        public static int SetShape(int data, int shape)
        {
            return shape * PowOf2(4) + data % PowOf2(4);
        }
        public static int SetPower(int data, int enable)
        {
            return data - data % PowOf2(4) + enable * PowOf2(3) + data % PowOf2(3);
        }
        public static int SetRotation(int data, int rotation)
        {
            return data - data % PowOf2(3) + rotation * 2 + data % 2;
        }
        public static int SetReverse(int data, int reverse)
        {
            return data - data % 2 + reverse;
        }

        public override BlockPlacementData GetPlacementValue(SubsystemTerrain subsystemTerrain, ComponentMiner componentMiner, int value, TerrainRaycastResult raycastResult)
        {
            int data = Terrain.ExtractData(value), newData = data;
            if (data == 0)
			{
                int rotation = 0, reverse = 0;
                Vector3 forward = Matrix.CreateFromQuaternion(componentMiner.ComponentCreature.ComponentCreatureModel.EyeRotation).Forward;
                float angleX = MathUtils.Abs(Functions.GetAngleByVector(forward, Vector3.UnitX));
                float angleXN = MathUtils.Abs(Functions.GetAngleByVector(forward, -Vector3.UnitX));
                float angleZ = MathUtils.Abs(Functions.GetAngleByVector(forward, Vector3.UnitZ));
                float angleZN = MathUtils.Abs(Functions.GetAngleByVector(forward, -Vector3.UnitZ));
                float min = MathUtils.Min(angleX, angleXN, angleZ, angleZN);
                if (angleX == min)
				{
                    rotation = 1;
                    reverse = 1;
                }
                else if (angleXN == min)
                {
                    rotation = 1;
                }
                else if (angleZ == min)
                {
                    rotation = 0;
                    reverse = 1;
                }
                else if (angleZN == min)
                {
                    rotation = 0;
                }
                newData = SetRotation(SetReverse(0, reverse), rotation);
            }
            BlockPlacementData result = new BlockPlacementData()
            {
                Value = Terrain.MakeBlockValue(Index, 0, newData),
                CellFace = raycastResult.CellFace
            };
            return result;
        }

        public static string[] ShapeName = new string[2] { "平直", "坡道" };
        public static string[] ReverseName = new string[2] { "正向", "反向" };
        public static string[] RotationName = new string[4] { "+0°", "+90°", "+180°", "+270°" };

		public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value)
		{
            string name = base.GetDisplayName(subsystemTerrain, value);
            int data = Terrain.ExtractData(value), shape = GetShape(data), reverse = GetReverse(data), rotation = GetRotation(data);
            if (shape != 0)
                name += " " + ShapeName[shape];
            if(reverse != 0)
                name += " " + ReverseName[reverse];
            if(rotation != 0)
                name += " " + RotationName[rotation].Replace("+", string.Empty);
            return name;
		}
	}
```

---

## 3. 组件 `ComponentSAConveyerBelt`

路径：`GunsCode/SACode/Machine/MachineComponents.cs`  
基类：`ComponentSAMachine`（库存/Point/子系统字段在基类，此处不整份粘贴）。

```csharp
    public class ComponentSAConveyerBelt : ComponentSAMachine, IUpdateable, IDrawable
    {
        public SubsystemBlockEntities m_subsystemBlockEntities;
        public SubsystemSky m_subsystemSky;
        public bool PowerSupply => SAMachineManager.FindNeiborPowerSupply(this, Point, m_subsystemTerrain);
        private bool m_lastPower;
        public float Progress;
        public override int AcquireItems(int value, int count)
        {//接收掉落物时优先放到数量少的格子
            int num = FindAcquireSlotForItem(this, value);
            while (count > 0 && num >= 0)
            {
                int minCountSlotToAcquire = num;
                for (int i = 0; i < SlotsCount; i++)
                {
                    if (GetSlotCount(i) == 0 && GetSlotCapacity(i, value) > 0)
                    {
                        minCountSlotToAcquire = i;
                        break;
                    }
                    int slotCount = GetSlotCount(i);
                    if (slotCount > 0 && GetSlotValue(i) == value && GetSlotCount(i) < GetSlotCapacity(i, value) && slotCount < GetSlotCount(minCountSlotToAcquire))
                        minCountSlotToAcquire = i;
                }
                if (minCountSlotToAcquire >= 0)
                {
                    if (GetSlotCount(minCountSlotToAcquire) < GetSlotCapacity(minCountSlotToAcquire, GetSlotValue(minCountSlotToAcquire)))
                    {
                        AddSlotItems(minCountSlotToAcquire, value, 1);
                        count--;
                    }
                }
                num = FindAcquireSlotForItem(this, value);
            }
            return count;
        }
		public override void AddSlotItems(int slotIndex, int value, int count)
		{
            //m_dropCd = 1f;
            Progress = 0;
			base.AddSlotItems(slotIndex, value, count);
		}
		public override int RemoveSlotItems(int slotIndex, int count)
		{
            Progress = 0;
			return base.RemoveSlotItems(slotIndex, count);
		}
		public int[] DrawOrders => SubsystemProjectiles.m_drawOrders;
        public DrawBlockEnvironmentData m_drawBlockEnvironmentData = new DrawBlockEnvironmentData();
        public PrimitivesRenderer3D m_primitivesRenderer = new PrimitivesRenderer3D();

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap)
        {
            base.Load(valuesDictionary, idToEntityMap);
            m_subsystemSky = base.Project.FindSubsystem<SubsystemSky>(throwOnError: true);
            m_subsystemBlockEntities = base.Project.FindSubsystem<SubsystemBlockEntities>(throwOnError: true);
            Progress = 0;
            m_isInitialized = false;
        }

        private bool m_isInitialized;
        public void Initialize()
		{
            TerrainChunk terrainChunk = m_subsystemTerrain.Terrain.GetChunkAtCell(Point.X, Point.Z);
            if (terrainChunk != null)
			{
                SAMachineManager.UpdateBeltState(new List<Point3>(), Point, false, m_subsystemTerrain);
                //            terrainChunk.ThreadState = TerrainChunkState.InvalidVertices1;
                ////int value = m_subsystemTerrain.Terrain.GetCellValue(Point.X, Point.Y, Point.Z);
                ////m_subsystemTerrain.ChangeCell(Point.X, Point.Y, Point.Z, 0);
                ////         m_subsystemTerrain.ChangeCell(Point.X, Point.Y, Point.Z, value);
                ////Log.Warning("1");
                Time.QueueTimeDelayedExecution(Time.FrameStartTime + 3, delegate
                {
                    m_subsystemTerrain.TerrainUpdater.DowngradeAllChunksState(TerrainChunkState.InvalidLight, forceGeometryRegeneration: true);
                });
				//m_subsystemTerrain.TerrainUpdater.UpdateChunkSingleStep(terrainChunk, m_subsystemSky.SkyLightValue);
				//    terrainChunk.ModificationCounter++;
				//m_subsystemTerrain.TerrainUpdater.DowngradeChunkNeighborhoodState(terrainChunk.Coords, 1, TerrainChunkState.InvalidLight, forceGeometryRegeneration: false);
				m_isInitialized = true;
            }
        }
        //private float m_dropCd;
        public override void Update(float dt)
        {
            base.Update(dt);
            if (!m_isInitialized && m_subsystemTerrain != null && Point != Point3.Zero)
                Initialize();
            //m_dropCd = MathUtils.Max(0, m_dropCd - dt);
            bool powerSupply = PowerSupply;
            if (powerSupply != m_lastPower)
			{
                SAMachineManager.UpdateBeltState(new List<Point3>(), Point, powerSupply, m_subsystemTerrain);
            }
            m_lastPower = powerSupply;

            int cellValue = m_subsystemTerrain.Terrain.GetCellValue(Point.X, Point.Y, Point.Z), id = Terrain.ExtractContents(cellValue), data = Terrain.ExtractData(cellValue);
            int power = SAConveyerBelt.GetPower(data);//, shape = SAConveyerBelt.GetShape(data);
            //float length = shape > 0 ? MathUtils.Sqrt(2f) : 1f;//轨迹总长度
            if (power > 0)
			{//运动状态
                int value = GetSlotValue(0), count = GetSlotCount(0);
                if(value > 0 && count > 0)
				{
                    Progress += 1f * dt;//速度与m_subsystemGMItemsBlockBehavior.BeltOffset同步
                    if(Progress >= 1f)
					{
                        DeliverBeltItem(value, count);
                    }
                }
				//if (m_subsystemGMItemsBlockBehavior.m_dropped && m_dropCd <= 0)
				//{
    //                if (count > 0)
    //                {
    //                    DeliverBeltItem(value, count);
    //                    RemoveSlotItems(0, count);
    //                }
                //}
			}
        }

		public void Draw(Camera camera, int drawOrder)
        {
            m_drawBlockEnvironmentData.SubsystemTerrain = m_subsystemTerrain;
            m_drawBlockEnvironmentData.InWorldMatrix = Matrix.Identity;
            Vector3[] itemPositions = ItemPositions;
            if (ItemPositions.Length < SlotsCount)
                return;
            for(int i = 0; i < SlotsCount; i++)
			{
                int slotValue = GetSlotValue(i);
                if (slotValue > 0 && GetSlotCount(i) > 0)
                {
                    int id = Terrain.ExtractContents(slotValue);
                    Block block = BlocksManager.Blocks[id];
                    float num = MathUtils.Sqr(m_subsystemSky.VisibilityRange);
                    if (Vector3.DistanceSquared(camera.ViewPosition, new Vector3(Point)) < num)
                    {
						int cellValue = m_subsystemTerrain.Terrain.GetCellValue(Point.X, Point.Y, Point.Z), cellData = Terrain.ExtractData(cellValue);
                        int rotation = SAConveyerBelt.GetRotation(cellData), shape = SAConveyerBelt.GetShape(cellData);
						//Vector3 faceToDirection = -CellFace.FaceToVector3(face), faceToRight = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, faceToDirection));
						Vector3 position = itemPositions[i];
                        int x = Terrain.ToCell(position.X);
                        int num2 = Terrain.ToCell(position.Y);
                        int z = Terrain.ToCell(position.Z);
                        TerrainChunk chunkAtCell = m_subsystemTerrain.Terrain.GetChunkAtCell(x, z);
                        if (chunkAtCell != null && chunkAtCell.State >= TerrainChunkState.InvalidVertices1 && num2 >= 0 && num2 < 255)
                        {
                            m_drawBlockEnvironmentData.Humidity = m_subsystemTerrain.Terrain.GetSeasonalHumidity(x, z);
                            m_drawBlockEnvironmentData.Temperature = m_subsystemTerrain.Terrain.GetSeasonalTemperature(x, z) + SubsystemWeather.GetTemperatureAdjustmentAtHeight(num2);
                            m_drawBlockEnvironmentData.Light = m_subsystemTerrain.Terrain.GetCellLightFast(x, num2, z);
                        }
                        m_drawBlockEnvironmentData.BillboardDirection = null;
                        m_drawBlockEnvironmentData.InWorldMatrix.Translation = position;
                        Matrix matrix = SAFunctions.GetMatrixOnMachine(slotValue, rotation);
                        //if (block is FlatBlock || block is GunsBaseBlock || block is GMItems)
                        //    matrix = Matrix.CreateRotationX(MathUtils.PI * -0.5f);
                        //else if (block is Guns)
                        //    matrix = Matrix.CreateRotationZ(MathUtils.PI * 0.5f) * Matrix.CreateRotationY(MathUtils.DegToRad(90f * (rotation + 1)));
                        //else if (block.IsPlaceable_(slotValue))
                        //    matrix = Matrix.CreateRotationY(MathUtils.DegToRad(0f));
                        //else
                        //    matrix = Matrix.CreateRotationY(MathUtils.DegToRad(90f * (rotation + 1)));
                        matrix.Translation = position;
                        block.DrawBlock(m_primitivesRenderer, slotValue, Color.White, 0.3f, ref matrix, m_drawBlockEnvironmentData);
                    }
                }
            }
            m_primitivesRenderer.Flush(camera.ViewProjectionMatrix);
        }
        /// <summary>
        /// 输送带的运动轨迹（起始位置和运动方向）
        /// </summary>
        public Ray3 MovementTrack
        {
			get
			{
                Ray3 result = new Ray3();
                Vector3 startOffset, endOffset;
                int data = Terrain.ExtractData(m_subsystemTerrain.Terrain.GetCellValue(Point.X, Point.Y, Point.Z));
                int shape = SAConveyerBelt.GetShape(data), reverse = SAConveyerBelt.GetReverse(data), rotation = SAConveyerBelt.GetRotation(data);
				switch (rotation)
				{
                    case 1: startOffset = new Vector3(1f, 0, 0.5f); endOffset = new Vector3(0, 0, 0.5f); break;
                    case 2: startOffset = new Vector3(0.5f, 0, 0); endOffset = new Vector3(0.5f, 0, 1f); break;
                    case 3: startOffset = new Vector3(0, 0, 0.5f); endOffset = new Vector3(1f, 0, 0.5f); break;
                    default: startOffset = new Vector3(0.5f, 0, 1f); endOffset = new Vector3(0.5f, 0, 0); break;
				}
                if(shape > 0)
                    endOffset.Y += 1;
                if(reverse > 0)
					(endOffset, startOffset) = (startOffset, endOffset);
				result.Position = new Vector3(Point) + startOffset;
                result.Direction = Vector3.Normalize(endOffset - startOffset);
                return result;
			}
        }

        public Vector3[] ItemPositions
		{
			get
			{
                Vector3[] result = new Vector3[SlotsCount];
                int data = Terrain.ExtractData(m_subsystemTerrain.Terrain.GetCellValue(Point.X, Point.Y, Point.Z));
                int shape = SAConveyerBelt.GetShape(data);//, power = SAConveyerBelt.GetPower(data);
                float length = shape > 0 ? MathUtils.Sqrt(2f) : 1f;//轨迹总长度
                float spacing = length / SlotsCount;//物品的间距，若要居中则两端间距取一半
                //float animateOffset = 0.5f * spacing;
                //if (power > 0)
                    //animateOffset = Progress * length;// m_subsystemGMItemsBlockBehavior.BeltOffset;
                Ray3 track = MovementTrack;
                for(int i = 0; i < SlotsCount; i++)
				{
					result[i] = track.Position + track.Direction * MathUtils.Clamp(spacing * i + Progress * length, 0, length) + Vector3.UnitY * 0.25f;
				}
                return result;
			}
		}

        public void DeliverBeltItem(int value, int count)
		{
            Ray3 track = MovementTrack;
            int cellValue = m_subsystemTerrain.Terrain.GetCellValue(Point.X, Point.Y, Point.Z), data = Terrain.ExtractData(cellValue);
            Point3 targetPoint = Terrain.ToCell(new Vector3(Point) + new Vector3(0.5f) + track.Direction);
            int cellValue2 = m_subsystemTerrain.Terrain.GetCellValue(targetPoint.X, targetPoint.Y, targetPoint.Z), id2 = Terrain.ExtractContents(cellValue2), data2 = Terrain.ExtractData(cellValue2);
            bool addDrops = true, removeItem = true;
            if(id2 == SAConveyerBelt.Index)
			{
                ComponentBlockEntity blockEntity = m_subsystemBlockEntities.GetBlockEntity(targetPoint.X, targetPoint.Y, targetPoint.Z);
                if (blockEntity != null)
				{
                    ComponentSAConveyerBelt componentSAConveyerBelt = blockEntity.Entity.FindComponent<ComponentSAConveyerBelt>();
                    if(componentSAConveyerBelt != null)
					{
                        int num2 = componentSAConveyerBelt.AcquireItems(value, count);
                        if(num2 == count)
						{
                            addDrops = false;
							removeItem = false;
						}
                        else if (num2 > 0)
                            count = num2;
						else
						{
                            addDrops = false;
                        }
                    }
                }
            }
            if(addDrops)
			{
                float length = 1f;//轨迹总长度
                float h = 0.3f;//掉落物高度
                int shape = SAConveyerBelt.GetShape(data);
                if(shape > 0)
				{
                    length = MathUtils.Sqrt(2f);
                    int reverse = SAConveyerBelt.GetReverse(data);
                    if (reverse > 0)
                        h = 1f;
                }
                m_subsystemPickables.AddPickable(value, count, track.Position + track.Direction * length + Vector3.UnitY * h, track.Direction * 1.5f + Vector3.UnitY, null);
            }
            if (removeItem)
                RemoveSlotItems(0, count);
        }
    }
```

---

## 4. 充能连锁与邻接（MachineManager）

路径：`GunsCode/SACode/Machine/MachineManager.cs`

```csharp
        public static void UpdateBeltState(List<Point3> passedPoints, Point3 point, bool power, SubsystemTerrain subsystemTerrain)
        {
            passedPoints.Add(point);
            TerrainChunk terrainChunk = subsystemTerrain.Terrain.GetChunkAtCell(point.X, point.Z);
            if (terrainChunk == null || terrainChunk.State <= TerrainChunkState.InvalidContents4)
                return;
            int value = subsystemTerrain.Terrain.GetCellValue(point.X, point.Y, point.Z);
            int id = Terrain.ExtractContents(value), data = Terrain.ExtractData(value);
            if (id == SAConveyerBelt.Index && SAConveyerBelt.GetPower(data) > 0 != power)
            {
                subsystemTerrain.ChangeCell(point.X, point.Y, point.Z, Terrain.ReplaceData(value, SAConveyerBelt.SetPower(data, power ? 1 : 0)));
                //string log = point.ToString() + ";  To " + power.ToString() + "  {";
                //if (passedPoints.Count > 0)
                //    foreach (Point3 point3 in passedPoints)
                //        log += point3.X.ToString() + ", ";
                //Log.Warning(log);
            }
            List<Point3> points = GetBeltConnectableFaces(value);
            if (points.Count > 0)
            {
                foreach (Point3 offset in points)
                {
                    Point3 point2 = point + offset;
                    if (!passedPoints.Contains(point2))
                    {
                        int value2 = subsystemTerrain.Terrain.GetCellValue(point2.X, point2.Y, point2.Z);
                        int id2 = Terrain.ExtractContents(value2), data2 = Terrain.ExtractData(value2);
                        //Log.Warning(point.ToString() + ";  " + point2.ToString() + ";  0");
                        if (id2 == SAConveyerBelt.Index && IsConnectableBelt(value, value2))
                        {
                            if (power)
                            {//若正在传递已充能，则无需考虑其它因素直接将相连传送带改为已充能
                                Time.QueueTimeDelayedExecution(Time.FrameStartTime + PowerTransportDelay, delegate
                                {
                                    UpdateBeltState(passedPoints, point2, power, subsystemTerrain);
                                    //Log.Warning(point.ToString() + ";  " + point2.ToString() + ";  true");
                                });
                            }
                            else
                            {//若正在传递未充能，则经过相连传送带时检测该传送带是否有电力，若有电力则从该传送带开始新的已充能传递
                                bool powerSupply = false;
                                ComponentBlockEntity blockEntity = subsystemTerrain.Project.FindSubsystem<SubsystemBlockEntities>(throwOnError: true).GetBlockEntity(point2.X, point2.Y, point2.Z);
                                if (blockEntity != null)
                                {
                                    ComponentSAConveyerBelt componentSAConveyerBelt2 = blockEntity.Entity.FindComponent<ComponentSAConveyerBelt>();
                                    if (componentSAConveyerBelt2 != null && componentSAConveyerBelt2.PowerSupply)
                                    {
                                        powerSupply = true;
                                    }
                                }
                                if (powerSupply)
                                {
                                    Time.QueueTimeDelayedExecution(Time.FrameStartTime + PowerTransportDelay, delegate
                                    {
                                        UpdateBeltState(new List<Point3>(), point2, true, subsystemTerrain);
                                        //Log.Warning(point.ToString() + ";  " + point2.ToString() + ";  true 1");
                                    });
                                }
                                else
                                {
                                    Time.QueueTimeDelayedExecution(Time.FrameStartTime + PowerTransportDelay, delegate
                                    {
                                        UpdateBeltState(passedPoints, point2, false, subsystemTerrain);
                                        //Log.Warning(point.ToString() + ";  " + point2.ToString() + ";  false");
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }

        public static bool IsConnectableBelt(int value1, int value2)
		{
            if(Terrain.ExtractContents(value1) == SAConveyerBelt.Index && Terrain.ExtractContents(value2) == SAConveyerBelt.Index)
			{
                int data1 = Terrain.ExtractData(value1), data2 = Terrain.ExtractData(value2);
                //if (data1 == data2)
                //    return true;
                data1 = SAConveyerBelt.SetPower(data1, 0);
                data2 = SAConveyerBelt.SetPower(data2, 0);
                return data1 == data2;
    //            int rotation1 = SAConveyerBelt.GetRotation(data1), rotation2 = SAConveyerBelt.GetRotation(data2);
    //            if(rotation1 == rotation2)
				//{
    //                int shape1 = SAConveyerBelt.GetShape(data1), shape2 = SAConveyerBelt.GetShape(data2);
    //                int reverse1 = SAConveyerBelt.GetReverse(data1), reverse2 = SAConveyerBelt.GetReverse(data2);
    //            }
            }
            return false;
		}

        public static List<Point3> GetBeltConnectableFaces(int value)
		{
            List<Point3> faces = new List<Point3>();
            int id = Terrain.ExtractContents(value), data = Terrain.ExtractData(value);
            if(id == SAConveyerBelt.Index)
			{
                int shape = SAConveyerBelt.GetShape(data), rotation = SAConveyerBelt.GetRotation(data);
                if(shape == 0)
				{
					switch (rotation)
					{
                        case 1:
                        case 3: faces.Add(new Point3(1, 0, 0)); faces.Add(new Point3(-1, 0, 0)); break;
                        default: faces.Add(new Point3(0, 0, 1)); faces.Add(new Point3(0, 0, -1)); break;
                    }
                }
				else
				{
                    switch (rotation)
                    {
                        case 1: faces.Add(new Point3(1, -1, 0)); faces.Add(new Point3(-1, 1, 0)); break;
                        case 2: faces.Add(new Point3(0, -1, -1)); faces.Add(new Point3(0, 1, 1)); break;
                        case 3: faces.Add(new Point3(-1, -1, 0)); faces.Add(new Point3(1, 1, 0)); break;
                        default: faces.Add(new Point3(0, -1, 1)); faces.Add(new Point3(0, 1, -1)); break;
                    }
                }
            }
            return faces;
		}
```

---

## 5. Behavior：编辑 / 交互 / 拆除后充能刷新

路径：`GunsCode/SACode/-行为.cs`

### 5.1 物品栏 / 方块编辑

```csharp
		public override bool OnEditInventoryItem(IInventory inventory, int slotIndex, ComponentPlayer componentPlayer)
		{
            int id = Terrain.ExtractContents(inventory.GetSlotValue(slotIndex));
            if (id == SAConveyerBelt.Index)
            {
                if (componentPlayer.DragHostWidget.IsDragInProgress)
                    return false;
                int value = inventory.GetSlotValue(slotIndex);
                int count = inventory.GetSlotCount(slotIndex);
                int data = Terrain.ExtractData(value);
                DialogsManager.ShowDialog(componentPlayer.GuiWidget, new EditConveyerBeltDialog(data, delegate (int newData)
                {
                    int num = Terrain.ReplaceData(value, newData);
                    if (num != value)
                    {
                        inventory.RemoveSlotItems(slotIndex, count);
                        inventory.AddSlotItems(slotIndex, num, count);
                    }
                }));
                return true;
            }
            else if (id == SAInserter.Index)
            {
                componentPlayer.ComponentGui.DisplaySmallMessage("先放置一个置物机再编辑其溢出面", Color.White, false, false);
            }
            return base.OnEditInventoryItem(inventory, slotIndex, componentPlayer);
		}
		public override bool OnEditBlock(int x, int y, int z, int value, ComponentPlayer componentPlayer)
		{
            int id = Terrain.ExtractContents(value);
            int data = Terrain.ExtractData(value);
            if (id == SAConveyerBelt.Index)
			{
                DialogsManager.ShowDialog(componentPlayer.GuiWidget, new EditConveyerBeltDialog(SubsystemTerrain, new Point3(x, y, z), delegate (int newData)
                {
                    if (newData != data && SubsystemTerrain.Terrain.GetCellContents(x, y, z) == id)
                    {
                        int value2 = Terrain.ReplaceData(value, newData);
                        SubsystemTerrain.ChangeCell(x, y, z, value2);
                        SAMachineManager.UpdateBeltState(new List<Point3>(), new Point3(x, y, z), false, SubsystemTerrain);
                    }
                }));
                return true;
            }
```

### 5.2 空手取 / 手持放

```csharp
            ComponentBlockEntity blockEntity = m_subsystemBlockEntities.GetBlockEntity(raycastResult.CellFace.X, raycastResult.CellFace.Y, raycastResult.CellFace.Z);
            if(id == SAConveyerBelt.Index)
			{
                if(handingId != SAConveyerBelt.Index && blockEntity != null)
                {//手持传送带时不触发交互
                    ComponentSAConveyerBelt componentSAConveyerBelt = blockEntity.Entity.FindComponent<ComponentSAConveyerBelt>();
                    if(componentSAConveyerBelt != null)
					{
                        int beltItemCount = componentSAConveyerBelt.GetSlotCount(0);
                        if (handingId <= 0 && beltItemCount > 0)
						{//空手点击，拿走输送带上物品
                            ComponentInventoryBase.AcquireItems(componentMiner.Inventory, componentSAConveyerBelt.GetSlotValue(0), beltItemCount);
                            componentSAConveyerBelt.RemoveSlotItems(0, beltItemCount);
                            return true;
						}
						else if(handingId > 0)
						{//将手上物品放入输送带
                            int handingCount = componentMiner.Inventory.GetSlotCount(componentMiner.Inventory.ActiveSlotIndex);
                            int num2 = componentSAConveyerBelt.AcquireItems(componentMiner.ActiveBlockValue, handingCount);
                            if (num2 < handingCount)
                                m_subsystemAudio.PlaySound("Audio/PickableCollected", 1f, 0f, raycastResult.HitPoint(), 3f, autoDelay: true);
                            componentMiner.RemoveActiveTool(handingCount - num2);
                            return num2 < handingCount;
						}
					}
				}
                return false;
```

### 5.3 拆除后邻带充能重算

```csharp
			else if (id == SAConveyerBelt.Index)
			{
                List<Point3> points = SAMachineManager.GetBeltConnectableFaces(value);
                if (points.Count > 0)
                {
                    foreach (Point3 offset in points)
                    {
                        Point3 point2 = new Point3(x, y, z) + offset;
                        bool powerSupply = false;
                        ComponentBlockEntity blockEntity2 = m_subsystemBlockEntities.GetBlockEntity(point2.X, point2.Y, point2.Z);
                        if(blockEntity2 != null)
						{
                            ComponentSAConveyerBelt componentSAConveyerBelt = blockEntity2.Entity.FindComponent<ComponentSAConveyerBelt>();
                            if (componentSAConveyerBelt != null && componentSAConveyerBelt.PowerSupply)
                                powerSupply = true;
                        }
                        SAMachineManager.UpdateBeltState(new List<Point3>(), point2, powerSupply, SubsystemTerrain);
                    }
                }
```

---

## 6. 踩传送带推动实体

路径：`GunsCode/-Component.cs`（某生物组件 Update 片段）

```csharp
            if (!IsTough && !m_componentCreature.ComponentBody.IsSneaking && m_componentCreature.ComponentBody.StandingOnValue.HasValue && Terrain.ExtractContents(m_componentCreature.ComponentBody.StandingOnValue.Value) == SAConveyerBelt.Index)
			{//踩传送带
                int cellValue = m_componentCreature.ComponentBody.StandingOnValue.Value, cellData = Terrain.ExtractData(cellValue);
                if(SAConveyerBelt.GetPower(cellData) > 0)
				{
                    Point3 point = Terrain.ToCell(m_componentCreature.ComponentBody.Position - 0.2f * Vector3.UnitY);
                    ComponentBlockEntity blockEntity = m_subsystemBlockEntities.GetBlockEntity(point.X, point.Y, point.Z);
                    if (blockEntity != null)
                    {
                        ComponentSAConveyerBelt componentSAConveyerBelt = blockEntity.Entity.FindComponent<ComponentSAConveyerBelt>();
                        if (componentSAConveyerBelt != null)
                        {
                            Ray3 track = componentSAConveyerBelt.MovementTrack;
                            m_componentCreature.ComponentBody.Velocity += dt * 10f * track.Direction;
                        }
                    }
                }
            }
```

---

## 7. 滚动贴图（SubsystemGMItemsBlockBehavior 片段）

路径：`GunsCode/-Subsystem.cs`

### 7.1 字段与加载

```csharp
        private Texture2D m_defaultConveyerBelt;
        private Texture2D m_belt;
        public RenderTarget2D m_animatedConveyerBelt;
        public Texture2D ConveyerBeltTexture
		{
			get
			{
                if (m_animatedConveyerBelt == null)
                    return m_defaultConveyerBelt;
                return m_animatedConveyerBelt;
			}
		}
                m_defaultConveyerBelt = ContentManager.Get<Texture2D>("Textures/Items/ConveyerBelt/ConveyerBelt");
                m_belt = ContentManager.Get<Texture2D>("Textures/Items/ConveyerBelt/Belt");
```

### 7.2 Update 中的 BeltOffset

```csharp
            BeltOffset += 1f * dt;
            if (BeltOffset >= 1f)
            {
                m_dropped = true;
                BeltOffset = 0;
            }
            else if (BeltOffset > 0f && m_dropped)
                m_dropped = false;

        }
        public bool m_dropped;

        public int ChainAnimation;
        public float BeltOffset;
```

### 7.3 DrawAnimatedTextures 中的传送带段

```csharp
            //更新传送带动态贴图
            int width = m_defaultConveyerBelt.Width, height = m_defaultConveyerBelt.Height;
            if (m_animatedConveyerBelt == null)
                m_animatedConveyerBelt = new RenderTarget2D(width, height, 1, ColorFormat.Rgba8888, DepthFormat.None);
            RenderTarget2D renderTarget1 = Display.RenderTarget;
            try
            {
                Display.RenderTarget = m_animatedConveyerBelt;
                Display.Clear(new Vector4(Color.Transparent));
                TexturedBatch2D empty = m_primitivesRenderer2D.TexturedBatch(m_defaultConveyerBelt, useAlphaTest: false, 0, DepthStencilState.None, null, BlendState.AlphaBlend, SamplerState.PointClamp);
                TexturedBatch2D belt = m_primitivesRenderer2D.TexturedBatch(m_belt, useAlphaTest: false, 1, DepthStencilState.None, null, BlendState.AlphaBlend, SamplerState.PointClamp);

                empty.QueueQuad(Vector2.Zero, new Vector2(width, height), 0, Vector2.Zero, Vector2.One, Color.White);

                float beltTop = height * BeltOffset * 0.5f;
                belt.QueueQuad(new Vector2(width * 0.5f, beltTop + height * 0.25f), new Vector2(width, height * 0.75f), 0, Vector2.Zero, new Vector2(1f, 1f - BeltOffset), Color.White);//正方向
                //belt.QueueQuad(new Vector2(width * 0.5f, height * 0.5f - beltTop), new Vector2(width, height * 0.5f - beltTop), new Vector2(width, height * 0.5f), new Vector2(width * 0.5f, height * 0.5f), 0, Vector2.One, new Vector2(0, 1), new Vector2(0, 1f - BeltOffset), new Vector2(1, 1f - BeltOffset), Color.White);//反方向
                if (beltTop != 0)
                {
                    belt.QueueQuad(new Vector2(width * 0.5f, height * 0.25f), new Vector2(width, beltTop + height * 0.25f), 0, new Vector2(0, 1 - BeltOffset), Vector2.One, Color.White);//正方向补偿
                    //belt.QueueQuad(new Vector2(width * 0.5f, 0), new Vector2(width, 0), new Vector2(width, height * 0.5f - beltTop), new Vector2(width * 0.5f, height * 0.5f - beltTop), 0, new Vector2(1, 1 - BeltOffset), new Vector2(0, 1 - BeltOffset), Vector2.Zero, new Vector2(1, 0), Color.White);//反方向补偿
                }
                m_primitivesRenderer2D.Flush();
            }
            finally
            {
                Display.RenderTarget = renderTarget1;
            }
```

---

## 8. 编辑对话框

路径：`GunsCode/Widgets/EditConveyerBeltDialog.cs`

```csharp
using System;
using System.Xml.Linq;
using Engine;

namespace Game
{
    public class EditConveyerBeltDialog : Dialog
    {
		public ButtonWidget m_okButton;
        public ButtonWidget m_cancelButton;
        public ButtonWidget m_shapeButton;
        public ButtonWidget m_reverseButton;
        public ButtonWidget m_rotationButton;
        public Action<int> m_handler;
		public int Data => m_constData ?? Terrain.ExtractData(m_subsystemTerrain.Terrain.GetCellValue(Point.X, Point.Y, Point.Z));
		public SubsystemTerrain m_subsystemTerrain;
		public Point3 Point;
		public int? m_constData;
		public int m_shape;
		public int m_reverse;
		public int m_rotation;

		public EditConveyerBeltDialog(SubsystemTerrain subsystemTerrain, Point3 point, Action<int> handler)
        {
			m_handler = handler;
			m_subsystemTerrain = subsystemTerrain;
			Point = point;
			Initialize();
		}

		public EditConveyerBeltDialog(int data, Action<int> handler)
		{
			m_constData = data;
			m_handler = handler;
			Initialize();
		}

		public void Initialize()
		{
			XElement node = ContentManager.Get<XElement>("Dialogs/EditConveyerBeltDialog");
			LoadContents(this, node);
			m_okButton = Children.Find<ButtonWidget>("OKButton");
			m_cancelButton = Children.Find<ButtonWidget>("CancelButton");
			m_shapeButton = Children.Find<ButtonWidget>("ShapeButton");
			m_reverseButton = Children.Find<ButtonWidget>("ReverseButton");
			m_rotationButton = Children.Find<ButtonWidget>("RotationButton");
			m_shape = SAConveyerBelt.GetShape(Data);
			m_reverse = SAConveyerBelt.GetReverse(Data);
			m_rotation = SAConveyerBelt.GetRotation(Data);
		}

		public override void Update()
        {
			if (m_shapeButton.IsClicked)
				m_shape = (m_shape + 1) % SAConveyerBelt.ShapeName.Length;
			if (m_reverseButton.IsClicked)
				m_reverse = (m_reverse + 1) % SAConveyerBelt.ReverseName.Length;
			if (m_rotationButton.IsClicked)
				m_rotation = (m_rotation + 1) % SAConveyerBelt.RotationName.Length;
			if (m_okButton.IsClicked)
			{
				int newData = SAConveyerBelt.SetShape(SAConveyerBelt.SetReverse(SAConveyerBelt.SetRotation(Data, m_rotation), m_reverse), m_shape);
				Dismiss(newData);
			}
			if (Input.Cancel || m_cancelButton.IsClicked)
			{
				Dismiss(null);
			}
			m_shapeButton.Text = SAConveyerBelt.ShapeName[m_shape];
			m_reverseButton.Text = SAConveyerBelt.ReverseName[m_reverse];
			m_rotationButton.Text = SAConveyerBelt.RotationName[m_rotation];
		}

        public void Dismiss(int? result)
        {
			DialogsManager.HideDialog(this);
			if (m_handler != null && result.HasValue)
			{
				m_handler(result.Value);
			}
		}

	}
}
```

---

## 9. 图鉴文案（玩家向，SA）

路径：`SiyahAkrep/Assets/Entries/Industry/ConveyerBelt.txt`

```
物品输送和分拣
  输送带可以用于传输物品。分拣机可用于分拣物品。

  输送带需要接入电力才能启动，但不需要消耗电力。手持物品点击空的输送带，可将手中的物品放上输送带；若空手点击输送带上的物品则可将物品拿下来。输送带也可以接收掉落物。输送带上的物品不会过期消失。对输送带使用编辑(G)可改变其形态。输送带若将物品运输到了末端，则会将其以掉落物的形式抛出。

  分拣机接收掉落物后，会从合适的输出面将其抛出。请提前为分拣机设定筛选条件：打开分拣机界面并按照输出面的颜色放入要筛选的物品。以下为不同情况下分拣机的筛选规则：

    *若分拣机接收到了与筛选条件相同的物品，则从对应的输出面抛出。
    *若没有设定筛选条件，则所有物品都可通过。
    *若有多个可通过的端口，则在这些端口中轮流抛出，该功能可实现相同物品分流。
    *若没有可通过的端口，则从顶面将物品抛出。
```

---

*摘录完成。开发 Logistics 时以 [输送带实现计划.md](../输送带实现计划.md) 为准；本文仅作 SA 行为与算法对照。*
