#!/usr/bin/env python3
"""파괴의 소리를 짓는다 — 소닉붐 + 캐논파이어, 잔향을 남기고 [지시 2026-10-16].

    python Tools/make_destruction.py          # 재기만 한다
    python Tools/make_destruction.py --apply  # SFX_Broken_*.wav 를 갈아 끼운다

기획자: "파괴의 소리는 소닉붐과, 캐논 파이어 쪽의 소리를 쓰는거지 (…)
         소닉붐과 캐논 파이어의 경우에 깔끔하게 소리가 끝나지 않고
         소리의 잔성이 남았으면 좋겠어."

역할을 나눈다
-------------
한 소리에 다 욱여넣으려던 것이 잘못이었다. enum 에 자리가 이미 있다:

    Punch          팔을 휘두르는 순간      whoosh — 그대로 둔다
    Strike         벽에 맞는 순간          스냅만. 초당 다섯 번 나므로 짧아야 한다
    Broken         건물이 완전히 부서짐    여기 — 소닉붐 + 캐논파이어
    FloorCollapse  한 층이 쏟아짐          하드코어 붐(순수 서브)이 어울린다

왜 잔향을 '붙이는가'
--------------------
원본은 둘 다 깔끔하게 끝난다. 파괴는 <b>공간이 울려야</b> 크게 들리는데
녹음에는 그 공간이 없다. 그래서 인공 잔향을 더한다.

잔향은 <b>지수적으로 줄어드는 잡음</b>과의 합성곱으로 만든다. 방 하나를
때렸을 때 돌아오는 소리가 그 모양이라, 가장 싼 값에 가장 그럴듯하다.
<b>원음은 그대로 두고 뒤에 꼬리만 더한다</b> — 섞어서 뭉개지 않는다.
"""

import sys
from pathlib import Path

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parent.parent
DL = Path.home() / "Downloads"
OUT = ROOT / "Assets" / "_Project" / "Audio" / "SFX"
SR = 48000

SONIC = DL / "182050__qubodup__sonic-boom.flac"
CANON = DL / "370274__hellboy1305__canon-fire.wav"

# ── 수치 [전부 제안·미승인 2026-10-16] ───────────────────────────────────
DRY = 0.60          # 원음 길이(초). 이 뒤는 잔향만 남는다
TAIL = 1.40         # 잔향이 꼬리를 끄는 시간(초)
WET = 0.42          # 잔향의 세기. 0이면 원본 그대로, 1이면 잔향이 원음만큼 크다
PREDELAY = 0.025    # 원음과 잔향 사이 틈(초). 0이면 뭉쳐서 원음이 흐려진다
DAMP = 3200.0       # 잔향의 고역이 꺼지는 지점(Hz). 큰 공간일수록 낮다
CEILING = 0.97


def load(path):
    x, sr = sf.read(str(path), always_2d=True)
    x = x.mean(axis=1)
    if sr != SR:
        n = int(round(len(x) * SR / sr))
        x = np.interp(np.linspace(0, len(x) - 1, n), np.arange(len(x)), x)
    return x.astype(np.float64)


def head(x, length, fade=0.12):
    """정점을 앞에 맞추고 length 로 자른다."""
    pi = int(np.argmax(np.abs(x)))
    s = max(0, pi - int(0.006 * SR))
    n = int(length * SR)
    out = np.zeros(n)
    take = min(n, len(x) - s)
    out[:take] = x[s:s + take]
    f = int(fade * SR)
    if f > 1:
        out[-f:] *= np.linspace(1.0, 0.0, f) ** 1.5
    return out


def room(seconds, damp):
    """
    방 하나의 임펄스 응답. <b>지수로 줄어드는 잡음</b>이다.

    실제 방을 때렸을 때 돌아오는 소리가 이 모양이다 — 수많은 반사가 겹쳐
    잡음처럼 되고, 에너지가 지수로 빠진다. 고역이 먼저 죽는 것(공기와 벽이
    고역을 더 먹는다)을 저역통과로 흉내 낸다.
    """
    n = int(seconds * SR)
    rng = np.random.default_rng(20261016)      # 고정 씨앗 — 돌릴 때마다 같은 방이다
    ir = rng.normal(0.0, 1.0, n) * np.exp(-np.linspace(0.0, 7.0, n))

    # 고역 감쇠 (1차 저역통과를 두 번)
    a = 1.0 / (1.0 + SR / (2 * np.pi * damp))
    for _ in range(2):
        out = np.empty_like(ir)
        prev = 0.0
        for i, v in enumerate(ir):
            prev += a * (v - prev)
            out[i] = prev
        ir = out

    # 맨 앞 1ms 는 지운다 — 직접음은 원음이 이미 들고 있다
    ir[: int(0.001 * SR)] = 0.0
    return ir / (np.abs(ir).max() + 1e-12)


def reverb(dry, ir, wet, predelay):
    """원음 뒤에 잔향을 <b>더한다</b>. 원음은 건드리지 않는다."""
    wetsig = np.convolve(dry, ir)[: len(dry) + len(ir)]
    wetsig /= (np.abs(wetsig).max() + 1e-12)

    pre = int(predelay * SR)
    out = np.zeros(len(dry) + len(ir) + pre)
    out[: len(dry)] += dry
    out[pre: pre + len(wetsig)] += wetsig * wet
    return out


def tail_ms(x, drop_db=40.0):
    pi = int(np.argmax(np.abs(x)))
    W = max(1, int(SR * 0.01))
    env = [np.abs(x[i:i + W]).max() for i in range(pi, len(x) - W, W)]
    if not env:
        return 0.0
    tgt = env[0] * 10 ** (-drop_db / 20)
    return next((j * W / SR * 1000 for j, v in enumerate(env) if v < tgt),
                len(env) * W / SR * 1000)


def main():
    apply = "--apply" in sys.argv
    for p in (SONIC, CANON):
        if not p.exists():
            print("  원본이 없다:", p)
            return 1

    ir = room(TAIL, DAMP)
    print("  %-22s %7s %10s %10s" % ("", "길이", "-40dB꼬리", "피크"))

    made = []
    for i, (name, src) in enumerate([("sonic", SONIC), ("canon", CANON)], 1):
        dry = head(load(src), DRY)
        print("  %-22s %6.2fs %8.0fms %9.3f"
              % (name + " 원본(자른 것)", len(dry) / SR, tail_ms(dry), np.abs(dry).max()))

        wetted = reverb(dry, ir, WET, PREDELAY)
        wetted *= CEILING / (np.abs(wetted).max() + 1e-12)
        print("  %-22s %6.2fs %8.0fms %9.3f"
              % ("  + 잔향", len(wetted) / SR, tail_ms(wetted), np.abs(wetted).max()))

        made.append((OUT / ("SFX_Broken_%02d.wav" % i), wetted))

    olds = sorted(OUT.glob("SFX_Broken_*.wav"))
    if olds:
        x, sr = sf.read(str(olds[0]), always_2d=True)
        x = x.mean(axis=1)
        print("\n  %-22s %6.2fs %8.0fms" % ("지금 쓰는 " + olds[0].name, len(x) / sr, tail_ms(x)))

    if not apply:
        print("\n  갈아 끼우려면 --apply")
        return 0

    for path, y in made:
        sf.write(str(path), y.astype(np.float32), SR, subtype="PCM_16")
    keep = {p.name for p, _ in made}
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
