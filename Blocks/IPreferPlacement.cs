namespace Logistics {
    /// <summary>
    /// 手持时优先对目标方块执行放置，而非打开其交互界面。
    /// 物流侧可交互方块的 <c>OnInteract</c> 经 <see cref="SCIENEW.BlockInterfaceResolver"/> 检测到本接口后返回 false。
    /// </summary>
    public interface IPreferPlacement {
    }
}
