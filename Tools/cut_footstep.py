# -*- coding: utf-8 -*-
"""발소리를 `robot_stamp (3)`에서 한 걸음만 떼어낸다 [지시 2026-10-14].

**기획자 지정**: "걸을 때 사운드는 robot_stamp (3)쪽 사운드를 써."

**왜 떼어내야 하는가.** 이 파일은 발소리 하나가 아니라 **발소리 15번이 연속으로**
들어 있다 (실측: 0.16 · 0.37 · 0.58 · 0.78 … 초, 약 0.20초 간격, 전체 4.10초).
게임은 걸음마다 한 번 울리므로 그대로 꽂으면 한 걸음에 열다섯 걸음이 쏟아진다.

**그래서 하는 일은 고르는 것뿐이다.** 제일 센 걸음 하나를 찾아, 그 시작부터
다음 걸음 직전까지를 가져온다. 길이를 내가 정하지 않는다 — **다음 걸음이 정한다.**

앞 판본은 이 파일을 안 쓰고 소음을 합성해 발소리를 만들었다. 기획자가 준 음원을
두고 내가 지어낸 것이라 틀렸다.

쓰는 법:
    python Tools/cut_footstep.py            # 재기만 한다
    python Tools/cut_footstep.py --write
"""
import os
import sys
import wave

import numpy as np

SR = 48000
PEAK = 10 ** (-2.0 / 20.0)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Assets', '_Project', 'Audio', 'SFX')
DEFAULT_SRC = os.path.join(os.path.expanduser('~'), 'Downloads')

SOURCE = 'robot_stamp (3).wav'
TARGET = 'SFX_Footstep_01'

# 걸음 간격은 2.2m / 6m/s = 0.37초다. 떼어낸 조각이 그보다 길면 다음 걸음과 겹친다.
STEP_GAP = 0.37


def load(path):
    w = wave.open(path, 'rb')
    n, sr, ch = w.getnframes(), w.getframerate(), w.getnchannels()
    x = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float64) / 32768.0
    w.close()
    if ch > 1:
        x = x.reshape(-1, ch).mean(axis=1)
    if sr != SR:
        m = int(round(x.size * SR / float(sr)))
        x = np.interp(np.linspace(0, x.size - 1, m), np.arange(x.size), x)
    return x


def envelope(x, window=0.01):
    k = max(1, int(SR * window))
    return np.convolve(np.abs(x), np.ones(k) / k, 'same')


def stamps(x, floor=0.25, apart=0.12):
    """걸음이 어디에 몇 개 있는지. 봉우리 하나가 걸음 하나다."""
    env = envelope(x)
    thr = float(np.max(env)) * floor
    gap = int(SR * apart)

    found, i = [], 0
    while i < env.size:
        if env[i] > thr:
            top = i + int(np.argmax(env[i:min(env.size, i + gap)]))
            found.append(top)
            i = top + gap
        else:
            i += 1
    return found, env


def rise_of(env, top, back=0.040, ratio=0.15):
    """봉우리에서 거꾸로 올라가 소리가 솟기 시작한 자리. 앞을 잘라 늦지 않게 한다."""
    lo = max(0, top - int(SR * back))
    above = env[lo:top + 1] > env[top] * ratio
    return lo + int(np.argmax(above)) if above.any() else top


def pick(x):
    """
    <b>제일 센 걸음 하나를 고르고, 다음 걸음 직전까지 가져온다.</b>

    길이를 숫자로 정하지 않는 것이 요점이다. 원본에 걸음이 줄지어 있으므로
    <b>다음 걸음까지의 간격이 곧 이 소리가 차지해도 되는 길이</b>다.
    """
    tops, env = stamps(x)
    if not tops:
        return None, 0, 0

    best = max(tops, key=lambda t: env[t])
    start = rise_of(env, best)

    later = [t for t in tops if t > best]
    end = rise_of(env, later[0]) if later else min(x.size, start + int(SR * STEP_GAP))
    end = min(end, start + int(SR * STEP_GAP), x.size)

    y = x[start:end].copy()
    k = max(1, int(y.size * 0.25))              # 끝을 내려 잘린 자리가 안 들리게
    y[-k:] *= np.linspace(1.0, 0.0, k) ** 1.5

    m = float(np.max(np.abs(y)))
    return (y * (PEAK / m) if m > 1e-9 else y), start, end


def save(name, x):
    path = os.path.normpath(os.path.join(OUT, name + '.wav'))
    data = np.clip(x, -1.0, 1.0)
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((data * 32767.0).astype('<i2').tobytes())
    return path


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    write = '--write' in sys.argv
    src = DEFAULT_SRC
    if '--src' in sys.argv:
        src = sys.argv[sys.argv.index('--src') + 1]

    path = os.path.join(src, SOURCE)
    if not os.path.exists(path):
        print('원본을 못 찾았다:', path)
        return 1

    x = load(path)
    tops, env = stamps(x)
    print('%s  %.2f초 · 걸음 %d개' % (SOURCE, x.size / float(SR), len(tops)))
    print('   ' + ' '.join('%.2f' % (t / float(SR)) for t in tops[:10])
          + ('  …' if len(tops) > 10 else ''))

    y, start, end = pick(x)
    if y is None:
        print('걸음을 못 찾았다')
        return 1

    print()
    print('고른 걸음: %.2f초 ~ %.2f초  ->  %.3f초짜리 한 장'
          % (start / float(SR), end / float(SR), y.size / float(SR)))
    print('걸음 간격 %.2f초보다 짧다 — 다음 걸음과 안 겹친다.' % STEP_GAP)

    if write:
        print()
        print('->', save(TARGET, y))
    else:
        print()
        print('재기만 했다. 실제로 쓰려면 --write 를 붙일 것.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
