# -*- coding: utf-8 -*-
"""구역의 지도와 규칙 — 시뮬레이터 공용 토대 [분할 2026-10-01].

<b>왜 갈랐나</b>: sim_foundation.py가 406줄이 되어 CLAUDE.md 규칙 3을 넘었고,
14번(sim_snag.py)도 같은 지도·같은 경로 탐색을 쓴다. 공용분을 여기 둔다.

여기 있는 숫자는 <b>전부 씬과 에셋에서 긁어온 실측값</b>이다. 지어낸 값이 없다.
정책(무엇을 먼저 칠 것인가)은 여기 없고 쓰는 쪽에 있다.
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


def path_len(p, q, rects, pad=PLAYER_R, gen=None, want_route=False):
    """장애물을 피해 가는 최단 거리. 가시성 그래프 + 다익스트라.

    <b>코너끼리의 가시성은 장애물이 바뀔 때만 다시 계산한다.</b> 안 그러면 한 번 물을 때마다
    O(코너²×장애물)이 돌고, 건물 30채면 한 판 시뮬레이션이 분 단위로 간다 (실측으로 확인했다).
    바뀌는 것은 출발점과 도착점뿐이라 그 두 줄만 매번 새로 잇는다."""
    if not blocked(p, q, rects, pad):
        return (math.dist(p, q), [p, q]) if want_route else math.dist(p, q)

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
        return (float('inf'), [p, q]) if want_route else float('inf')

    # 출발·도착에서 코너로 가는 줄만 새로 잇는다
    src = [(i, math.dist(p, c)) for i, c in enumerate(corners)
           if not blocked(p, c, rects, pad)]
    dstv = [math.dist(q, c) if not blocked(q, c, rects, pad) else float('inf')
            for c in corners]

    INF = float('inf')
    dist = [INF] * m
    prev = [-1] * m
    done = [False] * m
    pq = []
    for i, w in src:
        if w < dist[i]:
            dist[i] = w
            heapq.heappush(pq, (w, i))
    best, bestEnd = INF, -1
    while pq:
        d, i = heapq.heappop(pq)
        if done[i] or d >= best:
            continue
        done[i] = True
        if dstv[i] < INF and d + dstv[i] < best:
            best, bestEnd = d + dstv[i], i
        for j, w in adj[i]:
            nd = d + w
            if nd < dist[j]:
                dist[j] = nd
                prev[j] = i
                heapq.heappush(pq, (nd, j))
    if not want_route:
        return best
    if bestEnd < 0:
        return best, [p, q]
    route = []
    k = bestEnd
    while k >= 0:
        route.append(corners[k])
        k = prev[k]
    route.reverse()
    return best, [p] + route + [q]


def path_route(p, q, rects, pad=PLAYER_R, gen=None):
    """길이뿐 아니라 <b>실제로 꺾이는 점들</b>을 돌려준다.

    짐이 어디를 지나가는지 보려면 길이만으로는 안 된다 — 짐은 플레이어보다 훨씬 넓어서
    플레이어가 비껴간 건물에도 닿는다 (14번)."""
    if not blocked(p, q, rects, pad):
        return math.dist(p, q), [p, q]
    return path_len(p, q, rects, pad, gen, want_route=True)


_VIS = {}



# ── 세계 ──────────────────────────────────────────────────────────────────────
class Building:
    __slots__ = ('bid', 'x', 'z', 'yaw', 'kind', 'tier', 'sx', 'sz', 'break_time',
                 'debris_n', 'size_class', 'scatter', 'debris_r',
                 'rect_intact', 'rect_rubble', 'dead', 'dead_at')

    def __init__(self, s, t):
        self.bid, self.x, self.z, self.yaw = s['id'], s['x'], s['z'], s['yaw']
        self.kind = s['data']
        self.tier = t['tier']                    # 0=제일 작은 것 … 4=제일 큰 것
        self.size_class = t['debrisSizeClass']   # 잔해 1개가 주는 단위
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
