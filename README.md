# NewSchool

WinUI 3 · .NET 10 · SQLite 기반 교사용 학급 관리 프로그램.

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

```bash
dotnet publish -c Release
```

이후 Inno Setup 으로 `installer.iss` 또는 `Installer/NewSchoolSetup.iss` 컴파일.

## 설정 저장 위치

- **앱 데이터**: `%APPDATA%\NewSchool\` — DB, 사진, 백업
- **설정 DB**: `Settings.db` — 사용자 환경설정 키-값 저장
- **비밀 정보**: `secrets.json` (실행 디렉터리 옆)

## 라이선스

사내 배포용. 외부 공개 시 별도 협의 필요.
