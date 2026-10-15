#!/usr/bin/env python3
"""빌드를 차례로 전부 돌린다 — 걸어 두고 자리를 비우기 위한 것 [신설 2026-10-15].

    python Tools/build_all.py            # WebGL -> Android AAB
    python Tools/build_all.py webgl apk  # 순서와 대상을 직접 고른다

에디터가 열려 있으면 **닫힐 때까지 기다렸다가** 시작한다. 그래서 이 명령을 먼저
걸어 두고 유니티를 닫고 나가면 된다.

왜 순서가 WebGL 먼저인가
------------------------
플랫폼을 바꿀 때마다 텍스처 686장을 전부 재압축한다 (Assets 345MB). 그래서
플랫폼을 오가는 횟수가 곧 시간이다. WebGL은 예산이 100MB로 빠듯해서 먼저
결과를 봐야 손 쓸 시간이 남고, Android는 200MB라 여유가 있다.

끝나면 둘 다 크기를 예산과 대조해 한 장으로 적는다.

주의
----
도는 동안 유니티를 열지 말 것. 같은 Library를 건드려 둘 다 망가진다.
"""

import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RUNNER = ROOT / "Tools" / "build.py"
LOCK = ROOT / "Temp" / "UnityLockfile"

ORDER = ["webgl", "android"]
WAIT_LIMIT_MIN = 90          # 에디터가 이만큼 안 닫히면 포기한다


def wait_for_editor():
    """에디터가 닫히기를 기다린다. 이미 닫혀 있으면 바로 돌아온다."""
    if not LOCK.exists():
        return True

    print(f"  에디터가 열려 있다 — 닫히기를 기다린다 (최대 {WAIT_LIMIT_MIN}분)")
    print("  지금 유니티를 닫으시면 바로 시작한다.\n")

    started = time.time()
    told = 0
    while LOCK.exists():
        time.sleep(10)
        mins = (time.time() - started) / 60
        if mins >= WAIT_LIMIT_MIN:
            print(f"\n  {WAIT_LIMIT_MIN}분을 기다렸는데 아직 열려 있다. 그만둔다.")
            return False
        if int(mins) >= told + 5:
            told = int(mins)
            print(f"  | 아직 기다리는 중 ({told}분)")

    print("  에디터가 닫혔다. 시작한다.\n")
    time.sleep(3)        # 락이 풀린 뒤 파일 핸들이 정리될 틈
    return True


def main():
    targets = [t.lower() for t in sys.argv[1:]] or ORDER
    for t in targets:
        if t not in ("android", "apk", "webgl"):
            print(f"  쓸 수 없는 대상: {t}  (android | apk | webgl)")
            sys.exit(1)

    print("  순서: " + " -> ".join(targets))
    print("  각 빌드가 30~90분이다. 도는 동안 유니티를 열지 말 것.\n")

    if not wait_for_editor():
        sys.exit(1)

    results = []
    for i, t in enumerate(targets, 1):
        print("\n" + "=" * 64)
        print(f"  [{i}/{len(targets)}]  {t}")
        print("=" * 64)

        started = time.time()
        code = subprocess.call([sys.executable, str(RUNNER), t])
        mins = (time.time() - started) / 60
        results.append((t, code, mins))

        if code != 0:
            # 하나가 실패해도 다음 것은 돌린다 — 둘 다 못 건지는 것보다 낫다.
            print(f"\n  {t} 실패 (코드 {code}). 다음으로 넘어간다.")

    print("\n" + "=" * 64)
    print("  전부 끝났다")
    print("=" * 64)
    for t, code, mins in results:
        mark = "성공" if code == 0 else f"실패({code})"
        print(f"  {t:<10} {mark:<10} {mins:5.1f}분")

    print(f"\n  산출물: {ROOT / 'Builds'}")
    print(f"  로그:   {ROOT / 'Builds' / 'logs'}")

    sys.exit(0 if all(c == 0 for _, c, _ in results) else 1)


if __name__ == "__main__":
    main()
