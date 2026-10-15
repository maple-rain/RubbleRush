# -*- coding: utf-8 -*-
"""타격음을 **받은 음원에서 한 타씩 통째로** 떼어낸다 [지시 2026-10-15].

**기획자 지시.** "사운드 가공없이, 앞 뒤 사운드 길이만 자르고 만들어 달라",
"사운드의 중간부터 실행하지말고 처음부터 끝까지 사운드가 들리잖아 이런 한 사이클을
원하는거야, 이런 하나를 온전히 다 사용하기를 원하는건데".

**그래서 길이를 내가 정하지 않는다.** 전에는 0.30초니 0.45초니 하는 수를 정해 놓고
거기서 잘랐는데, 그러면 **감쇠 한가운데가 끊긴다** — 포성이 울리다 마는 소리가 되고,
그것이 '둔탁함'으로 들렸다. 이제는 **소리가 시작하는 곳이 시작, 끝나는 곳이 끝**이다.

**한 파일에 타격이 여러 번 들어 있다.** 무음으로 갈라서 한 타씩 뗀다. 실측하면
`firecracker_bang (2)`에는 온전한 타격이 둘 있고, 나머지에는 하나씩이다.
그 사이에 섞인 아주 작은 잡음 조각들(피크 -21~-50dB)은 타격이 아니므로 버린다.

**음원에 하는 일은 셋뿐이다.**
1. 한 타의 앞뒤 무음을 뗀다
2. 끝의 8ms를 0으로 내린다 — 잘린 자리의 '딱' 소리는 원본에 없던 소리다
3. 변형끼리 들리는 크기를 맞춘다 (배율 하나. 대역도 음높이도 안 건드린다)

섞지 않는다. 대역을 깎지 않는다. 피치를 옮기지 않는다.

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

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'Assets', '_Project', 'Audio', 'SFX')
DEFAULT_SRC = os.path.join(os.path.expanduser('~'), 'Downloads')

# 받은 음원. 한 파일에서 온전한 타격을 전부 떼어 쓴다.
SOURCES = ['firecracker_bang (1).wav',
           'firecracker_bang (2).wav',
           'firecracker_bang (3).wav',
           'cannon_fire (1).wav',
           'cannon_fire (2).wav',
           'cannon_fire (3).wav',
           'fighting_sound (1).wav',
           'punch_impact (1).wav']          # 있으면 쓴다. 없으면 조용히 건너뛴다

SILENCE_DB = -45.0      # 이 아래는 무음으로 본다 (파일 피크 기준)
JOIN = 0.08             # 이보다 짧게 끊기는 틈은 같은 한 타로 본다(초)
MIN_HIT = 0.15          # 이보다 짧으면 타격이 아니라 잡음 조각이다(초)
MIN_PEAK_DB = -12.0     # 파일 피크보다 이만큼 아래면 타격이 아니다
TAIL_FADE = 0.008       # 끝을 0으로 내리는 길이(초)

# <b>변형끼리 들리는 크기만 맞춘다.</b> 끄면 원본 음량 그대로 나온다.
# 가공이 아니다 — 배율 하나를 곱할 뿐이라 소리의 성질은 그대로다.
MATCH_LEVEL = True

# <b>고역을 올린다</b> [지시 2026-10-15: "조금 더 귀에 팍 하고 박혔으면, 하이피치 조금만 올려"].
#
# 음높이를 옮기는 것이 아니다 — <b>높은 쪽 대역만 키운다.</b> 음은 그대로고
# 날카로움만 올라간다. 0이면 원본 그대로다.
TREBLE_DB = 4.0
TREBLE_FROM = 2000.0        # 이 위를 올린다(Hz)


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


def envelope(x, window=0.010):
    k = max(1, int(SR * window))
    return np.convolve(np.abs(x), np.ones(k) / k, 'same')


def hits(x):
    """
    한 파일에서 <b>온전한 타격</b>들을 찾아 (시작, 끝) 목록으로 돌려준다.

    소리가 무음 문턱 위로 올라온 곳이 시작, 다시 내려가 <paramref name="JOIN"/>보다
    오래 머무는 곳이 끝이다. <b>길이를 숫자로 정하지 않는 것이 요점이다</b> —
    감쇠가 긴 포성은 길게, 짧은 폭죽은 짧게 저절로 잡힌다.
    """
    env = envelope(x)
    top = float(np.max(env))
    if top < 1e-9:
        return []

    loud = env > top * (10 ** (SILENCE_DB / 20.0))
    gap = int(SR * JOIN)
    peak_floor = float(np.max(np.abs(x))) * (10 ** (MIN_PEAK_DB / 20.0))

    out, i, n = [], 0, loud.size
    while i < n:
        if not loud[i]:
            i += 1
            continue
        start, last = i, i
        while i < n and (loud[i] or i - last < gap):
            if loud[i]:
                last = i
            i += 1
        if (last - start) >= int(SR * MIN_HIT) and float(np.max(np.abs(x[start:last + 1]))) >= peak_floor:
            out.append((start, last))
    return out


def cut(x, start, end):
    """한 타를 그대로 떼어 온다. 끝만 0으로 내린다."""
    y = x[start:end + 1].copy()
    k = min(y.size, max(1, int(SR * TAIL_FADE)))
    y[-k:] *= np.linspace(1.0, 0.0, k)
    return y


def shelf(x, from_hz, gain_db):
    """
    <b>높은 쪽 대역만 키우거나 줄인다</b>(하이셸프).

    음높이를 옮기는 것이 아니다 — 재생 속도도 길이도 그대로이고, 대역별 세기만 바뀐다.
    <paramref name="from_hz"/> 아래는 손대지 않고 그 위로 부드럽게 <paramref name="gain_db"/>까지 간다.
    """
    if abs(gain_db) < 0.01:
        return x
    spec = np.fft.rfft(x)
    f = np.fft.rfftfreq(x.size, 1.0 / SR)
    # from_hz에서 1배, 그 두 배 지점에서 목표치. 급하게 꺾지 않는다
    t = np.clip(np.log2(np.maximum(f, 1.0) / from_hz), 0.0, 1.0)
    spec *= 10 ** (gain_db * t / 20.0)
    y = np.fft.irfft(spec, n=x.size)
    peak = float(np.max(np.abs(y)))
    return y * (0.999 / peak) if peak > 0.999 else y


def heard(x):
    """
    <b>귀가 듣는 크기.</b> 300Hz~6kHz 대역의 세기로 잰다.

    그냥 RMS로 재면 저역을 그대로 세는데 귀는 저역에 훨씬 둔하다. 그러면 포성처럼
    저역이 많은 소리가 실제보다 크게 계산되어 <b>필요 이상으로 깎이고</b> 묵직함을 잃는다.

    <b>재기만 한다.</b> 이 필터는 소리에 걸지 않는다 — 곱할 배율 하나를 정할 뿐이다.
    """
    spec = np.abs(np.fft.rfft(x)) ** 2
    f = np.fft.rfftfreq(x.size, 1.0 / SR)
    return float(np.sqrt(spec[(f >= 300) & (f <= 6000)].sum() / max(1, x.size)))


def match_level(waves):
    """제일 조용한 것에 맞춘다. 내리는 것은 언제나 되므로 정확히 맞는다."""
    levels = [heard(w) for w in waves]
    target = min(l for l in levels if l > 1e-12)
    return [w * (target / l) if l > 1e-12 else w for w, l in zip(waves, levels)]


def save(name, x):
    path = os.path.normpath(os.path.join(OUT, name + '.wav'))
    data = np.clip(x, -1.0, 1.0)
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((data * 32767.0).astype('<i2').tobytes())
    return path


def db(v):
    return 20.0 * np.log10(max(float(v), 1e-9))


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    write = '--write' in sys.argv
    src = DEFAULT_SRC
    if '--src' in sys.argv:
        src = sys.argv[sys.argv.index('--src') + 1]

    os.makedirs(os.path.normpath(OUT), exist_ok=True)

    pieces, labels = [], []
    for name in SOURCES:
        path = os.path.join(src, name)
        if not os.path.exists(path):
            print('  %-24s 없음 — 건너뜀' % name[:-4])
            continue
        x = load(path)
        found = hits(x)
        print('  %-24s %5.2f초 -> 온전한 타격 %d개' % (name[:-4], x.size / float(SR), len(found)))
        for k, (s, e) in enumerate(found):
            piece = cut(x, s, e)
            pieces.append(shelf(piece, TREBLE_FROM, TREBLE_DB))
            labels.append('%s%s' % (name[:-4], '' if len(found) == 1 else ' #%d' % (k + 1)))
            print('        %6.0f~%6.0fms  길이 %5.0fms' % (s * 1000.0 / SR, e * 1000.0 / SR,
                                                          piece.size * 1000.0 / SR))

    if not pieces:
        print('타격을 하나도 못 찾았다.')
        return 1

    before = [db(np.sqrt((p ** 2).mean())) for p in pieces]
    if MATCH_LEVEL:
        pieces = match_level(pieces)

    print()
    longest = 0.0
    for i, p in enumerate(pieces):
        name = 'SFX_Strike_%02d' % (i + 1)
        if write:
            save(name, p)
        longest = max(longest, p.size / float(SR))
        print('  %s  %-24s %5.0fms  피크%6.1fdB  RMS%6.1fdB (맞추기 전%6.1f)'
              % (name, labels[i], p.size * 1000.0 / SR,
                 db(np.max(np.abs(p))), db(np.sqrt((p ** 2).mean())), before[i]))

    print()
    print('%d개. 길이를 정하지 않았다 — 소리가 끝나는 곳이 끝이다 (제일 긴 것 %.2f초).'
          % (len(pieces), longest))
    print('음원에 한 일: 앞뒤 무음 떼기 · 끝 8ms 내리기 · 크기 맞추기 · 고역 %+.1fdB.' % TREBLE_DB)
    print('섞지 않았고, 음높이도 길이도 그대로다 — 고역만 %d Hz 위에서 올렸다.' % TREBLE_FROM)
    print('연타는 초당 다섯 번이라 %.0f개까지 겹친다 — SfxPool 음성 수가 그만큼 있어야 한다.'
          % (longest / 0.2 + 1))
    print()
    if write:
        print('덮어썼다. 유니티가 다시 불러온다. 사운드뱅크에 %d장을 물려야 한다.' % len(pieces))
    else:
        print('재기만 했다. 실제로 쓰려면 --write 를 붙일 것.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
