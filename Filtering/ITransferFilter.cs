namespace Logistics.Filtering {
    /// <summary>
    /// 转移过滤契约。主机只调用 <see cref="Allows"/>；
    /// 自定义过滤（清单物品、组合条件等）实现本接口即可接入。
    /// </summary>
    public interface ITransferFilter {
        FilterMode Mode { get; }

        bool IsEmpty { get; }

        /// <summary>名单是否点名该 Value（与白/黑模式无关）。</summary>
        bool Matches(int value);

        /// <summary>
        /// 是否允许转移。默认：空名单全通；白名单看 Matches；黑名单看 !Matches。
        /// 特殊策略可自行覆写，不必走 Mode/Matches。
        /// </summary>
        bool Allows(int value) {
            if (IsEmpty) {
                return true;
            }
            return Mode == FilterMode.Allow ? Matches(value) : !Matches(value);
        }
    }
}
