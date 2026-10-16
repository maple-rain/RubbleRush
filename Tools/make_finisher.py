#!/usr/bin/env python3
"""마지막 한 방 — 건물이 무너지는 펀치의 소리를 만든다 [지시 2026-10-16].

    python Tools/make_finisher.py

기획자: "마지막에 건물이 무너지는 펀치를 날릴 때 사운드를 이거로 할까,
         무너지는 느낌을 주니까."

어느 타를 고를 것인가
---------------------
원본 `punch_impact (1).wav` 는 7.12초에 타가 넷 들어 있다. '무너지는 느낌'은
크레스트가 아니라 **저역과 감쇠 길이**가 정한다. 넷을 다 재서 골랐다:

    번호  길이     저역<200Hz  고역>4kHz  감쇠(-20dB)
    1    0.880s   32.2%       32.8%      680ms
    3    0.630s   38.2%       24.0%      480ms
    6    0.900s   36.5%       22.1%      820ms   <- 이것
    7    0.940s   10.8%       47.5%      100ms

6번이 저역이 많고 고역이 적고 가장 길게 끌린다. RMS도 가장 높고(두껍고)
크레스트가 가장 낮다(지속적이다). 7번은 찰싹 소리라 정반대다.

무엇을 하지 않는가
------------------
**피치를 안 건드린다** [지시 2026-10-15, 여러 번]. 길이와 음량만 손본다.
섞지도 않는다 — 겹치면 크레스트가 죽어 '먹먹'해진다.

음량은 지금의 Strike 묶음에 맞춘다
----------------------------------
2026-10-16 에 Strike 를 25% 올렸으므로 그 뒤 값에 맞춘다. 안 맞추면 마지막
한 방만 작게 들린다. 더 크게 가고 싶으면 SoundBank 의 Finisher volume 을
올리는 것이 맞다 — 파일을 더 올리면 클리핑으로 간다.
"""

import array
import math
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = Path.home() / "Downloads" / "punch_impact (1).wav"
OUT = ROOT / "Assets" / "_Project" / "Audio" / "SFX" / "SFX_Finisher_01.wav"
MATCH_GLOB = "SFX_Strike_*.wav"

PICK = (3.550, 4.470)     # 6번 타. 뒤를 20ms 더 둬서 꼬리를 안 자른다
LEAD = 0.006              # 타격 앞에 남길 여유(초). 0이면 어택이 잘려 '툭' 한다
TAIL_FADE = 0.012         # 끝을 눕히는 시간(초). 안 하면 뚝 끊겨 클릭이 난다
CEILING = 0.99


def read(path):
    w = wave.open(str(path))
    ch, sw, sr, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
    raw = w.readframes(n)
    w.close()
    a = array.array("h")
    a.frombytes(raw)
    xs = [v / 32768.0 for v in a]
    if ch == 2:
        xs = [(xs[i] + xs[i + 1]) / 2 for i in range(0, len(xs) - 1, 2)]
    return xs, sr


def write(path, xs, sr):
    out = array.array("h", [int(max(-1.0, min(1.0, v)) * 32767) for v in xs])
    w = wave.open(str(path), "wb")
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(sr)
    w.writeframes(out.tobytes())
    w.close()


def heard(xs, sr):
    """귀가 듣는 대역(300~6000Hz)의 세기. 생 RMS는 저역을 과대평가한다."""
    def lp(x, fc, order=2):
        y = list(x)
        for _ in range(order):
            al = 1.0 / (1.0 + sr / (2 * math.pi * fc))
            prev, out = 0.0, []
            for v in y:
                prev += al * (v - prev)
                out.append(prev)
            y = out
        return y

    low = lp(xs, 300)
    band = [a - b for a, b in zip(xs, low)]      # 300Hz 위
    band = lp(band, 6000)                        # 6kHz 아래
    return math.sqrt(sum(v * v for v in band) / max(1, len(band)))


def main():
    if not SRC.exists():
        print(f"  원본이 없다: {SRC}")
        print("  Downloads 의 음원은 저장소에 안 올린다(라이선스 불명) — 다시 받아 둘 것.")
        return 1

    xs, sr = read(SRC)
    s = max(0, int((PICK[0] - LEAD) * sr))
    e = min(len(xs), int(PICK[1] * sr))
    seg = xs[s:e]

    # 끝을 눕힌다
    fade = int(TAIL_FADE * sr)
    for i in range(min(fade, len(seg))):
        seg[len(seg) - 1 - i] *= i / fade

    # ── 기존 Strike 묶음의 평균 체감 음량에 맞춘다 ──────────────────────
    sfx = OUT.parent
    refs = sorted(sfx.glob(MATCH_GLOB))
    if not refs:
        print(f"  맞출 기준이 없다: {sfx / MATCH_GLOB}")
        return 1

    levels = []
    for f in refs:
        rx, rsr = read(f)
        levels.append(heard(rx, rsr))
    target = sum(levels) / len(levels)

    mine = heard(seg, sr)
    gain = target / (mine + 1e-12)
    peak_after = max(abs(v) for v in seg) * gain

    # 올렸을 때 천장을 넘으면 넘지 않을 만큼만 올린다
    if peak_after > CEILING:
        gain *= CEILING / peak_after
        capped = True
    else:
        capped = False

    seg = [v * gain for v in seg]

    print(f"  원본  {SRC.name}  ({len(xs)/sr:.2f}초)")
    print(f"  구간  {PICK[0]:.3f} ~ {PICK[1]:.3f}초  ->  {len(seg)/sr:.3f}초")
    print(f"  기준  Strike {len(refs)}개 평균 체감 {target:.5f}")
    print(f"  게인  x{gain:.3f}{'  (천장에 걸려 줄임)' if capped else ''}")
    print(f"  결과  피크 {max(abs(v) for v in seg):.3f} · 체감 {heard(seg, sr):.5f}")

    write(OUT, seg, sr)
    print(f"\n  냈다: {OUT}")
    print("  유니티에서 'Demo/16' 을 눌러 SoundBank 에 연결할 것.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
