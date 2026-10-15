# -*- coding: utf-8 -*-
"""음원의 <b>높은 쪽 대역만</b> 올리거나 내린다 [지시 2026-10-15].

**음높이를 옮기는 것이 아니다.** 재생 속도도 길이도 그대로이고, 대역별 세기만 바뀐다.
기획자가 "하이피치를 올려/줄여"라고 할 때 원하는 것이 이것이다 —
소리가 날카로워지거나 부드러워질 뿐, 음은 그대로다.

**원본은 git에 있다.** 제자리에서 덮어쓰므로 다시 돌리려면 먼저 되돌려야 한다:
    git checkout -- Assets/_Project/Audio/SFX/SFX_TowLoop_01.wav

쓰는 법:
    python Tools/tone.py            # 재기만 한다
    python Tools/tone.py --write
"""
import os
import sys
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
SFX = os.path.normpath(os.path.join(HERE, '..', 'Assets', '_Project', 'Audio', 'SFX'))

# (파일, 이 주파수 위를, 몇 dB)
JOBS = [
    # 견인 소리가 "너무 날것" [지시 2026-10-15]. 실측하면 1k~3k가 30.1%로
    # 거친 중고역이 많다. 끄는 내내 들리는 소리라 그 대역이 특히 피곤하다.
    ('SFX_TowLoop_01', 1500.0, -5.0),
]


def load(path):
    w = wave.open(path, 'rb')
    n, sr, ch = w.getnframes(), w.getframerate(), w.getnchannels()
    x = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float64) / 32768.0
    w.close()
    if ch > 1:
        x = x.reshape(-1, ch).mean(axis=1)
    return x, sr, ch


def save(path, x, sr, ch):
    data = np.clip(x, -1.0, 1.0)
    if ch > 1:
        data = np.repeat(data[:, None], ch, axis=1).reshape(-1)
    with wave.open(path, 'wb') as w:
        w.setnchannels(ch)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes((data * 32767.0).astype('<i2').tobytes())


def shelf(x, sr, from_hz, gain_db):
    """<paramref name="from_hz"/> 아래는 그대로 두고, 그 위로 부드럽게 기울인다."""
    if abs(gain_db) < 0.01:
        return x
    spec = np.fft.rfft(x)
    f = np.fft.rfftfreq(x.size, 1.0 / sr)
    t = np.clip(np.log2(np.maximum(f, 1.0) / from_hz), 0.0, 1.0)
    spec = spec * (10 ** (gain_db * t / 20.0))
    y = np.fft.irfft(spec, n=x.size)
    peak = float(np.max(np.abs(y)))
    return y * (0.999 / peak) if peak > 0.999 else y


def bands(x, sr):
    spec = np.abs(np.fft.rfft(x * np.hanning(x.size))) ** 2
    f = np.fft.rfftfreq(x.size, 1.0 / sr)
    total = spec.sum() + 1e-20
    out = []
    for lo, hi in ((0, 300), (300, 1000), (1000, 3000), (3000, 8000), (8000, sr / 2)):
        out.append(100.0 * spec[(f >= lo) & (f < hi)].sum() / total)
    return out


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    write = '--write' in sys.argv

    print('%-22s %7s %7s %7s %7s %7s' % ('', '<300', '300~1k', '1k~3k', '3k~8k', '8k 위'))
    for name, from_hz, gain in JOBS:
        path = os.path.join(SFX, name + '.wav')
        if not os.path.exists(path):
            print('%-22s 파일이 없다' % name)
            continue
        x, sr, ch = load(path)
        y = shelf(x, sr, from_hz, gain)

        print('%-22s' % (name + ' 전'), end='')
        for v in bands(x, sr):
            print(' %6.1f%%' % v, end='')
        print()
        print('%-22s' % ('  %+.0fdB (%.0fHz 위)' % (gain, from_hz)), end='')
        for v in bands(y, sr):
            print(' %6.1f%%' % v, end='')
        print()

        if write:
            save(path, y, sr, ch)

    print()
    print('음높이도 길이도 안 건드렸다 — 대역별 세기만 바뀐다.')
    if write:
        print('덮어썼다. 되돌리려면 git checkout 으로 원본을 복구할 것.')
    else:
        print('재기만 했다. 실제로 쓰려면 --write 를 붙일 것.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
