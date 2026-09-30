# CLAUDE.md

이 파일은 매 세션 컨텍스트에 로드된다. 짧게 유지하고, 상세는 `docs/`를 참조한다.

---

## 프로젝트

**철거 로봇이 폐시설을 부수고, 잔해를 케이블로 둘러싸 끌고, 구멍에 밀어넣는 3D 모바일 액션 게임.**
구멍이 커질수록 더 큰 것을 삼킬 수 있고, 후반에는 부수지 않고 건물을 통째로 밀어넣는다.

- 1인 개발 · 20 영업일 (개발 가용 15.5일)
- 목표: 개인 포트폴리오 · 게임 트레일러 유튜브 업로드 · itch.io + 구글 플레이 런칭
**➜ 새 세션이면 `HANDOFF.md`를 먼저 읽는다.** 현재 상태·미결 사항·역할 분담이 거기 있다.
**➜ 스테이지·점수·등급·스토리 작업이면 `docs/STAGE_SCORE_PLOT.md` 하나만 읽으면 된다.**
   그 주제의 현재 상태·결정 대기 목록·채울 CSV가 전부 거기 모여 있다 [2026-09-22 신설].

- 인수인계: `HANDOFF.md`
- 스테이지·점수·스토리: `docs/STAGE_SCORE_PLOT.md`
- 상세 기획: `docs/GAME_DESIGN.md` — **PART A만 확정. PART B는 AI 창작이며 미승인**
- 기술 설계: `docs/TECH_SPEC.md`
- 셋업 절차: `docs/SETUP.md`
- 밸런스 표(엑셀): `Tools/balance/*.csv` — 유니티 메뉴 `Demo/4` 내보내기 · `Demo/5` 불러오기
- 밸런스 시뮬레이터: `tools/balance_sim.py` (Python, Unity 불필요)

## 역할 분담 — 반드시 지킬 것

기획자가 **구조를 정하고 값을 채운다.** AI는 **골격을 제안하고 검증 도구를 만든다.**

- 수치와 구조는 **제안으로만** 낸다. 기획자 승인 없이 문서에 확정값으로 넣지 않는다
- 문서에 쓸 때는 `[확정]` / `[합의]` / `[제안·미승인]` 등급을 반드시 붙인다
- 검증 결과를 보고할 때는 **그 도구가 검증하지 않는 것**을 같이 밝힌다

이전 세션에서 AI가 기획자의 "기둥/벽면 2종"을 "6종 5티어"로 임의 확장하고
수치 24개를 만들어 확정값으로 기록한 사고가 있었다. 같은 일을 반복하지 않는다.

## 기술 스택

| 항목 | 값 |
|---|---|
| 엔진 | Unity 6 (6000.x) |
| 렌더 파이프라인 | URP · Mobile 프리셋 |
| 언어 | C# |
| 입력 | New Input System (터치 + 키보드 동시 지원) |
| 타깃 | Android (AAB) · WebGL |
| 3D 에셋 | VARCO 3D 생성 → Blender(bpy) 정리 → FBX |
| 오디오 | VARCO Sound 생성 |

---

## 절대 규칙

1. **Claude는 `.cs`와 `.py`만 편집한다.**
   `.unity` · `.prefab` · `.meta` · `.asset`은 절대 직접 편집하지 않는다. YAML이라 편집은 가능하지만 토큰을 폭식하고 파손 위험이 크다. 씬/프리팹 와이어링은 사람이 에디터에서 한다.

2. **씬은 하나다.** `Assets/_Project/Scenes/Main.unity` 하나로 간다.
   구역(Zone)은 씬이나 프리팹이 아니라 **`ZoneData` ScriptableObject로 정의하고 런타임에 생성한다.** 이렇게 해야 레벨 구성까지 코드가 소유할 수 있다.

3. **파일은 300줄 이하.** 넘으면 분리를 제안한다. 부분 수정 비용이 파일 크기에 비례한다.

4. **MeshCollider 금지.** 생성 메시는 논컨벡스에 고폴리다. Box/Capsule/Sphere 프리미티브만 쓴다.

5. **`Update()`에서 `GetComponent` / `Find` / `Instantiate` 금지.**
   참조는 `Awake`에 캐시하고, 생성은 전부 풀링한다.

6. **파티클·오디오·잔해는 반드시 풀링.** 모바일 GC 스파이크의 주범이다.

7. **수치를 코드에 하드코딩하지 않는다.** 전부 `GameConfig` 또는 해당 ScriptableObject로 뺀다. D16–18 시뮬레이션 밸런싱의 전제다.

8. **반복 수작업이 보이면 즉시 스크립트로 만든다.** `Assets/Editor/Tools/`의 `[MenuItem]` 또는 `Tools/blender/`의 bpy. 회수 기간이 보통 이틀 이하다.

---

## 폴더 구조

```
Assets/
  _Project/
    Art/{Models,Materials,VFX}
    Audio/{SFX,BGM}
    Data/{Structures,Zones,Config}      # ScriptableObject 에셋
    Prefabs/{Structures,Player,VFX}
    Scenes/Main.unity
    Scripts/
      Core/          # 게임 루프, 구역 관리, 풀링
      Player/        # 이동, 입력, 브레이커
      Destruction/   # 파괴 상태 머신, 지지 그래프, 연출
      Collection/    # 케이블, 견인, 구멍
      Hazard/        # 붕괴 낙하물, 위험 설비
      Data/          # ScriptableObject 정의
      UI/
      Utils/
    Settings/        # URP 에셋
  Editor/Tools/      # [MenuItem] 에디터 툴
Tools/blender/       # bpy 파이프라인 스크립트
docs/
```

**네임스페이스**는 폴더와 일치시킨다: `Demo.Core`, `Demo.Destruction`, `Demo.Collection` …

---

## 모바일 성능 예산

D2에 실측하기 전까지의 작업 기준값이다. 초과하면 즉시 알린다.

| 항목 | 상한 |
|---|---|
| 프레임 | 60fps 목표 / 30fps 최소 |
| 드로우콜 | 100 |
| 프레임당 트라이앵글 | 150k |
| 동시 구조물 | 60 |
| 동시 잔해 | **500** (초과분은 오래된 것부터 페이드) [지시 2026-09-30] |
| 실시간 그림자 | 사용 안 함 (블롭 섀도우로 대체) |
| 텍스처 | 기본 512 · 주요 오브젝트만 1024 |
| AAB | 200MB |
| WebGL | 100MB |

> **동시 잔해 80 → 500** [지시 2026-09-30]. 80은 실측 없이 이 표에 적힌 자리표시자였다.
> 구역 전체(30채)를 다 부수면 잔해가 344개 나오는데, 80이면 264개(77%)가 잘려서
> 다 부숴도 플레이어가 자기가 한 일의 1/4만 본다.
> 실측: 잔해 한 개는 **삼각형 12개짜리 큐브**라 344개를 다 띄워도 삼각형 예산의 **2.8%**다.
> 드는 것은 드로우콜 하나씩뿐이고, 데스크톱에서는 400개에서도 160fps가 나왔다.
> **드로우콜 100은 아직 검증 안 됐다** — 잔해 0개인 씬이 이미 82~84를 쓴다.
> 저사양 폰에서 감당하는지는 `DebrisBenchmark`(F9)로 실기에서 확인할 것.

---

## 작업 방식

하루 3세션. 각 세션은 **생성 → 에디터 통합** 한 쌍이다.

- 한 세션은 한 시스템만 다룬다. 무기 세션에서 UI를 건드리지 않는다.
- 에러는 **스택트레이스 전문**을 붙여서 준다. 추측 왕복이 가장 비싸다.
- 아키텍처·복잡 시스템·디버깅은 Opus, 보일러플레이트·에디터 툴·데이터 생성은 Sonnet.
- 빌드는 매주 올린다 (D5 · D10 · D14 · D18). 마지막에 몰아서 빌드하면 하루를 잃는다.

## 현재 상태

> 갱신: 2026-09-22. 상세는 `HANDOFF.md` §1.

프로젝트 이름은 임시로 **FP**. 게임 이름이 정해지면 바꾼다. 네임스페이스는 `Demo.*` 유지.

- [~] D1 셋업 — 프로젝트·폴더 구조·문서·gitignore 완료.
      **Player Settings(IL2CPP·ARM64·API24·Landscape·Linear) · Unity Recorder · Main.unity · git init 미완**
- [ ] D2 VARCO 파이프라인 실측
- [ ] D5 코어 루프 + 스토어 첫 업로드

**구조물은 기둥/벽면 2종** [확정 2026-09-22]. 6종 5티어 안은 폐기됐다.
**구멍 성장은 D9로 보류** — 그래서 `GameConfig`에 성장 필드가 일부러 없다.

### 에디터 없이 검증하는 법

`dotnet build`로 컴파일 에러를 먼저 잡는다. Unity 실물 어셈블리
(`C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Data\Managed\UnityEngine\*.dll`)를
참조하는 `netstandard2.1` 프로젝트에 `Assets\_Project\Scripts\**\*.cs`를 링크하면 된다.
순수 수학은 최소 대역을 만들어 콘솔 앱으로 돌린다. **복사본이 아니라 실물을 링크할 것.**
