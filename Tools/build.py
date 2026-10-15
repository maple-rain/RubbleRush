#!/usr/bin/env python3
"""에디터를 닫아 두고 빌드를 돌린다 [신설 2026-10-15].

    python Tools/build.py android      # AAB (스토어용)
    python Tools/build.py apk          # APK (기기에 바로 설치)
    python Tools/build.py webgl        # WebGL (itch.io용)

왜 필요한가
-----------
빌드 한 번이 30~90분이다 (실측: Assets 345MB · 텍스처 686장 · 플랫폼을 바꾸면
전부 재압축). 에디터에서 빌드를 걸면 그동안 유니티를 아무것도 못 한다.
배치 모드로 돌리면 걸어 두고 자리를 비울 수 있다.

반드시 알 것 — 유니티는 한 프로젝트를 한 번에 하나만 연다
--------------------------------------------------------
에디터를 열어 둔 채 이것을 돌리면 락에 걸려 실패한다. 그래서 시작 전에
`Temp/UnityLockfile`을 보고, 있으면 아무것도 하지 않고 멈춘다.
에디터를 닫고 다시 돌리면 된다.

진행 상황
---------
배치 모드는 화면이 없으므로 로그로만 보인다. 이 스크립트가 로그를 따라가며
의미 있는 줄만 추려서 찍는다. 다 끝나면 산출물 크기를 예산과 대조한다.
"""

import os
import re
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
UNITY = Path(r"C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe")

# CLAUDE.md 성능 예산 [확정]
BUDGET_MB = {"android": 200, "apk": 200, "webgl": 100}

# -buildTarget 에 넘길 이름. 플랫폼을 미리 지정해 두면 전환이 빌드보다 먼저 끝난다.
UNITY_TARGET = {"android": "Android", "apk": "Android", "webgl": "WebGL"}

# 로그에서 흘려보낼 것. 배치 로그는 수만 줄이라 전부 찍으면 아무것도 안 보인다.
NOISE = re.compile(
    r"^(Refresh|Reloading|Begin MonoManager|Registering|Initialize engine|"
    r"Loading GUID|Unloading \d|System memory|\[Physics|Mono config|"
    r"- (Starting|Finished|Completed) ?(reload|import)|<RI>|AssetDatabase:)",
    re.IGNORECASE,
)
# 반드시 보여 줄 것
KEEP = re.compile(r"\[빌드|\[빌드 전 검사\]|error|exception|failed|실패|막힘|경고 \d|Build Report",
                  re.IGNORECASE)


def die(msg, code=1):
    print("\n  " + msg)
    sys.exit(code)


def main():
    target = (sys.argv[1] if len(sys.argv) > 1 else "android").lower()
    if target not in UNITY_TARGET:
        die("쓸 수 있는 값: android | apk | webgl")

    if not UNITY.exists():
        die(f"유니티를 못 찾았다: {UNITY}\n  경로가 다르면 이 파일 위쪽 UNITY 를 고칠 것.")

    lock = ROOT / "Temp" / "UnityLockfile"
    if lock.exists():
        die(
            "에디터가 이 프로젝트를 열고 있다 (Temp/UnityLockfile).\n"
            "  유니티는 한 프로젝트를 한 번에 하나만 연다 — 에디터를 닫고 다시 돌릴 것.\n"
            "  (에디터에서 바로 빌드하고 싶으면 Demo/빌드 메뉴를 쓰면 된다.)"
        )

    logs = ROOT / "Builds" / "logs"
    logs.mkdir(parents=True, exist_ok=True)
    log = logs / f"build_{target}_{time.strftime('%m%d_%H%M%S')}.log"

    cmd = [
        str(UNITY),
        "-quit", "-batchmode", "-nographics",
        "-projectPath", str(ROOT),
        "-buildTarget", UNITY_TARGET[target],
        "-executeMethod", "Demo.EditorTools.BuildPipelineTool.Batch",
        "-demoTarget", "androidApk" if target == "apk" else target,
        "-logFile", str(log),
    ]

    print(f"  빌드 시작 — {target}")
    print(f"  로그: {log}")
    print("  처음 한 번은 텍스처를 전부 다시 임포트하므로 30~90분을 본다.\n")

    started = time.time()
    proc = subprocess.Popen(cmd)
    tail(proc, log, started)

    code = proc.wait()
    mins = (time.time() - started) / 60
    print(f"\n  끝났다 — 종료 코드 {code} · {mins:.1f}분")

    report(target)
    sys.exit(code)


def tail(proc, log, started):
    """로그가 생기는 대로 따라가며 의미 있는 줄만 찍는다."""
    pos, last_beat = 0, time.time()

    while proc.poll() is None:
        time.sleep(2)

        if log.exists():
            with log.open("r", encoding="utf-8", errors="replace") as f:
                f.seek(pos)
                chunk = f.read()
                pos = f.tell()

            for line in chunk.splitlines():
                s = line.strip()
                if not s or NOISE.search(s):
                    continue
                if KEEP.search(s) or len(s) < 200:
                    print("  | " + s[:180])
                    last_beat = time.time()

        # 오래 조용하면 살아 있다는 것만 알린다 — 멈춘 것으로 보이면 끄게 된다.
        if time.time() - last_beat > 120:
            print(f"  | … 도는 중 ({(time.time() - started) / 60:.0f}분 경과)")
            last_beat = time.time()


def report(target):
    """산출물을 찾아 예산과 대조한다."""
    out = ROOT / "Builds" / ("WebGL" if target == "webgl" else "Android")
    if not out.exists():
        print("  산출물 폴더가 없다 — 빌드가 실패했을 수 있다. 위 로그를 볼 것.")
        return

    newest, newest_at = None, 0
    for p in out.iterdir():
        at = p.stat().st_mtime
        if at > newest_at:
            newest, newest_at = p, at

    if newest is None:
        print("  산출물이 비어 있다.")
        return

    size = sum(f.stat().st_size for f in newest.rglob("*") if f.is_file()) \
        if newest.is_dir() else newest.stat().st_size
    mb = size / 1024 / 1024
    budget = BUDGET_MB[target]

    print(f"\n  산출물  {newest}")
    print(f"  크기    {mb:.1f}MB  /  예산 {budget}MB  ({mb / budget * 100:.0f}%)"
          + ("   ** 예산 초과 **" if mb > budget else ""))


if __name__ == "__main__":
    main()
