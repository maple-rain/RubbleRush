# -*- coding: utf-8 -*-
"""효과음을 직접 합성한다 — VARCO 크레딧이 떨어졌을 때의 길 [2026-10-08].

**무엇을 위한 것인가.** VARCO가 402(Payment Required)로 막혀 아홉 가지 소리를 못 받았다.
유료 라이브러리는 커밋할 수 없으므로 선택지가 아니다. 여기서 만드는 것은 **우리 것**이라
저장소에 넣을 수 있고 크레딧이 안 든다.

**기대치를 먼저 적는다.** 이건 *임시*다. 생성 모델이 만든 것보다 거칠다 —
소음과 필터로 빚은 것이라 "콘크리트 질감" 같은 건 없다. 목표는
[[fp-polish-order]]에 적은 그대로 **"있기는 하다"**이고, 크레딧이 생기면 덮어쓰면 된다.

**기준선에 맞춘다.** 이미 받아들인 음원 셋이 피크 -2.0 dBFS로 정규화돼 있어서
같은 값에 맞춘다. 길이도 각 사건의 성격에 맞춘다 — Strike가 1.2초인데
포획음이 5초면 박자가 안 맞는다.

쓰는 법:  python Tools/make_sfx.py          (이미 있는 파일은 안 건드린다)
          python Tools/make_sfx.py --force  (전부 다시 만든다)
"""
import os
import sys
import wave

import numpy as np

SR = 44100
PEAK = 10 ** (-2.0 / 20.0)      # 기준선과 같은 -2.0 dBFS
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                   '..', 'Assets', '_Project', 'Audio', 'SFX')

rng = np.random.default_rng(20261008)   # 돌릴 때마다 달라지면 비교가 안 된다


def t(seconds):
    return np.linspace(0, seconds, int(SR * seconds), endpoint=False)


def noise(seconds):
    return rng.standard_normal(int(SR * seconds)).astype(np.float64)


def noise_n(n):
    """샘플 수를 직접 준다. 초로 주면 반올림 때문에 한 개씩 어긋난다 — 실제로 깨졌다."""
    return rng.standard_normal(int(n)).astype(np.float64)


def env(n, attack, decay, power=2.0):
    """때림 포락선 — 빠르게 솟고 천천히 꺼진다. power가 클수록 꼬리가 짧다."""
    a = max(1, int(SR * attack))
    out = np.ones(n)
    out[:a] = np.linspace(0, 1, a) ** 0.5
    tail = np.linspace(0, 1, n - a) if n > a else np.zeros(0)
    out[a:] = (1 - tail) ** power
    d = max(1, int(SR * decay))
    if d < n:
        out[-d:] *= np.linspace(1, 0, d) ** 1.5
    return out


def lowpass(x, cutoff, order=2):
    """한 극 저역 필터를 여러 번. 소음에서 '묵직함'을 꺼내는 가장 싼 방법이다."""
    a = np.exp(-2.0 * np.pi * cutoff / SR)
    y = x
    for _ in range(order):
        out = np.empty_like(y)
        acc = 0.0
        for i in range(y.size):
            acc = (1 - a) * y[i] + a * acc
            out[i] = acc
        y = out
    return y


def lowpass_fast(x, cutoff, order=2):
    """위와 같은 필터를 FFT로. 샘플 루프는 44100개에서 너무 느리다."""
    n = x.size
    f = np.fft.rfftfreq(n, 1.0 / SR)
    h = 1.0 / (1.0 + (f / max(1.0, cutoff)) ** 2) ** (order / 2.0)
    return np.fft.irfft(np.fft.rfft(x) * h, n=n)


def highpass_fast(x, cutoff, order=2):
    n = x.size
    f = np.fft.rfftfreq(n, 1.0 / SR)
    h = 1.0 - 1.0 / (1.0 + (f / max(1.0, cutoff)) ** 2) ** (order / 2.0)
    return np.fft.irfft(np.fft.rfft(x) * h, n=n)


def thump(seconds, f0, f1, amount=1.0):
    """내려가는 사인 — 저역의 '쿵'. 금속/콘크리트 충격의 뼈대다."""
    tt = t(seconds)
    f = f0 * (f1 / f0) ** (tt / max(1e-6, seconds))
    phase = 2 * np.pi * np.cumsum(f) / SR
    return amount * np.sin(phase) * env(tt.size, 0.002, 0.05, 3.0)


def rubble(seconds, density=60, size=0.03, cutoff=2500):
    """돌 부스러기 — 짧은 충격을 흩뿌린다. 개수와 크기로 '양'을 조절한다."""
    n = int(SR * seconds)
    out = np.zeros(n)
    for _ in range(int(density * seconds)):
        at = rng.integers(0, max(1, n - int(SR * size)))
        ln = int(SR * size * rng.uniform(0.4, 1.6))
        if at + ln >= n:
            continue
        grain = noise_n(ln) * env(ln, 0.0005, 0.01, 4.0)
        out[at:at + ln] += grain * rng.uniform(0.2, 1.0)
    return lowpass_fast(out, cutoff)


def ring(seconds, freqs, decay=6.0):
    """금속 울림 — 몇 개의 부분음을 겹쳐 띵 하게."""
    tt = t(seconds)
    out = np.zeros(tt.size)
    for i, f in enumerate(freqs):
        out += np.sin(2 * np.pi * f * tt) * np.exp(-decay * (1 + i * 0.4) * tt) / (i + 1)
    return out


def mix(buf, part, at=0.0):
    """길이가 달라도 안전하게 겹친다. '+'로 더하다 길이가 어긋나 두 번 깨졌다."""
    i = int(SR * at)
    n = min(buf.size - i, part.size)
    if n > 0:
        buf[i:i + n] += part[:n]
    return buf


def norm(x, peak=PEAK):
    m = float(np.max(np.abs(x)))
    return x * (peak / m) if m > 1e-9 else x


def fade_edges(x, ms=4.0):
    """끝을 0으로 맞춘다 — 안 하면 재생이 끊길 때 '툭' 소리가 난다."""
    k = max(1, int(SR * ms / 1000.0))
    if x.size > 2 * k:
        x[:k] *= np.linspace(0, 1, k)
        x[-k:] *= np.linspace(1, 0, k)
    return x


def save(name, x):
    x = fade_edges(norm(np.asarray(x, dtype=np.float64)))
    data = np.clip(x, -1.0, 1.0)
    pcm = (data * 32767.0).astype('<i2')
    path = os.path.normpath(os.path.join(OUT, name + '.wav'))
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    return path


# ── 소리 하나하나 ───────────────────────────────────────────────
# 길이는 사건의 성격에서 나온다. Strike(1.2s)보다 길면 박자가 밀린다.

def punch(v):
    """유압 팔이 공기를 가르는 소리. 맞기 전이라 충격이 없다."""
    d = 0.26
    air = highpass_fast(noise(d), 600) * env(int(SR * d), 0.05, 0.08, 1.4)
    servo = np.sin(2 * np.pi * (180 + 40 * v) * t(d)) * env(int(SR * d), 0.02, 0.1, 2.0) * 0.25
    return air * 0.8 + servo


def cable_open(v):
    """릴이 풀리며 강선이 빠져나간다. 짧은 클랙 뒤 금속성 스치는 소리."""
    d = 0.42
    clack = ring(0.08, [1400 + 120 * v, 2600, 4100], decay=40) * 0.6
    unspool = highpass_fast(noise(d), 1800) * env(int(SR * d), 0.01, 0.12, 1.1) * 0.5
    out = np.zeros(int(SR * d))
    out[:clack.size] += clack
    return out + unspool


def cable_miss(v):
    """빈 땅에서 고리가 닫혔다. 힘없는 스냅과 흙 스치는 소리."""
    d = 0.30
    snap = highpass_fast(noise(0.05), 900) * env(int(SR * 0.05), 0.002, 0.02, 3.0)
    dirt = lowpass_fast(noise(d), 1200) * env(int(SR * d), 0.02, 0.1, 2.2) * 0.35
    out = np.zeros(int(SR * d))
    out[:snap.size] += snap * 0.55
    return out + dirt


def capture(v):
    """그물이 잔해를 조인다. 강선이 당겨지고 돌이 눌려 자리를 잡는다."""
    d = 0.55
    tighten = ring(0.18, [900 + 90 * v, 1800, 3300], decay=14) * 0.55
    settle = rubble(d, density=70, size=0.035, cutoff=1800) * 0.9
    low = thump(0.2, 150, 70, 0.5)
    out = np.zeros(int(SR * d))
    out[:tighten.size] += tighten
    out[:low.size] += low
    return out + settle


def snag(v):
    """끌던 잔해가 모서리에 걸린다. 돌이 돌에 쓸리고 줄이 팽팽해진다."""
    d = 0.38
    n = int(SR * d)
    out = np.zeros(n)
    mix(out, highpass_fast(noise(d), 700)[:n] * env(n, 0.01, 0.1, 1.3) * 0.7)
    mix(out, np.sin(2 * np.pi * (320 + 60 * v) * t(d))[:n] * env(n, 0.03, 0.12, 2.0) * 0.3)
    mix(out, thump(0.12, 120, 60, 0.4))
    return out


def drop(v):
    """짐을 땅에 내려놓는다. 뭉툭한 쿵 하나와 돌이 흩어지는 소리."""
    d = 0.60
    out = np.zeros(int(SR * d))
    low = thump(0.25, 110, 45, 1.0)
    out[:low.size] += low
    return out + rubble(d, density=55, size=0.05, cutoff=1400) * 0.85


def swallow(v):
    """조각 하나가 구덩이로 떨어진다. 가장자리에 구르다 아래서 둔탁한 소리."""
    d = 0.45
    out = np.zeros(int(SR * d))
    roll = rubble(0.18, density=90, size=0.02, cutoff=3000) * 0.5
    out[:roll.size] += roll
    hit = thump(0.22, 95 + 10 * v, 45, 0.9)
    at = int(SR * 0.20)
    out[at:at + hit.size] += hit[:max(0, out.size - at)] * 0.8
    return out


def hole_level_up(v):
    """구덩이가 넓어진다. 흙이 무너지는 저역 위로 밝은 음이 솟는다."""
    d = 1.10
    n = int(SR * d)
    out = np.zeros(n)
    mix(out, lowpass_fast(noise(d), 500)[:n] * env(n, 0.05, 0.3, 1.2) * 0.8)
    # 올라가는 3화음 — '보상'으로 읽히는 가장 단순한 장치. 변형마다 조를 옮긴다.
    root = 330 * (1.0 + 0.09 * v)
    for i, mul in enumerate([1.0, 1.26, 1.5]):
        seg = np.sin(2 * np.pi * root * mul * t(d)) * np.exp(-3.0 * t(d))
        mix(out, seg * (0.5 - 0.1 * i) * 0.7, at=0.18 * i)
    return out


def quota_done(v):
    """할당량 달성. 낮은 쿵 위에 밝은 금속 종.

    <b>v로 실제로 달라져야 한다</b> [고침 2026-10-08]. 처음엔 v를 안 써서 변형 둘이
    완전히 같은 파일이었다(검증에서 -999 dB로 잡혔다). 변형을 여럿 두는 유일한 이유가
    반복을 감추는 것인데, 같은 파일이면 그 이유가 사라진다. 음정과 쿵을 같이 옮긴다.
    """
    d = 1.30
    pad = np.zeros(int(SR * d))
    mix(pad, thump(0.3, 140 - 25 * v, 60 - 8 * v, 0.9))
    root = 660 * (1.0 + 0.12 * v)          # 변형마다 장2도쯤 옮긴다
    for i, mul in enumerate([1.0, 4.0 / 3.0, 2.0]):
        mix(pad, ring(d - 0.10 * i, [root * mul, root * mul * 2.01, root * mul * 3.03],
                      decay=3.5 + 0.8 * v) * (0.55 - 0.12 * i),
            at=0.10 * i + 0.02 * v * i)
    return pad


def upgrade_pick(v):
    """업그레이드를 골랐다 [지시 2026-10-14].

    반쯤 캐쥬얼한 장르로 간다고 했으므로 밝고 짧게 간다 — 완전5도 두 음이
    빠르게 올라가고 위에 반짝이는 고역을 얹는다. 길게 끌면 다음 행동이 늦어진다.
    """
    d = 0.40
    n = int(SR * d)
    out = np.zeros(n)

    root = 523 * (1.0 + 0.06 * v)                # 변형마다 조를 조금 옮긴다
    for i, mul in enumerate([1.0, 1.5]):         # 완전5도 — 제일 단순한 '됐다'
        seg = np.sin(2 * np.pi * root * mul * t(d)) * np.exp(-7.0 * t(d))
        mix(out, seg * (0.60 - 0.15 * i), at=0.07 * i)

    spark = highpass_fast(noise(0.12), 4000) * env(int(SR * 0.12), 0.002, 0.08, 3.0)
    mix(out, spark * 0.25, at=0.02)
    return out


def footstep(v):
    """
    <b>쓰지 않는다</b> [지시 2026-10-14].

    기획자가 발소리로 `robot_stamp (3)`을 지정했다. 그 파일에는 걸음이 15번
    들어 있어 `Tools/cut_footstep.py`가 한 걸음만 떼어낸다.

    이 함수는 기록으로만 남긴다 — 받은 음원을 두고 소음을 합성해 발소리를 만든 것이
    잘못이었다. RECIPES에서 뺐으므로 `make_sfx.py`를 돌려도 실행되지 않는다.
    """
    raise NotImplementedError('발소리는 Tools/cut_footstep.py가 만든다')


def ui_toggle(v):
    """버튼을 켜고 끈다 [지시 2026-10-14].

    아주 짧은 '뽁' 하나다. 길면 조작이 굼떠 보인다 — 누른 즉시 끝나야 한다.
    켤 때와 끌 때는 변형 둘로 높낮이를 갈라 둔다.
    """
    d = 0.09
    n = int(SR * d)
    tt = t(d)

    f0 = 900 + 500 * v
    out = np.sin(2 * np.pi * f0 * np.exp(-6.0 * tt) * tt) * env(n, 0.001, 0.05, 4.0)
    tick = highpass_fast(noise(0.03), 3000) * env(int(SR * 0.03), 0.001, 0.02, 3.0)
    mix(out, tick * 0.30)
    return out


RECIPES = {
    'Punch': (punch, 3),
    'CableOpen': (cable_open, 2),
    'CableMiss': (cable_miss, 2),
    'Capture': (capture, 3),
    'Snag': (snag, 2),
    'Drop': (drop, 2),
    'Swallow': (swallow, 3),
    'HoleLevelUp': (hole_level_up, 2),
    'QuotaDone': (quota_done, 2),

    # ── 반쯤 캐쥬얼로 방향을 잡으며 더한 것 [지시 2026-10-14] ──
    'UpgradePick': (upgrade_pick, 2),
    # 발소리는 여기서 만들지 않는다 [지시 2026-10-14].
    # 기획자가 robot_stamp (3)을 지정했고, Tools/cut_footstep.py가 거기서 한 걸음을
    # 떼어낸다. 이 표에 두면 그 파일을 합성음으로 덮어쓰게 된다.
    'UiToggle': (ui_toggle, 2),
}


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    force = '--force' in sys.argv
    os.makedirs(os.path.normpath(OUT), exist_ok=True)

    made, skipped = 0, 0
    for name, (fn, count) in RECIPES.items():
        for i in range(1, count + 1):
            file = '%s_%02d' % ('SFX_' + name, i)
            path = os.path.normpath(os.path.join(OUT, file + '.wav'))
            if os.path.exists(path) and not force:
                skipped += 1
                continue
            # 변형마다 다른 씨앗 값을 넣어 같은 소리가 반복되지 않게 한다
            save(file, fn((i - 1) / max(1, count - 1) if count > 1 else 0.5))
            made += 1
            print('만듦:', file)

    print()
    print('만든 것 %d · 건너뛴 것 %d (이미 있음 — 다시 만들려면 --force)' % (made, skipped))
    print('이건 임시다. VARCO 크레딧이 생기면 Demo/6으로 덮어쓰면 된다.')


if __name__ == '__main__':
    main()
