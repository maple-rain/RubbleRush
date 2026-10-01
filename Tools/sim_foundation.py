# -*- coding: utf-8 -*-
"""건물 토대(Rubble)를 리스크로 만들면 클리어 타임이 얼마나 바뀌는가 [지시 2026-10-01 · 11번].

<b>이 도구가 재는 것</b>: 똑같은 탐욕 정책을 장애물 집합만 바꿔 돌렸을 때의 총 소요 시간.
<b>재지 않는 것</b>: 재미, 긴장감, 학습 곡선. 그것은 사람이 플레이해야 나온다.

입력은 전부 씬·에셋에서 긁어온 실측값이다 (scratchpad/zone.json + 아래 CFG).
정책만 내가 정했고, 정책은 다섯 선택지에 <b>똑같이</b> 적용된다.
"""
import json, math, heapq, sys, io, os

# ── 실측 상수 (Assets/_Project/Data/Config/GameConfig_TEMP.asset) ──────────────
CFG = dict(
    moveSpeedBase=6.0, walkSpeedBase=2.24,
    speedPenaltyPerUnit=0.12, moveSpeedMin=2.5,
    breakerRange=3.5, tapsPerSecond=5.0,
    cableMaxLength=38.5, cablePointInterval=0.25, cableMinLoopArea=2.0,
    holeSuctionMargin=2.0, holeSuctionSpeed=7.0,
    quota=60, timeLimit=360.0,
    holePos=(0.0, -10.0), playerStart=(0.0, -12.0),
    holeLevels=[(0.44, 8), (0.79, 16), (1.23, 32), (1.84, 64), (3.50, 0)],
)
# CharacterController 실측
PLAYER_R = 0.70 + 0.08      # radius + skinWidth
STEP_OFFSET = 0.30          # 이것보다 높으면 못 넘는다
RUBBLE_H = 0.40             # 토대 높이 (5종 모두 동일, 실측)
RUBBLE_SHRINK = (0.90, 1.00)  # 토대 발자국 = 온전 발자국 × (x 0.9, z 1.0) — 5종 모두 동일

SCRATCH = os.environ.get('FP_SCRATCH', '.')


# ── 기하 ──────────────────────────────────────────────────────────────────────
class Rect:
    """yaw로 회전한 바닥 사각형. 충돌·가시성 판정은 전부 로컬 좌표로 내려서 본다."""
    __slots__ = ('cx', 'cz', 'hx', 'hz', 'c', 's', 'tag')

    def __init__(self, cx, cz, sx, sz, yaw_deg, tag=''):
        self.cx, self.cz = cx, cz
        self.hx, self.hz = sx * 0.5, sz * 0.5
        a = math.radians(yaw_deg)
        self.c, self.s = math.cos(a), math.sin(a)
        self.tag = tag

    def to_local(self, x, z):
        dx, dz = x - self.cx, z - self.cz
        return dx * self.c + dz * self.s, -dx * self.s + dz * self.c

    def to_world(self, lx, lz):
        return (self.cx + lx * self.c - lz * self.s,
                self.cz + lx * self.s + lz * self.c)

    def corners(self, pad):
        hx, hz = self.hx + pad, self.hz + pad
        return [self.to_world(sx * hx, sz * hz)
                for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]

    def contains(self, x, z, pad=0.0):
        lx, lz = self.to_local(x, z)
        return abs(lx) <= self.hx + pad and abs(lz) <= self.hz + pad

    def blocks_segment(self, p, q, pad):
        """선분이 확장된 사각형 내부를 지나는가. 슬래브 방식 (축정렬 로컬에서)."""
        ax, az = self.to_local(*p)
        bx, bz = self.to_local(*q)
        hx, hz = self.hx + pad, self.hz + pad
        # 양 끝이 이미 안쪽이면 막힌 것
        if abs(ax) <= hx and abs(az) <= hz:
            return True
        if abs(bx) <= hx and abs(bz) <= hz:
            return True
        t0, t1 = 0.0, 1.0
        for lo, hi, a, d in ((-hx, hx, ax, bx - ax), (-hz, hz, az, bz - az)):
            if abs(d) < 1e-12:
                if a < lo or a > hi:
                    return False
                continue
            u, v = (lo - a) / d, (hi - a) / d
            if u > v:
                u, v = v, u
            t0, t1 = max(t0, u), min(t1, v)
            if t0 > t1:
                return False
        # 접하기만 하는 것은 통과로 본다 (모서리 스치기)
        return (t1 - t0) > 1e-9


def blocked(p, q, rects, pad):
    for r in rects:
        if r.blocks_segment(p, q, pad):
            return True
    return False


def path_len(p, q, rects, pad=PLAYER_R, gen=None):
    """장애물을 피해 가는 최단 거리. 가시성 그래프 + 다익스트라.

    <b>코너끼리의 가시성은 장애물이 바뀔 때만 다시 계산한다.</b> 안 그러면 한 번 물을 때마다
    O(코너²×장애물)이 돌고, 건물 30채면 한 판 시뮬레이션이 분 단위로 간다 (실측으로 확인했다).
    바뀌는 것은 출발점과 도착점뿐이라 그 두 줄만 매번 새로 잇는다."""
    if not blocked(p, q, rects, pad):
        return math.dist(p, q)

    key = (gen, pad)
    cache = _VIS.get(key)
    if cache is None:
        eps = 0.02
        corners = []
        for r in rects:
            corners.extend(r.corners(pad + eps))
        m = len(corners)
        adj = [[] for _ in range(m)]
        for i in range(m):
            for j in range(i + 1, m):
                if not blocked(corners[i], corners[j], rects, pad):
                    w = math.dist(corners[i], corners[j])
                    adj[i].append((j, w))
                    adj[j].append((i, w))
        cache = (corners, adj)
        _VIS[key] = cache

    corners, adj = cache
    m = len(corners)
    if m == 0:
        return float('inf')

    # 출발·도착에서 코너로 가는 줄만 새로 잇는다
    src = [(i, math.dist(p, c)) for i, c in enumerate(corners)
           if not blocked(p, c, rects, pad)]
    dstv = [math.dist(q, c) if not blocked(q, c, rects, pad) else float('inf')
            for c in corners]

    INF = float('inf')
    dist = [INF] * m
    done = [False] * m
    pq = []
    for i, w in src:
        if w < dist[i]:
            dist[i] = w
            heapq.heappush(pq, (w, i))
    best = INF
    while pq:
        d, i = heapq.heappop(pq)
        if done[i] or d >= best:
            continue
        done[i] = True
        if dstv[i] < INF:
            best = min(best, d + dstv[i])
        for j, w in adj[i]:
            nd = d + w
            if nd < dist[j]:
                dist[j] = nd
                heapq.heappush(pq, (nd, j))
    return best


_VIS = {}



# ── 세계 ──────────────────────────────────────────────────────────────────────
class Building:
    __slots__ = ('bid', 'x', 'z', 'yaw', 'kind', 'sx', 'sz', 'break_time',
                 'debris_n', 'scatter', 'debris_r', 'rect_intact', 'rect_rubble', 'dead', 'dead_at')

    def __init__(self, s, t):
        self.bid, self.x, self.z, self.yaw = s['id'], s['x'], s['z'], s['yaw']
        self.kind = s['data']
        cs = t['colliderSize']
        self.sx, self.sz = cs[0], cs[2]
        self.break_time = t['breakTime']
        self.debris_n = t['debrisCount']
        self.scatter = t['debrisScatterRadius']
        self.debris_r = t['debrisRadius']
        self.dead = False
        self.dead_at = 0.0
        self.rect_intact = Rect(self.x, self.z, self.sx, self.sz, self.yaw, self.kind)
        self.rect_rubble = Rect(self.x, self.z, self.sx * RUBBLE_SHRINK[0],
                                self.sz * RUBBLE_SHRINK[1], self.yaw, self.kind + '_rubble')

    @property
    def circum(self):
        """토대를 감싸는 원의 반지름. 케이블 고리가 토대를 피해야 할 때 쓴다."""
        return math.hypot(self.sx * RUBBLE_SHRINK[0], self.sz * RUBBLE_SHRINK[1]) * 0.5


def load_world():
    d = json.load(io.open(os.path.join(SCRATCH, 'zone.json'), encoding='utf-8'))
    types = {t['name']: t for t in d['types']}
    return [Building(s, types[s['data']]) for s in d['structures']]


def hole_radius(xp):
    """누적 XP로 정해지는 구멍 반경. Hole.cs의 단계 상승 규칙 그대로."""
    lv, need = 0, CFG['holeLevels'][0][1]
    acc = xp
    while lv < len(CFG['holeLevels']) - 1 and need > 0 and acc >= need:
        acc -= need
        lv += 1
        need = CFG['holeLevels'][lv][1]
    return CFG['holeLevels'][lv][0], lv


def speed(load_units, tow_curve):
    """견인량에 따른 이동 속도.

    <b>지금 게임은 tow_curve='flat'이다</b> — PlayerController.ComputeMoveSpeed가
    아직 자리표시자라서 얼마를 끌어도 안 느려진다 (코드에 경고가 박혀 있다).
    'linear'는 GameConfig에 값만 들어 있고 아직 안 쓰이는 ① 후보다."""
    base = CFG['moveSpeedBase']
    if tow_curve == 'flat':
        return base
    return max(CFG['moveSpeedMin'], base - CFG['speedPenaltyPerUnit'] * load_units)


# ── 선택지 ────────────────────────────────────────────────────────────────────
# 각 선택지는 "토대가 무엇을 막는가"만 다르다. 정책·지도·수치는 전부 같다.
OPTIONS = {
    'A_그대로':   dict(player=False, cable=False, decay=None,
                      desc='지금 그대로. 토대는 장식이고 아무것도 안 막는다'),
    'B_장애물':   dict(player=True,  cable=False, decay=None,
                      desc='토대 콜라이더를 켠다. 플레이어가 못 지나간다'),
    'C_발판':     dict(player=False, cable=False, decay=None, step=True,
                      desc='올라설 수 있게 한다 (stepOffset 0.30→0.45). 길은 안 막는다'),
    'D_케이블':   dict(player=False, cable=True,  decay=None,
                      desc='케이블만 못 넘는다. 걸어서는 지나간다'),
    'E_치움':     dict(player=True,  cable=False, decay=20.0,
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

    # 지금 시점에 플레이어를 막는 사각형들
    def obstacle_gen(self):
        """장애물 집합의 지문. 이게 같으면 가시성 그래프를 다시 안 짓는다."""
        if self.o['decay'] is None:
            return (self.name, tuple(b.dead for b in self.bs))
        return (self.name, tuple((b.dead and (self.t - b.dead_at) < self.o['decay'])
                                 for b in self.bs))

    def player_rects(self, exclude=None):
        out = []
        for b in self.bs:
            if b is exclude:
                continue
            if not b.dead:
                out.append(b.rect_intact)
            elif self.o['player']:
                if self.o['decay'] is None or (self.t - b.dead_at) < self.o['decay']:
                    out.append(b.rect_rubble)
        return out

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
        reach = surf + min(CFG['breakerRange'] - 1.0, 2.0)
        return (b.x + dx * reach, b.z + dz * reach)

    def loop_cost(self, b):
        """잔해를 둘러싸는 데 드는 거리. 한 고리로 안 되면 쪼갠다.

        고리 반지름이 케이블 상한(38.5m / 2π = 6.13m)을 넘으면 한 번에 못 싼다.
        토대가 케이블(또는 발)을 막으면 고리가 토대 바깥으로 밀려난다."""
        r = b.scatter + 0.5
        floor = 0.0
        if b.dead and (self.o['cable'] or self.o['player']):
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

    def pick(self):
        """다음에 칠 건물. 단위당 드는 시간이 제일 싼 것.

        탐욕이다 — 최적해가 아니다. <b>다섯 선택지에 똑같이 적용되므로</b>
        선택지 사이의 차이는 정책이 아니라 지도에서 온다."""
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
            per = cost / v
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
            got = min(b.debris_n, CFG['quota'] - self.collected + 0)
            self.collected_in_tow = b.debris_n
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
