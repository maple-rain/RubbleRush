# -*- coding: utf-8 -*-
"""붕괴 소리가 늘어지는 것을 자른다 [지시 2026-10-14].

**문제.** 실측하면 `SFX_Broken_01`이 **7.24초**이고 꼬리만 6.58초다. 건물은 자주
부서지는데 한 번 부서질 때마다 7초가 깔리므로, 다음 타격음·다음 붕괴음이 전부 그 위에
얹힌다. `SFX_FloorCollapse`도 4.0초에 꼬리 3.87초다.

**왜 런타임 페이드가 아니라 음원을 자르는가.** `SfxPool`은 음성을 내주고 잊는다.
자리가 모자라면 울리던 것을 뺏어 다른 소리에 넘기므로, 페이드를 붙이면 음성마다
페이드 상태를 들고 돌려야 하고 **뺏긴 음성의 페이드를 취소**해야 한다 — 안 하면 새
타격음이 이전 소리의 페이드를 물려받아 작아진다. 음원을 자르면 런타임 비용이 0이다.

**변형끼리 음량도 맞춘다.** `FloorCollapse_01`은 피크 -7.8dBFS인데 `_02`는 -2.0이다.
둘을 무작위로 고르니 같은 사건이 **5.8dB 들쭉날쭉**하게 들렸다. 발소리와 같은 사고다.

**원본은 git에 있다.** 제자리에서 덮어쓰므로 다시 돌리려면 먼저 되돌려야 한다:
    git checkout -- Assets/_Project/Audio/SFX/SFX_Broken_01.wav

쓰는 법:
    python Tools/trim_collapse.py            # 재기만 한다
    python Tools/trim_collapse.py --write
"""
import os
import sys
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
SFX = os.path.normpath(os.path.join(HERE, '..', 'Assets', '_Project', 'Audio', 'SFX'))

# <b>피크가 아니라 RMS로 맞춘다</b> [고침 2026-10-14].
#
# 피크를 맞췄더니 FloorCollapse 두 변형이 피크는 똑같이 -3.0인데 RMS는 -15.3과 -22.4로
# <b>7dB 벌어졌다.</b> 피크는 제일 큰 한 점만 보므로, 성긴 소리와 꽉 찬 소리를 같게
# 만들지 못한다. 귀가 듣는 것은 평균 에너지 쪽이다.
#
# -16dBFS인 이유: 타격음이 RMS -23.1이고 사운드뱅크에서 붕괴 음량을 0.45(-6.9dB)로
# 두므로, -16 - 6.9 = -22.9 로 타격음과 거의 같은 자리에 선다. 그래도 붕괴가 더 크게
# 들리는 것은 <b>길이가 4~6배</b>이기 때문이고, 그게 사건의 크기에 맞다.
TARGET_RMS = 10 ** (-16.0 / 20.0)
CEILING = 10 ** (-1.5 / 20.0)   # 여기 닿으면 RMS를 포기한다 — 찌그러뜨리지 않는다

# 사건의 크기가 길이를 정한다. 층이 무너지는 것보다 건물이 통째로 부서지는 쪽이 크다.
#
# <b>변형은 묶어서 같은 배율을 건다.</b> 한 장씩 따로 맞추면 안 된다 — 원본 두 장은
# 이미 RMS가 -24.5로 같았는데, 피크로 한 장씩 맞췄더니 <b>7dB 벌어졌다.</b>
# 같이 만들어진 변형들의 관계는 원본이 옳다. 건드릴 것은 무리 전체의 크기뿐이다.
GROUPS = {
    'FloorCollapse': (['SFX_FloorCollapse_01', 'SFX_FloorCollapse_02'], 1.2),
    'Broken': (['SFX_Broken_01', 'SFX_Broken_02'], 1.8),
}

FADE_SHARE = 0.45               # 뒤쪽 몇 할을 페이드에 쓰는가


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
    if ch > 1:                                  # 원래 스테레오였으면 그대로 돌려준다
        data = np.repeat(data[:, None], ch, axis=1).reshape(-1)
    with wave.open(path, 'wb') as w:
        w.setnchannels(ch)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes((data * 32767.0).astype('<i2').tobytes())


def onset(x, sr, back=0.050, rise=0.10):
    """제일 큰 봉우리에서 거꾸로 올라가 소리가 솟는 자리를 찾는다.

    `make_strike.py`와 같은 방식이다. "문턱을 처음 넘는 지점"으로 잡으면 진짜 사건
    앞의 약한 소리에서 잘린다 — 거기서 한 번 당했다.
    """
    a = np.abs(x)
    top = int(np.argmax(a))
    peak = float(a[top])
    if peak < 1e-6:
        return 0
    lo = max(0, top - int(sr * back))
    above = a[lo:top + 1] > peak * rise
    return lo + int(np.argmax(above)) if above.any() else top


def rms_db(x):
    """귀가 듣는 크기. 피크는 제일 큰 한 점만 보므로 변형끼리 비교가 안 된다."""
    return 20 * np.log10(max(float(np.sqrt((x ** 2).mean())), 1e-9))


def tail_of(x, sr):
    """피크 뒤로 소리가 남아 있는 길이(초). '늘어진다'를 재는 값이다."""
    a = np.abs(x)
    top = int(np.argmax(a))
    if a[top] < 1e-9:
        return 0.0
    win = max(1, int(sr * 0.02))
    env = np.convolve(a, np.ones(win) / win, 'same')
    idx = np.where(env > a[top] * 0.01)[0]
    return (idx[-1] - top) / float(sr) if idx.size else 0.0


def shorten(x, sr, seconds):
    """앞을 맞추고, 뒤쪽을 서서히 꺼서 끝낸다. <b>음량은 안 건드린다</b> — 무리로 정한다."""
    x = x[onset(x, sr):]
    n = int(sr * seconds)
    if x.size > n:
        x = x[:n]
    else:                                        # 원본이 더 짧으면 그대로 둔다
        n = x.size

    fade = max(1, int(n * FADE_SHARE))
    # 제곱으로 떨어뜨린다. 직선으로 줄이면 중간에 뚝 꺼지는 것처럼 들린다
    x = x.copy()
    x[n - fade:] *= np.linspace(1.0, 0.0, fade) ** 2.0
    return x


def group_gain(waves):
    """
    무리 전체에 걸 배율 하나. <b>변형끼리의 관계는 그대로 둔다.</b>

    평균 RMS를 <see cref="TARGET_RMS"/>에 맞추되, 무리 중 <b>제일 뾰족한 한 장</b>이
    천장에 닿으면 거기서 멈춘다 — 한 장을 위해 전부를 찌그러뜨리지 않는다.
    """
    mean_rms = float(np.sqrt(np.mean([(w ** 2).mean() for w in waves])))
    if mean_rms < 1e-9:
        return 1.0
    gain = TARGET_RMS / mean_rms
    loudest = max(float(np.max(np.abs(w))) for w in waves)
    return min(gain, CEILING / loudest) if loudest > 1e-9 else gain


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    write = '--write' in sys.argv

    print('%-24s %20s   %20s' % ('', '전', '후'))
    for group, (names, seconds) in GROUPS.items():
        loaded, skipped = [], False
        for name in names:
            path = os.path.join(SFX, name + '.wav')
            if not os.path.exists(path):
                print('%-24s 파일이 없다' % name)
                skipped = True
                continue
            x, sr, ch = load(path)
            if x.size <= int(sr * seconds) * 1.02:
                print('%-24s %.2f초 — 이미 짧다, 건너뜀' % (name, x.size / float(sr)))
                skipped = True
                continue
            loaded.append((name, path, x, sr, ch))

        if skipped or not loaded:
            continue

        cut = [(nm, p, shorten(x, sr, seconds), x, sr, ch) for nm, p, x, sr, ch in loaded]
        gain = group_gain([y for _, _, y, _, _, _ in cut])

        for name, path, y, x, sr, ch in cut:
            y = y * gain
            print('%-24s %6.2f초 꼬리%5.2f RMS%6.1f   %6.2f초 꼬리%5.2f RMS%6.1f' % (
                name, x.size / float(sr), tail_of(x, sr), rms_db(x),
                y.size / float(sr), tail_of(y, sr), rms_db(y)))
            if write:
                save(path, y, sr, ch)

    print()
    if not write:
        print('재기만 했다. 실제로 쓰려면 --write 를 붙일 것.')
    else:
        print('덮어썼다. 원본이 필요하면 git checkout 으로 되돌린 뒤 다시 돌릴 것.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
