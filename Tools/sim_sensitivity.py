# -*- coding: utf-8 -*-
"""내가 지어낸 값과 구조를 쓸어 보고, 결론이 거기에 기대고 있는지 본다 [지시 2026-10-02].

<b>왜 필요한가.</b> 11·13·14를 구현하면서 숫자 스무 개쯤을 내가 정했다 —
감속률 0.45, 유예 0.35초, 판정 반경 배율 0.8, 고리 여유 0.5m, 설 자리 2.0m,
그리고 무엇보다 <b>플레이어가 어떤 순서로 건물을 치는가</b>(탐욕 정책)다.
전부 [제안·미승인]이라고 적어 두긴 했지만, <b>보고한 측정값들이 그 값들로 계산된 것</b>이라
"수치는 미승인"이라는 꼬리표만으로는 부족하다.

<b>이 파일이 하는 일</b>: 각 사견을 넓게 쓸어서 <b>결론의 순위가 뒤집히는지</b>를 본다.
순위가 유지되면 그 결론은 내 값에 기대지 않는 것이고, 뒤집히면 결론이 아니라 <b>내 취향</b>이었다.

<b>이 파일이 못 하는 일</b>: 어느 값이 옳은지는 못 정한다. 그건 기획자가 플레이해서 정한다.
"""
import sys, math, itertools, statistics
import sim_world as W
from sim_world import CFG
import sim_foundation as F
import sim_snag as S

POLICIES = ['greedy', 'nearest', 'biggest', 'quickest', 'farthest']


def run(resp, found, policy='greedy'):
    s = S.SnagSim(resp, foundation=found)
    s.policy = policy
    return s.run(cap=1e9)


def rank(vals):
    """값들의 순위(0이 가장 작음). 절대값이 흔들려도 순위가 같으면 결론은 산다."""
    order = sorted(range(len(vals)), key=lambda i: vals[i])
    out = [0] * len(vals)
    for pos, i in enumerate(order):
        out[i] = pos
    return tuple(out)


def sweep_14():
    """14번 — 감속률·유예·판정반경을 쓸었을 때 반응들의 순위가 유지되는가."""
    print('=' * 94)
    print('[1] 14번 — 내가 정한 세 숫자를 쓸어 본다')
    print('=' * 94)
    print('기준값: slowFactor 0.45 · graceSeconds 0.35 · radiusScale 0.80  (전부 내가 지어냈다)')
    print()
    base_s, base_g, base_r = S.SLOW_FACTOR, S.GRACE_SECONDS, S.RADIUS_SCALE
    ranks = {}
    print('%-34s %s' % ('(slowFactor, grace, radiusScale)',
                        ''.join('%11s' % r for r in S.RESPONSES)))
    for sf, gr, rs in itertools.product((0.25, 0.45, 0.70), (0.15, 0.35, 0.80), (0.5, 0.8, 1.2)):
        S.SLOW_FACTOR, S.GRACE_SECONDS, S.RADIUS_SCALE = sf, gr, rs
        vals = [run(r, 'A_그대로').t for r in S.RESPONSES]
        base = vals[0]
        ranks.setdefault(rank(vals), []).append((sf, gr, rs))
        if (sf, gr, rs) in ((0.45, 0.35, 0.8), (0.25, 0.15, 1.2), (0.70, 0.80, 0.5)):
            print('%-34s %s' % ('(%.2f, %.2f, %.2f)' % (sf, gr, rs),
                               ''.join('%10.0f초' % (v - base) for v in vals)))
    S.SLOW_FACTOR, S.GRACE_SECONDS, S.RADIUS_SCALE = base_s, base_g, base_r
    print()
    print('27가지 조합에서 나온 서로 다른 순위: %d가지' % len(ranks))
    for r, who in sorted(ranks.items(), key=lambda kv: -len(kv[1])):
        names = [S.RESPONSES[i] for i in sorted(range(len(r)), key=lambda i: r[i])]
        print('   %2d/27 : %s' % (len(who), ' < '.join(names)))


def sweep_policy():
    """11번 — 플레이어가 어떤 순서로 치든 토대 규칙의 순위가 유지되는가."""
    print()
    print('=' * 94)
    print('[2] 11번 — 플레이어 정책을 바꿔도 토대 규칙의 순위가 유지되는가')
    print('=' * 94)
    found = list(F.OPTIONS)
    print('%-10s %s %s' % ('정책', ''.join('%11s' % f for f in found), '   순위'))
    ranks = {}
    for pol in POLICIES:
        vals = [run('Pass', f, pol).t for f in found]
        base = vals[0]
        r = rank(vals)
        ranks.setdefault(r, []).append(pol)
        print('%-10s %s   %s' % (pol, ''.join('%10.1f초' % (v - base) for v in vals),
                                 ' < '.join(found[i] for i in sorted(range(len(r)), key=lambda i: r[i]))))
    print()
    print('정책 %d가지에서 나온 서로 다른 순위: %d가지' % (len(POLICIES), len(ranks)))


def sweep_model():
    """내가 정한 모형 상수(고리 여유·설 자리)를 쓸었을 때 선택지 간 차이가 유지되는가."""
    print()
    print('=' * 94)
    print('[3] 모형 상수 — 고리 여유와 설 자리를 바꿔 본다')
    print('=' * 94)
    print('둘 다 내가 지어낸 값이다. 절대 클리어 타임은 당연히 움직인다 —')
    print('보려는 것은 <b>선택지 사이의 차이</b>가 살아남는가다.')
    print()
    src = open(F.__file__, encoding='utf-8').read()
    found = list(F.OPTIONS)
    print('%-22s %10s %s' % ('(고리여유, 설자리)', 'A 절대', ''.join('%11s' % f for f in found[1:])))
    for margin, stand in ((0.0, 1.0), (0.5, 2.0), (1.2, 3.0)):
        F._LOOP_MARGIN, F._STAND_GAP = margin, stand
        vals = [run('Pass', f).t for f in found]
        print('%-22s %9.1f초 %s' % ('(%.1f, %.1f)' % (margin, stand), vals[0],
                                    ''.join('%10.1f초' % (v - vals[0]) for v in vals[1:])))
    F._LOOP_MARGIN, F._STAND_GAP = 0.5, 2.0


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sweep_14()
    sweep_policy()
