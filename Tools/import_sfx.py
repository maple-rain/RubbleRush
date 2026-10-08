# -*- coding: utf-8 -*-
"""받은 음원을 게임에 맞게 다듬어 들여온다 [2026-10-08].

**왜 그대로 못 쓰는가.** VARCO가 내주는 길이는 게임의 박자와 다르다 — 받은 `robot_punch`가
4.2~8.4초인데 타격음은 **초당 다섯 번** 울린다. 그대로 꽂으면 소리가 스무 겹으로 쌓여
진흙이 된다. 길이는 취향이 아니라 **게임의 박자가 정하는 값**이다.

**하는 일 넷.**
1. 앞 침묵을 자른다 — 붙어 있으면 반응이 늦은 느낌이 된다
2. 목표 길이로 자르고 끝에 짧은 페이드를 건다 — 안 하면 자른 자리에서 '툭' 한다
3. 피크를 −2.0 dBFS로 맞춘다 — 기존 음원이 전부 그 값이다
4. `SFX_<이름>_NN.wav`로 저장한다 — `Demo/16`이 그 이름으로 꽂는다

**원본은 안 건드린다.** 다운로드 폴더의 파일은 그대로 두고 복사본만 다듬는다 —
매핑이 틀렸을 때 다시 돌릴 수 있어야 한다.

**매핑은 내 판단이다 [제안·미승인].** 아래 표의 왼쪽을 고치면 그대로 바뀐다.
파일 이름이 내용으로 되어 있어서(`net_snap`, `pit_rumble`) 게임의 사건과 짝지으려면
누군가 판단을 해야 했다. 틀렸다고 생각되면 표만 고치고 다시 돌리면 된다.

쓰는 법:  python Tools/import_sfx.py            (다듬어 넣는다)
          python Tools/import_sfx.py --dry      (무엇을 할지만 보여 준다)
"""
import os
import re
import sys
import glob
import wave

import numpy as np

SRC = r'C:\Users\MBC-501-08\Downloads'
SFX_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                       '..', 'Assets', '_Project', 'Audio', 'SFX')
BGM_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                       '..', 'Assets', '_Project', 'Audio', 'BGM')

PEAK = 10 ** (-2.0 / 20.0)      # 기존 음원과 같은 -2.0 dBFS
SILENCE = 10 ** (-60.0 / 20.0)

# 받은 이름 -> (게임의 사건, 목표 길이 초, 몇 개 쓸 것인가)
#
# 목표 길이는 docs/AUDIO_PROMPTS.md에 적은 값이다. 거기서 왜 그 길이인지도 같이 적었다.
# 길이 0은 "자르지 않는다" — 루프와 음악이 그렇다.
MAP = [
    ('robot_arm_swing',   'Punch',          0.30, 3),
    ('robot_punch',       'Strike',         1.30, 4),
    ('concrete_thud',     'FloorCollapse',  4.00, 2),
    ('building_collapse', 'Broken',         8.00, 2),
    ('cable_spool',       'CableOpen',      0.50, 2),
    ('cable_snap',        'CableMiss',      0.35, 2),
    ('net_snap',          'Capture',        0.65, 3),
    ('rubble_scrape',     'Snag',           0.40, 2),
    ('machine_stamp',     'Drop',           0.70, 2),
    ('concrete_drop',     'Swallow',        0.55, 3),
    ('pit_rumble',        'HoleLevelUp',    1.20, 2),
    ('industrial_fanfare', 'QuotaDone',     1.40, 2),

    # 아직 호출 자리가 없는 것들. 넣어 두면 코드만 늘리면 바로 울린다.
    ('robot_stamp',       'Footstep',       0.25, 4),
    ('rubble_drag',       'TowLoop',        0.00, 1),   # 루프 — 자르지 않는다
    ('deep_wind',         'HoleAmbient',    0.00, 1),   # 루프
    ('beep_warning',      'Warning',        0.45, 2),
]

# 음악. 가장 긴 둘을 쓴다 — 짧은 것은 루프로 쓰기에 금방 티가 난다.
MUSIC = ('industrial_loop', ['BGM_Field', 'BGM_Demolition'])


def read(path):
    with wave.open(path, 'rb') as w:
        ch, width, rate, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(n)
    if width == 2:
        a = np.frombuffer(raw, dtype='<i2').astype(np.float64) / 32768.0
    elif width == 3:
        b = np.frombuffer(raw, dtype=np.uint8).reshape(-1, 3).astype(np.int32)
        v = b[:, 0] | (b[:, 1] << 8) | (b[:, 2] << 16)
        v = np.where(v & 0x800000, v - 0x1000000, v)
        a = v.astype(np.float64) / 8388608.0
    elif width == 4:
        a = np.frombuffer(raw, dtype='<i4').astype(np.float64) / 2147483648.0
    elif width == 1:
        a = (np.frombuffer(raw, dtype=np.uint8).astype(np.float64) - 128.0) / 128.0
    else:
        raise ValueError('샘플 폭 %d바이트' % width)
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


def trim_head(a):
    """앞 침묵을 뗀다. 소리가 시작되기 조금 전부터 남긴다 — 칼같이 자르면 어택이 뭉툭해진다."""
    loud = np.flatnonzero(np.abs(a) > SILENCE)
    if loud.size == 0:
        return a
    start = max(0, loud[0] - 64)
    return a[start:]


def trim_tail(a, rate, keep=0.06):
    """끝의 침묵을 뗀다. 꼬리를 조금 남겨 자연스럽게 꺼지게 한다.

    받은 음원은 소리가 끝난 뒤에도 한참 조용히 이어진다 - 실측에서 Strike 하나가
    1.30초 중 0.71초가 침묵이었다(무음 63%). 그대로 두면 변형마다 체감 길이가 달라
    같은 소리로 안 들린다.
    """
    loud = np.flatnonzero(np.abs(a) > SILENCE)
    if loud.size == 0:
        return a
    end = min(a.size, loud[-1] + int(rate * keep))
    return a[:end]


def match_rms(clips, cut_db=9.0):
    """한 묶음의 변형끼리 음량을 맞춘다.

    <b>묶음을 통째로 정규화한다.</b> 하나씩 키우고 피크로 깎으면 그 깎기가 보정을 되돌려
    오히려 벌어진다 - 실측에서 Footstep이 3.5dB에서 8.0dB로 더 벌어졌다.
    먼저 RMS를 가운데로 끌어당겨 상대 음량을 맞춘 뒤, 묶음에서 가장 큰 피크 하나만 보고
    전체에 같은 배율을 건다. 그러면 상대 관계가 정확히 보존되고 클리핑도 안 난다.

    개별 피크가 -2.0 dBFS에 못 미치는 것은 정상이다 - 변형끼리 같게 들리는 쪽이 우선이다.
    """
    if len(clips) < 2:
        return [norm(c) for c in clips]

    rms = [float(np.sqrt(np.mean(c * c))) for c in clips]
    mid = float(np.median(rms))
    lim = 10 ** (cut_db / 20.0)

    out = []
    for c, r in zip(clips, rms):
        g = 1.0 if r <= 1e-9 else min(max(mid / r, 1.0 / lim), lim)
        out.append(c * g)

    top = max(float(np.max(np.abs(c))) for c in out)
    if top > 1e-9:
        out = [c * (PEAK / top) for c in out]
    return out


def fit(a, rate, seconds):
    """목표 길이로 맞춘다. <b>가장 센 구간</b>을 고른다.

    앞에서부터 자르면 조용한 도입부를 잡는 수가 있다 - 실측에서 Swallow 하나가 그래서
    나머지 둘보다 7.5dB 조용했다. 에너지가 제일 많은 창을 고르면 "그 소리의 핵심"이 남는다.
    창을 옮긴 뒤 다시 앞 침묵을 떼지는 않는다 - 어택 직전의 숨까지가 그 소리다.
    """
    if seconds <= 0:
        return a
    n = int(rate * seconds)
    if a.size <= n:
        return a

    # 누적합으로 모든 창의 에너지를 한 번에 본다. 창을 하나씩 세면 느리다.
    e = np.concatenate(([0.0], np.cumsum(a * a)))
    win = e[n:] - e[:-n]
    start = int(np.argmax(win))
    start = max(0, start - int(rate * 0.01))        # 어택 10ms 앞에서 시작한다
    out = a[start:start + n].copy()
    if out.size < n:
        out = np.pad(out, (0, n - out.size))
    f = min(int(rate * 0.04), n // 4)      # 40ms 페이드 — 자른 자리의 '툭'을 없앤다
    if f > 0:
        out[-f:] *= np.linspace(1, 0, f) ** 1.5
    return out


def norm(a):
    m = float(np.max(np.abs(a)))
    return a * (PEAK / m) if m > 1e-9 else a


def group_files():
    """`name (1).wav` 꼴을 묶는다. 긴 것부터 — 자를 여유가 있는 쪽이 낫다."""
    groups = {}
    for f in glob.glob(os.path.join(SRC, '*.wav')):
        base = re.sub(r'\s*\(\d+\)', '', os.path.basename(f)[:-4]).strip()
        groups.setdefault(base, []).append(f)
    for fs in groups.values():
        fs.sort(key=lambda p: os.path.getsize(p), reverse=True)
    return groups


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    dry = '--dry' in sys.argv
    groups = group_files()

    print('받은 음원을 다듬어 넣는다 — 원본은 안 건드린다')
    print('%-20s %-14s %6s %5s  %s' % ('받은 이름', '게임의 사건', '목표s', '개수', '결과'))

    made = 0
    for src, name, seconds, count in MAP:
        fs = groups.get(src)
        if not fs:
            print('%-20s %-14s %6s %5d  없음 — 건너뜀' % (src, name, '-', count))
            continue

        clips, rate = [], 44100
        for f in fs[:count]:
            a, rate = read(f)
            a = fit(trim_head(a), rate, seconds)
            if seconds > 0:
                a = trim_tail(a, rate)               # 루프는 꼬리를 안 뗀다
            clips.append(a)

        clips = match_rms(clips)

        done = []
        for i, a in enumerate(clips, start=1):
            out = os.path.normpath(os.path.join(SFX_OUT, 'SFX_%s_%02d.wav' % (name, i)))
            if not dry:
                write(out, a, rate)
            done.append('%.2fs' % (a.size / rate))
            made += 1
        print('%-20s %-14s %6.2f %5d  %s' % (src, name, seconds, len(done), ' '.join(done)))

    # ── 음악 ────────────────────────────────────────────────────
    fs = groups.get(MUSIC[0], [])
    print()
    if len(fs) < 2:
        print('음악: %s 가 둘 미만이다 — 건너뜀' % MUSIC[0])
    else:
        for i, name in enumerate(MUSIC[1]):
            a, rate = read(fs[i])
            a = norm(trim_head(a)) * (10 ** (-4.0 / 20.0))   # 음악은 효과음보다 4dB 낮게
            out = os.path.normpath(os.path.join(BGM_OUT, name + '.wav'))
            if not dry:
                write(out, a, rate)
            print('음악: %-16s %.1f초  <- %s' % (name, a.size / rate, os.path.basename(fs[i])))
            made += 1

    print()
    print('%s %d개. 다음: 유니티 Demo/16 -> python Tools/check_sfx.py'
          % ('만들었을 것' if dry else '만듦', made))
    if dry:
        print('(--dry 라서 실제로는 아무 파일도 안 썼다)')


if __name__ == '__main__':
    main()
