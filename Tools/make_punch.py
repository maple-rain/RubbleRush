# -*- coding: utf-8 -*-
"""묵직한 펀치를 만든다 [2026-10-08]. 받은 음원만으로는 안 되어서 저역을 더한다.

**왜 받은 것을 못 쓰는가.** `Tools/sfx_feel.py`로 받은 음원 열아홉 개를 전부 쟀더니
타격에 쓸 저역이 어디에도 없었다 — 200Hz 아래 비율이 `robot_arm_swing` 2.9%,
`sword_swing` 5.1%, `robot_stamp` 0.9%였다. 제일 묵직한 `robot_punch`도 15.8%다.
**묵직함은 깎아서 못 만든다. 없는 것은 더해야 한다.**

**고치는 것 셋.**

1. **드릴** — `Punch`(주먹 뻗는 소리)의 스펙트럼 무게중심이 9983Hz였다. 거의 전부가
   고역이라 초당 다섯 번 반복하면 그대로 전동 드릴이다. 저역으로 끌어내린다.
2. **먹먹** — `Strike`가 1.30초인데 연타 간격은 0.20초다. 네 겹에서 여섯 겹이 쌓여
   어택이 뭉개진다. 꼬리를 연타 간격 아래로 자른다.
3. **씹힘** — `Strike` 혼자 음성 12개 중 6.5개를 물고 있어서 완파 소리(7.24초)가
   2.00초에서 끊겼다. 짧게 하면 1.75개로 내려가 자리가 남는다.

**펀치의 세 겹.** 게임의 타격음은 보통 이렇게 만든다. 하나라도 빠지면 다르게 들린다.

| 겹 | 무엇 | 왜 |
|---|---|---|
| 몸통(body) | 110 -> 45Hz로 떨어지는 사인, 빠르게 꺼짐 | **묵직함의 정체**. 가슴으로 느끼는 부분 |
| 중역(slap) | 원본 타격의 중역 | 무엇에 맞았는지 알려 준다 (콘크리트인지 철판인지) |
| 고역(snap) | 원본의 아주 짧은 앞머리 | 없으면 둔하고 늦게 들린다 |

쓰는 법:  python Tools/make_punch.py          (만들어 넣는다)
          python Tools/make_punch.py --dry    (무엇을 할지만 보여 준다)
"""
import argparse
import glob
import os
import re
import sys
import wave

import numpy as np

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = r'C:\Users\MBC-501-08\Downloads'
OUT = os.path.normpath(os.path.join(HERE, '..', 'Assets', '_Project', 'Audio', 'SFX'))

PEAK = 10 ** (-2.0 / 20.0)      # 기존 음원과 같은 -2.0 dBFS

# 연타 간격 0.20초 (punch.csv 실측). 꼬리가 이보다 길면 다음 발과 겹친다.
GAP = 0.20


def read(path):
    with wave.open(path, 'rb') as w:
        ch, width, rate, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(n)
    if width == 2:
        a = np.frombuffer(raw, dtype='<i2').astype(np.float64) / 32768.0
    elif width == 3:
        b = np.frombuffer(raw, dtype=np.uint8).reshape(-1, 3).astype(np.int32)
        v = b[:, 0] | (b[:, 1] << 8) | (b[:, 2] << 16)
        a = np.where(v & 0x800000, v - 0x1000000, v).astype(np.float64) / 8388608.0
    elif width == 4:
        a = np.frombuffer(raw, dtype='<i4').astype(np.float64) / 2147483648.0
    else:
        a = (np.frombuffer(raw, dtype=np.uint8).astype(np.float64) - 128.0) / 128.0
    if ch > 1:
        a = a.reshape(-1, ch).mean(axis=1)
    return a, rate


def write(path, a, rate):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    pcm = (np.clip(a, -1, 1) * 32767).astype('<i2')
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(pcm.tobytes())


def tilt(a, rate, corner, slope=2.0, high=False):
    """
    FFT로 기울인다. <b>칼같이 자르지 않는다</b> — 벽돌 담장처럼 자르면 시간축에서
    앞뒤로 울음(ringing)이 생겨 타격이 '띠잉' 한다. 완만한 기울기면 안 생긴다.
    """
    n = a.size
    f = np.fft.rfftfreq(n, 1.0 / rate)
    k = 1.0 / (1.0 + (np.maximum(f, 1e-6) / corner) ** (2 * slope))   # 저역 통과
    if high:
        k = 1.0 - k
    return np.fft.irfft(np.fft.rfft(a) * k, n=n)


def body(rate, seconds=0.22, f0=115.0, f1=45.0, curve=3.2, fade=0.9):
    """
    펀치의 몸통. 음이 <b>떨어지면서</b> 꺼진다 — 고정 주파수로 하면 북이 아니라 삑 소리다.
    주파수를 적분해서 위상을 만든다. 그냥 sin(2*pi*f(t)*t)로 쓰면 위상이 튀어 딸깍거린다.

    <b>꺼지는 속도가 '쿵'과 '툭'을 가른다</b> [고침 2026-10-08]. 처음엔 1.6을 썼는데
    50ms에 24%까지 떨어져 몸통이 거의 안 남았다 (저역 실효 -19.9dBFS). 0.9면 50ms에 45%,
    100ms에 20%로 가슴에 남는 길이가 된다.
    """
    t = np.arange(int(rate * seconds)) / rate
    f = f1 + (f0 - f1) * np.exp(-curve * t / seconds)
    phase = 2 * np.pi * np.cumsum(f) / rate
    env = np.exp(-curve * fade * t / seconds)
    out = np.sin(phase) * env
    # 맨 앞 2ms를 띄워 딸깍을 없앤다.
    lead = max(1, int(rate * 0.002))
    out[:lead] *= np.linspace(0, 1, lead)
    return out


def hit(a, rate, seconds, pre=0.004):
    """원본에서 <b>가장 센 자리</b>를 어택 조금 앞에서부터 떼어 온다."""
    peak = int(np.argmax(np.abs(a)))
    start = max(0, peak - int(rate * pre))
    out = a[start:start + int(rate * seconds)].copy()
    if out.size < int(rate * seconds):
        out = np.pad(out, (0, int(rate * seconds) - out.size))
    return out


def decay(a, rate, hold=0.02, power=2.2):
    """꼬리를 끌어내린다. 연타 간격 안에 끝나야 다음 발과 안 겹친다."""
    n = a.size
    k = max(1, int(rate * hold))
    env = np.ones(n)
    if k < n:
        env[k:] = np.linspace(1, 0, n - k) ** power
    return a * env


def norm(a, to=PEAK):
    m = float(np.max(np.abs(a)))
    return a * (to / m) if m > 1e-9 else a


def group(folder, stem):
    """`name.wav`와 `name (1).wav`를 묶는다. 긴 것부터 — 고를 여유가 있는 쪽이 낫다."""
    found = []
    for p in glob.glob(os.path.join(folder, '*.wav')):
        base = re.sub(r'\s*\(\d+\)', '', os.path.basename(p)[:-4]).strip()
        if base == stem:
            found.append(p)
    found.sort(key=os.path.getsize, reverse=True)
    return found


def strike(a, rate, seed):
    """
    맞는 소리. <b>몸통 + 중역 + 고역</b> 세 겹을 쌓는다.

    원본의 저역은 빼고 내가 만든 몸통을 넣는다 — 둘을 그대로 더하면 서로 간섭해서
    오히려 힘이 빠진다. 원본은 '무엇에 맞았는지'만 맡는다.
    """
    rng = np.random.default_rng(seed)
    length = 0.34
    n = int(rate * length)

    raw = hit(a, rate, length)
    mid = tilt(raw, rate, 150.0, slope=1.2, high=True)      # 저역을 비운다
    mid = decay(mid, rate, hold=0.03, power=2.0)

    # 몸통을 <b>고역보다 조금 늦게</b> 놓는다. 같은 순간에 겹치면 피크끼리 더해져
    # 정규화가 둘 다 눌러 버린다 — 어긋나면 같은 피크로 더 크게 들린다.
    # 실물에서도 딱 소리가 먼저 오고 묵직함이 뒤따른다.
    lead = int(rate * 0.006)
    low = body(rate, seconds=0.22,
               f0=float(rng.uniform(105, 125)), f1=float(rng.uniform(42, 50)))
    low = np.pad(low, (lead, max(0, n - low.size - lead)))[:n]

    snap = tilt(raw, rate, 3000.0, slope=1.0, high=True)
    snap = decay(snap, rate, hold=0.002, power=4.0)

    # 섞는 비율. 피크를 -2dBFS로 맞추므로 <b>중역·고역을 낮추면 몸통이 올라간다</b>.
    # 처음에 0.62/0.30으로 했더니 저역 실효가 -19.9dBFS에 그쳤다 (전 -21.3에서 +1.4뿐).
    # 중역이 피크를 쥐고 있어서 정규화가 몸통을 같이 눌렀기 때문이다.
    out = low * 1.00 + mid * 0.40 + snap * 0.20
    return norm(out)


def swing(a, rate, seed):
    """
    주먹을 뻗는 소리. <b>고역을 걷어낸다</b> — 9983Hz짜리를 초당 다섯 번 반복한 것이
    드릴로 들린 원인이다. 짧게 잘라 다음 발과 안 겹치게 한다.
    """
    rng = np.random.default_rng(seed)
    length = 0.13                                   # 연타 간격 0.20초보다 짧게
    raw = hit(a, rate, length, pre=0.002)

    low = tilt(raw, rate, float(rng.uniform(850, 1100)), slope=1.6)
    low = decay(low, rate, hold=0.01, power=1.8)

    # 바람 소리 한 겹. 원본에 저역이 2.9%뿐이라 깎기만 하면 거의 아무것도 안 남는다.
    t = np.arange(low.size) / rate
    air = rng.standard_normal(low.size)
    air = tilt(air, rate, 700.0, slope=1.4)
    air *= np.sin(np.pi * np.clip(t / length, 0, 1)) ** 1.5      # 가운데가 제일 크다

    return norm(low * 0.75 + air * 0.45)


MAP = [
    ('robot_punch',     'Strike', 3, strike),
    ('robot_arm_swing', 'Punch',  3, swing),
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--dry', action='store_true')
    args = ap.parse_args()

    print('묵직한 펀치를 만든다 — 받은 음원에 없는 저역을 더한다')
    print('%-18s %-9s %5s  %s' % ('원본', '사건', '길이', '결과'))

    made = 0
    for stem, name, count, build in MAP:
        paths = group(SRC, stem)
        if not paths:
            print('%-18s %-9s %5s  없음 — 건너뜀' % (stem, name, '-'))
            continue

        a, rate = read(paths[0])
        outs = []
        for i in range(1, count + 1):
            v = build(a, rate, seed=1000 * i + len(name))
            path = os.path.join(OUT, 'SFX_%s_%02d.wav' % (name, i))
            if not args.dry:
                write(path, v, rate)
            outs.append('%.2fs' % (v.size / rate))
            made += 1
        print('%-18s %-9s %5s  %s' % (stem, name, '', ' '.join(outs)))

    # 쓰고 남은 변형이 있으면 지운다 — 전에 3개를 썼는데 2개만 쓰면 옛것이 섞인다.
    if not args.dry:
        for _, name, count, _ in MAP:
            for p in glob.glob(os.path.join(OUT, 'SFX_%s_*.wav' % name)):
                k = int(re.search(r'_(\d+)\.wav$', p).group(1))
                if k > count:
                    os.remove(p)
                    meta = p + '.meta'
                    if os.path.exists(meta):
                        os.remove(meta)
                    print('남은 옛 변형 지움: %s' % os.path.basename(p))

    print()
    print('%s %d개. 확인: python Tools/sfx_feel.py --gap %.2f --only punch' % (
        '만들었을 것' if args.dry else '만듦', made, GAP))
    if args.dry:
        print('(--dry 라서 아무 파일도 안 썼다)')


if __name__ == '__main__':
    main()
