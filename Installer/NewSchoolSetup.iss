; ============================================================
; NewSchool Inno Setup Script
; ============================================================
; 사전 준비:
;   게시: dotnet publish -c Release -p:Platform=x64
;   (win-x64.pubxml 이 자동 적용 — 출력 경로는 아래 PublishDir 과 맞춰 둘 것)
;
; 런타임을 따로 챙길 필요가 없다. 1.0.0 부터 WinAppSDK 를 게시본에 함께 담으므로
; (csproj 의 WindowsAppSDKSelfContained=true) prerequisites\ 폴더도 쓰지 않는다.
; ============================================================

#define MyAppName "NewSchool"
#define MyAppVersion "1.0.1"
#define MyAppPublisher "Centwon"
#define MyAppExeName "NewSchool.exe"
#define MyAppURL "https://github.com/Centwons/NewSchool"

; 게시 출력 폴더 (상대 경로) — win-x64.pubxml 의 PublishDir 과 일치해야 한다.
; 옛 VS 기본값(..\bin\Release\Publish)이 남아 있어 1.0.0 컴파일이 "Source file does not exist" 로 멎었다.
; TargetFramework 를 올리면 이 경로도 함께 고쳐야 한다.
#define PublishDir "..\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; 설치 시 폴더 선택 허용
AllowNoIcons=yes
; 사용권 계약 — 설치 마법사에 동의 페이지를 띄운다.
;
; 이것이 없으면 최종 사용자가 어떤 조건에도 동의하지 않은 채 설치가 끝난다.
; 앱에 담아 함께 배포하는 Windows App SDK 의 라이선스(§3(b)(ii))는 "배포자와 외부
; 최종 사용자가 Microsoft 를 이 계약만큼 보호하는 약관에 동의하도록 할 것" 을
; 재배포 조건으로 걸고 있어, 동의 지점 자체가 필요하다.
LicenseFile=LICENSE_ko.txt
; 출력 설치파일
OutputDir=Output
OutputBaseFilename=NewSchoolSetup_{#MyAppVersion}
; 아이콘
SetupIconFile=..\newschool.ico
UninstallDisplayIcon={app}\bin\{#MyAppExeName}
; 압축
Compression=lzma2/ultra64
SolidCompression=yes
; 권한 (관리자 불필요)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; 최소 Windows 10 1809
MinVersion=10.0.17763
; 64비트 전용
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; UI
WizardStyle=modern
; 버전 정보
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 설치 프로그램

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 바로 가기 만들기"; GroupDescription: "추가 작업:"; Flags: unchecked
Name: "startupicon"; Description: "Windows 시작 시 자동 실행"; GroupDescription: "추가 작업:"; Flags: unchecked

[Files]
; === 게시본 전체를 {app}\bin 아래에 통째로 넣는다 ===
;
; 자체 포함(WindowsAppSDKSelfContained=true) 게시본은 WinAppSDK 런타임까지 들어와
; 파일이 50개 안팎이 된다. 그것을 설치 폴더 루트에 쏟으면 어지러우므로 한 겹 내린다
; — 루트에는 bin\ 과 언인스톨러만 남는다.
;
; 확장자별로 나열하지 않는 이유: 자체 포함 게시본에는 RestartAgent.exe·workloads.*.json·
; en-us\·ko-KR\·Microsoft.UI.Xaml\ 처럼 옛 목록(dll/pri/winmd)에 걸리지 않는 것들이 있어,
; 하나라도 빠지면 실행이 깨진다. 불필요 파일은 CleanPublishOutput 타겟이 이미 걷어냈다.
; (secrets.json 은 어셈블리에 내장되므로 게시 폴더에 없다 — 따로 복사하지 않는다.)
Source: "{#PublishDir}\*"; DestDir: "{app}\bin"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\bin\{#MyAppExeName}"
Name: "{group}\{#MyAppName} 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\bin\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\bin\{#MyAppExeName}"; Tasks: startupicon

[Run]
; 설치 완료 후 앱 실행 옵션
Filename: "{app}\bin\{#MyAppExeName}"; Description: "{#MyAppName} 실행"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 실행 중 생성된 파일 정리 (사용자 데이터는 건드리지 않음)
Type: filesandordirs; Name: "{app}\bin"

[Code]
// 런타임 설치 단계는 없다.
//
// 1.0.0 부터 WinAppSDK 런타임을 게시본에 함께 담는다(WindowsAppSDKSelfContained=true).
// 예전에는 prerequisites\WindowsAppRuntimeInstall-x64.exe(약 107MB)를 설치 파일에 안고
// 다니면서 설치 후 런타임 유무를 검사해 필요하면 깔았는데, 그 검사에는 함정이 있었다 —
// 'Microsoft.WindowsAppRuntime.2' 라는 이름을 2.2·2.3·2.4 가 공유해서 버전까지 비교하지
// 않으면 2.2 만 깔린 PC 도 "이미 설치됨" 으로 보였고, 앱은 시작조차 못 했다.
// 개발 PC 에는 최신 런타임이 있어 이 부류는 로컬 테스트로 드러나지 않는다.
//
// 자체 포함으로 바꾸면서 그 함정이 통째로 사라졌고, 설치 파일도 122.8MB → 29MB 대로 줄었다.
// 런타임 관련 코드를 남겨 두면 없는 파일을 찾아 헛돌므로 함께 걷어낸다.
