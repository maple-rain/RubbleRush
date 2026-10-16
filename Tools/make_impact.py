#!/usr/bin/env python3
"""타격음을 네 겹으로 짓는다 — 스냅 + 날 + 몸통 + 서브 [지시 2026-10-16].

    python Tools/make_impact.py          # 재기만 한다
    python Tools/make_impact.py --apply  # SFX_Strike_*.wav 를 갈아 끼운다

기획자: "압도적인 사운드로 타격감을 주는거였어 (…) 여러 사운드를 섞어서
         해달라고 한거지 (…) 저 큰 타격음이 랜덤으로 나오는게 아니라
         조율이 되어서 나왔으면 해."

받아 온 분석이 가리킨 것
------------------------
* '퍽'이 아니라 채찍·유리 같은 고음 스냅이 70%를 차지해야 경쾌해진다
* 쇠붙이 금속성 배음이 단단함을 만든다
* 여운을 0.05초로 칼같이 잘라야 연타가 진흙이 안 된다
* 막타에서만 저음을 터뜨려 대비를 준다

실측으로 확인한 것 (분석이 맞았다)
----------------------------------
새로 받은 세 음원은 <b>전부 저역</b>이라 그것만으로는 고음 스냅이 안 나온다.
피치를 2.5배로 올려도 800Hz 위가 2.7%, 포화를 50배로 먹여도 14%였고
그때 크레스트가 0.7dB(사각파)로 무너졌다. <b>없는 고역은 못 만든다.</b>

그런데 폴더에 연결 안 된 SFX_Boom 8장이 있었다 — 전에 주신 firecracker ·
cannon · fighting 에서 나온 것들이다. 재 보니:

    SFX_Boom_03   <120Hz 4.2%   800-3k 22.5%   >3kHz 42.4%   감쇠 90ms
    SFX_Boom_02   <120Hz 5.3%   800-3k 18.5%   >3kHz 32.8%   감쇠 65ms

<b>이것이 '챡'이다.</b> 고역이 51~65%고 감쇠가 65~90ms다.

그래서 넷을 겹친다
------------------
    snap  SFX_Boom_03       고역 65%          채찍·유리 스냅
    edge  SFX_Boom_02       고역 51%, 감쇠 65ms  금속성 날
    body  canon-fire        120-800Hz 57%     빽빽한 몸통
    sub   hardcore-boom     <80Hz 98%         무게

<b>서브만 짧게 끊는다.</b> 무게는 어택에만 필요하고, 꼬리가 길면 연타(0.2초
간격)에 네 겹이 쌓여 진흙이 된다. 분석의 "0.05초"가 그 말이다.

피치는 안 건드린다
------------------
"랜덤으로 나오는게 아니라 조율이 되어서" — 겹의 비중만 정해 둔다.
흔들지 않으므로 칠 때마다 같은 소리가 나고, 변주는 네 장을 돌려 쓴다.
"""

import sys
from pathlib import Path

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parent.parent
DL = Path.home() / "Downloads"
OUT = ROOT / "Assets" / "_Project" / "Audio" / "SFX"
SR = 48000

# (역할, 어디서, 비중, 이 겹의 길이(초))  [전부 제안·미승인 — 귀로 고칠 값]
LAYERS = [
    # <b>스냅만 쓴다</b> [고침 2026-10-16]. 역할을 나누라는 지시다 —
    # "펀치 소리, 타격 되는 부분의 소리, 그리고 파괴의 소리를 따로 재생".
    # 타격(Strike)은 초당 다섯 번 나므로 짧고 날카로워야 하고, 무게와 잔향은
    # 파괴(Broken)와 막타(Finisher)가 맡는다. 서브를 여기 넣으면 연타에 쌓인다.
    ("snap", OUT / "SFX_Boom_03.wav",                        1.00, 0.150),
    ("edge", OUT / "SFX_Boom_02.wav",                        0.60, 0.120),
]

LEAD = 0.004        # 정점 앞 여유(초)
FADE_RATIO = 0.45   # 각 겹의 뒤쪽 이만큼을 눕힌다 — 꼬리를 칼같이 자르는 장치
CEILING = 0.97

# 변주 4장. <b>비중만</b> 달리한다. 피치가 아니다.
VARIANTS = [
    (1.00, 1.00),
    (1.00, 0.80),
    (0.90, 1.15),
    (1.05, 0.90),
]


def load(path):
    x, sr = sf.read(str(path), always_2d=True)
    x = x.mean(axis=1)
    if sr != SR:
        n = int(round(len(x) * SR / sr))
        x = np.interp(np.linspace(0, len(x) - 1, n), np.arange(len(x)), x)
    return x.astype(np.float64)


def shape(x, length):
    """정점을 앞에 맞추고 length 로 자른 뒤 뒤쪽을 눕힌다."""
    pi = int(np.argmax(np.abs(x)))
    s = max(0, pi - int(LEAD * SR))
    n = int(length * SR)
    out = np.zeros(n)
    take = min(n, len(x) - s)
    out[:take] = x[s:s + take]

    f = int(n * FADE_RATIO)
    if f > 1:
        out[-f:] *= np.linspace(1.0, 0.0, f) ** 2      # 제곱 — 빨리 떨어진다
    return out


def heard(x, lo=300, hi=6000):
    """한 대역의 세기만 잰다. 기본은 귀가 듣는 300~6000Hz."""
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    X[(f < lo) | (f > hi)] = 0
    y = np.fft.irfft(X, len(x))
    return np.sqrt((y ** 2).mean()) + 1e-12


def crest(x):
    return 20 * np.log10(np.abs(x).max() / (np.sqrt((x ** 2).mean()) + 1e-20))


def report(x, label):
    pi = int(np.argmax(np.abs(x)))
    s = max(0, pi - int(0.005 * SR))
    e = min(len(x), pi + int(0.12 * SR))
    seg = x[s:e] * np.hanning(max(2, e - s))
    X = np.abs(np.fft.rfft(seg)) ** 2
    f = np.fft.rfftfreq(len(seg), 1 / SR)
    t = X.sum() + 1e-20
    b = [X[(f >= a) & (f < c)].sum() / t * 100 for a, c in
         ((0, 120), (120, 800), (800, 3000), (3000, SR / 2))]

    W = max(1, int(SR * 0.005))
    env = [np.abs(x[i:i + W]).max() for i in range(pi, len(x) - W, W)]
    d = 0.0
    if env:
        tgt = env[0] * 0.1
        d = next((j * W / SR * 1000 for j, v in enumerate(env) if v < tgt),
                 len(env) * W / SR * 1000)

    print("  %-14s %6.3fs %7.1f%% %7.1f%% %7.1f%% %7.1f%% %7.1fdB %7.0fms"
          % (label, len(x) / SR, b[0], b[1], b[2], b[3], crest(x), d))
    return b[2] + b[3]


def main():
    apply = "--apply" in sys.argv

    missing = [str(p) for _, p, _, _ in LAYERS if not p.exists()]
    if missing:
        print("  원본이 없다:")
        for m in missing:
            print("   ", m)
        return 1

    print("  %-14s %6s %8s %8s %8s %8s %8s %8s"
          % ("", "길이", "<120Hz", "120-800", "800-3k", ">3kHz", "크레스트", "-20dB"))
    print("  -- 겹 --")
    base = {}
    for role, path, w, length in LAYERS:
        x = shape(load(path), length)
        x = x / (np.abs(x).max() + 1e-12)   # 피크를 1로 맞춘다
        base[role] = (x, w)
        report(x, role + " " + path.stem[:8])

    longest = max(len(v[0]) for v in base.values())
    print("  -- 섞은 것 --")
    made = []
    for i, ws in enumerate(VARIANTS, 1):
        mix = np.zeros(longest)
        for (role, _, _, _), w in zip(LAYERS, ws):
            x, base_w = base[role]
            mix[:len(x)] += x * base_w * w
        pk = np.abs(mix).max()
        if pk > 0:
            mix *= CEILING / pk
        hi = report(mix, "Strike_%02d" % i)
        made.append((OUT / ("SFX_Strike_%02d.wav" % i), mix, hi))

    print("\n  고역(>800Hz) 합계: %.0f%%   <- 분석이 말한 목표는 70%%"
          % made[0][2])
    print("  이전 Strike 는 2.5%% 였다.")

    if not apply:
        print("\n  갈아 끼우려면 --apply")
        return 0

    olds = sorted(OUT.glob("SFX_Strike_*.wav"))
    for path, mix, _ in made:
        sf.write(str(path), mix.astype(np.float32), SR, subtype="PCM_16")
    keep = {p.name for p, _, _ in made}
    for old in olds:
        if old.name not in keep:
            old.unlink()
            m = old.with_suffix(".wav.meta")
            if m.exists():
                m.unlink()
    print(f"\n  냈다 — {len(made)}장. 유니티에서 'Demo/16'.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
