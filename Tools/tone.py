# -*- coding: utf-8 -*-
"""음원의 <b>높은 쪽 대역만</b> 올리거나 내린다 [지시 2026-10-15].

**음높이를 옮기는 것이 아니다.** 재생 속도도 길이도 그대로이고, 대역별 세기만 바뀐다.
기획자가 "하이피치를 올려/줄여"라고 할 때 원하는 것이 이것이다 —
소리가 날카로워지거나 부드러워질 뿐, 음은 그대로다.

**두 번 돌리면 두 번 깎인다.** 제자리에서 덮어쓰므로 이미 처리된 파일에 다시 돌리면
효과가 쌓인다. 실제로 한 번 당했다 — `git checkout --`로 되돌렸는데 그 커밋에 이미
깎인 것이 들어 있어서 `SFX_TowLoop_01`의 3k~8k가 5.7% -> 2.0% -> **0.6%**가 됐다.

**되돌릴 때는 처리 전 커밋을 지정한다**(아래 `BEFORE`):
    git checkout <BEFORE> -- Assets/_Project/Audio/SFX/SFX_TowLoop_01.wav

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

# 처리 전 원본이 들어 있는 커밋. 다시 돌리려면 여기서 복구한 뒤 실행한다.
#   git checkout 6eeddb7~1 -- "Assets/_Project/Audio/SFX/SFX_TowLoop_01.wav"
#   git checkout 6342748   -- "Assets/_Project/Audio/SFX/SFX_Capture_0*.wav"
BEFORE = {'SFX_TowLoop_01': '6eeddb7~1',
          'SFX_Capture_01': '6342748',
          'SFX_Capture_02': '6342748',
          'SFX_Capture_03': '6342748'}

# (파일, 이 주파수 위를, 몇 dB)
JOBS = [
    # 견인 소리가 "너무 날것" [지시 2026-10-15]. 실측하면 1k~3k가 30.1%로
    # 거친 중고역이 많다. 끄는 내내 들리는 소리라 그 대역이 특히 피곤하다.
    ('SFX_TowLoop_01', 1500.0, -5.0),

    # 케이블로 감쌌을 때 [지시 2026-10-15: "하이피치도 조금 낮춰줘"].
    # 실측하면 변형끼리 밝기가 제각각이다 — 01은 3kHz 위 39%, 02는 <b>82%</b>, 03은 12%.
    # 02가 유난히 쇳소리가 난다. 그래서 <b>밝은 둘만 더 깎는다.</b>
    ('SFX_Capture_01', 2000.0, -5.0),
    ('SFX_Capture_02', 2000.0, -8.0),
    ('SFX_Capture_03', 2000.0, -3.0),
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
        print('덮어썼다. <b>다시 돌리기 전에 반드시 처리 전 커밋에서 복구할 것</b> —')
        for name, ref in BEFORE.items():
            print('  git checkout %s -- "Assets/_Project/Audio/SFX/%s.wav"' % (ref, name))
    else:
        print('재기만 했다. 실제로 쓰려면 --write 를 붙일 것.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
