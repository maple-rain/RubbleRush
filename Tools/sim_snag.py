# -*- coding: utf-8 -*-
"""끌려오던 잔해가 건물에 걸렸을 때 — 다섯 반응의 클리어 타임 [지시 2026-10-01 · 14번].

<b>14번은 잔해끼리가 아니다.</b> 기획자 정정: "잔해가 건물이나 장해물과 부딪혔을 때".
그래서 N체 문제가 아니라 <b>움직이는 쪽(잔해) 대 고정된 쪽(건물 30채)</b>이다.

<b>이 도구가 재는 것</b>: 운반 경로에서 짐이 건물을 얼마나 스치고, 각 반응이 시간을 얼마나 먹는가.
<b>재지 않는 것</b>: 억울함. 걸려서 잃는 것이 '내 실수'로 읽히는지 '조작 불능'으로 읽히는지는
사람이 플레이해야 나온다.

<b>모형의 한계 — 반드시 같이 읽을 것</b>
  · 시뮬레이터의 플레이어는 <b>자기만</b> 피해 다닌다. 짐까지 생각해서 돌아가지 않는다.
    실제 플레이어는 짐이 걸리는 것을 보고 더 크게 돈다 → 여기 걸림 빈도는 <b>상한</b>이다.
  · 떨군 조각은 <b>그 자리에서 바로 다시 줍는다</b>고 본다 (고리 한 번 값).
    못 보고 지나치는 경우를 빼므로 잃는 시간은 <b>하한</b>이다.
  → 즉 실제 값은 여기 나온 빈도보다 낮고, 시간 손실은 여기 나온 값보다 크다.
"""
import sys, math
from sim_world import CFG, PLAYER_R, Rect, path_route, load_world, hole_radius, speed
from sim_foundation import Sim, OPTIONS

# DebrisSnag.cs의 인스펙터 기본값 — 코드와 같아야 한다
GRACE_SECONDS = 0.35
SLOW_FACTOR = 0.45
RADIUS_SCALE = 0.80
SLOT_SPACING = 0.9          # CargoTow.slotSpacing (씬 실측)
TRAIL_DISTANCE = 2.5        # CargoTow.trailDistance (씬 실측)
STEP = 0.25                 # 경로를 이만큼씩 끊어 본다(m). 케이블 점 간격과 같다

RESPONSES = ['Pass', 'Block', 'DropOff', 'Snap', 'Slow']
DESC = {
    'Pass':    '지금 그대로 — 잔해가 건물을 통과한다',
    'Block':   '건물 밖으로 밀려나기만 한다 — 보이지만 잃는 것은 없다',
    'DropOff': '밀려나고 + 걸린 조각만 그 자리에 남는다',
    'Snap':    '밀려나고 + 줄이 끊어져 전부 놓친다',
    'Slow':    '밀려나고 + 걸려 있는 동안 느려진다 (속도 ×%.2f)' % SLOW_FACTOR,
}


def slots(anchor, dirv, n):
    """CargoTow.PlaceSlots와 같은 해바라기 배열. 짐이 어디 있는지가 곧 걸림 판정 대상이다."""
    golden = 2.39996323
    rx, rz = dirv[1], -dirv[0]
    out = []
    for i in range(n):
        r = SLOT_SPACING * math.sqrt(i + 0.5)
        a = i * golden
        ca, sa = math.cos(a), math.sin(a)
        out.append((anchor[0] + (rx * ca + dirv[0] * sa) * r,
                    anchor[1] + (rz * ca + dirv[1] * sa) * r))
    return out


def sweep(route, n, debris_r, rects):
    """경로를 따라 짐을 끌고 가며 걸림을 센다.

    돌려주는 것: (걸린 거리 m, 걸림 사건 수, 사건당 최대 조각 수, 총 거리 m)"""
    disc = SLOT_SPACING * math.sqrt(max(1, n))
    leash = max(TRAIL_DISTANCE, disc)
    pad = debris_r * RADIUS_SCALE
    grace_m = GRACE_SECONDS * CFG['moveSpeedBase']

    stuck_m = 0.0
    run_m = 0.0            # 지금 몇 m째 연속으로 걸려 있는가
    events = 0
    worst = 0
    total = 0.0
    armed = True

    for k in range(len(route) - 1):
        a, b = route[k], route[k + 1]
        seg = math.dist(a, b)
        if seg < 1e-6:
            continue
        dirv = ((a[0] - b[0]) / seg, (a[1] - b[1]) / seg)   # 짐은 진행 방향의 <b>반대</b>쪽
        steps = max(1, int(seg / STEP))
        for s in range(steps):
            t = (s + 0.5) / steps
            px = a[0] + (b[0] - a[0]) * t
            pz = a[1] + (b[1] - a[1]) * t
            anchor = (px + dirv[0] * leash, pz + dirv[1] * leash)
            hit = 0
            for (sx, sz) in slots(anchor, dirv, n):
                for r in rects:
                    if r.contains(sx, sz, pad):
                        hit += 1
                        break
            d = seg / steps
            total += d
            if hit:
                stuck_m += d
                run_m += d
                if run_m >= grace_m and armed:
                    events += 1
                    armed = False
                if hit > worst:
                    worst = hit
            else:
                run_m = 0.0
                armed = True
    return stuck_m, events, worst, total


class SnagSim(Sim):
    """11번 시뮬레이터에 '짐이 건물을 스치는가'를 얹은 것. 정책은 건드리지 않는다."""

    def __init__(self, response, foundation='A_그대로', tow_curve='flat', careful=False):
        Sim.__init__(self, foundation, tow_curve)
        self.resp = response
        self.careful = careful
        self.stuck_m = 0.0
        self.haul_m = 0.0
        self.events = 0
        self.dropped = 0
        self.lost_time = 0.0

    def go(self, target, exclude=None):
        """짐을 끌고 갈 때만 걸림을 본다. 빈손 이동은 볼 것이 없다."""
        if self.collected_in_tow <= 0 or self.resp == 'Pass':
            return Sim.go(self, target, exclude)

        rects = self.player_rects(exclude)
        pad = PLAYER_R
        if self.careful:
            # <b>짐까지 생각해서 돌아간다.</b> 자기 몸이 아니라 짐의 폭으로 길을 찾는다.
            # 실제 플레이어가 걸리는 것을 보고 하는 일이고, 이게 걸림 빈도의 하한을 준다.
            pad = max(PLAYER_R, SLOT_SPACING * math.sqrt(max(1, self.collected_in_tow)))
        d, route = path_route(self.pos, target, rects, pad=pad,
                              gen=(self.obstacle_gen(), round(pad, 2)))
        if d == float('inf'):
            # 짐이 너무 넓어 길이 없다 — 자기 폭으로 되돌아가 긁히며 간다
            pad = PLAYER_R
            d, route = path_route(self.pos, target, rects, gen=self.obstacle_gen())
            if d == float('inf'):
                return None

        # <b>길 찾기와 걸림 판정은 다른 사각형을 본다</b> [고침 2026-10-02].
        # C_발판에서는 플레이어가 토대를 넘어가지만 끌려오는 짐은 거기 걸린다.
        stuck, events, worst, total = sweep(route, self.collected_in_tow,
                                            self.cur_debris_r, self.debris_rects(exclude))
        self.stuck_m += stuck
        self.haul_m += total
        self.events += events

        base = d / speed(self.collected_in_tow, self.tow)
        extra = 0.0
        if self.resp == 'Slow':
            # 걸린 구간만 느리게 간다
            extra = (stuck / (CFG['moveSpeedBase'] * SLOW_FACTOR)) - (stuck / CFG['moveSpeedBase'])
        elif self.resp in ('DropOff', 'Snap') and events > 0:
            # 그 자리에서 바로 다시 줍는다 — 고리 한 번 값 (모형의 하한)
            lost = worst if self.resp == 'DropOff' else self.collected_in_tow
            disc = SLOT_SPACING * math.sqrt(max(1, lost))
            extra = events * (2 * math.pi * disc) / CFG['moveSpeedBase']
            self.dropped += events * lost
        self.lost_time += extra

        self.walk += d
        self.detour += (d - math.dist(self.pos, target))
        self.t += base + extra
        self.pos = target
        return d


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    print('=' * 92)
    print('14번 — 끌던 잔해가 건물에 걸렸을 때 (할당량 %d · 토대는 지금 그대로)' % CFG['quota'])
    print('=' * 92)
    base = None
    print('%-9s %8s %9s %10s %9s %8s  %s'
          % ('반응', '클리어', '증가', '걸린거리', '걸림사건', '떨군조각', '설명'))
    for r in RESPONSES:
        s = SnagSim(r).run()
        if base is None:
            base = s.t
        pct = (s.stuck_m / s.haul_m * 100) if s.haul_m > 0 else 0.0
        print('%-9s %7.1fs %+8.1fs %7.1fm(%2.0f%%) %7d회 %7d개  %s'
              % (r, s.t, s.t - base, s.stuck_m, pct, s.events, s.dropped, DESC[r]))
    print()
    print('걸린거리 % = 짐을 끌고 간 거리 중 짐이 건물에 닿아 있던 구간의 비율')
    print('걸림사건 = %.2f초(%.1fm) 넘게 연속으로 걸려 DropOff/Snap이 터질 조건을 채운 횟수'
          % (GRACE_SECONDS, GRACE_SECONDS * CFG['moveSpeedBase']))

    print()
    print('-' * 92)
    print('조심해서 끄는 플레이어 — 자기 폭이 아니라 <짐의 폭>으로 길을 찾는다')
    print('-' * 92)
    print('%-9s %10s %10s %9s %9s %10s' % ('반응', '막 끌 때', '조심할 때', '걸린거리%', '걸림사건', '더 걸은 거리'))
    for r in RESPONSES:
        a = SnagSim(r, careful=False).run()
        b = SnagSim(r, careful=True).run()
        pa = (a.stuck_m / a.haul_m * 100) if a.haul_m > 0 else 0.0
        pb = (b.stuck_m / b.haul_m * 100) if b.haul_m > 0 else 0.0
        print('%-9s %9.1fs %9.1fs %4.0f%% -> %2.0f%% %4d -> %2d회 %9.1fm'
              % (r, a.t, b.t, pa, pb, a.events, b.events, b.walk - a.walk))


def sweep_quota():
    """할당량을 키우면 걸림이 어떻게 변하는가. 짐이 커질수록 더 걸린다."""
    print()
    print('=' * 92)
    print('할당량별 — 짐이 커지면 얼마나 더 걸리는가')
    print('=' * 92)
    print('%-7s %9s %10s %9s  %s' % ('할당량', '최대짐', '걸린거리%', '걸림사건', '반응별 증가(초)'))
    saved = CFG['quota']
    for q in (60, 120, 200, 344):
        CFG['quota'] = q
        row, base, pct, ev, big = [], None, 0.0, 0, 0
        for r in RESPONSES:
            s = SnagSim(r).run()
            if base is None:
                base = s.t
            if r == 'Block':          # Pass는 쓸기를 건너뛰므로 통계가 0이다
                pct = (s.stuck_m / s.haul_m * 100) if s.haul_m > 0 else 0.0
                ev = s.events
                big = max((bb.debris_n for bb in s.bs if bb.dead), default=0)
            row.append('%+.0f' % (s.t - base))
        CFG['quota'] = saved
        print('%-7d %7d개 %9.0f%% %8d회  %s' % (q, big, pct, ev, '  '.join(
            '%s %s' % (r, v) for r, v in zip(RESPONSES, row))))
    CFG['quota'] = saved


def matrix():
    """11번 5안 x 14번 5반응 = 25칸. <b>둘은 서로 영향을 준다.</b>

    토대를 막게 하면(B·C·E) 그 토대가 곧 잔해가 걸릴 자리가 되므로, 14번의 걸림이
    저절로 늘어난다. 따로 재면 이 상호작용이 안 보인다."""
    print()
    print('=' * 100)
    print('11 x 14 전 조합 (할당량 %d · 견인 감속 flat)' % CFG['quota'])
    print('=' * 100)
    from sim_foundation import OPTIONS as F
    found = list(F)
    base = SnagSim('Pass', foundation='A_그대로').run().t

    print('%-11s %s' % ('11번 vs 14번', ''.join('%12s' % r for r in RESPONSES)))
    for f in found:
        cells = []
        for r in RESPONSES:
            s = SnagSim(r, foundation=f).run()
            cells.append('%+6.1f초' % (s.t - base))
        print('%-11s %s' % (f, ''.join('%12s' % c for c in cells)))
    print()
    print('A_그대로 + Pass = %.1f초를 0으로 둔 상대값이다.' % base)

    print()
    print('-' * 100)
    print('걸림 빈도 — 토대를 막으면 짐이 걸릴 자리가 늘어난다')
    print('-' * 100)
    print('%-11s %12s %12s %12s' % ('11번', '걸린거리%', '걸림사건', '고리 쪼갬'))
    for f in found:
        s = SnagSim('Block', foundation=f).run()
        pct = s.stuck_m / s.haul_m * 100 if s.haul_m else 0.0
        print('%-11s %11.0f%% %11d회 %11d회' % (f, pct, s.events, s.blocked_loops))


if __name__ == '__main__':
    main()
    sweep_quota()
    matrix()
