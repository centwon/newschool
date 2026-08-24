# NewSchool

WinUI 3 (Windows App SDK 2.4) · .NET 10 · SQLite 기반 교사용 학급 관리 프로그램.

리치 텍스트 편집은 네이티브 Win2D 에디터(WinUIRichEditor)를 사용하며, WebView2 의존이 없습니다.

## 주요 기능
- 오늘 대시보드(홈) — 날짜 이동·현재 교시·오늘 시간표(내 수업/우리 반)·학사일정·급식·할일·메모
- 학생/학급 관리, 학생부 특기사항, 누가기록
- 자리 배정 (DB 영속화, 이력 기반 배치 옵션)
- 통합 내보내기 (누가기록·학생부·좌석배정·학생카드·학생정보 × Excel/PDF/HTML)
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

**.NET 런타임만 자체 포함**이고 Windows App SDK 런타임은 별도입니다(`WindowsAppSDKSelfContained=false`).
그래서 게시 폴더는 9개 파일 44MB 로 단출하고, 대신 설치 프로그램이 런타임을 함께 설치합니다.

이후 Inno Setup 으로 `Installer/NewSchoolSetup.iss` 컴파일.

> **릴리스 체크리스트 — Windows App SDK 버전을 올렸다면**
> 앱은 프레임워크 의존(`WindowsAppSDKSelfContained=false`)이라 설치 프로그램이 런타임을
> 함께 깔아 줍니다. SDK 를 올릴 때 아래 둘을 **반드시 같이** 갱신하세요.
>
> 1. `Installer/prerequisites/WindowsAppRuntimeInstall-x64.exe` — 같은 버전으로 교체
> 2. `Installer/NewSchoolSetup.iss` 의 `RequiredRuntimeVersion`
>
> 개발 PC 에는 최신 런타임이 이미 있어 **이 불일치는 로컬 테스트로 드러나지 않습니다.**
>
> ⚠ **지금 이 저장소가 바로 그 상태입니다(2026-08-24 기준).** 앱과 `RequiredRuntimeVersion` 은
> 2.4 인데 번들된 `WindowsAppRuntimeInstall-x64.exe` 는 **2.3** 에 멈춰 있습니다. 그래서 깨끗한
> PC 에서는 설치 후 앱이 시작되지 않고, **v1.0.0 게시가 이 때문에 보류 중**입니다.
> 게시하려면 2.4 런타임 설치기를 받아 교체해야 합니다.

> Release/게시에서만 나는 `CS1061` 류의 이상한 컴파일 오류는 대개 `obj\x64\Release` 에 남은
> 옛 XAML 생성 파일(`*.g.cs`) 탓입니다. 그 폴더를 지우고 다시 빌드하면 해소됩니다.

## 데이터 저장 위치

실행 파일 옆에 `portable.txt` 가 있으면 **포터블**, 없으면 **설치본**으로 동작합니다.
데이터 유무로 판정하던 옛 방식은 DB 가 사라지거나 동기화가 파일 이름을 바꿨을 때 조용히 다른
폴더를 보게 되어, 모드와 데이터 상태를 떼어 놓았습니다. 다만 표식만 보는 것은 아니고 예외가 둘
있습니다(`Settings.IsPortableLayout`).

- **표식이 있어도** 실행 파일 폴더에 쓸 수 없으면(읽기 전용 매체·`Program Files`) 설치본으로 물러섭니다
- **표식이 없어도** 1.0 이전 포터블 배치(실행 파일 옆 또는 `Data\` 아래 `Settings.db`)면 포터블로 보고,
  그때 표식 파일을 만들어 둡니다

| | 루트 |
|---|---|
| 포터블 | 실행 파일 폴더 |
| 설치본 | `%USERPROFILE%\NewSchool\` |

루트 아래 배치는 두 모드가 같습니다.

```
<루트>\
├── Data\            ← 사용자 자산. 이 폴더만 옮기면 데이터가 통째로 따라온다
│   ├── Settings.db · school.db · scheduler.db · board.db
│   ├── Photos\{연도}\      학생 사진
│   └── Files\{게시판}\     게시글 첨부
├── Backups\         backup_yyyyMMdd_HHmmss.zip (DB만 담김 — 사진·첨부 제외)
├── Exports\         xlsx · html · csv
├── Prints\          pdf
└── Logs\            30일 경과분 자동 삭제
```

- **비밀 정보**: `secrets.json` — 실행 파일 옆(데이터가 아니라 배포물이라 `Data\` 밖)
- **이관**: 1.0 이전 배치(루트에 DB·`Photos`·`Files` 가 흩어져 있던 형태)는 손으로 `Data\` 에 옮깁니다.
  앱을 완전히 종료한 뒤 `-wal`·`-shm` 까지 **함께** 옮겨야 합니다 — `.db` 만 옮기면 WAL 에만 있던
  최근 커밋이 사라집니다. 옮기고 나면 `Data\Settings.db` 를 보고 포터블로 알아보므로
  `portable.txt` 를 따로 만들 필요는 없습니다.
- **주의**: 학생 민감 필드와 구글 토큰은 DPAPI(CurrentUser)로 암호화됩니다. `Data\` 를 다른 Windows
  계정이나 PC 로 옮기면 구글 재로그인이 필요합니다(데이터가 깨지지는 않습니다).

## 라이선스

사내 배포용. 외부 공개 시 별도 협의 필요.
