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

# 6번 타. <b>끝을 4.470에서 4.620으로 늘렸다</b> [고침 2026-10-16].
#
# 기획자: "마지막에 펀치 후에 스르륵하고 무너지는 사운드가 약간 들려서 그래,
#          그렇기 때문에 건물이 무너지기 직전의 사운드와 어울린다고 생각했던거고."
#
# 그 '스르륵'이 이 음원을 고른 이유인데 <b>내가 그걸 잘라먹고 있었다.</b> 무음 분할이
# -45dB 문턱으로 끊어서, 그보다 작은 꼬리가 '무음'으로 잘렸다. 다시 재니:
#
#   타격 피크   원본 3.570s (-3.5 dBFS)
#   꼬리 끝     -55dB 기준 4.520s · -60dB 기준 4.570s · -65dB 기준 4.590s
#   내 구간     4.470s 에서 끊음  ->  100ms 넘게 날아갔고, 거기에 페이드까지 걸려
#                                    끝자락이 두 번 깎였다
#
# 4.620까지 둔다 — -65dB 끝(4.590)보다 30ms 더 있어서 페이드가 꼬리 본체를 안 먹는다.
# 다음 타가 5.590s라 다른 소리가 섞일 걱정은 없다.
PICK = (3.550, 4.620)

# <b>타격 부분을 떼어낸다</b> [지시 2026-10-16].
#
# 기획자: "마지막 펀치에 스르륵 사운드 부분만 넣은건가? 타격 지점에서 나는
#          소리로 바꾸면 되지 않을까."
#
# 재 보니 관찰이 맞았다. 이 음원의 타격은 평타보다 <b>6dB 약하고 뾰족하지 않다</b>:
#
#   평타(Strike_13)  -28dB 에서 올라가 0.09s 에 -1.7dB 로 뾰족하게 선다
#   이 음원          처음부터 -7.6dB 로 평평하다가 내려간다
#
# 그래서 '때렸다'가 아니라 '울린다'로 들린다. 둘을 같이 틀면 더 나쁘다 —
# 이 음원의 몸통이 평타의 정점보다 <b>90ms 먼저</b> 도착해서 두 번 친 것처럼 된다.
#
# 그래서 타격 몸통을 버리고 <b>스르륵부터</b> 쓴다. 때리는 소리는 평타가 맡고
# (DestructionFX.OnFinishingStrike 가 둘을 같이 울린다), 이 파일은 그 뒤에
# 무너지는 소리만 보탠다. 섞어서 한 파일로 만들지 않는 이유는 전에 배웠다 —
# 겹치면 크레스트가 죽어 먹먹해진다.
DROP_IMPACT = 0.120       # 구간 시작에서 이만큼을 버린다. -16dB 까지 내려온 자리다

LEAD = 0.006              # 타격 앞에 남길 여유(초). 0이면 어택이 잘려 '툭' 한다

# 페이드를 12 -> 25ms로 늘렸다. 꼬리가 작아서 짧게 끊으면 '툭' 하고 사라지는데,
# 길게 눕히면 스르륵이 자연히 멎는 것처럼 들린다. 꼬리 본체(-65dB 끝)보다
# 뒤에서만 걸리므로 들리는 부분은 안 깎인다.
TAIL_FADE = 0.025
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


def impact_only(xs, sr, pre=0.010, post=0.150):
    """
    <b>타격 부분만</b> 잘라 돌려준다 — 음량을 맞출 때 쓴다 [고침 2026-10-16].

    꼬리까지 넣어 평균을 내면 안 된다. 이 음원은 스르륵 꼬리가 1초 가까이 가는데
    그 구간은 조용하므로 <b>전체 평균을 끌어내린다.</b> 그 값을 평타에 맞추면
    모자란 만큼을 게인으로 메우게 되어 <b>타격만 과하게 커진다.</b>
    꼬리는 원래 작아야 하는 것이지 작아서 손해 볼 것이 아니다.
    """
    pk = max(range(len(xs)), key=lambda i: abs(xs[i]))
    s = max(0, pk - int(pre * sr))
    e = min(len(xs), pk + int(post * sr))
    return xs[s:e]


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
    s = max(0, int((PICK[0] + DROP_IMPACT - LEAD) * sr))
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

    # <b>타격 부분끼리</b> 비교한다. 꼬리 길이가 서로 다르므로 전체를 재면
    # 꼬리가 긴 쪽이 손해를 보고, 그 손해를 게인으로 메우면 타격만 커진다.
    levels = []
    for f in refs:
        rx, rsr = read(f)
        levels.append(heard(impact_only(rx, rsr), rsr))
    target = sum(levels) / len(levels)

    # <b>스르륵은 타격보다 작아야 한다</b> [고침 2026-10-16].
    #
    # 타격을 떼어냈으므로 남은 것을 '타격 음량'에 맞추면 안 된다 — 그러면 꼬리가
    # 평타만큼 커져서 무너지는 소리가 때리는 소리를 덮는다.
    #
    # 원본에서 <b>타격과 스르륵이 어떤 비율이었는지</b>를 그대로 가져온다.
    # 그 비율만 지키면, 평타가 타격 자리를 대신해도 들리는 균형은 원본과 같다.
    whole = xs[int((PICK[0] - LEAD) * sr):int(PICK[1] * sr)]
    src_impact = heard(impact_only(whole, sr), sr)
    src_tail = heard(whole[int(DROP_IMPACT * sr):], sr)
    ratio = src_tail / (src_impact + 1e-12)

    mine = heard(seg, sr)                 # 꼬리 전체로 잰다. 여기엔 타격이 없다
    gain = (target * ratio) / (mine + 1e-12)
    print(f"  원본 비율  스르륵 / 타격 = {20 * math.log10(ratio + 1e-12):+.1f}dB  (이 비율을 지킨다)")
    peak_after = max(abs(v) for v in seg) * gain

    # 올렸을 때 천장을 넘으면 넘지 않을 만큼만 올린다
    if peak_after > CEILING:
        gain *= CEILING / peak_after
        capped = True
    else:
        capped = False

    seg = [v * gain for v in seg]

    print(f"  원본  {SRC.name}  ({len(xs)/sr:.2f}초)")
    print(f"  구간  {PICK[0]+DROP_IMPACT:.3f} ~ {PICK[1]:.3f}초  ->  {len(seg)/sr:.3f}초  (타격 {DROP_IMPACT*1000:.0f}ms 떼어냄)")
    print(f"  기준  Strike {len(refs)}개 평균 체감 {target:.5f}")
    print(f"  게인  x{gain:.3f}{'  (천장에 걸려 줄임)' if capped else ''}")
    print(f"  결과  피크 {max(abs(v) for v in seg):.3f} · 타격 체감 {heard(impact_only(seg, sr), sr):.5f}")
    tail = seg[int(0.20 * sr):]
    import math as _m
    tr = _m.sqrt(sum(v * v for v in tail) / max(1, len(tail)))
    pk = max(abs(v) for v in seg)
    print(f"  꼬리  {len(tail)/sr:.3f}초 · 피크 대비 {20*_m.log10(tr/(pk+1e-12)):.1f}dB  (스르륵이 여기 있다)")

    write(OUT, seg, sr)
    print(f"\n  냈다: {OUT}")
    print("  유니티에서 'Demo/16' 을 눌러 SoundBank 에 연결할 것.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
