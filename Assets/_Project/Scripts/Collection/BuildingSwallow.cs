using Demo.Core;
using Demo.Data;
using Demo.Destruction;
using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 구멍이 <b>건물을 통째로 삼킨다</b> [지시 2026-10-15]. 6단계 업그레이드다.
    ///
    /// CLAUDE.md 첫 줄의 게임 설명 — "후반에는 부수지 않고 건물을 통째로 밀어넣는다" —
    /// 이 그 기능이고, 여기 오기 전까지 비어 있었다.
    ///
    /// <b>구멍과 건물의 크기 관계가 한쪽으로 정해져 있지 않다.</b> 실측: 6단계 구멍
    /// 반지름 4.00m, 건물 토대 외접반지름은 작은 것 1.08m에서 큰 것 8.07m다.
    /// 즉 <b>작은 건물보다는 크고 큰 건물보다는 작다.</b> 그래서 잔해의 판정
    /// (<see cref="Hole"/>)을 그대로 쓸 수 없고, 두 가지가 다르다:
    ///
    /// 1. <b>어느 쪽이 품든 집는다.</b> 건물이 구멍 위에 올라와 있거나, 구멍이 건물
    ///    밑에 들어가 있으면 된다. 한쪽만 보면 반대 크기 관계에서 영영 안 집힌다
    ///    (실제로 그렇게 났다 — 아래 Tick 주석).
    /// 2. <b>크기 한도가 따로 있다.</b> 구멍 반지름의 <see cref="GameConfig.swallowReach"/>배
    ///    까지만 넘어뜨린다. 그래야 "작은 것부터 삼켜 간다"가 읽힌다.
    ///
    /// 넘어가는 움직임은 <see cref="ToppleFall"/>이 맡는다.
    ///
    /// <b>Hole에 넣지 않고 따로 둔 이유</b>: Hole.cs가 이미 333줄이다 (CLAUDE.md 규칙 3).
    /// 건물 삼킴은 잔해 흡인과 엮이지 않는 자족적인 덩어리라 경계가 깨끗하다.
    /// </summary>
    public static class BuildingSwallow
    {
        /// <summary>
        /// 한 프레임 본다. 삼킨 건물 수를 돌려준다 (보통 0).
        /// </summary>
        /// <param name="hole">구멍</param>
        /// <param name="cfg">값들</param>
        /// <param name="onSwallowed">삼켰을 때. 점수·경험치·소리는 부른 쪽이 맡는다</param>
        public static int Tick(Hole hole, GameConfig cfg, System.Action<Destructible, int> onSwallowed)
        {
            if (hole == null || cfg == null) return 0;
            if (!Upgrades.Has(Upgrades.Swallow)) return 0;      // 6단계 카드를 안 골랐다

            var list = Destructible.Active;
            if (list == null || list.Count == 0) return 0;

            Vector3 at = hole.transform.position;
            float r = hole.Radius;
            float limit = r * Mathf.Max(1f, cfg.swallowReach);   // 넘어뜨릴 수 있는 최대 토대 반지름

            // <b>지금 부수고 있는 건물은 건드리지 않는다</b> [고침 2026-10-15].
            //
            // 구멍은 파괴 화면에 들어가도 계속 떠돈다. 그러다 <b>플레이어가 때리고 있는
            // 바로 그 건물</b> 밑에 닿으면 ToppleFall이 그것을 기울이고 가라앉혀 버렸다.
            // 화면은 타격 중인데 대상이 통째로 땅으로 꺼지고, 그 자리에 맞춰 뻗던
            // 로봇의 팔까지 같이 들어갔다 — 기획자: "타격화면 중에 구멍에 들어가면
            // 그에 맞춰 같이 팔도 땅으로 들어가버림".
            //
            // 모드로 막지 않고 <b>대상 하나만</b> 뺀다. 파괴 화면에 있는 동안에도 구멍이
            // 다른 건물을 삼키는 것은 멀쩡한 동작이고, 막으면 그만큼 멈춰 보인다.
            var mode = Core.GameModeController.Instance;
            Destructible busy = mode != null ? mode.Target : null;

            int took = 0;

            // 뒤에서부터 — 넘어가기 시작하면 목록이 바뀔 수 있다.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Destructible d = list[i];
                if (d == null || !d.isActiveAndEnabled) continue;
                if (d == busy) continue;                         // 지금 때리고 있는 건물
                if (ToppleFall.IsFalling(d)) continue;           // 이미 넘어가는 중

                d.FoundationFootprint(out Vector3 center, out float halfX, out float halfZ);
                float footprint = Mathf.Sqrt(halfX * halfX + halfZ * halfZ);
                if (footprint > limit) continue;                 // 아직 이 구멍엔 너무 크다

                // <b>둘 중 하나면 집는다</b> [고침 2026-10-15].
                //
                // 전에는 "구멍 중심이 토대 안에"만 봤다. 그것은 구멍이 건물보다 <b>작을 때</b>를
                // 전제로 만든 조건인데, 구멍을 지름 8m로 키우면서 관계가 뒤집혔다 —
                // 작은 건물이 구멍 안에 통째로 들어가 있어도 구멍 중심이 그 밑이 아니면
                // 영영 안 집혔다. 화면에 건물이 구멍 위에 앉아만 있던 것이 그것이다.
                //
                //   ① 건물이 구멍 위에 올라와 있다 — 구멍이 건물보다 클 때
                //   ② 구멍이 건물 밑에 들어가 있다 — 건물이 구멍보다 클 때
                //
                // 어느 쪽이든 "삼킬 수 있는 자리"다. 스치기만 한 것은 둘 다 아니다.
                float dx = at.x - center.x, dz = at.z - center.z;
                bool overHole = dx * dx + dz * dz <= r * r;                   // ①
                bool underBuilding = Mathf.Abs(dx) <= halfX && Mathf.Abs(dz) <= halfZ;   // ②
                if (!overHole && !underBuilding) continue;

                Destructible taken = d;
                int units = UnitsOf(taken, cfg);
                ToppleFall.Begin(taken, at, cfg.swallowTiltSeconds, cfg.swallowSinkSeconds,
                                 cfg.swallowTiltDegrees, r * 2f,
                                 _ => onSwallowed?.Invoke(taken, units));
                took++;
            }
            return took;
        }

        /// <summary>
        /// 건물 한 채가 몇 단위인가. <b>부숴서 나왔을 잔해와 같은 값</b>이어야 한다 —
        /// 통째로 삼키는 쪽이 이득이면 부수는 행동이 의미를 잃고, 손해면 카드를 고를 이유가 없다.
        ///
        /// <see cref="StructureData.TotalDebrisUnits"/>가 이미 그 값이다.
        /// </summary>
        static int UnitsOf(Destructible d, GameConfig cfg)
        {
            StructureData s = d != null ? d.Data : null;
            return s == null ? 1 : Mathf.Max(1, s.TotalDebrisUnits);
        }
    }
}
