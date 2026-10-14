# -*- coding: utf-8 -*-
"""타격음을 받은 음원 둘을 섞어 만든다 [지시 2026-10-14].

**재료.** 기획자가 받아 온 `firecracker_bang`과 `cannon_fire` 각 세 개.
둘 다 48kHz 모노이고 피크가 -2.0 dBFS로 이미 프로젝트 기준선과 같다.

**왜 그냥 못 쓰는가.** 실측하면 여섯 중 다섯이 **앞에 무음이 들어 있고**
(앞 50ms 에너지 0%) 길이가 1.15~5.79초다. 타격음은 **초당 다섯 번** 울리므로
한 발이 0.2초보다 길면 다음 발과 겹쳐 뭉갠다. 지금 쓰는 소리가 0.34초인 것이 그 이유다.

**어떻게 섞는가.** 둘은 역할이 다르다.
- `firecracker` : 날카로운 균열. **맨 앞의 과도음**만 쓴다 — 벽이 깨지는 소리다.
- `cannon`      : 묵직한 저역. 저역만 남겨 **뒤에 깔아** 무게를 준다.
한 소리에서 고역과 저역을 다 뽑으려 하지 않는다. 각자 잘하는 대역만 가져온다.

**소리가 시작되는 자리를 찾아 자른다.** 무음을 그대로 두면 주먹이 닿고 나서
소리가 늦게 나 '맞은 것 같지 않다'가 된다.

쓰는 법:
    python Tools/make_strike.py                      # 미리 듣기용으로 만들지 않고 재기만
    python Tools/make_strike.py --write
    python Tools/make_strike.py --write --src "경로"  # 원본이 다른 데 있으면
"""
import os
import sys
import wave

import numpy as np

SR = 48000
PEAK = 10 ** (-2.0 / 20.0)          # 프로젝트 기준선 -2.0 dBFS
LENGTH = 0.30                       # 한 발의 길이(초). 초당 다섯 번이라 0.2초 간격이다
CRACK_SHARE = 0.16                  # firecracker에서 가져올 앞부분(초)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Assets', '_Project', 'Audio', 'SFX')
DEFAULT_SRC = os.path.join(os.path.expanduser('~'), 'Downloads')


def load(path):
    """모노 float로 읽는다. 스테레오면 평균 낸다."""
    w = wave.open(path, 'rb')
    n, sr, ch, sw = w.getnframes(), w.getframerate(), w.getnchannels(), w.getsampwidth()
    raw = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float64) / 32768.0
    w.close()
    if ch > 1:
        raw = raw.reshape(-1, ch).mean(axis=1)
    if sr != SR:
        # 길이를 맞춰 선형 보간 — 리샘플러를 따로 들일 일이 아니다
        m = int(round(raw.size * SR / float(sr)))
        raw = np.interp(np.linspace(0, raw.size - 1, m), np.arange(raw.size), raw)
    return raw


def onset(x, back=0.030, rise=0.12):
    """
    <b>제일 큰 봉우리를 찾고 거기서 거꾸로 올라간다</b> [고침 2026-10-14].

    처음에는 "문턱을 처음 넘는 지점"으로 잡았는데, 5.79초짜리 파일은 진짜 폭발 앞에
    약한 소리가 먼저 있어서 <b>엉뚱한 데서 잘렸다</b> — 그 변형만 저역이 고역의
    1540배가 되어 때리는 소리가 아니라 웅웅거림이 남았다.

    봉우리를 기준으로 잡으면 앞에 뭐가 있든 <b>제일 센 한 방</b>에 맞춰진다.
    거기서 <paramref name="back"/>초 안쪽을 훑어 소리가 솟기 시작한 자리를 돌려준다.
    """
    a = np.abs(x)
    top = int(np.argmax(a))
    peak = float(a[top])
    if peak < 1e-6:
        return 0

    lo = max(0, top - int(SR * back))
    window = a[lo:top + 1]
    above = window > peak * rise
    return lo + int(np.argmax(above)) if above.any() else top


def lowpass(x, cutoff, order=3):
    """한 극 저역통과를 여러 번. scipy 없이 쓰려고 이 모양이다."""
    a = np.exp(-2.0 * np.pi * cutoff / SR)
    out = x.copy()
    for _ in range(order):
        y = np.empty_like(out)
        acc = 0.0
        for i in range(out.size):          # 길이가 0.3초라 파이썬 루프로도 즉시 끝난다
            acc = (1 - a) * out[i] + a * acc
            y[i] = acc
        out = y
    return out


def highpass(x, cutoff, order=2):
    return x - lowpass(x, cutoff, order)


def decay(n, hold, tail, power=2.2):
    """붙잡았다 꺼지는 포락선. hold 동안은 그대로 두고 그 뒤로 떨어뜨린다."""
    out = np.ones(n)
    h = min(n, max(1, int(SR * hold)))
    t = max(1, n - h)
    out[h:] = np.linspace(1.0, 0.0, t) ** power
    if tail > 0:
        k = min(n, max(1, int(SR * tail)))
        out[-k:] *= np.linspace(1.0, 0.0, k)
    return out


def norm(x, peak=PEAK):
    m = float(np.max(np.abs(x)))
    return x * (peak / m) if m > 1e-9 else x


def save(name, x):
    path = os.path.normpath(os.path.join(OUT, name + '.wav'))
    data = np.clip(x, -1.0, 1.0)
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((data * 32767.0).astype('<i2').tobytes())
    return path


def build(crack_raw, boom_raw, v):
    """한 발. 균열의 앞부분 + 저역만 남긴 포성."""
    n = int(SR * LENGTH)
    out = np.zeros(n)

    # ① 균열 — 소리가 시작되는 자리부터 짧게. 고역을 살려 '깨지는' 느낌을 남긴다
    c = crack_raw[onset(crack_raw):]
    c = c[:int(SR * CRACK_SHARE)]
    c = highpass(c, 700) * 0.85 + c * 0.15
    c = c * decay(c.size, 0.004, 0.02, 2.6)

    # <b>섞기 전에 각각 음량을 맞춘다</b> [고침 2026-10-14]. 원본마다 고역·저역이
    # 들어 있는 양이 다르다 — 그대로 더하면 변형마다 균형이 제각각이 된다
    # (실측: 저역:고역이 9.8 / 11.1 / 51.9로 2번만 다섯 배 무거웠다).
    # 각자 1로 맞춰 놓고 아래 가중치로만 섞으면 균형이 셋 다 같아진다.
    c = norm(c, 1.0)
    out[:c.size] += c * 1.0

    # ② 포성 — 저역만. 균열보다 조금 늦게 들어와야 '맞고 나서 울린다'가 된다
    b = boom_raw[onset(boom_raw):]
    b = b[:int(SR * (LENGTH - 0.01))]
    b = lowpass(b, 220, 3)
    b = b * decay(b.size, 0.01, 0.05, 1.8)
    b = norm(b, 1.0)                        # 균열과 같은 이유로 여기서도 맞춘다
    at = int(SR * (0.006 + 0.002 * v))      # 변형마다 몇 ms씩 어긋나게
    room = min(b.size, n - at)
    if room > 0:
        out[at:at + room] += b[:room] * (0.62 + 0.06 * v)   # 균열보다 작게 — 몸통은 받쳐 주는 역할이다

    out *= decay(n, LENGTH * 0.25, 0.012, 2.0)
    return norm(out)


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    write = '--write' in sys.argv
    src = DEFAULT_SRC
    if '--src' in sys.argv:
        src = sys.argv[sys.argv.index('--src') + 1]

    os.makedirs(os.path.normpath(OUT), exist_ok=True)

    made = []
    for i in (1, 2, 3):
        cp = os.path.join(src, 'firecracker_bang (%d).wav' % i)
        bp = os.path.join(src, 'cannon_fire (%d).wav' % i)
        if not (os.path.exists(cp) and os.path.exists(bp)):
            print('원본을 못 찾았다:', cp, '/', bp)
            return 1

        crack, boom = load(cp), load(bp)
        print('  %d번  균열 시작 %5.0fms · 포성 시작 %5.0fms'
              % (i, onset(crack) * 1000.0 / SR, onset(boom) * 1000.0 / SR))

        x = build(crack, boom, (i - 1) / 2.0)
        name = 'SFX_Strike_%02d' % i
        if write:
            print('    ->', save(name, x), '%.2f초' % (x.size / float(SR)))
        made.append(name)

    print()
    if not write:
        print('재기만 했다. 실제로 쓰려면 --write 를 붙일 것.')
    else:
        print('%d개를 덮어썼다. 유니티가 다시 불러온다.' % len(made))
        print('길이 %.2f초 — 초당 다섯 번 쳐도 겹치지 않는다.' % LENGTH)
    return 0


if __name__ == '__main__':
    sys.exit(main())
