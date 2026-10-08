# 효과음 주문서 — 외부 VARCO Sound 프로그램용

> [지시 2026-10-08] Unity의 VARCO Sound 에셋은 쓰지 않는다(크레딧 402). **외부 프로그램에서 뽑는다.**
> 뽑은 파일을 아래 이름 그대로 `Assets/_Project/Audio/SFX/`에 넣고
> 유니티 메뉴 **`Demo/16. 효과음 연결 — 파일 이름으로`**를 누르면 자동으로 연결된다.
> 그다음 **`python Tools/check_sfx.py`**로 검증한다.

---

## 파일 이름 규칙 — 반드시 지킬 것

```
SFX_<이름>_01.wav   SFX_<이름>_02.wav   …
```

`<이름>`은 아래 표의 것과 **철자·대소문자까지 똑같아야** 한다. 틀리면 `Demo/16`이
"이름을 못 읽은 파일"로 따로 알려 준다 — 조용히 무시되지는 않는다.

---

## 지금 상태

**12종 전부 임시 음원이 들어가 있다.** `Tools/make_sfx.py`가 numpy로 합성한 것이라
소리는 나지만 거칠다. 외부에서 뽑은 것으로 **덮어쓰면 된다** — 같은 이름으로 넣고
`Demo/16`을 누르면 그만이다.

파괴음 셋(Strike·FloorCollapse·Broken)은 **원래 있던 것**이라 급하지 않다.
다만 `SFX_Strike_03`은 검증에서 걸렸다 — 아래 §3 참조.

---

## 1. 우선순위 높음 — 플레이어가 제일 자주 듣는다

| 이름 | 개수 | 길이 | 순간 | 프롬프트 (영어) |
|---|---|---|---|---|
| `Capture` | 3 | **0.5~0.7s** | 고리가 닫히며 잔해를 잡았다 | A steel net cinching tight around a pile of concrete rubble. A sharp wire tightening snap followed by rubble shifting and settling against the mesh. Satisfying. No music. |
| `Swallow` | 3 | **0.4~0.6s** | 잔해 하나가 구멍에 빨려 들어간다 | A single chunk of concrete falling into a deep pit. A short tumble on the rim, then a distant hollow thud from below. Dry, no reverb tail. No music. |
| `Punch` | 3 | **0.2~0.3s** | 팔을 휘두른다 (맞기 전) | A large hydraulic robot arm swinging fast through the air. A short heavy servo whirr and a low air whoosh, metal joints under load. No impact at the end. No music. |

## 2. 우선순위 중간

| 이름 | 개수 | 길이 | 순간 | 프롬프트 (영어) |
|---|---|---|---|---|
| `HoleLevelUp` | 2 | **1.0~1.3s** | 구멍이 한 단계 커진다 | A pit in the ground widening. A deep earthy rumble of soil and concrete giving way, rising into a short bright tonal swell that feels like a reward. No voice. |
| `QuotaDone` | 2 | **1.2~1.5s** | 할당량을 채웠다 | A short triumphant industrial fanfare. Heavy machinery stamping a completion, a bright metallic chime over a low mechanical thump. Celebratory but brief. No voice. |
| `CableOpen` | 2 | **0.4~0.5s** | 케이블을 펴기 시작한다 | A steel cable spool releasing. A short mechanical clack followed by wire unspooling quickly, metallic and taut. Industrial. No music. |
| `Drop` | 2 | **0.5~0.7s** | 짐을 내려놓는다 | A load of concrete rubble released from a net onto bare ground. A dull collective thud and stones tumbling apart. Short. No music. |

## 3. 우선순위 낮음

| 이름 | 개수 | 길이 | 순간 | 프롬프트 (영어) |
|---|---|---|---|---|
| `CableMiss` | 2 | **0.3s** | 고리를 닫았는데 못 잡았다 | A steel cable loop closing on empty ground. A dull slack snap, a brief scrape of wire on dirt, no tension. Disappointing and short. No music. |
| `Snag` | 2 | **0.3~0.4s** | 끌던 짐이 건물에 걸렸다 | Heavy rubble being dragged catches hard on a concrete edge. A jarring scrape of stone on stone with a metallic cable strain. Short and rough. No music. |
| `Strike` | **1개만** | **1.2~1.4s** | — | 아래 설명 참조 |

### `SFX_Strike_03`을 바꿔야 하는 이유

검증에서 이것만 걸렸다 (`python Tools/check_sfx.py`):

| | Strike_01 | Strike_02 | **Strike_03** | Strike_04 |
|---|---|---|---|---|
| 길이 | 1.44s | 1.25s | **5.61s** | 1.15s |
| RMS | −11.7dB | −12.4dB | **−21.1dB** | −13.2dB |
| 무음 비율 | 18% | 24% | **68%** | 15% |

타격음은 **초당 다섯 번** 울린다. 5.6초짜리가 섞이면 겹쳐 쌓이고, 넷 중 한 번은
눈에 띄게 조용하고 길어서 **박자가 깨진다.** 같은 프롬프트로 하나만 다시 뽑아
1.2~1.4초짜리로 덮어쓰면 된다.

```
A heavy metal robot fist slamming into a solid concrete wall. One single short dry impact,
deep thud with a sharp crack, small bits of concrete crumbling away. No music, no reverb tail.
```

---

## 4. 아직 없는 것 — BGM

배경음악이 **하나도 없다.** `Sfx` 열거형에도 없고 재생 경로도 없다.
효과음보다 분위기를 크게 바꾸므로, 시간이 남으면 여기가 다음 자리다.
(필요하면 재생 구조를 만든다 — 지금은 `GameAudio`가 한 발짜리 효과음만 다룬다.)

---

## 5. 받은 뒤에 할 일

```bash
python Tools/check_sfx.py
```

이 자가 보는 것 — 파일이 제대로 생겼는가 · 거의 무음인가 · 클리핑으로 깨졌는가 ·
앞뒤에 침묵이 길게 붙었는가 · **변형끼리 음량이 고른가** · **변형이 사실상 같은 파일인가**.

마지막 둘이 중요하다. 변형을 여럿 두는 유일한 이유가 "같은 소리의 반복이 기계처럼
들려서"인데, 뽑은 둘이 거의 같으면 그 이유가 사라진다. 실제로 내가 합성할 때
그 실수를 했고 자가 잡았다 (−999 dB).

**이 자가 못 보는 것**: 좋게 들리는가. 그건 듣고 정할 일이다.
