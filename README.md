# NewSchool

WinUI 3 (Windows App SDK 2.2) · .NET 10 · SQLite 기반 교사용 학급 관리 프로그램.

리치 텍스트 편집은 네이티브 Win2D 에디터(WinUIRichEditor)를 사용하며, WebView2 의존이 없습니다.

## 주요 기능
- 학생/학급 관리, 학생부 특기사항, 누가기록
- 자리 배정 (DB 영속화, 이력 기반 배치 옵션)
- 통합 내보내기 (누가기록·학생부·좌석배정·학생카드 × Excel/PDF/HTML)
- 학사일정, 시간표, 학급 일지, 동아리, 게시판
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
dotnet build -c Debug
```

또는 Visual Studio 에서 `NewSchool.sln` 열고 빌드.

## 배포용 인스톨러 빌드

`Platform` 을 명시해야 합니다 (생략하면 `AnyCPU` 로 해석되어 존재하지 않는 게시 프로필을 찾다 실패합니다).
Native AOT 게시본은 아키텍처별로 별도 빌드해야 하며, 지원 아키텍처는 `win-x64`/`win-x86`/`win-arm64` 입니다.

```bash
dotnet publish -c Release -p:Platform=x64
```

`Properties/PublishProfiles/win-x64.pubxml` 이 자동 적용되어 자체 포함(self-contained) Native AOT 게시본을
`bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\` 에 생성합니다. x86/arm64는 `-p:Platform=x86` 또는
`-p:Platform=arm64` 로 교체.

이후 Inno Setup 으로 `installer.iss` 또는 `Installer/NewSchoolSetup.iss` 컴파일.

## 설정 저장 위치

- **앱 데이터**: `%APPDATA%\NewSchool\` — DB, 사진, 백업
- **설정 DB**: `Settings.db` — 사용자 환경설정 키-값 저장
- **비밀 정보**: `secrets.json` (실행 디렉터리 옆)

## 라이선스

사내 배포용. 외부 공개 시 별도 협의 필요.
