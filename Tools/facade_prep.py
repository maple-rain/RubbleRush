#!/usr/bin/env python3
"""VARCO가 뽑은 건물 정면도를 조각 판에 바로 쓸 수 있는 텍스처로 다듬는다.

조각 판(`WallFracture`)의 UV는 **아래 0, 위 1**로 판 전체에 딱 맞게 깔려 있다.
그래서 그림에 하늘이나 여백이 한 줄이라도 남으면 그게 곧 **벽의 일부**가 된다.
프롬프트로 "no sky"를 아무리 박아도 모델은 지붕 위 설비를 그리려고 하늘을 남긴다.
말로 막는 것보다 잘라내는 쪽이 확실하다.

세 가지를 한다.

1. **하늘 잘라내기** — 위에서부터 훑어 '밝고 무채색'인 픽셀이 사라지는 첫 행을 찾는다.
   그 행이 파라펫(옥상 난간벽) 윗선이다. 물탱크·환기구처럼 하늘로 삐져나온 것은 같이
   잘린다 — 평면 사각형인 판 위에서는 어차피 표현할 수 없고, 남기면 하늘색 얼룩이 된다.

2. **비율 맞추기** — 판의 실제 비율(가로 m / 세로 m)에 맞춰 가운데를 자른다.
   `aspectRatio`가 5종(1:1 3:4 4:3 9:16 16:9)뿐이라 생성만으로는 정확히 못 맞춘다.

3. **정사각으로 눌러 담기** — 유니티는 거듭제곱이 아닌 텍스처를 압축하지 못한다.
   4:3은 원리상 거듭제곱 쌍이 될 수 없으므로 정사각에 찌그러뜨려 넣고, 판의 UV가
   도로 펴게 둔다. 실측: 1024x768 무압축 7297KB -> 1024x1024 DXT1 1388KB.

    python Tools/facade_prep.py <입력> <출력> --aspect 1.333 [--size 1024]

`--aspect`는 그 면의 **가로 m ÷ 세로 m**다. 예를 들어 12×9 건물의 정면은 1.333,
5.5×1.0 건물의 측면은 1.0/3.0 = 0.333이다.
"""

import argparse
import sys

# 한국어 윈도우 콘솔은 cp949라 em dash 하나에 UnicodeEncodeError로 죽는다.
# 파일은 이미 쓰인 뒤 print에서 터지므로 증상이 특히 헷갈린다.
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
sys.stderr.reconfigure(encoding='utf-8', errors='replace')

import numpy as np
from PIL import Image


def sky_rows(rgb, bright_min=150, chroma_max=40):
    """행마다 '하늘로 보이는' 픽셀의 비율."""
    bright = rgb.mean(2)
    chroma = rgb.max(2) - rgb.min(2)
    return ((bright > bright_min) & (chroma < chroma_max)).mean(1)


def prep(src, dst, aspect, size=1024, sky_cut=0.02, max_sky=0.45):
    rgb = np.asarray(Image.open(src).convert("RGB")).astype(np.int16)
    H, W, _ = rgb.shape

    frac = sky_rows(rgb)
    limit = int(H * max_sky)
    top = next((y for y in range(limit) if frac[y] < sky_cut), 0)

    # 하늘이 limit까지 안 끊기면 판단을 포기한다. 잘못 자르면 건물이 잘려 나가므로
    # 조용히 반쯤 자르는 것보다 사람이 보게 하는 편이 낫다.
    if top >= limit - 1:
        print(f"[facade] 경고: 하늘 경계를 못 찾았다 ({src}). 자르지 않고 그대로 쓴다 — "
              "그림을 눈으로 확인할 것", file=sys.stderr)
        top = 0

    img = Image.open(src).convert("RGB").crop((0, top, W, H))
    w, h = img.size

    # 비율 맞추기 — 늘리지 않고 가운데를 자른다
    want = w / h
    if want > aspect:                       # 너무 넓다 -> 좌우를 자른다
        nw = int(round(h * aspect))
        x = (w - nw) // 2
        img = img.crop((x, 0, x + nw, h))
    elif want < aspect:                     # 너무 좁다 -> 위를 더 자른다 (아래의 입구는 지킨다)
        nh = int(round(w / aspect))
        img = img.crop((0, h - nh, w, h))

    # 정사각(거듭제곱)으로 <b>일부러 찌그러뜨려</b> 저장한다.
    #
    # 유니티는 거듭제곱이 아닌 텍스처를 블록 압축(DXT/ASTC)하지 못한다. 4:3은 원리상
    # 거듭제곱 쌍이 될 수 없으므로 1024x768로 두면 RGB24 무압축이 되고, 장당 3.0MB,
    # 20장이면 59.9MB다 — AAB 예산 200MB에서 감당할 수 없다.
    #
    # 그런데 조각 판의 UV는 0~1을 판의 <b>실제 가로세로</b>에 펼친다. 그러니 여기서
    # 세로로 찌그러뜨려 두면 벽에 붙는 순간 정확히 그만큼 도로 펴진다. 찌그러짐이
    # 상쇄되는 것이다. 텍스처 파일만 보면 창문이 길쭉해 보이는 것이 정상이다.
    #
    # 대가는 화소 밀도가 축마다 달라지는 것뿐이고(12x9m에 가로 85 세로 114 px/m),
    # 얻는 것은 압축이다 — 장당 0.4MB.
    if size:
        img = img.resize((size, size), Image.LANCZOS)

    img.save(dst)
    print(f"[facade] {src}\n     -> {dst}")
    print(f"   하늘 {top}px 잘라냄 ({top / H * 100:.1f}%) · {W}x{H} -> {img.size[0]}x{img.size[1]}")
    print(f"   내용 비율 {aspect:.3f}을 정사각에 눌러 담았다 — 판에 붙으면 펴진다")


if __name__ == "__main__":
    p = argparse.ArgumentParser(description="건물 정면도를 조각 판 텍스처로 다듬는다")
    p.add_argument("src")
    p.add_argument("dst")
    p.add_argument("--aspect", type=float, required=True,
                   help="그 면의 가로 m / 세로 m. 12x9 정면이면 1.333")
    p.add_argument("--size", type=int, default=1024,
                   help="정사각 한 변 픽셀. 거듭제곱이어야 압축이 걸린다 (기본 1024)")
    a = p.parse_args()
    prep(a.src, a.dst, a.aspect, a.size)
