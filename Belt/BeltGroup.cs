using Engine;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>
    /// 一条直线（含坡）输送带组。物品库存将挂在 Controller 侧（P1）；P0 仅拓扑。
    /// </summary>
    public sealed class BeltGroup {
        public const float DefaultSpeedAbs = 1f;

        public Guid Id { get; }
        public Point3 Controller { get; set; }
        public List<Point3> Members { get; } = [];
        public int Sign { get; set; } = 1;
        public float SpeedAbs { get; set; } = DefaultSpeedAbs;

        public BeltGroup(Guid id) {
            Id = id;
        }

        public void Write(ValuesDictionary vd) {
            vd.SetValue("Id", Id.ToString("D"));
            vd.SetValue("Controller", Controller);
            vd.SetValue("Sign", Sign);
            vd.SetValue("SpeedAbs", SpeedAbs);
            ValuesDictionary membersVd = new();
            for (int i = 0; i < Members.Count; i++) {
                membersVd.SetValue(i.ToString(), Members[i]);
            }
            vd.SetValue("Members", membersVd);
        }

        public static BeltGroup Read(ValuesDictionary vd) {
            string idStr = vd.GetValue("Id", string.Empty);
            if (!Guid.TryParse(idStr, out Guid id) || id == Guid.Empty) {
                id = Guid.NewGuid();
            }
            var group = new BeltGroup(id) {
                Controller = vd.GetValue("Controller", Point3.Zero),
                Sign = vd.GetValue("Sign", 1) >= 0 ? 1 : -1,
                SpeedAbs = MathF.Max(0f, vd.GetValue("SpeedAbs", DefaultSpeedAbs))
            };
            ValuesDictionary membersVd = vd.GetValue<ValuesDictionary>("Members", null);
            if (membersVd != null) {
                var indexed = new List<(int Index, Point3 Point)>();
                foreach (KeyValuePair<string, object> kv in membersVd) {
                    if (kv.Value is not Point3 p) {
                        continue;
                    }
                    if (!int.TryParse(kv.Key, out int index)) {
                        index = indexed.Count;
                    }
                    indexed.Add((index, p));
                }
                indexed.Sort((a, b) => a.Index.CompareTo(b.Index));
                foreach ((_, Point3 p) in indexed) {
                    group.Members.Add(p);
                }
            }
            return group;
        }
    }
}
