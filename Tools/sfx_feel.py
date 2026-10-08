# -*- coding: utf-8 -*-
"""소리의 '느낌'을 숫자로 잰다 [2026-10-08].

**왜 한 발만 들어서는 안 되는가.** 이 게임은 초당 대여섯 번 두드린다. 그래서 플레이어가
듣는 것은 음원 한 발이 아니라 **겹쳐 쌓인 합**이다. 음원 하나만 재면 멀쩡해 보이는데
합쳐 놓으면 전혀 다른 소리가 된다 — 어택이 뭉개져 먹먹해지고, 같은 간격으로 반복되는
지속음은 드릴이 된다. 그래서 이 도구는 **겹쳐서 잰다**.

재는 것 넷:

- **무게** (200Hz 아래 에너지 비율) — '묵직하다'의 정체. 낮으면 가볍고 깡통 같다
- **밝기** (스펙트럼 무게중심 Hz) — 높으면 쇳소리·드릴, 낮으면 둔탁한 타격
- **때림** (크레스트 = 피크 ÷ RMS, dB) — 높으면 한 방이 또렷하고, 낮으면 지속음이다.
  **겹쳤을 때 이 값이 떨어지는 정도가 곧 '먹먹함'이다**
- **꼬리** (피크 뒤 남는 길이) — 연타 간격보다 길면 다음 발과 겹친다

쓰는 법:
    python Tools/sfx_feel.py                    게임에 들어간 Punch/Strike를 잰다
    python Tools/sfx_feel.py --src              다운로드 폴더의 후보들을 같이 잰다
    python Tools/sfx_feel.py --gap 0.2          연타 간격을 바꿔 본다 (기본 0.2초)
    python Tools/sfx_feel.py --wav <폴더>       겹친 소리를 wav로 써서 직접 들어 본다
"""
import argparse
import glob
import os
import re
import sys
import wave

import numpy as np

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(os.path.abspath(__file__))
SFX = os.path.join(HERE, '..', 'Assets', '_Project', 'Audio', 'SFX')
SRC = r'C:\Users\MBC-501-08\Downloads'

# 연타 간격의 기본값. punch.csv 실측: 한 방 0.458/3.08 = 0.149초이고
# rush.fastGap 0.1 ~ rush.slowGap 0.3 사이에서 논다. 가운데를 잡는다.
GAP = 0.20


def read(path):
    with wave.open(path, 'rb') as w:
        ch, width, rate, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(n)
    if width == 2:
        a = np.frombuffer(raw, dtype='<i2').astype(np.float64) / 32768.0
    elif width == 3:
        b = np.frombuffer(raw, dtype=np.uint8).reshape(-1, 3).astype(np.int32)
        v = b[:, 0] | (b[:, 1] << 8) | (b[:, 2] << 16)
        a = np.where(v & 0x800000, v - 0x1000000, v).astype(np.float64) / 8388608.0
    elif width == 4:
        a = np.frombuffer(raw, dtype='<i4').astype(np.float64) / 2147483648.0
    else:
        a = (np.frombuffer(raw, dtype=np.uint8).astype(np.float64) - 128.0) / 128.0
    if ch > 1:
        a = a.reshape(-1, ch).mean(axis=1)
    return a, rate


def write(path, a, rate):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    pcm = (np.clip(a, -1, 1) * 32767).astype('<i2')
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(pcm.tobytes())


def crest_db(a):
    """피크 ÷ RMS. 한 방짜리 타격은 크고, 지속음은 작다."""
    rms = float(np.sqrt(np.mean(a * a)))
    peak = float(np.max(np.abs(a)))
    if rms <= 1e-9 or peak <= 1e-9:
        return 0.0
    return 20.0 * np.log10(peak / rms)


def spectrum(a, rate):
    """
    무게중심(Hz) · 200Hz 아래 <b>비율</b> · 200Hz 아래 <b>절대 레벨</b>(dBFS).

    <b>비율만 보면 속는다</b> [고침 2026-10-08]. 저역 비율은 전체 대비라서, 꼬리에
    웅웅거리는 저역이 길게 깔린 소리가 유리하게 나온다 — 실제로 Strike를 1.29초에서
    0.34초로 줄였더니 비율은 31%에서 16%로 <b>떨어졌는데</b> 절대 저역은 오히려 올랐다.
    "묵직하냐"는 저역이 <b>얼마나 센가</b>이지 <b>몇 퍼센트인가</b>가 아니다. 둘 다 낸다.
    """
    n = min(a.size, 1 << 16)
    if n < 64:
        return 0.0, 0.0, -99.0
    w = a[:n] * np.hanning(n)
    mag = np.abs(np.fft.rfft(w))
    f = np.fft.rfftfreq(n, 1.0 / rate)
    total = mag.sum()
    if total <= 1e-9:
        return 0.0, 0.0, -99.0

    centroid = float((f * mag).sum() / total)
    low = float(mag[f < 200.0].sum() / total)

    # 절대 레벨: 200Hz 아래만 남기고 되돌려 실효값을 본다.
    band = np.fft.irfft(np.fft.rfft(a) * (np.fft.rfftfreq(a.size, 1.0 / rate) < 200.0), n=a.size)
    rms = float(np.sqrt(np.mean(band * band)))
    return centroid, low, 20.0 * np.log10(max(rms, 1e-9))


def attack_ms(a, rate):
    """소리가 시작해 피크에 닿기까지. 짧을수록 '때린' 느낌이 난다."""
    peak = int(np.argmax(np.abs(a)))
    thr = np.max(np.abs(a)) * 0.1
    start = peak
    while start > 0 and abs(a[start]) > thr:
        start -= 1
    return (peak - start) * 1000.0 / rate


def tail_ms(a, rate, floor_db=-40.0):
    """피크 뒤로 남는 길이. 연타 간격보다 길면 다음 발과 겹친다."""
    peak = int(np.argmax(np.abs(a)))
    thr = np.max(np.abs(a)) * (10 ** (floor_db / 20.0))
    loud = np.flatnonzero(np.abs(a[peak:]) > thr)
    return (loud[-1] if loud.size else 0) * 1000.0 / rate


def residual_db(a, rate, gap):
    """
    다음 타격이 올 때 <b>앞 소리가 얼마나 남아 있는가</b> (피크 대비 dB).

    <b>먹먹함의 진짜 지표다.</b> 처음에는 크레스트(피크/RMS)가 떨어지는 정도로 재려 했는데
    겹친 소리에서 오히려 <b>올라갔다</b> — 피크끼리 보강되면서 피크는 커지고 RMS는 루트로만
    자라기 때문이다. 겹침은 크레스트로 안 보인다.

    대신 이걸 본다: 0 dB에 가까우면 앞 소리가 그대로 살아 있는 채로 다음 발이 와서 하나의
    지속음이 되고(= 먹먹), -20 dB쯤이면 발과 발 사이가 벌어져 또렷하게 들린다.
    """
    peak = int(np.argmax(np.abs(a)))
    top = float(np.max(np.abs(a)))
    at = peak + int(rate * gap)
    if top <= 1e-9 or at >= a.size:
        return -99.0

    # 그 지점 둘레 10ms의 실효값으로 본다. 한 표본만 보면 영교차에 걸려 엉뚱하게 작게 나온다.
    half = max(1, int(rate * 0.005))
    seg = a[max(0, at - half):at + half]
    rms = float(np.sqrt(np.mean(seg * seg))) if seg.size else 0.0
    return 20.0 * np.log10(max(rms, 1e-9) / top)


def feel(a, rate, gap=GAP):
    c, low, lowdb = spectrum(a, rate)
    return {
        'sec': a.size / rate,
        'low': low * 100.0,
        'lowdb': lowdb,
        'bright': c,
        'crest': crest_db(a),
        'attack': attack_ms(a, rate),
        'tail': tail_ms(a, rate),
        'left': residual_db(a, rate, gap),
    }


def stack(clips, rate, gap, hits=10, seed=7):
    """
    연타를 <b>실제로 합성한다</b>. 게임과 같은 짓을 한다 —
    변형을 무작위로 고르고 (<c>PlayAny</c>), 음높이를 흔들고 (<c>pitchJitter 0.08</c>),
    같은 간격으로 쌓는다. 플레이어가 듣는 것이 이것이다.
    """
    rng = np.random.default_rng(seed)
    step = int(rate * gap)
    longest = max(c.size for c in clips)
    out = np.zeros(step * hits + longest)

    for i in range(hits):
        c = clips[rng.integers(0, len(clips))]
        # 음높이 흔들기는 재생 속도를 바꾸는 것과 같다. 길이도 같이 바뀐다.
        k = 1.0 + float(rng.uniform(-0.08, 0.08))
        idx = np.clip((np.arange(int(c.size / k)) * k).astype(int), 0, c.size - 1)
        v = c[idx]
        at = step * i
        out[at:at + v.size] += v
    return out


def row(name, f, mark=''):
    return '  %-20s %5.2f %7.1f%% %8.1f %8.0f %7.1f %7.0f  %s' % (
        name, f['sec'], f['low'], f['lowdb'], f['bright'], f['crest'], f['tail'], mark)


HEAD = '  %-20s %5s %8s %8s %8s %7s %7s' % (
    '이름', '초', '무게%', '저역dB', '밝기Hz', '때림dB', '꼬리ms')


def group(folder, pattern):
    """`SFX_Strike_01.wav` 꼴, 또는 다운로드의 `name (1).wav` 꼴을 묶는다."""
    out = {}
    for p in sorted(glob.glob(os.path.join(folder, pattern))):
        base = os.path.basename(p)[:-4]
        base = re.sub(r'\s*\(\d+\)', '', base).strip()
        base = re.sub(r'^SFX_', '', base)
        base = re.sub(r'_\d+$', '', base)
        out.setdefault(base, []).append(p)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', action='store_true', help='다운로드 폴더의 후보도 같이 잰다')
    ap.add_argument('--gap', type=float, default=GAP, help='연타 간격(초)')
    ap.add_argument('--hits', type=int, default=10, help='몇 번 두드리는가')
    ap.add_argument('--wav', default='', help='겹친 소리를 이 폴더에 쓴다')
    ap.add_argument('--only', default='', help='이 이름이 들어간 것만')
    ap.add_argument('--dir', default='', help='다른 폴더를 잰다 (전/후 비교용)')
    args = ap.parse_args()

    sets = group(os.path.normpath(args.dir or SFX), 'SFX_*.wav')
    if args.src and os.path.isdir(SRC):
        for k, v in group(SRC, '*.wav').items():
            sets['[원본] ' + k] = v

    if args.only:
        sets = {k: v for k, v in sets.items() if args.only.lower() in k.lower()}

    print('소리의 느낌 — 한 발과, %.2f초 간격으로 %d번 두드렸을 때' % (args.gap, args.hits))
    print('무게=200Hz 아래 비율 · 밝기=스펙트럼 무게중심 · 때림=피크/RMS · 꼬리=피크 뒤 -40dB까지')
    print()
    print(HEAD)

    for name in sorted(sets):
        paths = sets[name]
        clips, rate = [], 44100
        for p in paths:
            a, rate = read(p)
            clips.append(a)
        if not clips:
            continue

        one = feel(clips[0], rate, args.gap)
        piled = stack(clips, rate, args.gap, args.hits)

        mark = []
        if one['tail'] > args.gap * 1000.0:
            mark.append('%.1f발 겹침' % (one['tail'] / 1000.0 / args.gap))
        if one['left'] > -12.0:
            mark.append('먹먹 — 다음 발이 올 때 %.0fdB 남아 있다' % one['left'])
        if one['bright'] > 5000.0 and one['low'] < 10.0:
            mark.append('가늘고 날카롭다 — 반복하면 드릴이 된다')

        print(row(name, one, ' · '.join(mark)))

        if args.wav:
            safe = re.sub(r'[^A-Za-z0-9_]+', '_', name)
            m = float(np.max(np.abs(piled)))
            write(os.path.join(args.wav, 'stack_%s.wav' % safe),
                  piled / m * 0.9 if m > 1e-9 else piled, rate)

    print()
    print('안 재는 것: 듣기 좋은가. 수치는 "왜 그렇게 들리는지"만 말한다.')


if __name__ == '__main__':
    main()
