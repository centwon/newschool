# NewSchool — OAuth 검증 데모 영상 촬영 대본

| 항목 | 값 |
|---|---|
| 대상 | Google OAuth 민감 스코프 검증 (프로젝트 `newschool-488701`) |
| 스코프 | `calendar.calendarlist.readonly` · `calendar.calendars` · `calendar.events` |
| 목표 길이 | 4~5분 |
| 업로드 | YouTube, **일부공개(Unlisted)** |
| 관련 문서 | [oauth-calendar-verification.md](oauth-calendar-verification.md) — 스코프 결정 근거·목적 설명 문구 |

---

## 1. Google의 필수 요구사항 4가지

[공식 문서](https://developers.google.com/identity/protocols/oauth2/production-readiness/sensitive-scope-verification) 원문 기준입니다.

| # | 요구사항 | 이 대본에서 다루는 곳 |
|---|---|---|
| 1 | OAuth 동의 절차를 **영어 화면으로** 보여줄 것 (`Show the OAuth grant process that users will experience, in English`) | 사전 준비 A, 장면 ② |
| 2 | 동의 화면에 **앱 이름**이 올바르게 표시될 것 | 장면 ② |
| 3 | **주소창에 OAuth client ID**가 보일 것 | 장면 ② — 가장 자주 반려되는 항목 |
| 4 | 요청한 **민감 스코프 각각의 기능**을 시연할 것 | 장면 ③④⑤⑥ |

추가로 콘솔 안내문이 요구하는 것:

- **"인증되지 않은 앱" 경고 화면을 편집으로 잘라내지 말 것** — 미검증 상태에서는 정상 동작이며, 영상에 나와야 합니다.
- **프로젝트에 등록된 모든 OAuth 클라이언트가 영상에 포함될 것** — 아래 사전 준비 C 참조.

### 영어에 대한 오해 정리

"in English"가 걸리는 대상은 **OAuth 동의 절차 화면**입니다. 나레이션이나 자막이 아닙니다.

- ✅ **앱 UI는 한국어로 둬도 됩니다.** NewSchool은 한국 교사용 프로그램이니 오히려 자연스럽습니다.
- ✅ **음성 나레이션은 불필요합니다.**
- ⚠️ **동의 화면은 반드시 영어여야 합니다.**
- 💡 **영어 캡션은 의무는 아니지만 권장합니다** — 요구사항 4번에서 리뷰어가 한국어 UI를 보며 무슨 동작인지 알아야 하기 때문입니다. 이 문서의 캡션 문구를 화면 위에 얹으세요.

---

## 2. 사전 준비

### A. 동의 화면을 영어로 만들기 ★필수

테스트용 Google 계정의 언어 설정을 English로 바꿉니다.

1. myaccount.google.com → 개인 정보 → 웹용 기본 설정 → 언어 → **English (United States)**를 맨 위로
2. 로그아웃 후 다시 로그인해 동의 화면이 영어로 뜨는지 확인

앱 코드를 고쳐 인증 URL에 `&hl=en`을 붙이는 방법도 있지만, 데모를 위해 배포 코드를 건드릴 필요는 없습니다.

### B. 테스트 계정 준비

- [ ] 실제 학생 정보가 없는 **테스트 전용 Google 계정** 사용
- [ ] 콘솔 **대상(Audience)**이 "테스트"면 이 계정을 **테스트 사용자로 등록**
- [ ] 그 계정 캘린더에 **일정 2~3건 미리 생성** — 장면 ⑥(Pull) 시연에 필요
- [ ] 앱에서 **기존 Google 연동 해제** (달력 페이지 → 설정 버튼 → 캘린더 설정 → 연동 해제) — 초기 상태에서 시작해야 함

### C. OAuth 클라이언트 정리 ★확인 필요

콘솔 **클라이언트** 메뉴를 여세요.

- 데스크톱 클라이언트 1개뿐 → 영상 하나로 끝
- 여러 개 → **각각 시연하거나, 안 쓰는 것을 삭제**. 삭제가 훨씬 간단합니다.

### D. 빌드 — 릴리스 아닌 로컬 빌드로

새 스코프 3종은 아직 어떤 사용자에게도 배포되지 않았습니다. `bin\x64\Debug\...\NewSchool.exe`를 그대로 쓰면 그것이 Google이 말하는 "스테이징 환경"입니다.

> ⚠️ **검증 통과 전까지 앱 설치 파일을 새로 배포하지 마세요.** (홈페이지 `main` push는 무관합니다 — 앱 바이너리는 별도 릴리스입니다.)

### E. 화면·데이터

- [ ] 1080p 이상, 커서 강조 켜기
- [ ] 학생 실명·연락처가 보이지 않도록 데모 데이터로 교체
- [ ] 브라우저 탭·북마크에 사생활 노출 없는지 확인
- [ ] 알림 팝업 끄기 (녹화 중 메신저 알림 등)

### F. 녹화 도구

앱과 브라우저를 계속 오가는 촬영이라 **여러 프로그램에 걸쳐 전체 화면을 녹화**할 수 있어야 합니다.

- ❌ **Xbox Game Bar (Win+G) 는 쓰지 마세요.** 단일 앱 창만 녹화해서 앱↔브라우저 전환을 못 담습니다.
- ✅ **Clipchamp** (Windows 11 내장) — 화면 녹화 + 자막 삽입 + 1080p 내보내기를 한 도구로 끝낼 수 있어 이 작업에 가장 잘 맞습니다.
- ✅ **Snipping Tool** (`Win+Shift+S` → 화면 녹화) — 녹화만 간단히. 자막은 별도 편집 필요.
- ✅ **OBS Studio** — 무료. 통제력이 가장 좋지만 설정 학습이 필요합니다.

> **주소창 확대는 후반 편집이 아니라 촬영 중에 하세요.** 녹화 후 확대하면 화질이 뭉개져 client ID가 안 읽힙니다. 브라우저에서 `Ctrl` + `+` 로 확대한 상태로 찍으세요.

---

## 2-1. 화면 지도 — 어디를 찍는가

Google 관련 장면은 **거의 전부 캘린더 설정 다이얼로그 하나 안**에 있습니다.

**달력 페이지 → 설정 버튼 → 캘린더 설정 다이얼로그**

다이얼로그에는 Expander 4개가 접힌 채로 있습니다. 그중 3개를 순서대로 펼치면 됩니다 — 펼치는 동작 자체가 자연스러운 장면 전환이 됩니다.

| 펼칠 섹션 | 그 안에서 할 것 | 관련 스코프 |
|---|---|---|
| **Google 캘린더 연동** | 토글 `사용` → **[계정 연동]** → 브라우저 OAuth → 복귀 후 캘린더 목록 → **[캘린더 선택 저장]** | 요구사항 1~3 + `calendarlist.readonly` |
| **동기화** | **[지금 동기화]** | `events` (Pull) |
| **학사일정 동기화** | **[학사일정 동기화]** | `calendars` (두 번째 캘린더) |

다이얼로그 **밖**에서 찍을 것은 셋뿐입니다.

- **달력 페이지 본체** — 일정 추가·수정·삭제 (`events` Push)
- **브라우저 `calendar.google.com`** — 반영 확인 + Pull용 일정 생성
- **브라우저 `newschool.centwon.com/Privacy.html`** — 방침 노출

---

## 3. 촬영 대본

**끊지 말고 한 번에** 녹화하세요. 캡션은 후반 작업으로 얹어도 됩니다.

### 장면 ① 앱 소개 — 0:00~0:20

| 화면 | 영어 캡션 |
|---|---|
| 앱 실행, 제목 표시줄에 **NewSchool** 보이는 상태 | `NewSchool - a Windows desktop app for K-12 teachers in South Korea` |
| **달력** 페이지를 잠깐 보여줌 | `It manages class schedules, homeroom tasks, and school events.` |
| 달력 페이지의 **설정 버튼** 클릭 → 캘린더 설정 다이얼로그 | `The app is not yet connected to any Google account.` |

### 장면 ② OAuth 동의 절차 — 0:20~1:20 ★가장 중요

| 화면 | 영어 캡션 |
|---|---|
| **"Google 캘린더 연동"** 섹션 펼치기 → 토글 `사용` → **[계정 연동]** 클릭 | `The teacher starts the Google sign-in from the calendar settings.` |
| 브라우저가 열림 — **주소창을 확대해 `client_id=...`가 읽히도록 3초 이상 정지** | `The OAuth client ID is visible in the address bar.` |
| 계정 선택 화면 | `Signing in with a test account.` |
| **"Google hasn't verified this app"** 경고 → Advanced → Go to NewSchool (unsafe) | `The app is not verified yet, so this warning is expected.` |
| 동의 화면 — **앱 이름 NewSchool 확인**, 권한 3개를 마우스로 하나씩 짚음 | `The consent screen shows the app name and the three requested scopes.` |
| Allow 클릭 | `Granting all three permissions.` |
| `http://127.0.0.1:.../callback/` 의 "인증 완료!" 페이지 | `The app receives the authorization code on a local loopback address.` |
| 앱으로 복귀, **"✅ Google 계정 연동됨"** | `The account is now connected.` |

> **주소창 확대를 잊지 마세요.** client ID 미노출이 데모 영상 반려 사유 1위입니다. 확대가 어려우면 브라우저 확대(Ctrl +)로 주소창 글씨를 키운 뒤 녹화하세요.

### 장면 ③ `calendar.calendarlist.readonly` — 1:20~1:50

| 화면 | 영어 캡션 |
|---|---|
| 같은 섹션 아래 **"동기화할 캘린더"** 목록이 채워짐 | `SCOPE 1: calendar.calendarlist.readonly` |
| 목록을 스크롤 | `The app lists the teacher's calendars so they can choose sync targets.` |
| 체크박스로 대상 선택 → **[캘린더 선택 저장]** | `Only the selected calendars will be synchronized.` |

### 장면 ④ `calendar.calendars` — 1:50~2:20

| 화면 | 영어 캡션 |
|---|---|
| 브라우저에서 **calendar.google.com** 열기 | `SCOPE 2: calendar.calendars` |
| 왼쪽 목록에 **`{학교명}` 캘린더가 새로 생긴 것**을 가리킴 | `The app created a dedicated secondary calendar for the school.` |
| 기존 개인 캘린더와 나란히 보이는 상태 | `School events stay separate from the teacher's personal calendar.` |

### 장면 ⑤ `calendar.events` — Push (앱 → Google) — 2:20~3:20

| 화면 | 영어 캡션 |
|---|---|
| 다이얼로그 **닫고** 달력 페이지에서 **새 일정 추가** (제목·시간·장소 입력) | `SCOPE 3: calendar.events - creating an event in the app` |
| 저장 | `events.insert - the event is pushed to Google Calendar.` |
| calendar.google.com 새로고침 → 일정이 나타남 | `The same event now appears in Google Calendar.` |
| 앱에서 **제목 수정** → Google에서 갱신 확인 | `events.update - edits are propagated.` |
| 앱에서 **삭제** → Google에서도 사라짐 | `events.delete - deleting in the app removes it from Google Calendar.` |
| — | `This is why a read-only scope cannot support the app.` |

### 장면 ⑥ `calendar.events` — Pull (Google → 앱) — 3:20~3:50

| 화면 | 영어 캡션 |
|---|---|
| **calendar.google.com에서 일정 새로 생성** (예: Staff meeting) | `Now creating an event directly in Google Calendar.` |
| 앱 → 설정 버튼 → **"동기화"** 섹션 펼치기 → **[지금 동기화]** | `events.list with syncToken pulls changes back into the app.` |
| 앱 달력에 해당 일정이 나타남 | `Two-way synchronization is the core feature of the app.` |

### 장면 ⑦ 학사일정 일괄 등록 (선택) — 3:50~4:20

넣으면 `calendar.calendars`의 두 번째 캘린더 용도가 명확해집니다. 시간이 빠듯하면 생략 가능합니다.

| 화면 | 영어 캡션 |
|---|---|
| 같은 다이얼로그의 **"학사일정 동기화"** 섹션 펼치기 → **[학사일정 동기화]** | `Official academic events come from the Korean Ministry of Education's open API.` |
| calendar.google.com에서 `{학교명} 학사일정` 캘린더가 종일 일정으로 채워진 것 확인 | `They are published to a separate app-created calendar.` |

### 장면 ⑧ 데이터 삭제 경로 — 4:20~4:50

리뷰어가 특히 확인하고 싶어하는 부분입니다. **생략하지 마세요.**

| 화면 | 영어 캡션 |
|---|---|
| **"Google 캘린더 연동"** 섹션의 **[연동 해제]** | `Disconnecting revokes the token with Google...` |
| 상태가 "연동되지 않음"으로 바뀜 | `...and immediately deletes the credentials stored on the PC.` |
| 브라우저에서 **myaccount.google.com/permissions** | `The teacher can also revoke access from their Google Account settings.` |

### 장면 ⑨ 개인정보처리방침 — 4:50~5:00

| 화면 | 영어 캡션 |
|---|---|
| **newschool.centwon.com/Privacy.html** 열기 | `The privacy policy is published at newschool.centwon.com.` |
| 3항(요청하는 Google 권한 범위) 스크롤 | `It lists exactly the three scopes requested here.` |

---

## 4. 촬영 후 최종 확인

- [ ] 동의 화면이 **영어**로 나왔는가 ★
- [ ] 주소창의 **`client_id`가 읽히는가** ★
- [ ] 동의 화면에 **앱 이름 NewSchool**이 정확히 표시됐는가
- [ ] **"인증되지 않은 앱" 경고 화면을 잘라내지 않았는가**
- [ ] **스코프 3종 각각의 기능**이 전부 나왔는가 (장면 ③④⑤⑥)
- [ ] 프로젝트의 **모든 OAuth 클라이언트**가 영상에 포함됐는가
- [ ] 학생 실명·연락처 등 **개인정보가 화면에 노출되지 않았는가**
- [ ] 편집으로 끊긴 곳 없이 **연속된 흐름**인가

## 5. 업로드

1. YouTube Studio 업로드
2. 공개 상태 → **일부공개(Unlisted)**
   - ❌ **비공개(Private)로 두면 심사관이 볼 수 없어 반려됩니다.**
3. 링크를 콘솔 **데모 동영상: 범위 사용 방식**의 `YouTube 링크` 란에 입력
4. 시크릿 창에서 링크를 열어 **로그인 없이 재생되는지** 확인
