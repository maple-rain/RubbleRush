# -*- coding: utf-8 -*-
"""타격음을 받은 음원 둘을 섞어 만든다 [지시 2026-10-14].

**재료.** 기획자가 받아 온 `firecracker_bang`과 `cannon_fire` 각 세 개.
둘 다 48kHz 모노이고 피크가 -2.0 dBFS로 이미 프로젝트 기준선과 같다.

**왜 그냥 못 쓰는가.** 실측하면 여섯 중 다섯이 **앞에 무음이 들어 있고**
(앞 50ms 에너지 0%) 길이가 1.15~5.79초다. 타격음은 **초당 다섯 번** 울리므로
한 발이 0.2초보다 길면 다음 발과 겹쳐 뭉갠다. 지금 쓰는 소리가 0.34초인 것이 그 이유다.

**어떻게 섞는가.** 실측하면 셋이 가진 것이 다르다 (시작부터 0.30초 구간, 네 대역):

| 음원 | <150Hz | 150~500 | 500~2k | >2k |
|---|---|---|---|---|
| `firecracker` | 2~38% | 13~29% | 23~28% | **18~61%** |
| `cannon` | **74~90%** | 7~17% | 2~8% | 0.5~2% |
| `fighting` | 44% | 29% | 9% | 18% |

**`firecracker`가 전 대역을 가진 유일한 음원이다 — 그래서 본체로 쓴다.**
`cannon`은 서브베이스뿐이라 무게만 맡고, `fighting`은 받침이다.

처음에는 거꾸로 했다. `firecracker`를 앞 0.16초만 뿌리는 양념으로 쓰고
`fighting_sound`를 본체+중역으로 올려 **그 한 장이 66%**를 차지했다.
기획자가 "제공한 6개를 쓰라고 했는데 쓴 것 같지 않다"고 지적했고, 재 보니
처음 받은 6개가 34%뿐이었다 — `cannon`은 150Hz 아래로 깎아 6%만 넣어
**폰 스피커로는 들릴 수가 없었다.** 대역 균형을 맞추는 데 쫓겨 재료를 잃은 것이다.

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

# ── 음원이 차지할 몫 ───────────────────────────────────────────────────────
#
# <b>피크가 아니라 에너지로 나눈다</b> [고침 2026-10-14]. 겹마다 피크를 1.0으로 맞추고
# 가중치를 곱하면, 피크가 같아도 <b>0.30초짜리 저역과 0.16초짜리 고역은 에너지가
# 12배 차이 난다.</b> "균열 0.95"라고 적어 두고 실제로는 4.9%였던 적이 있다.
#
# 여기 숫자는 그대로 <b>최종 에너지 비율</b>이고, 합이 1이다. 0.50이면 절반이다.
# <b>겹이 아니라 음원 단위로 적는다</b> — 어느 음원이 몇 %인지가 바로 보여야,
# "제공한 음원을 안 쓰고 있다"가 숫자로 드러난다.
SHARE_CRACK = 0.50                  # firecracker — 본체. 전 대역을 가진 유일한 음원
SHARE_BOOM = 0.26                   # cannon — 무게. 서브베이스뿐이라 이쪽만 맡는다
SHARE_THUD = 0.24                   # fighting — 받침. 중역을 채워 폰에서 들리게 한다

BOOM_CUT = 260                      # cannon을 어디까지 남길지(Hz)
THUD_LO, THUD_HI = 500, 3200        # fighting에서 떠 올 중역

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


def highpass(x, cutoff, times=1):
    """
    저역통과를 빼서 만든 고역통과. <b>차수를 올리면 거꾸로 나빠진다</b> [실측 2026-10-14].

    이 뺄셈은 저역통과가 <b>입력과 위상이 맞을 때만</b> 상쇄된다. 한 극씩 쌓을수록
    위상이 밀려 뺄셈이 어긋나고, 상쇄되지 못한 저역이 그대로 남는다. 실측하면
    600Hz에서 order=1은 저역을 27.9%까지 깎는데 <b>order=8은 79.1%를 남긴다</b> —
    가파르게 만들려고 올린 숫자가 정확히 반대로 작용한다.

    가파르게 하려면 <b>같은 필터를 여러 번 건다.</b> 2번이면 20.8%, 3번이면 9.6%다.
    전에 <c>order=2</c>를 "2차라 가파르다"로 읽어 중역 겹이 42.7%나 저역이었다 —
    그래서 폰 스피커용으로 넣은 겹이 제 몫을 못 했다.
    """
    out = x
    for _ in range(max(1, times)):
        out = out - lowpass(out, cutoff, 1)
    return out


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


def share(x, fraction):
    """
    이 겹이 합친 소리에서 차지할 <b>에너지 몫</b>을 정한다 [고침 2026-10-14].

    피크를 맞추는 <see cref="norm"/>과 다르다. 피크는 <b>제일 큰 한 점</b>만 보므로
    길이가 다른 겹끼리는 비교가 안 된다 — 0.30초 저역과 0.16초 고역은 피크가 같아도
    귀에 닿는 양이 12배 차이 난다. 에너지(제곱합)로 맞추면 적은 숫자가 곧 들리는 비율이다.

    제곱합을 <paramref name="fraction"/>으로 맞추므로 진폭은 그 <b>제곱근</b>배가 된다.
    """
    e = float((x ** 2).sum())
    return x * np.sqrt(fraction / e) if e > 1e-12 else x


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
    """300Hz 아래 · 500Hz 위가 각각 몇 %인가. 폰에서 들리는지는 뒤쪽 숫자가 정한다."""
    spec = np.abs(np.fft.rfft(x * np.hanning(x.size))) ** 2
    f = np.fft.rfftfreq(x.size, 1.0 / SR)
    total = spec.sum() + 1e-20
    return 100.0 * spec[f < 300].sum() / total, 100.0 * spec[f >= 500].sum() / total


def read_back(path):
    """
    <b>방금 쓴 파일을 다시 읽어 잰다</b> [고침 2026-10-14].

    메모리에 있는 배열이 아니라 <b>디스크에 남은 것</b>을 재야 한다. 2026-10-14에
    "저역 78~82%"라고 보고했는데 실제로 쓰인 파일은 93.5%였던 일이 있다 —
    섞는 중간 단계를 재고 결과를 안 쟀기 때문이다. 게임이 읽는 것은 파일이다.
    """
    w = wave.open(path, 'rb')
    raw = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16)
    w.close()
    return raw.astype(np.float64) / 32768.0


def pitch(x, rate):
    """재생 속도를 바꿔 음을 옮긴다. 한 음원을 세 변형에 쓸 때 같은 소리로 안 들리게 한다."""
    if abs(rate - 1.0) < 0.001 or x.size < 4:
        return x
    m = max(4, int(round(x.size / rate)))
    return np.interp(np.linspace(0, x.size - 1, m), np.arange(x.size), x)


def build(crack_raw, boom_raw, thud_raw, v):
    """
    한 발 = 세 겹. <b>음원 하나가 한 겹</b>이라, 몫이 곧 "그 음원이 몇 %인가"다.

    ① 본체(firecracker) : <b>전 대역.</b> 깎지 않는다 — 때리는 소리 자체다
    ② 무게(cannon)      : 서브베이스. 이 음원에는 그것밖에 없다
    ③ 받침(fighting)    : 중역만. 폰 스피커가 낼 수 있는 대역을 채운다

    전에는 ①을 앞 0.16초만 쓰고 ③을 본체+중역 둘로 늘려 <b>fighting 한 장이 66%</b>
    였다. 기획자가 준 음원 여섯이 34%였고, 그나마 cannon은 150Hz 아래로 깎여
    실제로는 안 들렸다. <b>재료를 고르는 일과 대역을 맞추는 일을 섞어 버린 것</b>이
    원인이다 — 지금은 재료를 먼저 정하고, 대역은 각 음원 안에서만 손본다.
    """
    n = int(SR * LENGTH)
    out = np.zeros(n)
    for lay in layers(crack_raw, boom_raw, thud_raw, v).values():
        out += lay

    out *= decay(n, LENGTH * 0.3, 0.012, 1.8)
    return norm(out)


def layers(crack_raw, boom_raw, thud_raw, v):
    """
    음원 하나당 겹 하나. 전부 길이 <see cref="LENGTH"/>인 배열로 돌려준다.

    <b>섞기와 재기가 같은 것을 보게 하려고 따로 뺐다</b> [고침 2026-10-14].
    전에는 기여도를 재려고 "그 음원만 빼고 다시 지어서 줄어든 양"을 봤는데,
    <see cref="build"/>가 끝에서 정규화하므로 <b>빠진 만큼 다시 키워져 차이가 지워졌다</b> —
    firecracker를 빼도 0%로 나오는 식이었다. 결과물을 다시 만들어 재지 않고,
    <b>만들 때 쓴 겹을 그대로 내준다.</b>
    """
    n = int(SR * LENGTH)
    out = {}

    # ① 본체 — firecracker. <b>기획자가 지정한 음원이고, 전 대역을 가진 유일한 음원이다.</b>
    # 필터를 걸지 않는다. 걸면 이 소리가 가진 것을 버리게 된다.
    c = crack_raw[onset(crack_raw):][:n]
    c = pitch(c, 1.0 + 0.05 * (v - 0.5) * 2.0)
    c = share(c * decay(c.size, 0.015, 0.05, 1.9), SHARE_CRACK)
    lay = np.zeros(n)
    lay[:min(c.size, n)] += c[:min(c.size, n)]
    out['firecracker'] = lay

    # ② 무게 — cannon. 실측 150Hz 아래가 74~90%라 이 음원은 무게 말고 낼 것이 없다.
    # 자르는 자리를 150에서 260으로 올렸다 — 150이면 폰 스피커가 통째로 못 낸다.
    b = boom_raw[onset(boom_raw):][:int(SR * (LENGTH - 0.01))]
    b = lowpass(b, BOOM_CUT, 2)
    b = share(b * decay(b.size, 0.012, 0.05, 1.6), SHARE_BOOM)
    at = int(SR * (0.008 + 0.003 * v))
    lay = np.zeros(n)
    room = min(b.size, n - at)
    if room > 0:
        lay[at:at + room] += b[:room]
    out['cannon'] = lay

    # ③ 받침 — fighting에서 중역만. <b>폰 스피커를 위한 겹</b> [실측 2026-10-14].
    # 저역만 쌓으면 헤드폰에서는 묵직한데 폰에서는 '툭' 소리만 남는다.
    m = thud_raw[onset(thud_raw):][:int(SR * 0.16)]
    m = highpass(lowpass(m, THUD_HI, 2), THUD_LO, 3)
    m = share(m * decay(m.size, 0.004, 0.04, 2.2), SHARE_THUD)
    lay = np.zeros(n)
    lay[:min(m.size, n)] += m[:min(m.size, n)]
    out['fighting'] = lay

    return out


def source_mix(crack_raw, boom_raw, thud_raw, v):
    """<b>음원별로 실제 몇 %인가.</b> 적은 몫이 아니라 섞을 때 쓴 겹을 재서 낸다."""
    es = {k: float((lay ** 2).sum())
          for k, lay in layers(crack_raw, boom_raw, thud_raw, v).items()}
    total = sum(es.values()) or 1.0
    return {k: 100.0 * e / total for k, e in es.items()}


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    write = '--write' in sys.argv
    src = DEFAULT_SRC
    if '--src' in sys.argv:
        src = sys.argv[sys.argv.index('--src') + 1]

    os.makedirs(os.path.normpath(OUT), exist_ok=True)

    tp = os.path.join(src, 'fighting_sound (1).wav')
    if not os.path.exists(tp):
        print('본체 음원을 못 찾았다:', tp)
        return 1
    thud = load(tp)
    print('  받침  fighting_sound (1)  시작 %.0fms — 중역 %d~%dHz만 쓴다'
          % (onset(thud) * 1000.0 / SR, THUD_LO, THUD_HI))

    made = []
    for i in (1, 2, 3):
        cp = os.path.join(src, 'firecracker_bang (%d).wav' % i)
        bp = os.path.join(src, 'cannon_fire (%d).wav' % i)
        if not (os.path.exists(cp) and os.path.exists(bp)):
            print('원본을 못 찾았다:', cp, '/', bp)
            return 1

        crack, boom = load(cp), load(bp)
        v = (i - 1) / 2.0
        x = build(crack, boom, thud, v)
        name = 'SFX_Strike_%02d' % i

        if write:
            path = save(name, x)
            lo, hi = bands(read_back(path))      # 디스크에 남은 것을 잰다
        else:
            lo, hi = bands(x)

        mix = source_mix(crack, boom, thud, v)
        print('  %s  %s  저역 %.1f%%  고역 %.1f%%   |  firecracker %.0f%% · cannon %.0f%% '
              '· fighting %.0f%%'
              % (name, '썼다 ' if write else '(안 씀)', lo, hi,
                 mix.get('firecracker', 0), mix.get('cannon', 0), mix.get('fighting', 0)))
        made.append(name)

    print()
    print('오른쪽은 음원별 실제 기여도다 — 적은 몫이 아니라 섞을 때 쓴 겹을 잰 값이다.')
    print('기획자가 준 여섯 장(firecracker + cannon)이 합쳐서 몇 %인지 여기서 바로 보인다.')
    print()
    if not write:
        print('재기만 했다. 실제로 쓰려면 --write 를 붙일 것.')
    else:
        print('%d개를 덮어썼다. 유니티가 다시 불러온다.' % len(made))
        print('길이 %.2f초 — 초당 다섯 번 쳐도 겹치지 않는다.' % LENGTH)
        print('고역이 10%보다 낮으면 폰 스피커에서 사라진다 — SHARE_THUD를 올릴 것.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
