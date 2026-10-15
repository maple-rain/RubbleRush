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
#
# <b>묶음마다 쓰임이 다르다</b> [지시 2026-10-15]:
#   Strike  = 적중. 부딪힌 물체에서 나는 소리
#   Whoosh  = 휘두름. 주먹이 공기를 가르는 소리 (소닉붐 자리)
#   Barrage = 연타 뭉치. 한 조각이 0.7~2.1초라 한 대씩이 아니라 몰아칠 때 쓴다
GROUPS = {
    # <b>묶음 이름 = 사운드뱅크의 Sfx 이름</b> [고침 2026-10-15].
    # Demo/16(파일 이름으로 자동 연결)이 SFX_<Sfx이름>_NN 규칙을 쓴다. 이름이 어긋나 있으면
    # 그 메뉴를 한 번 누르는 순간 연결이 통째로 뒤집힌다 — 실제로 그 위험을 안고 있었다.
    'Boom': ['firecracker_bang (1).wav',
               'firecracker_bang (2).wav',
               'firecracker_bang (3).wav',
               'cannon_fire (1).wav',
               'cannon_fire (2).wav',
               'cannon_fire (3).wav',
               'fighting_sound (1).wav'],
    'Strike': ['Heavy body impact_1.wav',
               'Heavy body impact_2.wav',
               'Heavy body impact_3.wav'],
    'Punch': ['Fast movement whoosh_1.wav',
               'Fast movement whoosh_2.wav',
               'Fast movement whoosh_3.wav'],
    'Barrage': ['Rapid punch barrage_1.wav',
                'Rapid punch barrage_2.wav',
                'Rapid punch barrage_3.wav'],
}

# 묶음마다 고역을 얼마나 올릴지(dB). 음을 옮기는 것이 아니라 대역 세기다.
TREBLE = {'Boom': 4.0, 'Strike': 4.0, 'Punch': 0.0, 'Barrage': 2.0}

# <b>정점을 같은 자리에 맞춘다</b> [지시 2026-10-15]. (앞으로 둘 시간, 전체 길이) 초.
#
# 휘두름 소리는 주먹이 나가는 순간에 틀고 타격은 165ms 뒤에 온다. 그런데 원본 여덟 장은
# 제일 센 지점이 133ms에서 765ms까지 <b>632ms나 흩어져 있었다</b> — 어느 장이 뽑히느냐에
# 따라 바람소리가 주먹과 맞기도 하고 600ms 늦게 나기도 했다.
# 게다가 길이가 1.57초까지라 연타(0.19초 간격)에서 아홉 개가 겹쳤다.
#
# 정점을 0.13초에 두면 타격(0.165초)보다 35ms 먼저 분다 — 스쳐 지나가고 맞는 순서다.
ALIGN = {'Punch': (0.13, 0.34)}

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


def align_peak(x, lead, total):
    """
    <b>제일 센 지점이 항상 같은 자리에 오게</b> 잘라 낸다.

    앞이 모자라면 무음을 채우고, 남으면 버린다. 뒤는 <paramref name="total"/>까지만 쓰고
    끝 20%를 내려 끊기는 소리를 없앤다. <b>소리 자체는 안 건드린다</b> — 어느 구간을
    가져올지만 정한다.
    """
    # <b>샘플 하나의 최대값이 아니라 포락선의 봉우리를 쓴다.</b>
    # 바람소리는 과도음이 없어서 어느 한 샘플이 제일 큰지가 들쭉날쭉하다 —
    # 그걸 기준으로 맞추니 여덟 장이 40~130ms로 90ms 어긋났다.
    # 20ms로 고르게 편 다음 보면 '소리가 제일 센 구간'이 잡힌다.
    k = max(1, int(SR * 0.020))
    env = np.convolve(np.abs(x), np.ones(k) / k, 'same')
    top = int(np.argmax(env))
    want_lead = int(SR * lead)
    n = int(SR * total)

    start = top - want_lead
    if start >= 0:
        y = x[start:start + n]
    else:
        y = np.concatenate([np.zeros(-start), x[:n + start]])
    if y.size < n:
        y = np.pad(y, (0, n - y.size))

    y = y.copy()
    k = max(1, int(y.size * 0.20))
    y[-k:] *= np.linspace(1.0, 0.0, k) ** 1.5
    return y


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

    total = 0
    for group, files in GROUPS.items():
        pieces, labels = [], []
        for name in files:
            path = os.path.join(src, name)
            if not os.path.exists(path):
                print('  [%s] %-28s 없음 — 건너뜀' % (group, name[:-4]))
                continue
            x = load(path)
            found = hits(x)
            print('  [%s] %-28s %5.2f초 -> %d개' % (group, name[:-4], x.size / float(SR), len(found)))
            for k, (a, b) in enumerate(found):
                piece = cut(x, a, b)
                if group in ALIGN:
                    piece = align_peak(piece, ALIGN[group][0], ALIGN[group][1])
                pieces.append(shelf(piece, TREBLE_FROM, TREBLE.get(group, 0.0)))
                labels.append('%s%s' % (name[:-4], '' if len(found) == 1 else ' #%d' % (k + 1)))

        if not pieces:
            continue
        before = [db(np.sqrt((p ** 2).mean())) for p in pieces]
        if MATCH_LEVEL:
            pieces = match_level(pieces)

        longest = 0.0
        for i, p in enumerate(pieces):
            nm = 'SFX_%s_%02d' % (group, i + 1)
            if write:
                save(nm, p)
            longest = max(longest, p.size / float(SR))
            print('      %-18s %-26s %5.0fms  피크%6.1f  RMS%6.1f (전%6.1f)'
                  % (nm, labels[i], p.size * 1000.0 / SR,
                     db(np.max(np.abs(p))), db(np.sqrt((p ** 2).mean())), before[i]))
        print('      -> %s %d장 · 제일 긴 것 %.2f초 · 고역 %+.1fdB'
              % (group, len(pieces), longest, TREBLE.get(group, 0.0)))
        print()
        total += len(pieces)

    print('모두 %d장. 길이를 정하지 않았다 — 소리가 끝나는 곳이 끝이다.' % total)
    print('음원에 한 일: 앞뒤 무음 떼기 · 끝 8ms 내리기 · 묶음 안에서 크기 맞추기 · 고역.')
    print('섞지 않았고, 음높이도 길이도 그대로다.')
    print()
    if write:
        print('덮어썼다. 사운드뱅크 연결은 따로 해야 한다.')
    else:
        print('재기만 했다. 실제로 쓰려면 --write 를 붙일 것.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
