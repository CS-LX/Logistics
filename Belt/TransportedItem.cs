using Engine;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>Group 上的一件在途物（非世界 Pickable）。</summary>
    public sealed class TransportedItem {
        public int Value;
        public int Count = 1;
        public float BeltPosition;
        public float SideOffset;
        public Vector3 Velocity;

        public void Write(ValuesDictionary vd) {
            vd.SetValue("Value", Value);
            vd.SetValue("Count", Count);
            vd.SetValue("BeltPosition", BeltPosition);
            vd.SetValue("SideOffset", SideOffset);
            vd.SetValue("Velocity", Velocity);
        }

        public static TransportedItem Read(ValuesDictionary vd) {
            return new TransportedItem {
                Value = vd.GetValue("Value", 0),
                Count = Math.Max(1, vd.GetValue("Count", 1)),
                BeltPosition = vd.GetValue("BeltPosition", 0f),
                SideOffset = vd.GetValue("SideOffset", 0f),
                Velocity = vd.GetValue("Velocity", Vector3.Zero)
            };
        }
    }
}
