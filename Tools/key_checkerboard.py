#!/usr/bin/env python3
"""체커보드가 구워진 JPG에서 알파를 복원한다.

무료 소재 사이트에서 받은 아이콘은 대개 "투명 배경"이라고 적혀 있지만 실제로는
JPG다. JPG에는 알파 채널이 없으므로 그 회색 체커보드는 **픽셀에 그대로 그려져 있다.**
그대로 유니티에 넣으면 화면에 체커보드가 같이 나온다.

되살리는 근거는 색이다.

    체커보드 · 워터마크 : 무채색      R ≈ G ≈ B      → chroma ≈ 0
    아이콘 본체        : 유채색      R ≫ G, B       → chroma ≫ 0

    chroma = max(R,G,B) - min(R,G,B)

화살표이미지.jpg 실측: 본체 205 · 배경 0 · 워터마크 2. 경계가 이만큼 벌어져 있으면
임계값 하나로 갈린다. **워터마크도 무채색이라 같이 지워진다** — 따로 자를 필요가 없다.

RGB는 살아남은 픽셀의 중앙값으로 **전면을 덮어쓴다.** 투명한 자리까지 포함해서다.
JPG가 가장자리를 배경과 섞어 놓아 분홍빛 픽셀이 남는데, 알파만 0으로 만들고 두면
밉맵·블록압축·이중선형보간이 그 분홍을 도로 끌어올려 테두리에 흰 띠가 생긴다.
색을 평평하게 깔아 두면 무엇과 섞이든 같은 색이라 띠가 생길 수 없다.

    python Tools/key_checkerboard.py <입력> <출력> [--threshold 60] [--size 256]

무채색 아이콘(흰색·검은색 로고)에는 쓸 수 없다. 그때는 밝기로 갈라야 한다.
"""

import argparse
import sys

# 한국어 윈도우 콘솔은 cp949라 em dash 하나에 UnicodeEncodeError로 죽는다.
# 파일은 이미 쓰인 뒤 print에서 터지므로 증상이 특히 헷갈린다.
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
sys.stderr.reconfigure(encoding='utf-8', errors='replace')

import numpy as np
from PIL import Image


def key(src, dst, threshold=60, floor=18, size=256, pad=0.04, solid_percentile=85):
    rgb = np.asarray(Image.open(src).convert("RGB")).astype(np.int16)

    chroma = rgb.max(2) - rgb.min(2)
    # floor 아래는 완전 투명, threshold 위는 완전 불투명, 사이는 비례.
    # 사이 구간이 JPG가 흐려 놓은 가장자리를 그대로 안티에일리어싱으로 쓴다.
    # floor가 없으면 JPG 압축 잡음이 배경에 알파 1~10짜리 얼룩을 남긴다 — 어두운 배경에서는
    # 안 보이지만 밝은 배경에서 지저분한 점으로 드러난다.
    alpha = np.clip((chroma.astype(np.float32) - floor) / max(1, threshold - floor), 0.0, 1.0)

    solid = chroma > np.percentile(chroma[alpha > 0.5], solid_percentile) if (alpha > 0.5).any() else None
    if solid is None or not solid.any():
        sys.exit(f"[key] 유채색 픽셀이 없다. 무채색 아이콘이면 이 스크립트를 쓸 수 없다: {src}")
    color = np.median(rgb[solid], axis=0)

    ys, xs = np.nonzero(alpha > 0.5)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    a = alpha[y0:y1, x0:x1]

    # 정사각으로 패딩한다 — 비율을 안 건드리면서, 회전 중심이 곧 그림의 중심이 되게.
    # 화면 가장자리 표시는 매 프레임 회전하므로 중심이 어긋나면 화살표가 흔들린다.
    side = int(max(a.shape) * (1.0 + pad * 2))
    canvas = np.zeros((side, side), np.float32)
    oy, ox = (side - a.shape[0]) // 2, (side - a.shape[1]) // 2
    canvas[oy:oy + a.shape[0], ox:ox + a.shape[1]] = a

    # 알파만 단채널로 리샘플하고 색은 그 뒤에 칠한다. RGBA째로 resize하면 Pillow가
    # 알파 0인 자리의 RGB를 0으로 밀어 버려서(실측: 투명 픽셀 22,683개가 전부 검정)
    # 위에서 평평하게 깔아 둔 색이 사라지고, 되살리려던 흰 테두리가 검은 테두리로 바뀐다.
    mask = Image.fromarray((canvas * 255).astype(np.uint8), "L")
    if size and side != size:
        mask = mask.resize((size, size), Image.LANCZOS)

    n = mask.size[0]
    out = np.zeros((n, n, 4), np.uint8)
    out[..., :3] = color.astype(np.uint8)          # 투명한 자리까지 전부 같은 색
    out[..., 3] = np.asarray(mask)

    img = Image.fromarray(out, "RGBA")
    img.save(dst)

    print(f"[key] {src}\n   -> {dst}")
    print(f"   크롭 {x1 - x0}x{y1 - y0} -> 정사각 {side} -> 출력 {img.size[0]}")
    print(f"   색 RGB({int(color[0])},{int(color[1])},{int(color[2])}) · "
          f"불투명 비율 {(canvas > 0.5).mean():.1%}")


if __name__ == "__main__":
    p = argparse.ArgumentParser(description="체커보드가 구워진 JPG에서 알파를 복원한다")
    p.add_argument("src")
    p.add_argument("dst")
    p.add_argument("--threshold", type=int, default=60,
                   help="이 chroma 위를 완전 불투명으로 본다 (기본 60)")
    p.add_argument("--floor", type=int, default=18,
                   help="이 chroma 아래를 완전 투명으로 본다. JPG 잡음 제거용 (기본 18)")
    p.add_argument("--size", type=int, default=256, help="출력 한 변 픽셀. 0이면 리사이즈 안 함")
    a = p.parse_args()
    key(a.src, a.dst, a.threshold, a.floor, a.size)
