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
    /// <b>구멍이 건물보다 작다.</b> 실측: 6단계 구멍 반지름 1.74m, 건물 토대 외접반지름은
    /// 작은 것 1.08m에서 큰 것 8.07m다. 그래서 "구멍 안에 들어오면 삼킨다"는
    /// 잔해의 판정(<see cref="Hole"/>)을 그대로 쓸 수 없다. 두 가지가 다르다:
    ///
    /// 1. <b>닿기만 해도 된다.</b> 건물은 움직이지 않으므로 구멍이 건물 쪽으로 가서 만난다.
    ///    토대와 구멍이 겹치면 집는다.
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

            int took = 0;

            // 뒤에서부터 — 넘어가기 시작하면 목록이 바뀔 수 있다.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Destructible d = list[i];
                if (d == null || !d.isActiveAndEnabled) continue;
                if (ToppleFall.IsFalling(d)) continue;           // 이미 넘어가는 중

                d.FoundationFootprint(out Vector3 center, out float halfX, out float halfZ);
                float footprint = Mathf.Sqrt(halfX * halfX + halfZ * halfZ);
                if (footprint > limit) continue;                 // 아직 이 구멍엔 너무 크다

                // <b>구멍이 건물 밑에 들어가 있어야 한다</b> [고침 2026-10-15].
                //
                // 전에는 "토대와 구멍이 닿기만 하면"이었다. 그러니 구멍이 모서리를 살짝
                // 스치기만 해도 건물이 넘어가기 시작했고, 건물은 구멍보다 훨씬 넓으니
                // <b>구멍이 아니라 맨땅으로 가라앉는 것처럼</b> 보였다.
                // 기획자: "구멍에 들어가야하는거지 땅 속으로 들어가는건 아니니까."
                //
                // 구멍 <b>중심</b>이 토대 안에 들어와야 집는다. 그래야 건물 밑으로
                // 구멍을 몰고 들어가는 동작이 생기고 — 그게 "요리조리 움직여서 넣는" 재미다.
                if (Mathf.Abs(at.x - center.x) > halfX) continue;
                if (Mathf.Abs(at.z - center.z) > halfZ) continue;

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
