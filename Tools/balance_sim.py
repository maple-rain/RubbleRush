# -*- coding: utf-8 -*-
"""
밸런스 시뮬레이터 — 전략별 처리량과 티어 해금 시점

┌──────────────────────────────────────────────────────────────────────┐
│ 경고: 아래 상수(S, TIER_R, CABLE, R0, C)는 전부 AI가 만든 값이며       │
│       기획자가 승인한 적이 없다. 구조물 6종·티어 5단계라는 전제부터     │
│       AI 창작이다. 기획자 원안은 "기둥 / 벽면 2종"이다.                │
│                                                                      │
│ 이 스크립트가 검증하는 것: 주어진 값들이 주어진 구조 안에서             │
│                            내적으로 일관적인가.                        │
│ 검증하지 않는 것: 구조가 옳은가, 게임이 재미있는가.                    │
│                                                                      │
│ 모델 미반영 항목은 docs/GAME_DESIGN.md §13 참조.                      │
│ 값을 바꿀 때는 아래 상수만 고치면 된다.                                │
└──────────────────────────────────────────────────────────────────────┘
"""
import sys, io, math
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

C = dict(move_speed=6.0, penalty=0.12, speed_min=2.5, time_limit=180.0,
         d_target=10.0, d_hole=15.0, stun=2.5, batch_fail=0.10)

# name, tier, breakTime, units, scatter, collapseRisk
S = [
    ("판자",     0, 0.8,  2, 1.2, 0.00),
    ("벽면",     1, 1.5,  4, 1.8, 0.00),
    ("배관",     1, 2.0,  5, 2.4, 0.00),
    ("기둥",     2, 4.0, 12, 3.5, 0.35),
    ("철골",     3, 5.5, 20, 4.2, 0.40),
    ("건물코어", 4, 8.0, 32, 5.0, 0.45),
]
TIER_R = [1.5, 2.2, 3.2, 4.5, 6.5]
CABLE  = 55.0
R0     = 1.5

def loop_len(sc): return 2*math.pi*(sc+1.0)
def spd(load): return max(C["speed_min"], C["move_speed"] - C["penalty"]*load)

def cycle(s, batch):
    _, tier, bt, u1, scat, risk = s
    u = u1*batch
    t = 0.0
    for _ in range(batch):
        t += C["d_target"]/spd(0) + bt + risk*C["stun"]
    L = loop_len(scat*math.sqrt(batch))
    if L > CABLE: return None, 0
    t += L/spd(0) * (1 + C["batch_fail"]*(batch-1))
    t += C["d_hole"]/spd(u)
    return t, u

def radius(total, k):
    V0 = math.pi*R0*R0
    return math.sqrt((V0 + k*total)/math.pi)

def run(pool, batch, k):
    t, total = 0.0, 0
    unl, guard, timeline = {}, 0, []
    while t < C["time_limit"] and guard < 4000:
        guard += 1
        r = radius(total, k)
        avail = [s for s in pool if r >= TIER_R[s[1]]-1e-9] or [pool[0]]
        best = None
        for s in avail:
            ct, u = cycle(s, batch)
            if ct is None:
                ct, u = cycle(s, 1)
                if ct is None: continue
            if best is None or u/ct > best[0]: best = (u/ct, ct, u, s[0], s[1])
        if best is None: break
        _, ct, u, nm, tier = best
        if t+ct > C["time_limit"]: break
        rb = r; t += ct; total += u; r = radius(total, k)
        for ti, need in enumerate(TIER_R):
            if rb < need <= r: unl[ti] = round(t,1)
        timeline.append(nm)
    return total, round(radius(total,k),2), unl, timeline

def show(k):
    print(f"\n{'='*74}\n면적 성장 k = {k}   (반경 = sqrt((pi*1.5^2 + k*누적)/pi))\n{'='*74}")
    print(f"  {'대상':<10}{'단위':>5}{'사이클':>8}{'단위/초':>9}{'고리m':>8}")
    rates = []
    for s in S:
        ct, u = cycle(s, 1)
        rates.append(u/ct)
        print(f"  {s[0]:<10}{u:>5}{ct:>8.1f}{u/ct:>9.2f}{loop_len(s[4]):>8.1f}")
    print(f"  효율 격차 (최고/최저): {max(rates)/min(rates):.2f}배")

    print(f"\n  티어 해금에 필요한 누적 단위:")
    for ti, need in enumerate(TIER_R):
        u_need = (math.pi*need*need - math.pi*R0*R0)/k
        print(f"    T{ti} (반경 {need}m) → {u_need:6.0f}단위")

    plays = [("A 벽면만",[S[1]],1), ("B 기둥 위주",[S[3]],1),
             ("C 몰아서 3개",S,3), ("D 최고 티어",S,1)]
    print(f"\n  {'전략':<16}{'처리량':>8}{'최종반경':>10}   해금")
    res=[]
    for lab,pool,b in plays:
        tot,r,unl,tl = run(pool,b,k); res.append((lab,tot,r,unl,tl))
        u=" ".join(f"T{a}@{v}s" for a,v in sorted(unl.items())) or "없음"
        print(f"  {lab:<16}{tot:>8}{r:>10}   {u}")
    best=max(res,key=lambda x:x[1]); a=res[0][1]; b=res[1][1]
    print(f"  >> 최적 {best[0]} ({best[1]})  최적/안전 {best[1]/max(1,a):.2f}배  기둥/벽면 {b/max(1,a):.2f}배")
    print(f"  >> 최적 전략 진행: {' '.join(best[4][:18])}")
    return res

for k in (0.55, 0.80, 1.10):
    show(k)
