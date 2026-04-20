; ============================================================
; NewSchool Inno Setup Script
; ============================================================
; 사전 준비:
;   1. Visual Studio에서 게시(Publish) 실행
;   2. prerequisites\ 폴더에 다음 파일 배치:
;      - MicrosoftEdgeWebview2Setup.exe  (WebView2 부트스트래퍼, ~2MB)
;        다운로드: https://developer.microsoft.com/microsoft-edge/webview2/
;      - WindowsAppRuntimeInstall.exe    (Windows App SDK 런타임, ~3MB)
;        다운로드: https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
; ============================================================

#define MyAppName "NewSchool"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Centwon"
#define MyAppExeName "NewSchool.exe"
#define MyAppURL "https://github.com/Centwons/NewSchool"

; 게시 출력 폴더 (상대 경로)
#define PublishDir "..\bin\Release\Publish"

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

; === DLL 및 런타임 파일 (exe, dll, pri, winmd) ===
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.pri"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.winmd"; DestDir: "{app}"; Flags: ignoreversion

; === Assets 폴더 (아이콘, Jodit, 도움말) ===
Source: "{#PublishDir}\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs

; === Secrets (존재할 때만 — Google OAuth + NEIS API key) ===
Source: "{#PublishDir}\secrets.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; === 런타임 부트스트래퍼 (임시 폴더에 설치용으로만 복사) ===
Source: "prerequisites\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist
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
// WebView2 Runtime 설치 여부 확인
function IsWebView2Installed: Boolean;
var
  RegKey: String;
  Version: String;
begin
  Result := False;
  // 64비트 레지스트리 확인
  RegKey := 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BEE-271A5914D3B2}';
  if RegQueryStringValue(HKLM, RegKey, 'pv', Version) then
  begin
    Result := (Version <> '') and (Version <> '0.0.0.0');
    Exit;
  end;
  // 사용자 레지스트리 확인
  RegKey := 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BEE-271A5914D3B2}';
  if RegQueryStringValue(HKCU, RegKey, 'pv', Version) then
  begin
    Result := (Version <> '') and (Version <> '0.0.0.0');
  end;
end;

// Windows App SDK Runtime 설치 여부 확인
function IsWindowsAppSDKInstalled: Boolean;
var
  ResultCode: Integer;
begin
  // WindowsAppRuntime 패키지가 등록되어 있는지 PowerShell로 확인
  Result := Exec('powershell.exe',
    '-NoProfile -Command "if (Get-AppxPackage -Name ''Microsoft.WindowsAppRuntime.1.8'' -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    // WebView2 Runtime 설치
    if not IsWebView2Installed then
    begin
      if FileExists(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe')) then
      begin
        Log('WebView2 Runtime 설치 중...');
        Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'),
          '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        Log('WebView2 설치 결과: ' + IntToStr(ResultCode));
      end;
    end
    else
      Log('WebView2 Runtime 이미 설치됨');

    // Windows App SDK Runtime 설치
    if not IsWindowsAppSDKInstalled then
    begin
      if FileExists(ExpandConstant('{tmp}\WindowsAppRuntimeInstall-x64.exe')) then
      begin
        Log('Windows App SDK Runtime 설치 중...');
        Exec(ExpandConstant('{tmp}\WindowsAppRuntimeInstall-x64.exe'),
          '--quiet', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        Log('Windows App SDK Runtime 설치 결과: ' + IntToStr(ResultCode));
      end;
    end
    else
      Log('Windows App SDK Runtime 이미 설치됨');
  end;
end;
