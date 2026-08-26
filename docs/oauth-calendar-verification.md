# NewSchool — Google Calendar API OAuth 검증 준비 자료

| 항목 | 값 |
|---|---|
| 앱 이름 | NewSchool (교사용 학사 관리 데스크톱 앱, Windows / WinUI 3) |
| Google Cloud 프로젝트 | `newschool-488701` |
| 홈페이지 | https://newschool.centwon.com |
| 개인정보처리방침 | https://newschool.centwon.com/Privacy.html |
| OAuth 클라이언트 유형 | 데스크톱 앱 (Loopback redirect `http://127.0.0.1:{port}/callback/` + PKCE) |
| 작성일 | 2026-08-13 |
| 조사 기준 | `main` 브랜치, 커밋 `dd2fb2a` |

> ### ⚠ 이 문서는 **조사 시점(2026-08-13)의 기록**입니다 — 아래 권장사항은 이미 반영됐습니다
>
> 본문이 "…요청합니다 / …하지 않습니다" 처럼 현재형으로 적혀 있지만, 그것은 **조사 당시**의 코드
> 상태입니다. 지금 코드는 다음과 같습니다(2026-08-26 확인).
>
> | 본문이 지적한 것 | 현재 상태 |
> |---|---|
> | 전체 권한 `auth/calendar` 스코프 1개만 요청 (1-2, 2장) | ✅ **세분화 3종으로 교체 완료** — `calendarlist.readonly` + `calendars` + `events` (`GoogleAuthService.cs` 의 `Scope` 상수) |
> | 부여된 스코프를 전혀 확인하지 않음 (3장) | ✅ **부분 동의 검사 구현** — `RequiredScopes` 와 대조해 빠진 스코프를 찾고, `DescribeScope` 로 무엇이 안 되는지 알린다 |
> | `calendar.app.created` 채택 여부 | ❌ **미채택으로 결론** — 로컬 "개인" 캘린더가 사용자 primary 에 매핑되므로 동작하지 않는다(재논의 대상 아님) |
>
> 개인정보처리방침도 이 결정에 맞춰 갱신돼 있습니다. **남은 일은 Google Cloud Console 등록·데모
> 영상 촬영·심사 제출뿐입니다.**

---

## 1. 코드베이스 전수 조사 결과

Calendar 기능은 **이미 구현되어 출시된 상태**입니다(설계 단계 아님). 따라서 지시서의 2번(계획 단계 추정)은 해당 사항이 없고, 실제 호출 코드에서 스코프를 역산했습니다.

### 1-1. 관련 파일

| 파일 | 역할 |
|---|---|
| [Google/GoogleAuthService.cs](../Google/GoogleAuthService.cs) | OAuth 2.0 (PKCE + loopback), 토큰 DPAPI 암호화 저장/갱신/revoke |
| [Google/GoogleCalendarApiClient.cs](../Google/GoogleCalendarApiClient.cs) | Calendar REST v3 호출 (HttpClient + System.Text.Json) |
| [Google/GoogleSyncService.cs](../Google/GoogleSyncService.cs) | 양방향 동기화, 증분 동기화(syncToken), 학사일정 재조정 |
| [Google/GoogleCalendarModels.cs](../Google/GoogleCalendarModels.cs) | DTO — **실제로 읽고 쓰는 필드 범위**를 확정하는 근거 |
| [Dialogs/CalendarSettingsDialog.xaml.cs](../Dialogs/CalendarSettingsDialog.xaml.cs) | 연동 UI, 캘린더 매핑, 수동 동기화 |
| [App.xaml.cs](../App.xaml.cs) | 앱 시작 시 + 주기적(기본 15분) 백그라운드 동기화 |
| [Scheduler/UnifiedItemDialog.xaml.cs](../Scheduler/UnifiedItemDialog.xaml.cs) | 일정 저장 직후 즉시 Push |

### 1-2. 현재 요청 중인 스코프

```csharp
// Google/GoogleAuthService.cs:26
private const string Scope = "https://www.googleapis.com/auth/calendar";
```

**전체 권한(full) 스코프 1개만** 요청합니다. `openid` / `userinfo.email` / `userinfo.profile`은 **요청하지 않습니다** ([GoogleAuthService.cs:101-109](../Google/GoogleAuthService.cs#L101)의 인증 URL 구성에 `Scope` 상수 하나만 들어감). → 이 사실이 3장의 개인정보처리방침 수정 사유입니다.

### 1-3. 실제로 호출하는 API 전수

| Calendar API 메서드 | HTTP | 코드 위치 | 읽기/쓰기 |
|---|---|---|---|
| `calendarList.list` | `GET /users/me/calendarList` | [GoogleCalendarApiClient.cs:36-60](../Google/GoogleCalendarApiClient.cs#L36) | 읽기 (**사용자의 전체 캘린더 목록**) |
| `calendars.insert` | `POST /calendars` | [GoogleCalendarApiClient.cs:63-82](../Google/GoogleCalendarApiClient.cs#L63) | **쓰기 (보조 캘린더 생성)** |
| `events.list` | `GET /calendars/{id}/events` | [GoogleCalendarApiClient.cs:89-125](../Google/GoogleCalendarApiClient.cs#L89) | 읽기 (`singleEvents=true`, `syncToken` 증분) |
| `events.insert` | `POST /calendars/{id}/events` | [GoogleCalendarApiClient.cs:128-141](../Google/GoogleCalendarApiClient.cs#L128) | **쓰기** |
| `events.update` | `PUT /calendars/{id}/events/{eid}` | [GoogleCalendarApiClient.cs:144-157](../Google/GoogleCalendarApiClient.cs#L144) | **쓰기** |
| `events.delete` | `DELETE /calendars/{id}/events/{eid}` | [GoogleCalendarApiClient.cs:160-169](../Google/GoogleCalendarApiClient.cs#L160) | **삭제** |

**호출하지 않는 API** (`Google/` 전체 grep 결과 `acl`, `freebusy`, `settings`, `colors`, `attendee`, `conferenceData`, `attachments` 전부 0건):
`acl.*` (공유 권한), `calendars.delete` (캘린더 삭제), `calendarList.insert/delete` (구독 변경), `freebusy.query`, `settings.*`, `colors.get`, `channels.stop` (푸시 알림 미사용 — 폴링 방식).

> ⚠️ 즉, **캘린더 설정/공유 권한(ACL)은 전혀 건드리지 않고, 캘린더 자체를 삭제하지도 않습니다.** 그런데 지금 요청 중인 `calendar` 스코프는 "share, and permanently delete all the calendars"까지 허용합니다 — **실사용 범위보다 넓습니다.** 이것이 2장 결론의 핵심 근거입니다.

### 1-4. 접근하는 캘린더의 범위 — **사용자 기본(primary) 캘린더 포함**

```csharp
// Dialogs/CalendarSettingsDialog.xaml.cs:188, 214-222
var primaryCal = googleCalendars.Find(c => c.Primary == true);
...
if (local.Title == CategoryNames.Personal && primaryCal != null)
    targetGoogleId = primaryCal.Id;          // 로컬 '개인' 캘린더 → 사용자 기본 캘린더
...
if (local.SyncMode == "None") local.SyncMode = "TwoWay";   // 양방향(쓰기·삭제 포함)
```

정리하면 3종류의 캘린더에 접근합니다.

1. **앱이 직접 생성한 보조 캘린더**
   - `"{학교명}"` — [CalendarSettingsDialog.xaml.cs:195](../Dialogs/CalendarSettingsDialog.xaml.cs#L195)
   - `"{학교명} 학사일정"` — [GoogleSyncService.cs:747-748](../Google/GoogleSyncService.cs#L747)
2. **사용자의 기본(primary) 캘린더** — 로컬 "개인" 카테고리와 양방향 매핑 (위 코드)
3. **사용자가 목록에서 체크한 기존 캘린더** — [CalendarSettingsDialog.xaml.cs:241-300](../Dialogs/CalendarSettingsDialog.xaml.cs#L241) (`LoadGoogleCalendarListAsync` / `OnGoogleCalendarSaveClicked`)

삭제도 실제로 수행합니다: 로컬에서 지운 일정을 Google에서 제거([GoogleSyncService.cs:359-374](../Google/GoogleSyncService.cs#L359)), NEIS에서 사라진 학사일정 정리([GoogleSyncService.cs:694-711](../Google/GoogleSyncService.cs#L694)).

### 1-5. 읽고 쓰는 이벤트 필드 (심사관이 묻는 "데이터 최소 수집" 근거)

[GoogleCalendarModels.cs:123-180](../Google/GoogleCalendarModels.cs#L123) 기준으로 DTO에 정의된 필드가 전부입니다:

`id`, `status`, `summary`, `description`, `location`, `colorId`, `start`, `end`, `recurrence`, `updated`, `created`, `extendedProperties.private`

**정의되어 있지 않은 = 역직렬화조차 하지 않는 필드**: `attendees`, `organizer`, `creator`, `hangoutLink`, `conferenceData`, `attachments`, `guestsCanModify`, `visibility`.
→ **다른 사람(참석자)의 이메일·이름 등 제3자 개인정보는 앱에 들어오지 않습니다.** 검증 신청서에 명시할 강력한 근거입니다.

Ktask 통합용으로 `extendedProperties.private`에 `itemType` / `isDone` / `completed` 3개 키만 씁니다([GoogleSyncService.cs:538-552](../Google/GoogleSyncService.cs#L538)).

### 1-6. 데이터 저장 위치 (개인정보처리방침 근거)

- 이벤트 데이터: 로컬 SQLite (`KEvent` 테이블). **외부 서버로 전송하지 않습니다** — 앱은 Google API 외에 어떤 백엔드와도 통신하지 않습니다.
- 토큰: `Settings.db`에 **DPAPI(`DataProtectionScope.CurrentUser`) 암호화** 후 저장 ([GoogleAuthService.cs:315-345](../Google/GoogleAuthService.cs#L315)).
- 연결 해제 시 `oauth2.googleapis.com/revoke` 호출 + 토큰 3종 즉시 삭제 ([GoogleAuthService.cs:213-237](../Google/GoogleAuthService.cs#L213)).

---

## 2. 스코프 결정

### 2-1. 결론

> **`https://www.googleapis.com/auth/calendar` (전체) → 아래 3개 세분화 스코프 조합으로 축소할 것을 권장합니다. 코드 동작은 전혀 바뀌지 않습니다.**

| 스코프 | 필요한 이유 (호출 대응) |
|---|---|
| `https://www.googleapis.com/auth/calendar.calendarlist.readonly` | `calendarList.list` — 사용자가 동기화 대상 캘린더를 고를 수 있도록 목록 표시 |
| `https://www.googleapis.com/auth/calendar.calendars` | `calendars.insert` — 학교/학사일정 전용 보조 캘린더 생성 |
| `https://www.googleapis.com/auth/calendar.events` | `events.list/insert/update/delete` — 양방향 동기화 |

**축소되는 권한**: 캘린더 공유·ACL 변경, 캘린더 자체의 영구 삭제. 1-3에서 확인했듯 앱이 전혀 쓰지 않는 권한입니다. 심사관에게 "필요 최소 권한만 요청했다"를 코드로 증명할 수 있게 됩니다.

### 2-2. 왜 더 좁힐 수 없는가 (심사 대응 논리)

- **`calendar.readonly` / `calendar.events.readonly` 불가** — 양방향 동기화 제품입니다. 앱에서 만든 수업·학급 일정을 Google에 올리고(`events.insert`), 수정·삭제를 반영해야 합니다([GoogleSyncService.cs:301-375](../Google/GoogleSyncService.cs#L301)).
- **`calendar.events.owned` 불가** — 교사가 학교에서 **공유받아 구독 중인** 캘린더(관리자가 만든 학사 캘린더 등)에도 일정을 쓸 수 있어야 합니다. `owned`는 소유 캘린더로 제한됩니다.
- **`calendar.app.created` 불가 (현재 코드 기준)** — 이 스코프는 "앱이 만든 보조 캘린더"로만 접근이 제한됩니다. 그런데 1-4에서 확인했듯 로컬 "개인" 캘린더가 **사용자의 기본(primary) 캘린더**에 양방향 매핑됩니다. primary는 앱이 만든 캘린더가 아니므로 동작하지 않습니다.

### 2-3. 대안 — 심사를 최대한 쉽게 가고 싶다면 (선택, 제품 사양 변경 필요)

`calendar.app.created` **단 1개**로 줄일 수 있습니다. "우리 앱이 만든 캘린더만 건드립니다"는 심사에서 가장 통과가 쉬운 서사입니다. 대신 **개인 캘린더 동기화 기능을 포기**해야 합니다.

필요한 코드 변경 (2곳):

1. [CalendarSettingsDialog.xaml.cs:188, 214-215](../Dialogs/CalendarSettingsDialog.xaml.cs#L188) — `primaryCal` 매핑 제거, 로컬 "개인" 캘린더도 앱 생성 보조 캘린더(또는 별도 `"NewSchool 개인"` 캘린더)로 매핑
2. [GoogleAuthService.cs:26](../Google/GoogleAuthService.cs#L26) — `Scope` 상수 교체

`GetCalendarListAsync`는 그대로 둬도 됩니다 — `app.created` 스코프에서는 `calendarList.list`가 앱이 만든 캘린더만 돌려주므로 오히려 UI가 자연히 정리됩니다.

**판단**: 개인 캘린더 동기화가 실사용 가치가 높은 기능이면 2-1(3개 조합)로 가고, "학교 일정만 구글에 올라가면 된다"면 2-3이 압도적으로 편합니다. **제품 사양 결정이라 개발자 판단이 필요합니다.**

### 2-4. ✅ [구현 완료] 부분 동의(granular consent) 처리

스코프를 3개로 나누면 동의 화면에 **체크박스가 3개** 뜨고 사용자가 일부만 허용할 수 있습니다. 현재 코드는 부여된 스코프를 **전혀 확인하지 않습니다**:

```csharp
// Google/GoogleCalendarModels.cs:25-26 — 파싱은 하지만
[JsonPropertyName("scope")]
public string? Scope { get; set; }
```

`tokenResp.Scope`를 읽는 코드가 저장소 전체에 없었습니다(grep 0건). 부분 동의 시 API가 403을 반환하는데, 앱은 그것을 일반 동기화 오류로만 표시하게 됩니다.

**적용한 조치** — `ExchangeCodeForTokenAsync`에서:
1. `FindMissingScopes(tokenResp.Scope)`로 3종이 모두 부여됐는지 검사
2. 부족하면 **토큰을 저장하지 않고** 발급된 액세스 토큰을 즉시 revoke → 반쪽 권한으로 "연동됨" 상태가 되는 것을 원천 차단
3. `LastAuthError`에 빠진 권한을 사람이 읽는 이름("일정 조회·등록·수정·삭제" 등)으로 담아 설정 화면에 표시
4. 전체 `calendar` 권한은 3종의 상위 집합이므로 통과, `scope` 필드가 비어 있으면 판단 보류(통과)

회귀 테스트 9건: [GoogleScopeConsentTests.cs](../NewSchool.Tests/GoogleScopeConsentTests.cs). 특히 `calendar.events.readonly`가 `calendar.events`로 오인되지 않는지(문자열 `Contains` 검사의 함정) 확인합니다.

### 2-5. 검증 등급

Google Calendar API는 **restricted scope 목록에 포함되지 않습니다** (restricted = Gmail, Drive, Fit, Chat, Data Portability, Photos Ambient, Health — [Google 문서](https://support.google.com/cloud/answer/13464325) 확인). 따라서:

- ✅ **Sensitive scope 검증**: 브랜드 검증(완료) + 개인정보처리방침 + **데모 영상** + 스코프 사용 목적 설명
- ❌ **CASA 제3자 보안평가 불필요** (연 수천 달러 비용 발생하는 절차 — 해당 없음)

> 콘솔에서 스코프를 추가할 때 표시되는 분류(민감/제한)를 최종 확인하세요. Calendar 스코프는 콘솔상 "민감한 범위"로 표시됩니다.

---

## 3. 개인정보처리방침 보완 초안

### 3-1. 🚨 먼저 고쳐야 할 불일치 (검증 탈락 사유)

현재 방침 1항에 이렇게 적혀 있습니다:

> **Google 계정 연동 정보:** 이메일 주소, 사용자 이름, 프로필 이미지 URL

**그런데 앱은 `openid` / `userinfo.email` / `userinfo.profile` 스코프를 요청하지 않습니다**(1-2 참조). 이메일·이름·프로필 이미지를 받을 방법이 없습니다. 심사관은 **요청 스코프와 방침 기재 항목을 대조**하므로, "요청하지도 않은 데이터를 수집한다고 써 둔" 이 문장은 반려 사유가 됩니다.

같은 이유로 2항의 "**사용자 식별 및 관리:** Google 계정을 통한 로그인 사용자 본인 확인"도 사실과 다릅니다. 앱은 Google 로그인으로 사용자를 식별하지 않고, 캘린더 접근 권한만 위임받습니다.

또 3항의 "**Restricted Scopes 포함**" 문구는 2-5에서 확인했듯 Calendar에 해당하지 않으니 빼는 편이 정확합니다.

### 3-2. 교체 초안 — [Privacy.html](../Privacy.html) 1~4항

아래 HTML로 기존 1~4항을 통째로 교체하면 됩니다. (스코프는 **2-1안(3개 조합)** 기준으로 작성했습니다. 2-3안을 택하면 해당 문장만 바꾸세요.)

```html
<h2>1. 수집·처리하는 정보 항목</h2>
<p>서비스는 Windows PC에 설치되어 동작하는 데스크톱 프로그램이며, 별도의 서버를 운영하지 않습니다.
   모든 데이터는 이용자의 PC에 저장되고, 이용자가 직접 연동한 Google 계정 외에는 어디로도 전송되지 않습니다.</p>
<div class="box">
    <ul>
        <li><strong>Google Calendar 데이터:</strong> 이용자가 동기화 대상으로 선택한 캘린더의
            일정 제목, 설명, 장소, 시작·종료 일시, 반복 규칙, 색상, 일정 식별자(ID), 최종 수정 시각.
            <br><span style="color:#888">※ 일정의 참석자 정보, 주최자·작성자 이메일, 첨부파일, 화상회의 링크는
            요청하지도 저장하지도 않습니다.</span></li>
        <li><strong>Google 인증 토큰:</strong> OAuth 2.0 액세스 토큰 및 리프레시 토큰</li>
        <li><strong>학사 관리 데이터:</strong> 이용자가 프로그램에 직접 입력한 학급·수업·업무 일정 등
            (이용자 PC의 로컬 데이터베이스에만 저장)</li>
    </ul>
</div>
<p>서비스는 이용자의 <strong>이메일 주소·이름·프로필 사진 등 계정 프로필 정보를 요청하지 않으며 수집하지 않습니다.</strong>
   Google 로그인은 캘린더 접근 권한을 위임받는 용도로만 사용되고, 회원 식별에는 사용되지 않습니다.</p>

<h2>2. 이용 목적</h2>
<ul>
    <li><strong>Google 캘린더 양방향 동기화:</strong> 프로그램에 입력한 학사일정·수업·학급·업무 일정을
        Google 캘린더에 반영하고, Google 캘린더에서 변경된 내용을 프로그램으로 가져오는 기능 제공</li>
    <li><strong>전용 캘린더 생성:</strong> 이용자의 기존 캘린더와 학교 일정이 섞이지 않도록
        학교 전용 보조 캘린더를 생성</li>
</ul>
<p>수집된 Google 사용자 데이터는 위 기능 제공 목적 외에 사용되지 않으며, 광고·프로파일링·AI 모델 학습에
   사용하지 않습니다.</p>

<h2>3. 요청하는 Google 권한 범위(Scope)</h2>
<div class="box">
    <ul>
        <li><code>calendar.calendarlist.readonly</code> — 동기화할 캘린더를 이용자가 선택할 수 있도록
            캘린더 목록을 읽습니다.</li>
        <li><code>calendar.calendars</code> — 학교 일정 전용 보조 캘린더를 생성합니다.</li>
        <li><code>calendar.events</code> — 선택된 캘린더의 일정을 읽고, 만들고, 수정하고, 삭제합니다.
            (양방향 동기화에 필요)</li>
    </ul>
</div>
<p>서비스는 캘린더의 <strong>공유 설정(ACL) 변경, 캘린더 자체의 삭제, 다른 Google 서비스(Gmail·Drive 등)
   접근 권한을 요청하지 않습니다.</strong></p>

<h2>4. 저장 방식 · 보관 기간 · 삭제 방법</h2>
<ul>
    <li><strong>저장 위치:</strong> 모든 일정 데이터와 인증 토큰은 이용자 PC의 로컬 데이터베이스 파일에만
        저장됩니다. 서비스 운영자는 이용자의 데이터에 접근할 수 없습니다.</li>
    <li><strong>토큰 보호:</strong> Google 인증 토큰은 Windows DPAPI(현재 사용자 계정 전용 암호화)로
        암호화하여 저장합니다.</li>
    <li><strong>보관 기간:</strong> 이용자가 연동을 유지하는 동안 보관하며, 별도의 만료 기간을 두지 않습니다.</li>
    <li><strong>삭제 방법:</strong>
        <ol>
            <li>프로그램의 <em>달력</em> 화면에서 상단 <em>⚙ 캘린더 설정</em> 버튼을 누른 뒤
                <em>Google 연동 해제</em>를 실행하면, Google에 토큰 무효화(revoke)를
                요청하고 PC에 저장된 토큰을 즉시 삭제합니다.</li>
            <li><a href="https://myaccount.google.com/permissions" target="_blank">Google 계정 권한 관리</a>에서
                NewSchool의 접근 권한을 직접 철회할 수 있습니다.</li>
            <li>프로그램을 삭제하고 데이터 폴더를 제거하면 로컬에 저장된 일정 사본도 모두 사라집니다.</li>
        </ol>
    </li>
    <li><strong>Google로 전송된 일정:</strong> 연동 해제 후에도 이미 Google 캘린더에 생성된 일정은 그대로
        남습니다. 이용자가 Google 캘린더에서 직접 삭제할 수 있습니다.</li>
</ul>
```

기존 5~7항(제3자 제공, 이용자 권리, 문의처)은 그대로 두고, 하단 `시행일자`만 개정일로 갱신하세요.

> ✅ Google 심사에서 자주 확인하는 **"Limited Use" 문구**는 기존 3항(→ 교체 후 새 2항 마지막 문단)의 "위 기능 제공 목적 외에 사용되지 않으며, 광고·프로파일링·AI 모델 학습에 사용하지 않습니다"로 충족됩니다. 여기에 다음 문장을 추가하면 더 안전합니다: *"NewSchool의 Google API 사용은 <a href="https://developers.google.com/terms/api-services-user-data-policy">Google API Services User Data Policy</a>의 Limited Use 요건을 준수합니다."*

---

## 4. OAuth 동의 화면 — 스코프별 사용 목적 설명

콘솔에서 민감한 범위를 추가하면 스코프마다 **"범위가 어떤 방식으로 사용되나요?"** 입력란이 나옵니다. 여기에 넣을 문구입니다. **심사는 영어로 진행되므로 영문을 넣으세요.** 국문은 내부 검토용입니다.

### 4-0. 콘솔 붙여넣기용 — **1000자 제한 대응판 (스코프 3종 통합)**

입력란은 스코프별이 아니라 **전체 1000자 제한**입니다. 아래 941자 통합본을 그대로 붙여 넣으세요. 마크다운 백틱과 en dash(`–`)는 뺐습니다.

```text
NewSchool is a Windows desktop app for K-12 teachers in South Korea that two-way syncs school schedules with Google Calendar.

calendarlist.readonly: calendarList.list lists the teacher's calendars so they can pick sync targets, and detects the app's existing school calendar to avoid duplicates. Subscriptions are never changed.

calendars: calendars.insert creates a secondary calendar named after the school, plus one for academic events from Korea's NEIS open API, kept separate from personal calendars.

events: events.list/insert/update/delete keep the app's local schedule database and Google Calendar in sync both ways. Read-only scopes cannot do this, and events.owned is insufficient because teachers are commonly given write access to a school-wide calendar owned by an administrator.

The app never changes sharing settings or ACLs, never deletes calendars, and never reads attendees, organizers, attachments, or conference data.
```

**59자 여유**를 남겨뒀습니다. 콘솔이 줄바꿈을 다르게 세거나 뒤에 문장을 덧붙일 여지를 위해서입니다.

구조는 **앱 정체성 1줄 → 스코프별 1문단씩 → 하지 않는 일 1줄**입니다. 마지막 줄이 3종 전체에 걸리는 부정문이라, 스코프마다 반복하지 않고 한 번에 처리해 분량을 줄였습니다.

더 줄여야 하면 `events` 문단의 `events.owned` 반박 절을 먼저 빼세요(약 130자). 다만 그게 이 신청에서 가장 방어가 필요한 부분이라 마지막 수단으로 두세요.

아래 4-1~4-3은 같은 논지의 전체판(국문 대역 포함)입니다. 콘솔에는 안 들어가지만, **심사관이 추가 질의를 보내오면 그때 근거로 쓰세요** — 특히 4-3의 필드 목록(참석자·주최자 미조회)이 유용합니다.

### 4-1. `https://www.googleapis.com/auth/calendar.calendarlist.readonly`

**EN**
> NewSchool is a Windows desktop application for K–12 teachers in South Korea that manages school schedules, class timetables, and homeroom tasks. After a teacher connects their Google account, the app calls `calendarList.list` once to display the list of the teacher's calendars so that the teacher can choose which calendars to synchronize. The app also uses this list to detect whether a school-specific calendar it previously created still exists, so it does not create duplicates. The app only reads calendar names, IDs, and colors from this list; it never modifies calendar subscriptions. Without this scope the teacher would have no way to select a synchronization target.

**KO**
> NewSchool은 한국 초·중·고 교사를 위한 Windows 데스크톱 학사 관리 프로그램입니다. 교사가 Google 계정을 연동하면 `calendarList.list`를 호출해 캘린더 목록을 보여주고, 교사가 동기화할 캘린더를 직접 선택합니다. 또한 앱이 이전에 만든 학교 전용 캘린더가 아직 남아 있는지 확인해 중복 생성을 방지하는 데 사용합니다. 목록에서 캘린더 이름·ID·색상만 읽으며, 구독 상태를 변경하지 않습니다. 이 권한이 없으면 동기화 대상 선택 자체가 불가능합니다.

### 4-2. `https://www.googleapis.com/auth/calendar.calendars`

**EN**
> The app creates one secondary calendar named after the teacher's school (e.g. "밀양중학교") and, when the teacher imports the official academic calendar from the Korean Ministry of Education's NEIS open API, a second calendar named "{school} 학사일정". These dedicated calendars keep school events separate from the teacher's personal calendar, so the teacher can toggle or share them independently. This requires `calendars.insert`. The app never deletes calendars and never changes calendar sharing settings or ACLs.

**KO**
> 앱은 교사의 학교명을 딴 보조 캘린더(예: "밀양중학교")를 하나 생성하고, 교사가 NEIS 학사일정을 가져올 때 "{학교명} 학사일정" 캘린더를 추가로 생성합니다. 학교 일정을 개인 캘린더와 분리해 두어 교사가 독립적으로 켜고 끄거나 공유할 수 있게 하기 위함이며, `calendars.insert` 호출에 이 권한이 필요합니다. 캘린더를 삭제하거나 공유 설정(ACL)을 변경하는 일은 없습니다.

### 4-3. `https://www.googleapis.com/auth/calendar.events`

**EN**
> This is the core feature of the app: two-way synchronization between the teacher's local schedule database and Google Calendar.
> - `events.list` (with `syncToken` for incremental sync) pulls changes made in Google Calendar into the app.
> - `events.insert` pushes lessons, homeroom events, school-wide academic events, and work tasks created in the app to Google Calendar.
> - `events.update` propagates edits in either direction using a last-write-wins rule based on the `updated` timestamp.
> - `events.delete` removes events from Google Calendar when the teacher deletes them in the app, and removes academic events that no longer exist in the official school calendar.
>
> A read-only scope cannot support this feature. `calendar.events.owned` is insufficient because Korean teachers are frequently granted write access to a school-wide calendar owned by an administrator, and they need to publish class events to it. The app reads and writes only these event fields: summary, description, location, start, end, recurrence, colorId, status, and its own `extendedProperties.private` keys. It never reads attendees, organizers, attachments, or conference data.

**KO**
> 앱의 핵심 기능인 로컬 일정 DB ↔ Google 캘린더 양방향 동기화에 필요합니다.
> - `events.list` (`syncToken` 증분 동기화): Google 캘린더에서 변경된 내용을 앱으로 가져옵니다.
> - `events.insert`: 앱에서 만든 수업·학급·학사·업무 일정을 Google 캘린더에 올립니다.
> - `events.update`: `updated` 타임스탬프 기준 last-write-wins로 양쪽 수정 사항을 반영합니다.
> - `events.delete`: 교사가 앱에서 지운 일정, 그리고 학교 공식 학사일정에서 사라진 항목을 Google에서도 제거합니다.
>
> 읽기 전용 스코프로는 구현이 불가능합니다. `calendar.events.owned`로는 부족한데, 한국 학교에서는 관리자가 소유한 학교 공용 캘린더에 교사가 쓰기 권한을 부여받아 수업 일정을 올리는 경우가 흔하기 때문입니다. 앱이 읽고 쓰는 필드는 제목·설명·장소·시작·종료·반복규칙·색상·상태와 앱 전용 `extendedProperties.private` 키뿐이며, 참석자·주최자·첨부파일·화상회의 정보는 읽지 않습니다.

### 4-4. `calendar` 단일 스코프를 유지하는 경우 (2-1을 적용하지 않을 때)

**EN**
> NewSchool provides two-way synchronization between a Korean K–12 teacher's local schedule database and Google Calendar. It needs to: list the teacher's calendars so the teacher can choose synchronization targets (`calendarList.list`); create a dedicated secondary calendar for school events (`calendars.insert`); and read, create, edit, and delete events on the calendars the teacher selected, including their primary calendar (`events.list/insert/update/delete`). Because the teacher can map the app's "Personal" category to their primary Google Calendar and because Korean schools commonly share a school-wide calendar that teachers write to, narrower scopes such as `calendar.app.created` or `calendar.events.owned` do not cover the required access. The app does not modify calendar sharing settings, does not delete calendars, and does not read attendee or organizer information.

**KO**
> NewSchool은 교사의 로컬 일정 DB와 Google 캘린더를 양방향 동기화합니다. 이를 위해 동기화 대상 선택용 캘린더 목록 조회(`calendarList.list`), 학교 일정 전용 보조 캘린더 생성(`calendars.insert`), 교사가 선택한 캘린더(기본 캘린더 포함)의 일정 조회·생성·수정·삭제(`events.*`)가 필요합니다. 앱의 "개인" 카테고리를 사용자의 기본 캘린더에 매핑할 수 있고, 한국 학교에서는 공용 캘린더를 공유받아 쓰는 경우가 많아 `calendar.app.created`·`calendar.events.owned` 같은 좁은 스코프로는 요구 범위를 충족하지 못합니다. 공유 설정 변경, 캘린더 삭제, 참석자·주최자 정보 조회는 하지 않습니다.

---

## 5. 데모 영상

촬영 대본은 별도 문서로 분리했습니다 → **[oauth-demo-video-script.md](oauth-demo-video-script.md)**

Google 필수 요구사항 4가지(영어 동의 화면 · 앱 이름 · 주소창 client ID · 스코프별 기능 시연), 사전 준비, 장면별 영어 캡션 문구, 업로드 절차가 들어 있습니다.

핵심만 짚으면:

- **"in English"는 앱 UI가 아니라 OAuth 동의 화면에 걸린 요구입니다.** 테스트 계정 언어를 English로 바꿔 촬영하세요. 앱은 한국어 그대로 둬도 됩니다.
- **주소창의 `client_id`를 판독 가능하게** 3초 이상 노출 — 반려 사유 1위.
- **"인증되지 않은 앱" 경고 화면을 잘라내지 마세요** — 미검증 상태의 정상 동작이라 영상에 있어야 합니다.
- **아직 배포되지 않은 로컬 빌드로 촬영**하는 것이 Google이 말하는 "스테이징 환경"입니다. 검증 통과 전까지 앱 설치 파일을 새로 배포하지 마세요.
- YouTube **일부공개(Unlisted)** — 비공개면 심사관이 못 봅니다.

## 6. 실행 순서 요약

**채택안: 2-1 (세분화 스코프 3개).** 2-3(`calendar.app.created`)은 미채택 — 개인 캘린더 동기화 기능을 유지하기로 함.

| # | 할 일 | 상태 | 산출/파일 |
|---|---|---|---|
| 1 | **[결정]** 스코프 안 선택 → **2-1 채택** | ✅ 완료 | — |
| 2 | 스코프 상수 교체 (전체 → 세분화 3종) | ✅ 완료 | [GoogleAuthService.cs](../Google/GoogleAuthService.cs) |
| 3 | 부분 동의 검증 + 실패 사유 노출(`LastAuthError`) | ✅ 완료 | [GoogleAuthService.cs](../Google/GoogleAuthService.cs), [CalendarSettingsDialog.xaml.cs](../Dialogs/CalendarSettingsDialog.xaml.cs) |
| 3b | 부분 동의 회귀 테스트 9건 | ✅ 완료 | [GoogleScopeConsentTests.cs](../NewSchool.Tests/GoogleScopeConsentTests.cs) |
| 4 | (2-3 전용) primary 캘린더 매핑 제거 | ⏭ 해당 없음 | — |
| 5 | 개인정보처리방침 1~4항 교체 + 개정일 갱신 | ✅ 코드 반영 / ⬜ **배포 필요** | [Privacy.html](../Privacy.html) |
| 5b | CHANGELOG · 도움말 반영 | ✅ 완료 | [CHANGELOG.md](../CHANGELOG.md), [Assets/help.html](../Assets/help.html) |
| 6 | 콘솔 Data Access에 스코프 3종 등록 + 4장 문구 입력 | ⬜ 남음 | Google Cloud Console |
| 7 | 5장 체크리스트대로 데모 영상 촬영 → YouTube(Unlisted) | ⬜ 남음 | — |
| 8 | 검증 신청 제출 | ⬜ 남음 | — |

> ⚠️ **5번(방침 배포)을 6번보다 먼저** 하세요. 심사관이 스코프 등록 직후 방침 페이지를 크롤링합니다.
> 현재 `Privacy.html`은 저장소에만 수정돼 있고 `newschool.centwon.com`에는 아직 옛 버전이 올라가 있습니다.

### 콘솔에 등록할 스코프 (복사용)

```
https://www.googleapis.com/auth/calendar.calendarlist.readonly
https://www.googleapis.com/auth/calendar.calendars
https://www.googleapis.com/auth/calendar.events
```

2026-08-13 콘솔 확인 결과 **등록된 범위가 하나도 없습니다**(민감/비민감/제한 전부 비어 있음). 따라서 기존 `auth/calendar`를 제거하는 절차는 필요 없고, 위 3종을 새로 추가하기만 하면 됩니다. 추가하면 **"민감한 범위"** 칸에 3줄이 들어갑니다 — Calendar는 sensitive이지 restricted가 아니므로 "제한된 범위"는 비어 있는 것이 정상입니다.

추가할 때 스코프마다 **"범위가 어떤 방식으로 사용되나요?"** 입력란이 나옵니다 → 4-0의 영문 문구를 붙여 넣으세요.

> 참고: 콘솔의 범위 목록은 **런타임 인증 요청을 막지 않습니다.** 지금 등록된 범위가 0개인데도 앱이 `auth/calendar`를 요청해 정상 동작하는 것이 그 증거입니다. 이 목록은 검증 심사용 신고에 가깝습니다. 따라서 코드 배포와 콘솔 등록의 선후 관계를 신경 쓸 필요는 없습니다(단, 개인정보처리방침 배포는 심사 전에 끝나 있어야 합니다).

### 기존 사용자 영향

없습니다. 이미 전체 `calendar` 권한으로 발급된 리프레시 토큰은 세분화 3종을 모두 포함하므로 계속 동작하고, 재연동을 강제하지 않습니다. `FindMissingScopes`가 전체 권한을 상위 스코프로 인정하도록 처리해 뒀습니다([GoogleScopeConsentTests.전체_권한은_세분화_3종을_포함하므로_통과](../NewSchool.Tests/GoogleScopeConsentTests.cs)).
