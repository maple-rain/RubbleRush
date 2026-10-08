# -*- coding: utf-8 -*-
"""배경음악을 합성한다 [2026-10-08].

**기대치를 먼저 적는다.** 이건 *임시*다. 작곡 모델이 만든 것과 비교할 물건이 아니다 —
소수의 음을 쌓아 만든 루프라 단조롭다. 목표는 [[fp-polish-area]]에 적은 대로
**"있기는 하다"**이고, VARCO Sound 데스크톱에서 뽑으면 덮어쓰면 된다.

**두 곡을 만든다.**
- `BGM_Field` : 돌아다니며 부수고 끄는 동안. 느리고 비어 있어야 한다 —
  타격음이 초당 다섯 번 울리므로 음악이 바쁘면 그게 다 묻힌다.
- `BGM_Demolition` : 벽을 두드리는 동안. 같은 조·같은 박자에 타악과 저음만 더한다.
  조가 다르면 화면이 바뀔 때 어긋난 소리가 난다.

**이어져야 한다.** 루프 지점에서 튀지 않도록 마디 길이를 샘플 수에 정확히 맞추고,
꼬리를 머리에 겹쳐 감는다. 페이드로 끝을 0으로 만들면 매 바퀴 숨이 끊긴다.

쓰는 법:  python Tools/make_bgm.py          (이미 있으면 안 건드린다)
          python Tools/make_bgm.py --force
"""
import os
import sys
import wave

import numpy as np

SR = 44100
PEAK = 10 ** (-6.0 / 20.0)      # 효과음(-2.0)보다 낮게. 음악이 크면 타격음이 묻힌다
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                   '..', 'Assets', '_Project', 'Audio', 'BGM')

BPM = 84                        # 느릿한 작업 속도. 뛰는 곡이 아니다
BEAT = 60.0 / BPM
BARS = 8
BEATS = BARS * 4
TOTAL = int(round(SR * BEAT * BEATS))   # 루프가 딱 맞게 샘플 수를 반올림한다

rng = np.random.default_rng(20261008)

# 단조 5음계. 반음이 없어 아무 음이나 겹쳐도 부딪히지 않는다 —
# 화성을 일일이 짜지 않고도 들을 만한 소리가 나온다.
ROOT = 110.0                    # A2
SCALE = [0, 3, 5, 7, 10]        # 단5음계


def note_hz(step):
    octave, deg = divmod(step, len(SCALE))
    return ROOT * (2.0 ** (octave + SCALE[deg] / 12.0))


def at(beat):
    return int(round(SR * BEAT * beat))


def mix(buf, part, beat=0.0, gain=1.0):
    """루프 경계를 넘어가면 앞으로 감는다 — 그래야 이어 들을 때 꼬리가 안 잘린다."""
    i = at(beat)
    n = part.size
    for k in range(0, n, TOTAL):
        seg = part[k:k + TOTAL]
        j = (i + k) % TOTAL
        end = min(TOTAL, j + seg.size)
        buf[j:end] += seg[:end - j] * gain
        rest = seg.size - (end - j)
        if rest > 0:
            buf[:rest] += seg[end - j:] * gain
    return buf


def env(n, attack, release, power=2.0):
    out = np.ones(n)
    a = max(1, int(SR * attack))
    r = max(1, int(SR * release))
    if a < n:
        out[:a] = np.linspace(0, 1, a) ** 0.6
    if r < n:
        out[-r:] *= np.linspace(1, 0, r) ** power
    return out


def pluck(hz, seconds, bright=0.5):
    """현을 뜯은 소리. 배음 몇 개를 겹치고 빠르게 꺼뜨린다."""
    t = np.arange(int(SR * seconds)) / SR
    out = np.zeros(t.size)
    for h, amp in ((1, 1.0), (2, 0.42 * bright), (3, 0.22 * bright), (5, 0.08 * bright)):
        out += np.sin(2 * np.pi * hz * h * t) * amp * np.exp(-3.2 * h * t)
    return out * env(t.size, 0.004, seconds * 0.5, 1.6)


def pad(hz, seconds):
    """길게 깔리는 음. 살짝 어긋난 두 음을 겹쳐 흔들리게 한다."""
    t = np.arange(int(SR * seconds)) / SR
    out = np.zeros(t.size)
    for det in (-0.6, 0.0, 0.6):
        out += np.sin(2 * np.pi * (hz * (1 + det / 100.0)) * t)
    return out / 3.0 * env(t.size, seconds * 0.25, seconds * 0.45, 1.2)


def kick(seconds=0.35):
    t = np.arange(int(SR * seconds)) / SR
    f = 110 * (40.0 / 110.0) ** (t / seconds)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * env(t.size, 0.001, seconds * 0.8, 3.0)


def metal(seconds=0.22):
    """금속을 두드리는 소리. 공장다운 박자를 만든다."""
    n = int(SR * seconds)
    x = rng.standard_normal(n)
    f = np.fft.rfftfreq(n, 1.0 / SR)
    h = 1.0 - 1.0 / (1.0 + (f / 2600.0) ** 2) ** 1.5      # 고역만
    x = np.fft.irfft(np.fft.rfft(x) * h, n=n)
    return x * env(n, 0.001, seconds * 0.9, 3.0)


def norm(x):
    m = float(np.max(np.abs(x)))
    return x * (PEAK / m) if m > 1e-9 else x


def save(name, x):
    os.makedirs(os.path.normpath(OUT), exist_ok=True)
    pcm = (np.clip(norm(x), -1, 1) * 32767).astype('<i2')
    path = os.path.normpath(os.path.join(OUT, name + '.wav'))
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    return path


# ── 곡 ──────────────────────────────────────────────────────────
# 두 곡은 같은 뼈대를 쓴다. 조와 박자가 같아야 화면이 바뀔 때 어긋나지 않는다.

def bones():
    """두 곡이 공유하는 바탕 — 느린 저음 패드와 드문드문한 뜯는 음."""
    buf = np.zeros(TOTAL)

    # 네 마디마다 바뀌는 저음. 1도 - 7도 - 4도 - 1도
    for bar, deg in enumerate([0, -2, 3, 0] * (BARS // 4)):
        mix(buf, pad(note_hz(deg) * 0.5, BEAT * 4), beat=bar * 4, gain=0.55)

    # 드문드문 뜯는 음. 마디마다 두세 개만 — 비어 있어야 타격음이 들린다
    for bar in range(BARS):
        for b, deg in ((0.0, 7), (1.5, 9), (2.5, 5), (3.5, 10))[: 2 if bar % 2 else 3]:
            mix(buf, pluck(note_hz(deg), BEAT * 1.6), beat=bar * 4 + b, gain=0.34)
    return buf


def field():
    buf = bones()
    # 아주 느린 맥박. 두 마디에 한 번만 — 있는 듯 없는 듯
    for bar in range(0, BARS, 2):
        mix(buf, kick(), beat=bar * 4, gain=0.5)
    return buf


def demolition():
    buf = bones() * 0.8
    # 같은 조·같은 박자에 타악만 더한다. 두드리는 화면이라 박자가 또렷해야 한다
    for bar in range(BARS):
        for b in (0.0, 2.0):
            mix(buf, kick(), beat=bar * 4 + b, gain=0.8)
        for b in (1.0, 3.0, 3.5):
            mix(buf, metal(), beat=bar * 4 + b, gain=0.3)
    # 낮게 깔리는 쇳소리
    for bar in range(0, BARS, 4):
        mix(buf, pad(note_hz(0) * 0.25, BEAT * 16), beat=bar * 4, gain=0.4)
    return buf


TRACKS = {'BGM_Field': field, 'BGM_Demolition': demolition}


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    force = '--force' in sys.argv
    made = 0
    for name, fn in TRACKS.items():
        path = os.path.normpath(os.path.join(OUT, name + '.wav'))
        if os.path.exists(path) and not force:
            print('건너뜀 (이미 있음):', name)
            continue
        save(name, fn())
        made += 1
        print('만듦: %s  %d마디 · %dBPM · %.1f초' % (name, BARS, BPM, TOTAL / SR))

    if made:
        print()
        print('이건 임시다. VARCO Sound 데스크톱에서 뽑으면 같은 이름으로 덮어쓰면 된다.')


if __name__ == '__main__':
    main()
