# 사운드 생성 프롬프트 — VARCO Sound 데스크톱용

> [지시 2026-10-08] Unity 플러그인은 402(크레딧 소진). **데스크톱 프로그램으로 뽑는다.**
> 데스크톱은 작동 확인됨 — 같은 날 16:21에 정상 음원 3개를 생성했다 (SFX_SHOPPING_LIST.md 참조).

---

## 받은 뒤에 할 일 — 세 단계

| | 무엇 | 어디에 |
|---|---|---|
| 1 | 파일을 아래 **정확한 이름**으로 저장 | 효과음 `Assets/_Project/Audio/SFX/` · 음악 `Assets/_Project/Audio/BGM/` |
| 2 | 유니티 메뉴 **`Demo/16. 효과음 연결 — 파일 이름으로`** | SoundBank에 자동으로 꽂힌다 |
| 3 | **`python Tools/check_sfx.py`** | 무음·클리핑·앞침묵·변형 중복을 잡는다 |

**지금은 12종 전부 임시 합성음이 들어가 있다.** 같은 이름으로 덮어쓰면 그대로 교체된다.
음악은 이름이 고정이라 덮어쓰기만 하면 된다 (`BGM_Field.wav` · `BGM_Demolition.wav`).

---

## 이 게임이 소리를 내는 **모든** 순간

코드의 `Demo.Data.Sfx` 열거형이 전부다. 여기 없는 순간에는 소리가 안 난다.

| # | 이름 | 언제 | 빈도 | 2D/3D |
|---|---|---|---|---|
| 1 | `Punch` | 팔을 휘두르는 순간 (맞기 전) | 초당 5회 | 3D |
| 2 | `Strike` | 주먹이 벽에 닿는 순간 | 초당 5회 | 3D |
| 3 | `FloorCollapse` | 한 층이 통째로 쏟아진다 | 층마다 1회 | 3D |
| 4 | `Broken` | 건물이 완파된다 | 건물마다 1회 | 3D |
| 5 | `CableOpen` | 케이블을 펴기 시작한다 | 포획 시도마다 | 3D |
| 6 | `CableMiss` | 고리를 닫았는데 못 잡았다 | 실패마다 | 3D |
| 7 | `Capture` | 고리가 닫히며 잔해를 잡았다 | 성공마다 | 3D |
| 8 | `Snag` | 끌던 짐이 건물·토대에 걸렸다 | 가끔 | 3D |
| 9 | `Drop` | 짐을 내려놓는다 (Q) | 가끔 | 3D |
| 10 | `Swallow` | 잔해 하나가 구멍에 빨려 든다 | 초당 여러 번 | 3D |
| 11 | `HoleLevelUp` | 구멍이 한 단계 커진다 | 판당 4회 | **2D** |
| 12 | `QuotaDone` | 할당량을 채웠다 | 판당 1회 | **2D** |
| M1 | `BGM_Field` | 필드에서 계속 | 루프 | 2D |
| M2 | `BGM_Demolition` | 파괴 화면에서 계속 | 루프 | 2D |

**2D는 거리와 무관하게 같은 크기로 들린다.** 단계 상승과 할당량 달성은 화면 전체의
사건이라 그렇게 해 뒀다 — 3D로 두면 플레이어가 구멍에서 멀 때 제일 중요한 소리가 안 들린다.

---

## 모든 프롬프트에 공통으로 넣을 것

```
No music. No voice. No reverb tail. Mono. Dry close-mic recording.
Abandoned concrete demolition site. Heavy industrial machinery.
```

**왜 reverb를 빼는가.** 이 게임은 소리가 **겹쳐서** 난다 — 타격음이 초당 다섯 번이고
그 위에 삼킴·포획이 포개진다. 잔향이 붙은 음원끼리 겹치면 금방 진흙이 된다.
공간감은 유니티가 거리 감쇠로 준다.

---

# 1. 효과음

## 1-1 파괴 — 제일 자주 들린다

### `Punch` ×3 · **0.2~0.3초**
```
A large hydraulic robot arm swinging fast through empty air. A short heavy servo whirr
and a low air whoosh, metal joints creaking under load. The swing ENDS IN NOTHING -
there must be no impact, no hit, no crash at the end. Very short and dry.
No music, no voice, no reverb tail.
```
> 충돌음을 넣으면 `Strike`와 겹쳐 두 번 때린 것처럼 들린다. "끝에 아무것도 없다"를 꼭 넣을 것.

### `Strike` ×4 · **1.2~1.4초**
```
A heavy metal robot fist slamming into a solid concrete wall. One single short dry impact:
a deep thud with a sharp crack on top, then small bits of concrete crumbling away for less
than a second. Punchy and immediate. No music, no reverb tail.
```
> **기존 `SFX_Strike_03`을 반드시 교체할 것.** 그것만 5.61초·RMS −21.1dB·무음 68%로
> 나머지 셋(1.15~1.44초·−11.7~−13.2dB)과 동떨어져 있다. 초당 다섯 번 울리는 소리라
> 넷 중 하나가 길고 조용하면 박자가 깨진다.

### `FloorCollapse` ×2 · **3~5초**
```
An entire concrete floor of a building giving way at once. A deep heavy crunch, then a
short avalanche of falling concrete slabs and rubble tumbling down, settling into silence.
Powerful and low, felt in the chest. No music, no voice.
```

### `Broken` ×2 · **8~10초**
```
A derelict concrete building finally collapsing into a pile of rubble. A huge low boom,
then a long crumbling roar of falling debris and twisting steel, fading into settling dust.
Heavy and final - this is the biggest sound in the game. No music, no voice.
```

## 1-2 케이블과 견인

### `CableOpen` ×2 · **0.4~0.5초**
```
A steel cable spool releasing. A short mechanical clack of a latch, then wire unspooling
quickly - metallic, taut, slightly ringing. Industrial and mechanical, not electronic.
No music, no reverb tail.
```

### `CableMiss` ×2 · **0.3초**
```
A steel cable loop closing on empty ground. A dull slack snap with no tension behind it,
and a brief scrape of wire dragging across dirt. It must sound DISAPPOINTING and limp -
the opposite of a satisfying catch. Very short. No music, no reverb tail.
```
> 이것과 `Capture`는 **짝으로 들린다.** 플레이어는 고리를 닫는 순간 손끝을 보고 있어서,
> 성공/실패를 소리로 가른다. 둘이 확실히 달라야 한다 — 하나는 맥빠지고 하나는 꽉 찬다.

### `Capture` ×3 · **0.5~0.7초**
```
A steel net cinching tight around a pile of concrete rubble. A sharp wire tightening snap,
then rubble shifting and settling against the mesh with a gritty crunch. It must feel
SATISFYING and solid - this is the reward sound of the game's core action.
No music, no reverb tail.
```

### `Snag` ×2 · **0.3~0.4초**
```
Heavy rubble being dragged catches hard on a concrete edge. A jarring scrape of stone on
stone with a metallic cable strain groaning under it. Rough, abrupt, slightly unpleasant -
it should read as "something went wrong". Short. No music.
```

### `Drop` ×2 · **0.5~0.7초**
```
A load of concrete rubble released from a net onto bare ground. A dull collective thud as
the whole pile lands at once, then stones tumbling apart and settling. Unceremonious.
Short. No music, no reverb tail.
```

## 1-3 구멍

### `Swallow` ×3 · **0.4~0.6초**
```
A single chunk of concrete falling into a deep pit. A short tumble as it scrapes over the
rim, a brief silent drop, then a distant hollow thud from far below. Dry and compact.
No music, no reverb tail.
```
> 초당 여러 개가 겹쳐 울린다. **길면 진흙이 된다** — 0.6초를 넘기지 말 것.

### `HoleLevelUp` ×2 · **1.0~1.3초** · 2D
```
A pit in the ground widening. A deep earthy rumble of soil and concrete giving way,
rising into a short bright tonal swell that feels like a REWARD - an upward movement,
warm and encouraging, but made of industrial materials rather than a musical chime.
No voice, no melody.
```

### `QuotaDone` ×2 · **1.2~1.5초** · 2D
```
A short triumphant industrial fanfare. Heavy machinery stamping down to mark a completion,
with a bright metallic chime ringing over a low mechanical thump. Celebratory but brief
and industrial - a factory whistle feeling, not an orchestral fanfare. No voice.
```

---

# 2. 배경음악

**두 곡은 같은 조·같은 BPM이어야 한다.** 화면이 바뀌면 `MusicPlayer`가 **겹쳐 넘긴다**
(crossfade 1.2초). 조나 박자가 다르면 그 1.2초 동안 어긋난 소리가 난다.

| | 값 |
|---|---|
| BPM | **84** (느린 작업 속도) |
| 조 | **A 단조** (A minor) |
| 길이 | 각 **60~90초** · 루프 가능해야 함 |
| 음량 | 효과음보다 **한참 작게**. 타격음이 초당 다섯 번이라 음악이 크면 다 묻힌다 |

### `BGM_Field` — 필드
```
A slow, sparse, atmospheric industrial loop at 84 BPM in A minor. Low sustained synth pad
drones, a few isolated plucked metallic notes with long gaps between them, a very quiet
deep pulse every two bars. Desolate and patient, like working alone on an abandoned
industrial site at dusk. Lots of empty space - the track must stay out of the way and
NEVER build to a climax. Seamless loop. No vocals, no drums kit, no melody hook.
```
> **비어 있어야 한다.** 플레이어는 이 곡 위에서 초당 다섯 번 벽을 친다.
> 곡이 바쁘면 타격의 리듬이 음악과 싸운다.

### `BGM_Demolition` — 파괴 화면
```
The same slow industrial loop at 84 BPM in A minor, same key and same harmony as the field
track, but now with PERCUSSION: a heavy low kick on beats 1 and 3, metallic factory clanks
on the off-beats, and a deeper sustained bass drone underneath. More driving and physical,
but still restrained - no build, no climax, no melody hook. It should feel like the same
piece of music with the machinery switched on. Seamless loop. No vocals.
```

### 선택 — 있으면 좋은 것
| 이름 | 길이 | 프롬프트 |
|---|---|---|
| `BGM_Result` | 10~15초 | `A short industrial outro sting in A minor at 84 BPM. A final heavy machine stamp, a slow metallic ring decaying, a sense of work completed. Non-looping, ends cleanly. No vocals.` |

> `BGM_Result`를 쓰려면 재생 코드를 조금 늘려야 한다 (`MusicPlayer`는 지금 루프 두 개만 안다).
> 필요하면 말해 주면 넣는다.

---

# 3. 아직 소리가 없는 순간 — 넣을지 정할 것

코드에 호출 자리가 **없다.** 넣기로 하면 `Sfx` 열거형과 호출부를 같이 늘려야 한다.

| 후보 | 왜 있으면 좋은가 | 프롬프트 초안 |
|---|---|---|
| `Footstep` ×4 | 로봇이 미끄러지듯 움직인다. 발소리가 무게를 준다 | `A heavy metal robot foot stamping onto gravel and concrete dust. One single short dull clank with grit underneath. Very short, dry. No music.` |
| `TowLoop` | 짐을 끌 때 계속 나는 소리. 지금은 무음 | `A seamless loop of heavy concrete rubble being dragged across a gravel yard inside a steel net. Continuous low grinding scrape with metallic cable creaks. Steady, no variation, loops without a seam. No music.` |
| `HoleAmbient` | 구멍 근처의 공기. 긴장을 준다 | `A seamless loop of a low hollow wind moaning out of a deep pit. Dark, empty, slightly unsettling. Very quiet and steady. No music, no voice.` |
| `Warning` | 할당량 시간이 얼마 안 남았을 때 | `A short dry industrial warning beep, two pulses, muted and mechanical. Not alarming, just a nudge. Very short. No music.` |

**내 의견**: `TowLoop`이 제일 크다. 짐을 끄는 것이 이 게임의 절반인데 지금 **완전히 무음**이다.
그다음이 `Footstep`. 나머지 둘은 시간이 남으면.

---

## 품질 기준 — 받은 뒤 `check_sfx.py`가 보는 것

| 항목 | 기준 |
|---|---|
| 피크 | **−2.0 dBFS** 근처 (기존 음원이 전부 그렇다) |
| 앞 침묵 | **0.15초 미만**. 길면 반응이 늦은 느낌이 된다 |
| 무음 비율 | 90% 미만 |
| 클리핑 | 8샘플 미만 |
| 변형끼리 | RMS 차 **6dB 이내**, 그리고 **서로 달라야** 한다 (−30dB보다 닮으면 중복으로 잡힌다) |

마지막 항목이 중요하다. 변형을 여럿 두는 유일한 이유가 "같은 소리의 반복이 기계처럼
들려서"인데, 뽑은 둘이 거의 같으면 그 이유가 사라진다. 내가 합성할 때 실제로 그 실수를
했고 자가 −999dB로 잡았다.

**이 자가 못 보는 것**: 좋게 들리는가. 그건 듣고 정할 일이다.
