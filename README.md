# NewSchool

WinUI 3 (Windows App SDK 2.4) · .NET 10 · SQLite 기반 교사용 학급 관리 프로그램.

리치 텍스트 편집은 네이티브 Win2D 에디터(WinUIRichEditor)를 사용하며, WebView2 의존이 없습니다.

## 주요 기능
- 오늘 대시보드(홈) — 날짜 이동·현재 교시·오늘 시간표(내 수업/우리 반)·학사일정·급식·할일·메모
- 학생/학급 관리, 학생부 특기사항, 누가기록
- 자리 배정 (DB 영속화, 이력 기반 배치 옵션)
- 통합 내보내기 (누가기록·학생부·좌석배정·학생카드·학생정보 × Excel/PDF/HTML,
  표 형태인 누가기록·학생부·학생정보는 CSV 추가)
- 수업 관리 6탭 — 수업 개설 · 단원 · 시수 · 진도 · 시간표 입력 · 주별 시간표(휴강·교체·보강·대강)
- 학사일정, 학급 시간표, 학급 일지, 수업 일지, 동아리, 게시판
- Google Calendar 연동, NEIS 오픈 API 연동

전체 기능·파일 구조는 [FEATURES.md](FEATURES.md) 참조.

## 개발 환경 설정

### 필수
- .NET 10 SDK
- Windows 10 1809 (10.0.17763) 이상
- Visual Studio 2022 이상 또는 `dotnet` CLI

### 클론 후 빌드 절차

```bash
git clone https://github.com/centwon/newschool.git
cd newschool
```

**1. 비밀 정보 파일 생성 (선택)**

Google OAuth / NEIS API 기능을 쓰려면 `secrets.json` 을 만들어야 합니다. 템플릿 복사 후 본인 키 입력:

```bash
cp secrets.template.json secrets.json
```

`secrets.json` 내용:

```json
{
  "google_oauth": {
    "client_id": "your-google-client-id.apps.googleusercontent.com",
    "client_secret": "your-google-client-secret"
  },
  "neis_api_key": "your-neis-api-key"
}
```

- **Google OAuth**: [Google Cloud Console](https://console.cloud.google.com) → OAuth 2.0 클라이언트 ID 발급
- **NEIS API**: [나이스 데이터포털](https://open.neis.go.kr) → 회원가입 → 인증키 발급

> `secrets.json` 은 `.gitignore` 에 포함되어 저장소에 올라가지 않습니다.  
> 파일이 없어도 빌드는 성공하며, 관련 기능만 비활성화됩니다.

**2. 빌드**

```bash
dotnet build -c Debug -p:Platform=x64
```

또는 Visual Studio 에서 `NewSchool.sln` 열고 빌드.

**3. 테스트**

```bash
dotnet test NewSchool.Tests -p:Platform=x64
```

게시 전 1회 실행을 습관화합니다 — 자세한 범위는 [TEST_PLAN.md](TEST_PLAN.md) 참조.

## 배포용 인스톨러 빌드

`Platform` 을 명시해야 합니다 (생략하면 `AnyCPU` 로 해석되어 존재하지 않는 게시 프로필을 찾다 실패합니다).
Native AOT 게시본은 아키텍처별로 별도 빌드해야 하며, 지원 아키텍처는 `win-x64`/`win-x86`/`win-arm64` 입니다.

```bash
dotnet publish -c Release -p:Platform=x64
```

`Properties/PublishProfiles/win-x64.pubxml` 이 자동 적용되어 Native AOT 게시본을
`bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\` 에 생성합니다. x86/arm64는 `-p:Platform=x86` 또는
`-p:Platform=arm64` 로 교체.

**.NET 런타임과 Windows App SDK 를 모두 자체 포함**합니다(`WindowsAppSDKSelfContained=true`).
게시 폴더는 **43개 파일·5개 폴더·92MB** 이고, 그 대신 **설치 프로그램에 런타임 설치 단계가 없습니다.**

게시본에서 닿지 않는 파일은 `NewSchool.csproj` 의 두 곳이 걷어냅니다 —
`RemoveUnusedWinAppSdkAssets`(WebView2·Windows AI 등 전이 자산, 게시 목록 단계에서 제외)와
`CleanPublishOutput`(게시 후 정리). 무엇을 왜 빼는지는 그 자리 주석에 적어 두었습니다.
**WinAppSDK 를 올리면 그 목록을 다시 볼 것** — 스모크는 PDF 출력·리치에디터·엑셀 내보내기·
구글 연동·인쇄입니다.

이후 Inno Setup 으로 `Installer/NewSchoolSetup.iss` 컴파일.

> **왜 자체 포함인가 — 겪고 나서 바꾼 것**
> 1.0 이전에는 프레임워크 의존이라 설치 프로그램이 런타임(약 107MB)을 함께 깔았습니다.
> 그래서 SDK 를 올릴 때마다 번들한 설치기와 `RequiredRuntimeVersion` 을 같이 갱신해야 했고,
> **개발 PC 에는 최신 런타임이 이미 있어 그 불일치가 로컬 테스트로는 드러나지 않았습니다.**
> 실제로 번들이 2.3 에 멈춘 채 앱만 2.4 로 올라가, 깨끗한 PC 에서 설치 후 앱이 시작되지 않는
> 일이 있었습니다.
>
> 자체 포함으로 바꾸면서 이 부류가 원천적으로 사라졌습니다. 설치 파일은 오히려
> **117.1MB → 26MB** 로 줄었고, `prerequisites\` 와 `RequiredRuntimeVersion` 은 함께 없앴습니다.
> 대가는 설치 폴더가 커진 것인데, 앱 파일을 모두 `{app}\bin\` 아래로 내려 정리했습니다.

## 데이터 저장 위치

실행 파일 폴더 **또는 그 부모**에 `portable.txt` 가 있으면 **포터블**, 없으면 **설치본**으로
동작합니다. 부모까지 보는 이유는 1.0 부터 앱 파일이 `bin\` 아래로 내려갔기 때문입니다 —
표식을 `bin\` 안에 두면 `bin\` 이 루트가 되어 `Data\` 가 프로그램 파일 50개 사이에 섞입니다.
**표식은 `bin\` 의 부모(루트)에 두세요.**

데이터 유무로 판정하던 옛 방식은 DB 가 사라지거나 동기화가 파일 이름을 바꿨을 때 조용히 다른
폴더를 보게 되어, 모드와 데이터 상태를 떼어 놓았습니다. 다만 표식만 보는 것은 아니고 예외가 셋
있습니다(`Settings.IsPortableLayout` · `Settings.FindPortableRoot`).

- **표식이 있어도** 실행 파일 폴더에 쓸 수 없으면(읽기 전용 매체·`Program Files`) 설치본으로 물러섭니다
- **표식이 없어도** 1.0 이전 포터블 배치(실행 파일 옆 또는 `Data\` 아래 `Settings.db`)면 포터블로 보고,
  그때 표식 파일을 만들어 둡니다
- **양쪽에 다 있으면** 가까운 쪽(실행 파일 폴더)이 이깁니다

| | 루트 |
|---|---|
| 포터블 | 실행 파일 폴더, 또는 표식이 거기 있으면 **그 부모**(설치 폴더를 통째로 옮긴 경우) |
| 설치본 | `%USERPROFILE%\NewSchool\` |

루트 아래 배치는 두 모드가 같습니다.

```
<루트>\
├── portable.txt     ← 포터블일 때만. bin\ 이 아니라 여기에 둔다
├── bin\             ← 앱 파일 50개 (설치본은 {app}\bin, 루트엔 이것과 언인스톨러만)
│   └── NewSchool.exe
├── Data\            ← 사용자 자산. 이 폴더만 옮기면 데이터가 통째로 따라온다
│   ├── Settings.db · school.db · scheduler.db · board.db
│   ├── Photos\{연도}\      학생 사진
│   └── Files\{게시판}\     게시글 첨부
├── Backups\         backup_yyyyMMdd_HHmmss.zip (DB만 담김 — 사진·첨부 제외)
├── Exports\         xlsx · html · csv
├── Prints\          pdf
└── Logs\            30일 경과분 자동 삭제
```

- **비밀 정보**: `secrets.json` — 빌드 시 어셈블리에 내장되므로 배포본에는 파일이 없습니다.
  재빌드 없이 키를 바꾸려면 **실행 파일 옆(`bin\`)** 에 두면 그 값이 우선합니다
  (데이터가 아니라 배포물이라 `Data\` 밖 — `Services/SecretsService.cs`)
- **이관**: 1.0 이전 배치(루트에 DB·`Photos`·`Files` 가 흩어져 있던 형태)는 손으로 `Data\` 에 옮깁니다.
  앱을 완전히 종료한 뒤 `-wal`·`-shm` 까지 **함께** 옮겨야 합니다 — `.db` 만 옮기면 WAL 에만 있던
  최근 커밋이 사라집니다. 옮기고 나면 `Data\Settings.db` 를 보고 포터블로 알아보므로
  `portable.txt` 를 따로 만들 필요는 없습니다.
- **주의**: 구글 토큰(액세스·리프레시·만료)은 DPAPI(CurrentUser)로 암호화됩니다. `Data\` 를 다른 Windows
  계정이나 PC 로 옮기면 구글 재로그인이 필요합니다(데이터가 깨지지는 않습니다).
  학생 쪽에서 같은 방식으로 암호화되는 칸은 `Student.ResidentNumber` **하나뿐이며, 값을 넣는 화면도
  가져오기 경로도 없어 늘 비어 있습니다**(자리를 남겨 둔 것은 의도된 결정 — `Models/Student.cs` 주석 참고).
  전화번호·주소·보호자 정보 등 나머지 항목은 평문으로 저장됩니다.

## 라이선스

**개인과 학교에서 자유롭게 내려받아 사용할 수 있습니다.** 사용료를 받지 않습니다.

소스 코드는 **[MIT 라이선스](LICENSE)** 로 공개합니다. 가져다 쓰거나, 고치거나, 다시
배포하셔도 됩니다. 저작권 표시와 라이선스 문구만 함께 남겨 주세요.

배포되는 설치 파일은 NewSchool 자체 코드만이 아니라 아래 제3자 구성 요소를 함께
담고 있습니다. MIT 는 그중 **NewSchool 자체 코드에만** 적용되며, 함께 담긴 구성 요소는
각자의 라이선스를 따릅니다 — 그래서 [이용약관](Terms.html) 제7조가 그 구성 요소에
대해서만 몇 가지 제한을 둡니다(자체 코드는 그 제한을 받지 않습니다).

### 제3자 구성 요소

이 프로그램은 MiniExcel·SQLitePCLRaw·SQLite·QuestPDF·Skia·HarfBuzz·HtmlAgilityPack·
WinUIRichEditor·Windows App SDK·.NET 등을 포함해 함께 배포합니다. 각 구성 요소의 저작권
표시와 라이선스 전문은 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) 에 있으며,
같은 파일이 게시본에 실려 설치 폴더(`{app}\bin\`)와 포터블 폴더에도 함께 들어갑니다.

QuestPDF 는 Community License 로 사용합니다. 2026-07-06 시행 v3.0 부터 **OSI 승인
오픈소스가 아닌 소스 공개형 상업 라이선스**이며 MIT 가 적용되지 않습니다 — 개인·연 매출
100만 달러 미만 조직·자선단체·학술기관은 무상입니다.

> **포크하시는 분께.** QuestPDF 자격은 **쓰는 주체마다 따로** 판정됩니다. 이 저장소가
> MIT 라고 해서 QuestPDF 까지 자유로워지는 것은 아닙니다. 연 매출 100만 달러 이상
> 기업, 공공기관, 상장사가 이 코드를 가져다 자기 용도로 쓴다면 **유료 QuestPDF
> 라이선스가 따로 필요합니다.** PDF 기능을 걷어내면 해당하지 않습니다.
