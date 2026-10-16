#!/usr/bin/env python3
"""압도적인 타격음을 세 겹으로 짓는다 [지시 2026-10-16].

    python Tools/make_impact.py          # 재기만 한다
    python Tools/make_impact.py --apply  # SFX_Strike_*.wav 를 갈아 끼운다

기획자: "압도적인 사운드로 타격감을 주는거였어. 지금은 너무 깔끔하고 (…)
         너무 슴슴한것 같아. (…) 음을 섞어서 쓰고싶은거지."

왜 이번엔 섞어도 되는가
-----------------------
전에 섞었다가 "이상하게 만져서 먹먹해졌어"를 들었다. 그때는 <b>같은 대역의
타격음끼리</b> 겹쳤기 때문이다 — 두 트랜지언트가 어긋나며 서로를 뭉갰다.

이번 셋은 대역이 갈린다 (실측):

    파일        <80Hz   80-400  400-2k  크레스트   역할
    hardcore    98.1%    1.8%    0.1%   15.4dB   서브 — 압도감
    canon       38.4%   56.9%    2.2%    4.6dB   몸통 — 빽빽한 펀치
    sonic        2.4%   31.5%   55.6%   14.2dB   중역 — 뚫고 나오는 날

겹치는 구간이 거의 없으니 더해도 서로를 지우지 않는다. 임팩트 사운드의
정석(서브 + 몸통 + 날)이고, 지금 쓰는 '깨끗한 타격 녹음'에 없던 저역이 생긴다.

무엇을 지키는가
---------------
* <b>피치를 안 건드린다</b> [지시, 여러 번].
* <b>트랜지언트를 맞춘다</b> — 세 겹의 정점을 같은 샘플에 둔다. 어긋나면
  그때부터가 '먹먹'이다.
* <b>크레스트를 잰다</b> — 섞기 전후를 찍어서 얼마나 내줬는지 보고한다.

왜 짧게 자르는가
----------------
초당 다섯 번 울리는 소리다. hardcore 의 감쇠가 1880ms 라 그대로 쓰면
연타할 때 네 겹이 쌓여 진흙이 된다. 그래서 타격음은 짧게 자르고,
<b>길게 끄는 몫은 마지막 한 방(Finisher)</b>이 맡는다.
"""

import sys
from pathlib import Path

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parent.parent
DL = Path.home() / "Downloads"
OUT = ROOT / "Assets" / "_Project" / "Audio" / "SFX"
SR = 48000                      # 기존 SFX 와 같다

# 겹: (파일, 대역 역할, 기본 비중) [전부 제안·미승인 — 귀로 듣고 고칠 값]
LAYERS = [
    ("sub",  "41166__jobro__hardcore-boom-3.mp3",    1.00),
    ("body", "370274__hellboy1305__canon-fire.wav",  0.85),
    ("edge", "182050__qubodup__sonic-boom.flac",     0.45),
]

LEAD = 0.008        # 정점 앞에 남길 여유(초)
LENGTH = 0.42       # 한 타의 길이(초). 연타 간격 0.2초의 두 배까지만
FADE = 0.10         # 끝을 눕히는 시간(초)
CEILING = 0.97

# 변주 4장. 겹의 비중을 조금씩 달리해 기계음처럼 안 들리게 한다.
# <b>피치가 아니다</b> — 섞는 비율만 바꾼다.
VARIANTS = [
    (1.00, 1.00, 1.00),
    (1.00, 0.88, 1.25),
    (0.90, 1.10, 0.80),
    (1.10, 0.95, 1.10),
]


def load(name):
    x, sr = sf.read(str(DL / name), always_2d=True)
    x = x.mean(axis=1)
    if sr != SR:                                   # 선형 보간 리샘플
        n = int(round(len(x) * SR / sr))
        x = np.interp(np.linspace(0, len(x) - 1, n), np.arange(len(x)), x)
    return x.astype(np.float64)


def crest(x):
    pk = np.abs(x).max()
    rms = np.sqrt((x ** 2).mean()) + 1e-20
    return 20 * np.log10(pk / rms)


def cut(x, lead, length):
    """정점을 기준으로 잘라 길이를 맞춘다. 세 겹의 정점이 여기서 정렬된다."""
    pi = int(np.argmax(np.abs(x)))
    s = max(0, pi - int(lead * SR))
    out = np.zeros(int(length * SR))
    take = min(len(out), len(x) - s)
    out[:take] = x[s:s + take]

    f = int(FADE * SR)
    if f > 1:
        out[-f:] *= np.linspace(1.0, 0.0, f) ** 1.5
    return out


def soft_limit(x, ceiling=CEILING):
    a = np.abs(x)
    over = a > ceiling
    if not over.any():
        return x, 0
    room = 1.0 - ceiling
    shaped = ceiling + room * np.tanh((a[over] - ceiling) / room)
    y = x.copy()
    y[over] = np.sign(x[over]) * shaped
    return y, int(over.sum())


def main():
    apply = "--apply" in sys.argv

    missing = [n for _, n, _ in LAYERS if not (DL / n).exists()]
    if missing:
        print("  원본이 없다:")
        for m in missing:
            print("   ", DL / m)
        return 1

    print("  겹을 읽고 정점을 맞춘다\n")
    print("  %-6s %-40s %9s %9s" % ("역할", "파일", "크레스트", "비중"))
    base = {}
    for role, name, w in LAYERS:
        x = cut(load(name), LEAD, LENGTH)
        base[role] = (x, w)
        print("  %-6s %-40s %7.1fdB %9.2f" % (role, name[:40], crest(x), w))

    print("\n  %-10s %8s %8s %9s %9s" % ("낼 파일", "피크", "RMS", "크레스트", "눌린 샘플"))
    made = []
    for i, (ws, wb, we) in enumerate(VARIANTS, 1):
        mix = (base["sub"][0] * base["sub"][1] * ws
             + base["body"][0] * base["body"][1] * wb
             + base["edge"][0] * base["edge"][1] * we)

        pk = np.abs(mix).max()
        if pk > 0:
            mix *= CEILING / pk                    # 천장에 맞춰 올린다
        mix, hit = soft_limit(mix)

        path = OUT / f"SFX_Strike_{i:02d}.wav"
        print("  %-10s %8.3f %8.4f %7.1fdB %9d"
              % (path.name, np.abs(mix).max(), np.sqrt((mix ** 2).mean()), crest(mix), hit))
        made.append((path, mix))

    # ── 지금 쓰는 것과 견준다 ────────────────────────────────────────────
    olds = sorted(OUT.glob("SFX_Strike_*.wav"))
    if olds:
        xs, _ = sf.read(str(olds[0]), always_2d=True)
        xs = xs.mean(axis=1)
        print("\n  지금 것과 견주기")
        print("  %-14s %7s %9s %9s" % ("", "길이", "크레스트", "<80Hz"))

        def low(x, sr=SR):
            X = np.fft.rfft(x * np.hanning(len(x)))
            f = np.fft.rfftfreq(len(x), 1 / sr)
            p = np.abs(X) ** 2
            return p[f < 80].sum() / (p.sum() + 1e-20) * 100

        print("  %-14s %6.2fs %7.1fdB %8.1f%%" % (olds[0].name, len(xs) / SR, crest(xs), low(xs)))
        m = made[0][1]
        print("  %-14s %6.2fs %7.1fdB %8.1f%%" % ("새 1번", len(m) / SR, crest(m), low(m)))

    if not apply:
        print("\n  실제로 갈아 끼우려면 --apply 를 붙일 것.")
        print("  ** 지금 SFX_Strike_*.wav 16개 중 1~4번을 덮어쓴다. 나머지는 지워야 한다. **")
        return 0

    for path, mix in made:
        sf.write(str(path), mix.astype(np.float32), SR, subtype="PCM_16")
    for old in olds:
        if old.name not in {p.name for p, _ in made}:
            old.unlink()
            meta = old.with_suffix(".wav.meta")
            if meta.exists():
                meta.unlink()
    print(f"\n  냈다 — {len(made)}장. 남은 옛 파일은 지웠다.")
    print("  유니티에서 'Demo/16' 을 눌러 다시 연결할 것.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
