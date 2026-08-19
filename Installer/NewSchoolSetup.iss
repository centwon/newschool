; ============================================================
; NewSchool Inno Setup Script
; ============================================================
; 사전 준비:
;   1. 게시: dotnet publish -c Release -p:Platform=x64
;      (win-x64.pubxml 이 자동 적용 — 출력 경로는 아래 PublishDir 과 맞춰 둘 것)
;   2. prerequisites\ 폴더에 다음 파일 배치:
;      - WindowsAppRuntimeInstall-x64.exe (Windows App SDK 런타임, 약 108MB — 설치 파일 크기의 대부분)
;        다운로드: https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
; ============================================================

#define MyAppName "NewSchool"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Centwon"
#define MyAppExeName "NewSchool.exe"
#define MyAppURL "https://github.com/Centwons/NewSchool"

; ⚠ 번들하는 런타임 버전 — csproj 의 Microsoft.WindowsAppSDK 버전과 반드시 같아야 한다.
;   SDK 를 올리면 (1) 이 값 (2) prerequisites\WindowsAppRuntimeInstall-x64.exe 둘 다 갱신할 것.
;   2.2 → 2.4 상향 때 프리레퀴지싯이 2.3 에 멈춰 있어 1.0.0 게시 직전에 잡혔다.
#define RequiredRuntimeVersion "2.4.0.0"

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
; 출력 설치파일
OutputDir=Output
OutputBaseFilename=NewSchoolSetup_{#MyAppVersion}
; 아이콘
SetupIconFile=..\newschool.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
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
; === 메인 실행 파일 ===
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; === DLL 및 런타임 파일 (dll, pri, winmd) ===
; winmd 는 Native AOT 게시본에 없다(CleanPublishOutput 이 정리). 없으면 건너뛴다 —
; 이 플래그가 없어서 "No files found matching *.winmd" 로 컴파일이 중단됐다.
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.pri"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.winmd"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; === Assets 폴더 (아이콘, Jodit, 도움말) ===
Source: "{#PublishDir}\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs

; === Secrets (존재할 때만 — Google OAuth + NEIS API key) ===
Source: "{#PublishDir}\secrets.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; === 런타임 부트스트래퍼 (임시 폴더에 설치용으로만 복사) ===
Source: "prerequisites\WindowsAppRuntimeInstall-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

; === 불필요 파일 명시적 제외 ===
; *.xaml, *.pdb, app.manifest, .gitignore, Properties\ 는 포함하지 않음
; (CleanPublishOutput 타겟이 이미 삭제하지만 이중 안전)

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
; 설치 완료 후 앱 실행 옵션
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} 실행"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 실행 중 생성된 파일 정리 (사용자 데이터는 건드리지 않음)
Type: filesandordirs; Name: "{app}\Assets"

[Code]
// Windows App SDK Runtime 설치 여부 확인
//
// ⚠ 이름만 보면 안 된다. 'Microsoft.WindowsAppRuntime.2' 는 2.2·2.3·2.4 가 공유하는
//   이름이라, 예전에 2.2 만 깔린 PC 에서도 "이미 설치됨" 으로 보이고
//   런타임 설치를 건너뛰어 앱이 시작도 못 하게 된다.
//   개발 PC 에는 최신 런타임이 있어 이 결함은 로컬 테스트로 절대 드러나지 않는다.
//   반드시 버전까지 비교한다.
function IsWindowsAppSDKInstalled: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('powershell.exe',
    '-NoProfile -Command "$v = Get-AppxPackage -Name ''Microsoft.WindowsAppRuntime.2'' -ErrorAction SilentlyContinue | ' +
    'Where-Object { [version]$_.Version -ge [version]''{#RequiredRuntimeVersion}'' }; if ($v) { exit 0 } else { exit 1 }"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    // Windows App SDK Runtime 설치
    //   실패해도 설치는 끝난다. 다만 그 PC 에서는 앱이 시작조차 못 하므로
    //   조용히 넘어가지 않고 반드시 알린다 — 원인을 모른 채 "안 켜진다" 가 되는 게 최악이다.
    if not IsWindowsAppSDKInstalled then
    begin
      if FileExists(ExpandConstant('{tmp}\WindowsAppRuntimeInstall-x64.exe')) then
      begin
        Log('Windows App SDK Runtime 설치 중...');
        Exec(ExpandConstant('{tmp}\WindowsAppRuntimeInstall-x64.exe'),
          '--quiet', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        Log('Windows App SDK Runtime 설치 결과: ' + IntToStr(ResultCode));

        if not IsWindowsAppSDKInstalled then
          MsgBox('Windows App SDK 런타임 {#RequiredRuntimeVersion} 설치에 실패했습니다.' + #13#10 +
                 '이 상태로는 NewSchool 이 실행되지 않습니다.' + #13#10#13#10 +
                 '아래에서 런타임을 직접 설치한 뒤 다시 실행해 주세요.' + #13#10 +
                 'https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads',
                 mbError, MB_OK);
      end
      else
        MsgBox('Windows App SDK 런타임 {#RequiredRuntimeVersion} 이 필요한데 설치 파일에 포함되어 있지 않습니다.' + #13#10 +
               '이 상태로는 NewSchool 이 실행되지 않습니다.' + #13#10#13#10 +
               '아래에서 런타임을 직접 설치한 뒤 다시 실행해 주세요.' + #13#10 +
               'https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads',
               mbError, MB_OK);
    end
    else
      Log('Windows App SDK Runtime {#RequiredRuntimeVersion} 이상 이미 설치됨');
  end;
end;
