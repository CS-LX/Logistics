namespace Logistics {
    /// <summary>落料斗抽槽模式。</summary>
    public enum HopperExtractMode {
        /// <summary>优先出料区，无候选则全槽。</summary>
        OutputPreferred = 0,
        /// <summary>仅出料区；无声明则不抽。</summary>
        OutputOnly = 1,
        /// <summary>整库顺序。</summary>
        EntireInventory = 2
    }
}
