using Engine;
using Game;

namespace Logistics {
    /// <summary>
    /// 在途物推进与去向：定速 + Sign 前进、末端直角交接、交接不成则弹出掉落物、吸入掉落物、
    /// 以及把站在运转带上的身体沿切向推走。无机械能时在途物停住但仍可被抓取机抽塞。
    /// </summary>
    public sealed class BeltTransportSimulator {
        /// <summary>对齐 SA：站立加速度系数。</summary>
        const float CreaturePushAcceleration = 10f;

        readonly BeltGroupRegistry m_registry;
        readonly BeltTopology m_topology;
        readonly BeltPowerSensor m_power;
        readonly SubsystemTerrain m_subsystemTerrain;
        readonly SubsystemPickables m_subsystemPickables;
        readonly SubsystemGameInfo m_subsystemGameInfo;
        readonly SubsystemBodies m_subsystemBodies;
        readonly List<TransportedItem> m_ejectBuffer = [];

        public BeltTransportSimulator(
            BeltGroupRegistry registry,
            BeltTopology topology,
            BeltPowerSensor power,
            SubsystemTerrain subsystemTerrain,
            SubsystemPickables subsystemPickables,
            SubsystemGameInfo subsystemGameInfo,
            SubsystemBodies subsystemBodies) {
            m_registry = registry;
            m_topology = topology;
            m_power = power;
            m_subsystemTerrain = subsystemTerrain;
            m_subsystemPickables = subsystemPickables;
            m_subsystemGameInfo = subsystemGameInfo;
            m_subsystemBodies = subsystemBodies;
        }

        public void TickInventories(float dt) {
            foreach (BeltGroup group in m_registry.Groups) {
                if (group.Inventory.Count == 0) {
                    continue;
                }
                // 无机械能：在途物停住，不推进、不末端弹出；臂仍可抽塞
                if (!m_power.IsGroupRunning(group)) {
                    foreach (TransportedItem item in group.Inventory.Items) {
                        item.Velocity = Vector3.Zero;
                    }
                    continue;
                }
                float length = BeltPath.TotalLength(group, m_subsystemTerrain);
                foreach (TransportedItem item in group.Inventory.Items) {
                    if (!BeltPath.TryGetWorldPose(group, item.BeltPosition, item.SideOffset, m_subsystemTerrain, out _, out Vector3 tangent)) {
                        continue;
                    }
                    Vector3 travel = group.Sign >= 0 ? tangent : -tangent;
                    Vector3 desired = travel * group.SpeedAbs;
                    item.Velocity = Vector3.Lerp(item.Velocity, desired, MathF.Min(1f, dt * 8f));
                }
                m_ejectBuffer.Clear();
                group.Inventory.Tick(group.Sign, group.SpeedAbs, length, dt, m_ejectBuffer);
                foreach (TransportedItem item in m_ejectBuffer) {
                    HandleBeltEnd(group, item);
                }
            }
        }

        /// <summary>站在运转中的输送带上且非潜行 → 沿带切向加速（含坡）。</summary>
        public void PushStandingBodies(float dt) {
            if (m_registry.Count == 0 || dt <= 0f) {
                return;
            }
            foreach (ComponentBody body in m_subsystemBodies.Bodies) {
                if (body.IsSneaking || !body.StandingOnValue.HasValue) {
                    continue;
                }
                int standingValue = body.StandingOnValue.Value;
                if (Terrain.ExtractContents(standingValue) != m_topology.BeltIndex) {
                    continue;
                }
                Point3 cell = Terrain.ToCell(body.Position - 0.2f * Vector3.UnitY);
                if (!m_registry.TryGetAt(cell, out BeltGroup group) || !m_power.IsGroupRunning(group)) {
                    continue;
                }
                if (!BeltPath.TryGetMemberCenterBeltPosition(group, cell, m_subsystemTerrain, out float center, out _)) {
                    continue;
                }
                if (!BeltPath.TryGetWorldPose(group, center, 0f, m_subsystemTerrain, out _, out Vector3 tangent)) {
                    continue;
                }
                Vector3 travel = group.Sign >= 0 ? tangent : -tangent;
                if (travel.LengthSquared() < 1e-8f) {
                    continue;
                }
                body.Velocity += dt * CreaturePushAcceleration * Vector3.Normalize(travel);
            }
        }

        /// <summary>将掉落物吸入对应 Group；成功则标记 ToRemove。</summary>
        public bool TryAbsorbWorldItem(Point3 cell, WorldItem worldItem) {
            if (worldItem == null || worldItem.ToRemove || !m_registry.TryGetAt(cell, out BeltGroup group)) {
                return false;
            }
            // 与玩家自动拾取相同的等待期，避免刚弹出的掉落物立刻被吸回
            if (worldItem is Pickable ageCheck) {
                double age = m_subsystemGameInfo.TotalElapsedGameTime - ageCheck.CreationTime;
                if (age < ageCheck.TimeWaitToAutoPick) {
                    return false;
                }
            }
            int count = worldItem is Pickable pickable ? pickable.Count : 1;
            if (count <= 0) {
                return false;
            }
            float beltPos = BeltPath.WorldToBeltPosition(group, worldItem.Position, m_subsystemTerrain);
            var item = new TransportedItem {
                Value = worldItem.Value,
                Count = count,
                BeltPosition = beltPos,
                SideOffset = 0f,
                Velocity = worldItem.Velocity
            };
            if (!group.Inventory.TryInsert(item)) {
                return false;
            }
            if (worldItem is Pickable p) {
                p.Count = 0;
            }
            worldItem.ToRemove = true;
            return true;
        }

        /// <summary>末端：优先正交滑入邻组，否则弹出 Pickable。</summary>
        void HandleBeltEnd(BeltGroup group, TransportedItem item) {
            if (TryHandoffToOrthogonal(group, item)) {
                return;
            }
            EjectAsPickable(group, item);
        }

        /// <summary>末端直角邻接另一 Group 时滑入，继承速度并带 SideOffset。</summary>
        bool TryHandoffToOrthogonal(BeltGroup source, TransportedItem item) {
            if (source.Members.Count == 0) {
                return false;
            }
            Point3 exitCell = source.Sign >= 0 ? source.Members[^1] : source.Members[0];
            if (!m_topology.TryGetRotation(exitCell, out int exitRotation)) {
                return false;
            }
            bool exitAlongZ = (exitRotation & 1) == 0;

            float sourceLength = BeltPath.TotalLength(source, m_subsystemTerrain);
            float posePos = source.Sign >= 0
                ? MathF.Min(item.BeltPosition, sourceLength)
                : MathF.Max(item.BeltPosition, 0f);
            if (!BeltPath.TryGetWorldPose(source, posePos, item.SideOffset, m_subsystemTerrain, out Vector3 exitPos, out Vector3 exitTangent)) {
                return false;
            }
            Vector3 sourceTravel = source.Sign >= 0 ? exitTangent : -exitTangent;
            Vector3 inheritVelocity = item.Velocity.LengthSquared() > 1e-4f
                ? item.Velocity
                : sourceTravel * source.SpeedAbs;

            BeltGroup bestTarget = null;
            float bestEntryPos = 0f;
            float bestSide = 0f;
            Vector3 bestTravel = default;
            float bestScore = float.MaxValue;

            foreach (Point3 n in EnumerateForwardCells(exitCell, exitAlongZ, sourceTravel)) {
                if (!m_registry.TryGetAt(n, out BeltGroup target) || target.Id == source.Id) {
                    continue;
                }
                if (!m_topology.TryGetRotation(n, out int nRotation)) {
                    continue;
                }
                bool nAlongZ = (nRotation & 1) == 0;
                // 直角：轴正交（同轴应已在同组）
                if (exitAlongZ == nAlongZ) {
                    continue;
                }
                if (!BeltPath.TryGetMemberCenterBeltPosition(target, n, m_subsystemTerrain, out float entryCenter, out _)) {
                    continue;
                }
                float targetLength = BeltPath.TotalLength(target, m_subsystemTerrain);
                const float inset = 0.12f;
                float entryPos = target.Sign >= 0
                    ? MathF.Min(entryCenter, MathF.Max(0f, targetLength - inset))
                    : MathF.Max(entryCenter, MathF.Min(targetLength, inset));

                if (!BeltPath.TryGetWorldPose(target, entryPos, 0f, m_subsystemTerrain, out Vector3 entryWorld, out Vector3 entryTangent)) {
                    continue;
                }
                Vector3 targetTravel = target.Sign >= 0 ? entryTangent : -entryTangent;
                // 目标推进应大致离开出口（避免立刻顶回）
                Vector3 leave = entryWorld - exitPos;
                if (leave.LengthSquared() > 1e-6f && Vector3.Dot(Vector3.Normalize(leave), targetTravel) < -0.25f) {
                    continue;
                }

                // SideOffset 由绘制侧按「路径切向」解释（见 BeltPath.TryGetWorldPose），
                // 这里必须用同一基准；用行进方向会让反向组的入带侧左右颠倒。
                Vector3 lateral = Vector3.Cross(Vector3.UnitY, entryTangent);
                if (lateral.LengthSquared() < 1e-6f) {
                    lateral = Vector3.UnitX;
                }
                else {
                    lateral = Vector3.Normalize(lateral);
                }
                float side = Math.Clamp(Vector3.Dot(exitPos - entryWorld, lateral), -0.45f, 0.45f);
                float score = Vector3.DistanceSquared(exitPos, entryWorld);
                if (score >= bestScore) {
                    continue;
                }
                bestScore = score;
                bestTarget = target;
                bestEntryPos = entryPos;
                bestSide = side;
                bestTravel = targetTravel;
            }

            if (bestTarget == null) {
                return false;
            }

            item.BeltPosition = bestEntryPos;
            item.SideOffset = bestSide;
            item.Velocity = Vector3.Lerp(inheritVelocity, bestTravel * bestTarget.SpeedAbs, 0.35f);
            if (bestTarget.Inventory.TryInsert(item)) {
                return true;
            }
            // 间距占满：塞回源末端排队；仍失败则交给弹出
            float sourceLengthClamp = BeltPath.TotalLength(source, m_subsystemTerrain);
            item.BeltPosition = source.Sign >= 0
                ? MathF.Max(0f, sourceLengthClamp - 0.05f)
                : MathF.Min(sourceLengthClamp, 0.05f);
            item.SideOffset = 0f;
            item.Velocity = inheritVelocity;
            return source.Inventory.TryInsert(item);
        }

        /// <summary>
        /// 只认出口格「正前方」的一格（坡道再带上下一层）：带子指着空地就该把物品抛下，
        /// 侧面或背后贴着的横向带不得截走。
        /// </summary>
        static IEnumerable<Point3> EnumerateForwardCells(Point3 exitCell, bool alongZ, Vector3 travel) {
            int dx = 0;
            int dz = 0;
            if (alongZ) {
                if (MathF.Abs(travel.Z) < 1e-4f) {
                    yield break;
                }
                dz = travel.Z > 0f ? 1 : -1;
            }
            else {
                if (MathF.Abs(travel.X) < 1e-4f) {
                    yield break;
                }
                dx = travel.X > 0f ? 1 : -1;
            }
            yield return new Point3(exitCell.X + dx, exitCell.Y, exitCell.Z + dz);
            yield return new Point3(exitCell.X + dx, exitCell.Y + 1, exitCell.Z + dz);
            yield return new Point3(exitCell.X + dx, exitCell.Y - 1, exitCell.Z + dz);
        }

        /// <summary>对齐抓取机吐出思路，但缩小偏置/初速，减少带上图标→掉落物的断层感。</summary>
        void EjectAsPickable(BeltGroup group, TransportedItem item) {
            float length = BeltPath.TotalLength(group, m_subsystemTerrain);
            float posePos = group.Sign >= 0
                ? MathF.Min(item.BeltPosition, length)
                : MathF.Max(item.BeltPosition, 0f);
            if (!BeltPath.TryGetWorldPose(group, posePos, item.SideOffset, m_subsystemTerrain, out Vector3 pos, out Vector3 tangent)) {
                pos = new Vector3(group.Members[^1]) + new Vector3(0.5f, BeltPath.ItemCenterHeight, 0.5f);
                tangent = Vector3.UnitZ;
            }
            Vector3 travel = group.Sign >= 0 ? tangent : -tangent;
            Vector3 spawn = pos + travel * 0.2f;
            Vector3 velocity = item.Velocity.LengthSquared() > 1e-4f
                ? item.Velocity + Vector3.UnitY * 0.04f
                : travel * MathF.Max(group.SpeedAbs, 0.8f) + Vector3.UnitY * 0.04f;
            m_subsystemPickables.AddPickable(item.Value, item.Count, spawn, velocity, null);
        }
    }
}
