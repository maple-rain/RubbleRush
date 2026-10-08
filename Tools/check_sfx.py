# -*- coding: utf-8 -*-
"""효과음이 쓸 만한 파일인지 객관적으로 잰다 [2026-10-08].

**이 도구가 못 하는 일을 먼저 적는다.** "좋게 들리는가"는 못 잰다. 그건 사람이 듣고
정한다. 여기서 재는 것은 *기계가 확인할 수 있는 것*뿐이다 — 파일이 제대로 생겼는가,
소리가 들어 있기는 한가, 클리핑으로 깨졌는가, 앞뒤에 침묵이 길게 붙어 있는가,
**변형끼리 음량이 들쭉날쭉한가**.

**왜 필요한가.** 생성 모델은 조용한 파일이나 앞에 1초 침묵이 붙은 파일을 아무렇지 않게
돌려준다. 그대로 꽂으면 게임에서는 "그 소리만 안 나는" 것으로 보이고 **에러가 안 난다**.
SoundBank가 비어 있을 때와 증상이 같아서 원인을 찾는 데 한참 걸린다.

**기준선은 이미 받아들인 음원이다.** Strike/FloorCollapse/Broken 셋은 이 프로젝트에
들어와 있던 것이라, 새로 만든 것을 그 셋과 같은 자로 재서 벗어난 것만 짚는다.
절대 기준을 내가 지어내지 않는다.

쓰는 법:  python Tools/check_sfx.py
"""
import os
import sys
import wave

import numpy as np

SFX_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                       '..', 'Assets', '_Project', 'Audio', 'SFX')

# 프로젝트에 이미 들어와 있던 것들. 새로 만든 것의 기준선이 된다.
BASELINE = ('Strike', 'FloorCollapse', 'Broken')

SILENCE_DB = -60.0      # 이보다 작으면 '소리 없음'으로 친다
CLIP_AT = 0.999         # 이 이상이면 꼭짓점이 잘린 것으로 본다


def read(path):
    """WAV를 -1..1 모노 float로 읽는다. PCM 8/16/24/32비트와 float32를 받는다."""
    with wave.open(path, 'rb') as w:
        ch, width, rate, frames = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(frames)

    if width == 1:                       # 8비트는 부호 없음
        a = np.frombuffer(raw, dtype=np.uint8).astype(np.float32)
        a = (a - 128.0) / 128.0
    elif width == 2:
        a = np.frombuffer(raw, dtype='<i2').astype(np.float32) / 32768.0
    elif width == 3:                     # 24비트는 numpy에 타입이 없다 — 손으로 조립한다
        b = np.frombuffer(raw, dtype=np.uint8).reshape(-1, 3).astype(np.int32)
        v = b[:, 0] | (b[:, 1] << 8) | (b[:, 2] << 16)
        v = np.where(v & 0x800000, v - 0x1000000, v)
        a = v.astype(np.float32) / 8388608.0
    elif width == 4:
        a = np.frombuffer(raw, dtype='<i4').astype(np.float32) / 2147483648.0
    else:
        raise ValueError('샘플 폭 %d바이트는 못 읽는다' % width)

    if ch > 1:
        a = a.reshape(-1, ch).mean(axis=1)
    return a, rate, ch, width * 8


def db(x):
    return -999.0 if x <= 1e-9 else 20.0 * float(np.log10(x))


def edge_silence(a, rate):
    """앞/뒤에 붙은 침묵의 길이(초). 길면 타이밍이 밀려 '반응이 늦은' 느낌이 된다."""
    thr = 10.0 ** (SILENCE_DB / 20.0)
    loud = np.flatnonzero(np.abs(a) > thr)
    if loud.size == 0:
        return None, None
    return loud[0] / rate, (a.size - 1 - loud[-1]) / rate


def measure(path):
    a, rate, ch, bits = read(path)
    if a.size == 0:
        return dict(name=os.path.basename(path), empty=True)

    peak = float(np.max(np.abs(a)))
    rms = float(np.sqrt(np.mean(a * a)))
    head, tail = edge_silence(a, rate)
    thr = 10.0 ** (SILENCE_DB / 20.0)

    return dict(
        name=os.path.splitext(os.path.basename(path))[0],
        empty=False,
        seconds=a.size / rate,
        rate=rate, ch=ch, bits=bits,
        peak_db=db(peak),
        rms_db=db(rms),
        quiet_share=float(np.mean(np.abs(a) <= thr)),
        clipped=int(np.count_nonzero(np.abs(a) >= CLIP_AT)),
        dc=float(np.mean(a)),
        head=head, tail=tail,
    )


def difference_db(folder, name_a, name_b):
    """두 음원이 얼마나 다른가 — 차이의 RMS를 신호 RMS 대비 dB로. 작을수록 닮았다.

    완전히 같은 파일이면 -inf에 가깝고, 서로 다른 소음이면 0 dB 언저리가 된다.
    길이가 다르면 짧은 쪽에 맞춰 비교한다 — 앞부분이 같으면 같은 소리로 들린다.
    """
    try:
        a, _, _, _ = read(os.path.join(folder, name_a + '.wav'))
        b, _, _, _ = read(os.path.join(folder, name_b + '.wav'))
    except Exception:
        return None
    n = min(a.size, b.size)
    if n == 0:
        return None
    a, b = a[:n], b[:n]
    sig = float(np.sqrt(np.mean(a * a)))
    dif = float(np.sqrt(np.mean((a - b) ** 2)))
    if sig <= 1e-9:
        return None
    return db(dif / sig)


def group_of(name):
    """SFX_Strike_02 -> Strike"""
    n = name[4:] if name.startswith('SFX_') else name
    cut = n.rfind('_')
    if cut > 0 and n[cut + 1:].isdigit():
        n = n[:cut]
    return n


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    d = os.path.normpath(SFX_DIR)
    files = sorted(f for f in os.listdir(d) if f.lower().endswith('.wav'))
    if not files:
        print('음원이 없다:', d)
        return

    rows = []
    for f in files:
        try:
            rows.append(measure(os.path.join(d, f)))
        except Exception as e:
            rows.append(dict(name=os.path.splitext(f)[0], empty=True, error=str(e)))

    # ── 기준선 ─────────────────────────────────────────────────
    base = [r for r in rows if not r['empty'] and group_of(r['name']) in BASELINE]
    bl_peak = (min(r['peak_db'] for r in base), max(r['peak_db'] for r in base)) if base else None
    bl_rms = (min(r['rms_db'] for r in base), max(r['rms_db'] for r in base)) if base else None
    bl_sec = (min(r['seconds'] for r in base), max(r['seconds'] for r in base)) if base else None

    print('효과음 검증 — %d개 [2026-10-08]' % len(rows))
    print('기준선 = 이미 받아들인 %s' % ', '.join(BASELINE))
    if base:
        print('  피크 %.1f~%.1f dBFS · RMS %.1f~%.1f dBFS · 길이 %.2f~%.2fs'
              % (bl_peak[0], bl_peak[1], bl_rms[0], bl_rms[1], bl_sec[0], bl_sec[1]))
    print()
    print('%-22s %7s %7s %7s %7s %7s %6s %6s  %s'
          % ('파일', '길이s', '피크dB', 'RMSdB', '무음%', '앞침묵', '뒤침묵', '클립', '판정'))

    flagged = 0
    for r in sorted(rows, key=lambda x: x['name']):
        if r['empty']:
            print('%-22s %s' % (r['name'], '읽기 실패 — ' + r.get('error', '빈 파일')))
            flagged += 1
            continue

        notes = []
        if r['peak_db'] < -40:
            notes.append('거의 무음')
        if r['quiet_share'] > 0.9:
            notes.append('9할이 침묵')
        if r['clipped'] > 8:
            notes.append('클리핑 %d' % r['clipped'])
        if r['head'] is not None and r['head'] > 0.15:
            notes.append('앞 침묵 %.2fs' % r['head'])
        if abs(r['dc']) > 0.01:
            notes.append('DC %.3f' % r['dc'])
        if base and not (bl_rms[0] - 9 <= r['rms_db'] <= bl_rms[1] + 9):
            notes.append('음량이 기준선 밖')
        if r['rate'] < 22050:
            notes.append('%dHz' % r['rate'])

        if notes:
            flagged += 1
        print('%-22s %7.2f %7.1f %7.1f %6.0f%% %7s %6s %6d  %s'
              % (r['name'], r['seconds'], r['peak_db'], r['rms_db'],
                 r['quiet_share'] * 100,
                 '%.2f' % r['head'] if r['head'] is not None else '-',
                 '%.2f' % r['tail'] if r['tail'] is not None else '-',
                 r['clipped'],
                 ' · '.join(notes) if notes else 'O'))

    # ── 변형끼리 고른가, 그리고 <b>서로 다르기는 한가</b> ───────
    print()
    print('변형끼리 — 음량이 고른가, 그리고 서로 다르기는 한가')
    print('  변형을 여럿 두는 유일한 이유가 "반복이 기계처럼 들려서"다.')
    print('  사실상 같은 파일이 둘이면 그 이유가 사라지고 용량만 는다.')
    groups = {}
    for r in rows:
        if not r['empty']:
            groups.setdefault(group_of(r['name']), []).append(r)

    dup = 0
    for g, rs in sorted(groups.items()):
        if len(rs) < 2:
            continue
        spread = max(x['rms_db'] for x in rs) - min(x['rms_db'] for x in rs)
        span = max(x['seconds'] for x in rs) - min(x['seconds'] for x in rs)

        # 가장 닮은 한 쌍을 찾는다. 차이를 신호 대비 dB로 재면 길이·음량과 무관하다.
        worst = 999.0
        pair = ''
        for i in range(len(rs)):
            for j in range(i + 1, len(rs)):
                diff = difference_db(d, rs[i]['name'], rs[j]['name'])
                if diff is not None and diff < worst:
                    worst = diff
                    pair = '%s↔%s' % (rs[i]['name'][-2:], rs[j]['name'][-2:])

        notes = []
        if spread > 6:
            notes.append('음량 들쭉날쭉')
        if worst < -30:
            notes.append('%s가 사실상 같은 파일 (%.0f dB)' % (pair, worst))
            dup += 1
        print('  %-16s 변형 %d개 · RMS 차 %4.1f dB · 길이 차 %.2fs · 최소 차이 %5.1f dB  %s'
              % (g, len(rs), spread, span, worst if worst < 900 else float('nan'),
                 ' · '.join(notes) if notes else '고르고 서로 다르다'))

    if dup:
        print()
        print('  똑같은 변형을 가진 소리 %d가지 — 변형을 하나로 줄이거나 실제로 다르게 만들 것.' % dup)

    print()
    print('짚인 파일 %d / %d. 이 도구는 "좋게 들리는가"는 못 잰다 — 그건 듣고 정할 일이다.'
          % (flagged, len(rows)))


if __name__ == '__main__':
    main()
