# 셋업 · D1 체크리스트

순서대로 진행한다. **스토어 관련 항목이 코드보다 먼저다** — 승인 대기가 일정을 잡아먹기 때문이다.

---

## 0. 가장 먼저 — 강사 확인 3항목

답에 따라 전체 일정 골격이 달라진다. 코드 쓰기 전에 물어본다.

- [ ] **NCAI가 구글 플레이 조직(organization) 계정을 제공하는가?**
      조직 계정은 "테스터 20명 × 14일" 요건에서 면제된다. 제공되면 아래 §1의 압박이 사라진다.
- [ ] **"런칭"이 프로덕션 출시인가, 내부 테스트 링크로 인정되는가?**
      내부 테스트(Internal testing) 트랙은 20명·14일 요건 없이 즉시 링크가 나온다.
- [ ] **모바일이 필수인가, itch.io WebGL만으로 대체 가능한가?**
      WebGL만이면 Android 빌드·서명·스토어 대응 2일이 회수된다.

---

## 1. 구글 플레이 (D1 오전)

> **전제**: 2023년 11월 이후 생성된 **개인** 개발자 계정은 프로덕션 출시 전
> **테스터 20명이 14일 연속 비공개 테스트**에 참여해야 한다.
> 이것이 사실이면 **D5에 첫 AAB가 스토어에 올라가야 한다.**
> 최신 정책은 Play Console에서 직접 확인할 것.

- [ ] 개발자 등록 신청 ($25 일회성) — **오늘 넣는다.** 승인 대기가 며칠 걸릴 수 있다
- [ ] 앱 등록 (패키지명 확정: `com.<본인>.<프로젝트명>`)
- [ ] **테스터 20명 확보** — 같은 과정 수강생으로 상호 테스트 그룹을 만든다.
      서로의 게임을 등록해주면 전원이 요건을 채운다. 기술 문제가 아니라 사람 문제이므로 첫날에 해결한다
- [ ] 스토어 등록정보 초안 (제목, 짧은 설명, 아이콘은 나중)
- [ ] 개인정보처리방침 URL 준비 (GitHub Pages나 Notion 공개 페이지면 충분)
- [ ] 업로드 키 생성 및 **안전한 위치에 백업** (분실하면 앱 갱신 불가)

---

## 2. Unity 프로젝트

- [ ] Unity 6 (6000.x) 설치 — 모듈에 **Android Build Support** (JDK/SDK/NDK 포함), **WebGL Build Support** 체크
- [ ] 새 프로젝트: **Universal 3D** 템플릿
- [ ] `Project Settings → Player`
  - Company / Product Name, 패키지명 (Play Console과 일치)
  - Android: **IL2CPP · ARM64 · Minimum API 24 · Landscape 고정**
  - Color Space: **Linear**
- [ ] URP 에셋을 모바일 프리셋으로: 그림자 끄기(또는 최소), HDR 끄기, MSAA 2x 이하, 추가 광원 최소
- [ ] `Project Settings → Quality` — Android/WebGL 프로필 분리

### 패키지 (Package Manager)

- [ ] Input System (New)
- [ ] Cinemachine (선택 — 카메라를 직접 짜면 불필요)
- [ ] Unity Recorder ← **트레일러 클립용. D8부터 쓴다**
- [ ] TextMeshPro

### 폴더 생성

`CLAUDE.md`의 폴더 구조대로 미리 만들어둔다. 빈 폴더는 Git이 추적하지 않으므로 `.gitkeep`을 넣는다.

---

## 3. Git

- [ ] 저장소 생성 후 **Unity용 `.gitignore`** 적용 (`Library/`, `Temp/`, `Build/`, `Logs/`, `UserSettings/`)
- [ ] **Git LFS** 설치 및 트래킹 설정

```
*.fbx      filter=lfs diff=lfs merge=lfs -text
*.png      filter=lfs diff=lfs merge=lfs -text
*.jpg      filter=lfs diff=lfs merge=lfs -text
*.wav      filter=lfs diff=lfs merge=lfs -text
*.mp3      filter=lfs diff=lfs merge=lfs -text
*.blend    filter=lfs diff=lfs merge=lfs -text
```

- [ ] `Project Settings → Editor` → Asset Serialization을 **Force Text**, Version Control을 **Visible Meta Files**로
- [ ] 첫 커밋

---

## 4. 문서 배치

- [ ] `CLAUDE.md`를 프로젝트 루트에 둔다
- [ ] `docs/GAME_DESIGN.md`, `docs/TECH_SPEC.md`, `docs/SETUP.md` 배치
- [ ] Claude Code를 프로젝트 폴더에서 열고, 첫 지시로 `CLAUDE.md`와 `docs/`를 읽게 한다

---

## 5. Blender + MCP (D1 오후 ~ D2)

- [ ] Blender 설치
- [ ] Blender MCP 애드온 + 서버 연결 확인 — **연결만 먼저 확인한다.** 여기서 막히면 D2 계획이 바뀐다
- [ ] `Tools/blender/` 폴더 생성

---

## 6. D2 실측 — 이날 뽑아야 할 숫자

D2는 만드는 날이 아니라 **재는 날**이다. 아래 숫자가 나오면 §5 에셋 예산이 확정된다.

- [ ] 구조물 1종을 **3단계(온전/파손/잔해) 전부** 생성 → Unity에서 실제로 부숴본다
- [ ] **1종당 생성 시간** (VARCO 프롬프트 → 3단계 완성)
- [ ] **1종당 용량(MB)** — 텍스처 압축 적용 후
- [ ] **모바일 실기에서 동시에 몇 개가 60fps로 돌아가는가**
- [ ] VARCO Sound 1종 생성 → Unity 반입 시간
- [ ] Claude 한도 실측 (터미널에서 `claude` 실행 후 `/usage`)

**추정하지 말고 잰다.** 여기서 정한 숫자는 D15까지 바꾸지 않는다.

---

## 7. 주요 마일스톤

| 일자 | 내용 |
|---|---|
| D2 | 실측 완료 → 볼륨 확정 |
| **D5** | 코어 루프 1바퀴 + **첫 AAB 테스트 트랙 업로드** ← 14일 시계 시작 |
| D10 | 5분 완주 가능 · 빌드 갱신 |
| D14 | 볼륨 투입 완료 · 빌드 갱신 |
| **D15** | **기능 동결.** 이후 추가 금지 |
| D18 | WebGL 빌드 · itch.io 업로드 |
| D19 | 프로덕션 승격 신청 · 트레일러 편집 |
| D20 | 유튜브 업로드 · 발표 |

---

## 8. 잊지 말 것

- **트레일러 클립은 D8부터 매일 모은다.** 연출 레이어가 붙는 시점부터 Unity Recorder를 켜두고 잘 나온 장면을 저장한다. D19에 처음 촬영하면 늦는다
- **빌드는 매주 올린다.** 마지막에 몰아서 빌드하면 그날 하루를 통째로 잃는다
- **D15 동결 절대 준수.** 미완성 기능은 추가가 아니라 제거한다. 반쯤 된 기능이 발표에서 가장 위험하다
