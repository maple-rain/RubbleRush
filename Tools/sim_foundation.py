# -*- coding: utf-8 -*-
"""건물 토대(Rubble)를 리스크로 만들면 클리어 타임이 얼마나 바뀌는가 [지시 2026-10-01 · 11번].

<b>이 도구가 재는 것</b>: 똑같은 탐욕 정책을 장애물 집합만 바꿔 돌렸을 때의 총 소요 시간.
<b>재지 않는 것</b>: 재미, 긴장감, 학습 곡선. 그것은 사람이 플레이해야 나온다.

지도·속도·경로 탐색은 sim_world.py에 있다 (실측값). 여기 있는 것은 선택지와 정책뿐이다.
"""
import sys, math
from sim_world import (CFG, PLAYER_R, RUBBLE_SHRINK, Rect, path_len,
                       Building, load_world, hole_radius, speed)

# ── 선택지 ────────────────────────────────────────────────────────────────────
# 각 선택지는 "토대가 무엇을 막는가"만 다르다. 정책·지도·수치는 전부 같다.
# player  — 걸어서 막히는가 (Destructible의 BoxCollider가 켜져 있고 stepOffset보다 높은가)
# debris  — 끌려오던 잔해가 걸리는가 (ObstacleField가 집어 드는가 = 콜라이더가 켜져 있는가)
# cable   — 케이블 궤적이 끊기는가 (CableController가 ObstacleField.BlocksCable에 묻는가)
#
# <b>player와 debris가 갈린다</b> [고침 2026-10-02]. C_발판은 콜라이더를 켜 둔 채
# stepOffset만 올리는 것이라, <b>플레이어는 넘어가지만 짐은 걸린다.</b>
# C#에서 확인한 사실이고 (ObstacleField는 box.enabled만 본다), 처음 모형은 이걸 놓쳤다.
# 내가 지어낸 모형 상수 [사견 표시 2026-10-02]. 갈아 끼울 수 있게 모듈 수준에 둔다 —
# sim_sensitivity.py가 이것들을 쓸어서 결론이 여기 기대는지 본다.
_LOOP_MARGIN = 0.5      # 고리를 잔해 흩어짐보다 얼마나 더 크게 그리는가(m)
_STAND_GAP = 2.0        # 건물 표면에서 얼마나 떨어져 서서 때리는가(m)

OPTIONS = {
    'A_그대로':   dict(player=False, debris=False, cable=False, decay=None,
                      desc='지금 그대로. 토대는 장식이고 아무것도 안 막는다'),
    'B_장애물':   dict(player=True,  debris=True,  cable=False, decay=None,
                      desc='콜라이더를 켠다. 플레이어도 짐도 못 지나간다'),
    # '발판'은 해석이 둘이다 — 어느 쪽인지는 기획 결정이다 [사견 분리 2026-10-02].
    # GameConfig.platformBlocksCargo가 이 둘을 가른다.
    'C_발판_짐걸림': dict(player=False, debris=True,  cable=False, decay=None,
                      desc='올라설 수 있다. 플레이어는 넘지만 짐은 턱에 걸린다 (platformBlocksCargo 켬)'),
    'C_발판_짐통과': dict(player=False, debris=False, cable=False, decay=None,
                      desc='올라설 수 있다. 플레이어도 짐도 넘어간다 (platformBlocksCargo 끔)'),
    'D_케이블':   dict(player=False, debris=False, cable=True,  decay=None,
                      desc='케이블 궤적만 끊긴다. 걸어서도 짐도 지나간다'),
    'E_치움':     dict(player=True,  debris=True,  cable=False, decay=20.0,
                      desc='막지만 20초 뒤 사라진다'),
}


class Sim:
    def __init__(self, opt, tow_curve='flat'):
        self.o = OPTIONS[opt]
        self.name = opt
        self.tow = tow_curve
        self.bs = load_world()
        self.t = 0.0
        self.pos = CFG['playerStart']
        self.xp = 0
        self.collected = 0
        self.log = []
        self.walk = 0.0        # 총 걸은 거리
        self.detour = 0.0      # 장애물 때문에 더 걸은 거리
        self.loops = 0         # 케이블 고리를 그린 횟수
        self.blocked_loops = 0  # 한 고리로 못 싸서 쪼갠 횟수
        self.collected_in_tow = 0   # 지금 끌고 있는 단위. 속도가 이걸 읽는다
        self.cur_debris_r = 0.5     # 지금 끌고 있는 조각의 반경. 14번이 읽는다

    # 지금 시점에 플레이어를 막는 사각형들
    def obstacle_gen(self):
        """장애물 집합의 지문. 이게 같으면 가시성 그래프를 다시 안 짓는다."""
        if self.o['decay'] is None:
            return (self.name, 'p', tuple(b.dead for b in self.bs))
        return (self.name, 'p', tuple((b.dead and (self.t - b.dead_at) < self.o['decay'])
                                      for b in self.bs))

    def rects(self, key, exclude=None):
        """지금 <paramref name="key"/>를 막는 사각형들. key는 'player' 또는 'debris'.

        온전한 건물은 둘 다 막는다. 토대는 규칙에 따라 갈린다 —
        C_발판에서는 플레이어만 넘어가고 짐은 걸린다."""
        out = []
        for b in self.bs:
            if b is exclude:
                continue
            if not b.dead:
                out.append(b.rect_intact)
            elif self.o[key]:
                if self.o['decay'] is None or (self.t - b.dead_at) < self.o['decay']:
                    out.append(b.rect_rubble)
        return out

    def player_rects(self, exclude=None):
        return self.rects('player', exclude)

    def debris_rects(self, exclude=None):
        return self.rects('debris', exclude)

    def go(self, target, exclude=None):
        """목적지까지 걸어간다. 직선 거리도 같이 재서 우회분을 뽑는다."""
        rects = self.player_rects(exclude)
        d = path_len(self.pos, target, rects, gen=self.obstacle_gen())
        if d == float('inf'):
            return None
        straight = math.dist(self.pos, target)
        self.walk += d
        self.detour += (d - straight)
        self.t += d / speed(self.collected_in_tow, self.tow)
        self.pos = target
        return d

    def stand_point(self, b):
        """건물을 때릴 수 있는 자리. 접근 방향의 면에서 1.5m 떨어진 곳."""
        dx, dz = self.pos[0] - b.x, self.pos[1] - b.z
        n = math.hypot(dx, dz) or 1.0
        dx, dz = dx / n, dz / n
        # 그 방향으로 박스 표면까지의 거리
        lx, lz = dx * b.rect_intact.c + dz * b.rect_intact.s, -dx * b.rect_intact.s + dz * b.rect_intact.c
        tx = (b.sx * 0.5) / abs(lx) if abs(lx) > 1e-6 else 1e9
        tz = (b.sz * 0.5) / abs(lz) if abs(lz) > 1e-6 else 1e9
        surf = min(tx, tz)
        reach = surf + min(CFG['breakerRange'] - 1.0, _STAND_GAP)
        return (b.x + dx * reach, b.z + dz * reach)

    def loop_cost(self, b):
        """잔해를 둘러싸는 데 드는 거리. 한 고리로 안 되면 쪼갠다.

        고리 반지름이 케이블 상한(38.5m / 2π = 6.13m)을 넘으면 한 번에 못 싼다.
        토대가 케이블(또는 발)을 막으면 고리가 토대 바깥으로 밀려난다."""
        r = b.scatter + _LOOP_MARGIN
        floor = 0.0
        if b.dead and (self.o['cable'] or self.o['debris']):
            floor = b.circum + PLAYER_R
        r = max(r, floor)
        rmax = CFG['cableMaxLength'] / (2 * math.pi)
        if r <= rmax:
            self.loops += 1
            return 2 * math.pi * r, 1
        # 쪼갠다: k조각, 각 조각 반지름 r/sqrt(k)
        k = 2
        while r / math.sqrt(k) > rmax and k < 12:
            k += 1
        self.loops += k
        self.blocked_loops += 1
        return 2 * math.pi * r * math.sqrt(k), k

    def fits_now(self, b):
        r, _ = hole_radius(self.xp)
        return b.debris_r < r

    # 다음에 칠 건물을 어떻게 고를 것인가. <b>이것은 내 추측이지 기획 결정이 아니다</b>
    # [사견 표시 2026-10-02]. 선택지 사이의 차이가 '정책 때문'인지 '지도 때문'인지를
    # 가르려면 정책을 바꿔 가며 같은 비교를 돌려 봐야 한다. 그래서 갈아 끼울 수 있게 뒀다.
    policy = 'greedy'

    def pick(self):
        """다음에 칠 건물. 기본은 단위당 드는 시간이 제일 싼 것(탐욕).

        <b>최적해가 아니고, 사람이 그렇게 논다는 근거도 없다.</b>
        다섯 선택지에 똑같이 적용되므로 선택지 사이의 <i>차이</i>는 정책에서 오지 않는다 —
        그 주장이 맞는지는 <c>sim_sensitivity.py</c>가 정책을 바꿔 가며 확인한다."""
        best, best_cost = None, float('inf')
        rects_now = self.player_rects()
        gen_now = self.obstacle_gen()
        for b in self.bs:
            if b.dead or not self.fits_now(b):
                continue
            sp = self.stand_point(b)
            # 자기 건물을 빼지 않는다 — 설 자리는 중심에서 플레이어 쪽으로 뻗은 반직선 위라
            # 가는 길이 그 건물 안으로 들어갈 수가 없다. 빼면 후보마다 그래프를 새로 지어야 한다.
            d1 = path_len(self.pos, sp, rects_now, gen=gen_now)
            if d1 == float('inf'):
                continue
            d2 = math.dist(sp, CFG['holePos'])     # 귀환은 대략치로만 (고르기용)
            v = min(b.debris_n, CFG['quota'] - self.collected)
            if v <= 0:
                continue
            cost = (d1 + d2) / CFG['moveSpeedBase'] + b.break_time + b.scatter * 2 * math.pi / CFG['moveSpeedBase']

            if self.policy == 'greedy':
                per = cost / v                       # 단위당 시간이 싼 것
            elif self.policy == 'nearest':
                per = d1                             # 가까운 것부터
            elif self.policy == 'biggest':
                per = -v                             # 많이 나오는 것부터
            elif self.policy == 'quickest':
                per = b.break_time                   # 빨리 부서지는 것부터
            elif self.policy == 'farthest':
                per = -d1                            # 먼 것부터 (최악에 가까운 대조군)
            elif self.policy == 'ladder':
                # <b>기획 확정 설계</b> [2026-09-22 · 재확인 2026-10-02].
                # 구멍 단계가 오르면 한 종류가 새로 열리고, 그 종류를 부수는 것이 이득이도록
                # 수치를 맞춘다. 그러면 플레이어는 <b>열린 것 중 제일 큰 종류</b>를 치게 된다.
                # 같은 종류끼리는 가까운 것부터 — 그건 거리 말고 고를 근거가 없다.
                per = -b.tier * 10000.0 + d1
            else:
                raise SystemExit('모르는 정책: %s' % self.policy)

            if per < best_cost:
                best, best_cost = b, per
        return best

    def run(self, cap=3000.0):
        while self.collected < CFG['quota'] and self.t < cap:
            b = self.pick()
            if b is None:
                self.log.append('칠 수 있는 건물이 없다 (구멍이 작거나 길이 막혔다)')
                break
            t0 = self.t
            # ① 간다
            if self.go(self.stand_point(b), exclude=None) is None:
                b.dead = True
                b.dead_at = self.t
                continue
            # ② 부순다
            self.t += b.break_time
            b.dead = True
            b.dead_at = self.t
            # ③ 둘러싼다
            loop_d, k = self.loop_cost(b)
            self.walk += loop_d
            self.t += loop_d / speed(0, self.tow)
            self.collected_in_tow = b.debris_n
            self.cur_debris_r = b.debris_r      # 14번이 걸림 판정에 쓴다
            # ④ 구멍까지 끌고 간다
            self.go(CFG['holePos'])
            # ⑤ 쏟는다
            disc = 0.9 * math.sqrt(max(1, b.debris_n))
            reach = hole_radius(self.xp)[0] + CFG['holeSuctionMargin']
            sweep = max(0.0, 2.0 * (disc - reach))
            self.walk += sweep
            self.t += sweep / speed(b.debris_n, self.tow) + disc / CFG['holeSuctionSpeed']
            self.collected_in_tow = 0
            self.xp += b.debris_n
            self.collected += min(b.debris_n, CFG['quota'] - self.collected)
            self.log.append('%-16s %5.1fs  누적 %2d/%d  (고리 %d)' %
                            (b.kind.replace('SD_TEMP_', '') + '#' + str(b.bid),
                             self.t - t0, self.collected, CFG['quota'], k))
        return self


def run_all(tow_curve='flat'):
    out = {}
    for k in OPTIONS:
        out[k] = Sim(k, tow_curve).run()
    return out


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    for curve in ('flat', 'linear'):
        print('=' * 78)
        print('견인 감속 곡선 = %s  %s' % (curve, '(지금 게임의 실제 동작 — 자리표시자)' if curve == 'flat' else '(GameConfig ① 선형안, 아직 미사용)'))
        print('=' * 78)
        res = run_all(curve)
        base = res['A_그대로'].t
        print('%-11s %8s %8s %9s %8s %7s %7s  %s' %
              ('선택지', '클리어', '증가', '걸은거리', '우회', '고리', '쪼갬', '설명'))
        for k, s in res.items():
            print('%-11s %7.1fs %+7.1fs %8.1fm %7.1fm %6d %6d  %s' %
                  (k, s.t, s.t - base, s.walk, s.detour, s.loops, s.blocked_loops, OPTIONS[k]['desc']))
        print()
