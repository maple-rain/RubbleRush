#!/usr/bin/env python3
"""타격음을 25% 올린다 [지시 2026-10-16].

    python Tools/boost_strike.py --dry     # 재기만 한다 (기본)
    python Tools/boost_strike.py --apply   # 실제로 덮어쓴다

왜 파일을 건드리는가
--------------------
믹서 쪽에는 여유가 없다. SoundBank의 Strike 항목이 이미 volume 1.0이고,
SfxPool.cs:114 가 `Mathf.Clamp01(volume)` 으로 자른다. 1.0이 천장이다.
그래서 올리려면 음원 자체를 올려야 한다.

클리핑을 어떻게 피하는가
------------------------
16개 중 6개는 1.25배에서 1.0을 넘는다 (가장 큰 피크 0.949). 넘는 꼭대기만
tanh로 둥글린다 — 임팩트음의 피크는 1~2ms짜리 트랜지언트라 귀에는 안 들리고,
크레스트를 조금 내주는 대신 RMS가 올라서 체감은 오히려 단단해진다.

두 번 적용되지 않게
-------------------
tone.py에서 한 번 당한 적이 있다 — git checkout 으로 이미 처리된 커밋이
돌아와서 같은 처리가 두 번 먹었다. 그래서 여기서는 처리 전 피크를 재서
**이미 올라가 있으면 거부한다**. BEFORE_PEAK 가 그 기록이다.
"""

import array
import math
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SFX = ROOT / "Assets" / "_Project" / "Audio" / "SFX"
PATTERN = "SFX_Strike_*.wav"

GAIN = 1.25          # 지시: "25퍼센트 정도"
CEILING = 0.99       # 이 위를 tanh로 둥글린다. 1.0에 딱 붙이면 리샘플링에서 넘친다

# 처리 전 피크 [실측 2026-10-16, 커밋 5ff9d15 시점].
# 지금 파일이 이것보다 뚜렷이 크면 이미 한 번 올린 것이다 — 그때는 아무것도 안 한다.
BEFORE_PEAK = {
    "SFX_Strike_01": 0.763, "SFX_Strike_02": 0.903, "SFX_Strike_03": 0.841,
    "SFX_Strike_04": 0.698, "SFX_Strike_05": 0.606, "SFX_Strike_06": 0.597,
    "SFX_Strike_07": 0.738, "SFX_Strike_08": 0.861, "SFX_Strike_09": 0.759,
    "SFX_Strike_10": 0.857, "SFX_Strike_11": 0.668, "SFX_Strike_12": 0.662,
    "SFX_Strike_13": 0.949, "SFX_Strike_14": 0.738, "SFX_Strike_15": 0.711,
    "SFX_Strike_16": 0.901,
}


def read(path):
    w = wave.open(str(path))
    meta = (w.getnchannels(), w.getsampwidth(), w.getframerate())
    raw = w.readframes(w.getnframes())
    w.close()
    if meta[1] != 2:
        raise RuntimeError(f"{path.name}: 16비트가 아니다 ({meta[1]*8}비트)")
    a = array.array("h")
    a.frombytes(raw)
    return [s / 32768.0 for s in a], meta


def write(path, samples, meta):
    ch, sw, sr = meta
    out = array.array("h", [int(max(-1.0, min(1.0, s)) * 32767) for s in samples])
    w = wave.open(str(path), "wb")
    w.setnchannels(ch)
    w.setsampwidth(sw)
    w.setframerate(sr)
    w.writeframes(out.tobytes())
    w.close()


def soft_limit(x):
    """CEILING 아래는 그대로 두고, 위만 부드럽게 눕힌다.

    문턱 위의 초과분만 tanh에 넣으므로 신호 대부분은 한 치도 안 변한다.
    경계에서 기울기가 이어져 '꺾인' 소리가 안 난다.
    """
    a = abs(x)
    if a <= CEILING:
        return x
    over = a - CEILING
    room = 1.0 - CEILING
    shaped = CEILING + room * math.tanh(over / room)
    return math.copysign(shaped, x)


def rms(xs):
    return math.sqrt(sum(v * v for v in xs) / len(xs)) if xs else 0.0


def main():
    apply = "--apply" in sys.argv
    files = sorted(SFX.glob(PATTERN))
    if not files:
        print(f"  음원을 못 찾았다: {SFX / PATTERN}")
        sys.exit(1)

    # ── 두 번 적용 막기 ───────────────────────────────────────────────────
    suspicious = []
    for f in files:
        known = BEFORE_PEAK.get(f.stem)
        if known is None:
            continue
        xs, _ = read(f)
        peak = max(abs(v) for v in xs)
        if peak > known * 1.08:          # 8%는 인코딩 오차 여유
            suspicious.append((f.stem, known, peak))

    if suspicious:
        print("  이미 올라가 있다 — 아무것도 하지 않는다.")
        print("  (두 번 올리면 되돌릴 수 없다. 되돌리려면 git 으로 복원할 것.)")
        for n, was, now in suspicious:
            print(f"    {n}  기록 {was:.3f}  ->  지금 {now:.3f}")
        sys.exit(2)

    print(f"  {'적용' if apply else '시험 (덮어쓰지 않음)'} — 게인 {GAIN}배 · 천장 {CEILING}\n")
    print(f"  {'파일':<22}{'피크 전':>9}{'피크 후':>9}{'RMS 변화':>11}{'눌린 샘플':>11}")

    tot_s, tot_n = 0, 0
    for f in files:
        xs, meta = read(f)
        before_peak, before_rms = max(abs(v) for v in xs), rms(xs)

        boosted = [v * GAIN for v in xs]
        touched = sum(1 for v in boosted if abs(v) > CEILING)
        out = [soft_limit(v) for v in boosted]

        after_peak, after_rms = max(abs(v) for v in out), rms(out)
        db = 20 * math.log10((after_rms + 1e-12) / (before_rms + 1e-12))
        pct = touched * 100.0 / len(out)
        tot_s += touched
        tot_n += len(out)

        print(f"  {f.name:<22}{before_peak:>9.3f}{after_peak:>9.3f}"
              f"{db:>+10.2f}dB{pct:>10.3f}%")

        if apply:
            write(f, out, meta)

    print(f"\n  눌린 샘플 전체 {tot_s}개 / {tot_n}개 ({tot_s*100.0/tot_n:.3f}%)")
    print(f"  목표 RMS 변화는 +{20*math.log10(GAIN):.2f}dB 다. 그보다 작은 파일은"
          " 그만큼 리미터가 먹은 것이다.")

    if not apply:
        print("\n  실제로 덮어쓰려면 --apply 를 붙일 것.")


if __name__ == "__main__":
    main()
