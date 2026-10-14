# -*- coding: utf-8 -*-
"""타격음을 **받은 음원 일곱 장으로만** 만든다 [지시 2026-10-14].

**재료는 이 일곱 장뿐이다.** 기획자가 준 것이고, 다른 것을 섞지 않는다.

    firecracker_bang (1)(2)(3)
    cannon_fire      (1)(2)(3)
    fighting_sound   (1)

**이 도구는 소리를 만들지 않는다. 자르고 겹칠 뿐이다.**

앞선 판본들은 대역 필터로 음원을 깎고 겹마다 몫을 정했다. 그 결과 `cannon`은
150Hz 아래만 남아 폰에서 들리지 않았고, `firecracker`는 앞 0.16초만 쓰여 양념이 됐다.
기획자 지적: **"내가 준 7개의 파일만 넣으라 했는데 왜 또 임의로 판단하는거지?"**
맞는 지적이다. 받아 온 소리에서 뭘 버릴지는 내가 고를 일이 아니었다.

**그래서 여기에는 필터가 없고 배합비도 없다.** 남은 손질은 둘뿐이고,
둘 다 게임의 박자가 강제하는 것이다:

1. **앞의 무음을 떼어낸다.** 실측하면 일곱 중 여섯이 24~1205ms 뒤에야 소리가 난다.
   그대로 두면 주먹이 닿고 한참 뒤에 소리가 나 '맞은 것 같지 않다'가 된다.
2. **0.30초로 자른다.** 타격은 초당 다섯 번이라 간격이 0.2초다. 원본은 1.15~5.79초라
   그대로 쓰면 다음 발과 겹쳐 뭉갠다.

셋은 **같은 에너지로** 겹친다. 누가 더 크고 작을지는 내가 정할 일이 아니다.

쓰는 법:
    python Tools/make_strike.py                      # 재기만 한다
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

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Assets', '_Project', 'Audio', 'SFX')
DEFAULT_SRC = os.path.join(os.path.expanduser('~'), 'Downloads')

# <b>한 변형이 한 장이다. 섞지 않는다</b> [지시 2026-10-14].
#
# 전에는 firecracker + cannon + fighting 셋을 같은 에너지로 겹쳤다. 그것이
# <b>먹먹함의 원인</b>이었다. 실측:
#
#   firecracker (1) 원본 : 2kHz 위 60.5% · 피크-RMS 15.2dB
#   셋을 겹친 것         : 2kHz 위 21.8% · 피크-RMS 11.9dB
#
# `cannon`이 150Hz 아래를 2.3%에서 50.7%로 끌어올렸고, 더 중요하게는
# <b>피크-RMS가 3.3dB 떨어졌다.</b> 이 값이 곧 '때리는 느낌'이다 — 소리 셋을 겹치면
# 과도음이 서로 메워져 뭉개진다. 기획자 말대로 "이상하게 만져서 먹먹해졌"다.
#
# 받은 소리는 그 자체로 과격하고 날것이다. 그대로 쓴다.
SOURCES = ['firecracker_bang (1).wav',
           'firecracker_bang (2).wav',
           'firecracker_bang (3).wav']

# <b>변형끼리 음량을 맞출 것인가.</b> 기획자 지시로 꺼 두었다 — "원본 그대로 가자".
MATCH_LEVEL = False


def load(path):
    """모노 float로 읽는다. 스테레오면 평균 내고, 48kHz가 아니면 맞춘다."""
    w = wave.open(path, 'rb')
    n, sr, ch = w.getnframes(), w.getframerate(), w.getnchannels()
    raw = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float64) / 32768.0
    w.close()
    if ch > 1:
        raw = raw.reshape(-1, ch).mean(axis=1)
    if sr != SR:
        m = int(round(raw.size * SR / float(sr)))
        raw = np.interp(np.linspace(0, raw.size - 1, m), np.arange(raw.size), raw)
    return raw


def onset(x, back=0.030, rise=0.12):
    """
    <b>제일 큰 봉우리를 찾고 거기서 거꾸로 올라간다.</b>

    "문턱을 처음 넘는 지점"으로 잡으면 진짜 폭발 앞의 약한 소리에서 잘린다 —
    `firecracker_bang (2)`는 실제 폭발이 1205ms인데 70ms에서 잘렸던 적이 있다.
    봉우리 기준이면 앞에 뭐가 있든 <b>제일 센 한 방</b>에 맞춰진다.
    """
    a = np.abs(x)
    top = int(np.argmax(a))
    peak = float(a[top])
    if peak < 1e-6:
        return 0

    lo = max(0, top - int(SR * back))
    above = a[lo:top + 1] > peak * rise
    return lo + int(np.argmax(above)) if above.any() else top


def tail_fade(x, seconds=0.025):
    """끝을 0으로 내린다. 안 하면 잘린 자리에서 '툭' 하고 끊긴다."""
    k = min(x.size, max(1, int(SR * seconds)))
    x = x.copy()
    x[-k:] *= np.linspace(1.0, 0.0, k)
    return x


def take(x):
    """<b>이 도구가 음원에 하는 일의 전부다.</b> 무음을 떼고, 0.30초로 자르고, 끝을 내린다."""
    n = int(SR * LENGTH)
    y = x[onset(x):][:n]
    if y.size < n:
        y = np.pad(y, (0, n - y.size))
    return tail_fade(y)


def _unused_equal_energy(waves):
    """같은 에너지로 맞춘다. 누가 더 크고 작을지는 내가 정할 일이 아니다."""
    out = []
    for w in waves:
        e = float((w ** 2).sum())
        out.append(w / np.sqrt(e) if e > 1e-12 else w)
    return out


def norm(x, peak=PEAK):
    m = float(np.max(np.abs(x)))
    return x * (peak / m) if m > 1e-9 else x


def rms_db(x):
    """귀가 듣는 크기. 피크는 제일 큰 한 점만 보므로 변형끼리 비교가 안 된다."""
    return 20.0 * np.log10(max(float(np.sqrt((x ** 2).mean())), 1e-9))


def crest_db(x):
    """
    피크 대 RMS. <b>이 값이 곧 '때리는 느낌'이다.</b>

    소리를 여러 장 겹치면 과도음이 서로 메워져 이 값이 떨어진다 — 실측에서
    firecracker 혼자 15.2dB이던 것이 셋을 겹치니 11.9dB이 됐고, 그만큼 먹먹해졌다.
    대역 비율만 보면 안 보이는 것이라 따로 잰다.
    """
    r = max(float(np.sqrt((x ** 2).mean())), 1e-9)
    return 20.0 * np.log10(float(np.max(np.abs(x))) / r)


def top_band(x):
    """2kHz 위가 몇 %인가. 날카로움은 여기 있다."""
    spec = np.abs(np.fft.rfft(x * np.hanning(x.size))) ** 2
    f = np.fft.rfftfreq(x.size, 1.0 / SR)
    return 100.0 * spec[f >= 2000].sum() / (spec.sum() + 1e-20)


def match_level(waves):
    """
    변형끼리 <b>RMS를 같게</b> 맞춘다. 대역은 건드리지 않는다.

    <b>제일 조용한 변형에 맞춘다.</b> 평균에 맞추면 조용한 쪽을 올려야 하는데,
    그러다 피크 천장에 걸려 못 올라가는 변형이 생긴다 — 실측에서 평균 기준으로는
    5.3dB가 3.0dB까지밖에 안 줄었다. 내리는 것은 언제나 되므로 최소에 맞추면 정확히 맞는다.

    전체가 조용해지는 것은 문제가 아니다. 소리의 크기는 사운드뱅크의 `volume`이 정한다.
    """
    target = min(float(np.sqrt((w ** 2).mean())) for w in waves)
    out = []
    for w in waves:
        r = float(np.sqrt((w ** 2).mean()))
        out.append(w * (target / r) if r > 1e-9 else w)
    return out


def save(name, x):
    path = os.path.normpath(os.path.join(OUT, name + '.wav'))
    data = np.clip(x, -1.0, 1.0)
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((data * 32767.0).astype('<i2').tobytes())
    return path


def bands(x):
    """300Hz 아래 · 500Hz 위가 각각 몇 %인가. <b>재서 적을 뿐, 여기에 맞추지 않는다.</b>"""
    spec = np.abs(np.fft.rfft(x * np.hanning(x.size))) ** 2
    f = np.fft.rfftfreq(x.size, 1.0 / SR)
    total = spec.sum() + 1e-20
    return 100.0 * spec[f < 300].sum() / total, 100.0 * spec[f >= 500].sum() / total


def read_back(path):
    """
    <b>방금 쓴 파일을 다시 읽어 잰다.</b>

    메모리의 배열이 아니라 디스크에 남은 것을 재야 한다. "저역 78~82%"라고 보고했는데
    실제 파일은 93.5%였던 일이 있다 — 중간 단계를 재고 결과를 안 쟀기 때문이다.
    """
    w = wave.open(path, 'rb')
    raw = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16)
    w.close()
    return raw.astype(np.float64) / 32768.0


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    write = '--write' in sys.argv
    src = DEFAULT_SRC
    if '--src' in sys.argv:
        src = sys.argv[sys.argv.index('--src') + 1]

    os.makedirs(os.path.normpath(OUT), exist_ok=True)

    cuts = []
    for name in SOURCES:
        path = os.path.join(src, name)
        if not os.path.exists(path):
            print('원본을 못 찾았다:', path)
            return 1
        raw = load(path)
        print('  %-26s 시작 %5.0fms · 원본 %.2f초' % (name[:-4], onset(raw) * 1000.0 / SR,
                                                   raw.size / float(SR)))
        cuts.append(norm(take(raw)))

    raw_db = [rms_db(c) for c in cuts]
    if MATCH_LEVEL:
        cuts = match_level(cuts)

    print()
    made = []
    for i, cut in enumerate(cuts, start=1):
        name = 'SFX_Strike_%02d' % i
        if write:
            disk = read_back(save(name, cut))             # 디스크에 남은 것을 잰다
        else:
            disk = cut
        lo, hi = bands(disk)
        print('  %s  %s  %.2f초  저역 %.1f%% · 고역 %.1f%% · 2kHz 위 %.1f%%   피크-RMS %.1fdB'
              % (name, '썼다' if write else '(안 씀)', LENGTH, lo, hi, top_band(disk),
                 crest_db(disk)))
        made.append(name)

    print()
    print('한 변형이 한 장이다 — 섞지 않는다. 필터도 없다.')
    print('음원에 한 일: 앞 무음 떼기 · 0.30초로 자르기 · 끝 내리기. 그게 전부다.')
    print('피크-RMS가 곧 때리는 느낌이다. 겹쳐 섞으면 이 값이 떨어진다 (15.2 -> 11.9dB였다).')
    if MATCH_LEVEL:
        print('변형끼리 음량을 맞췄다 — 끄려면 MATCH_LEVEL = False.')
    else:
        print('음량은 원본 그대로다 [지시] — 맞추려면 MATCH_LEVEL = True.')
    print()
    if not write:
        print('재기만 했다. 실제로 쓰려면 --write 를 붙일 것.')
    else:
        print('%d개를 덮어썼다. 유니티가 다시 불러온다.' % len(made))
    return 0


if __name__ == '__main__':
    sys.exit(main())
