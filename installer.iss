; NewSchool Inno Setup Script
; 사용법:
;   1. Visual Studio에서 게시(Publish) 실행
;   2. prerequisites 폴더에 런타임 설치 파일 다운로드:
;      - MicrosoftEdgeWebview2Setup.exe
;        https://go.microsoft.com/fwlink/p/?LinkId=2124703
;      - WindowsAppRuntimeInstall-x64.exe
;        https://aka.ms/windowsappsdk/1.8/latest/windowsappruntimeinstall-x64.exe
;   3. Inno Setup Compiler에서 이 파일 열기
;   4. Compile (Ctrl+F9) 실행
;   5. installer_output 폴더에 NewSchoolSetup_x.x.x.exe 생성됨

#define MyAppName "NewSchool"
#define MyAppVersion "1.0.4"
#define MyAppPublisher "Centwon"
#define MyAppExeName "NewSchool.exe"
#define MyAppDescription "교사를 위한 학급 경영 도우미"

; 게시 폴더 경로 (Visual Studio 게시 후 생성됨)
#define PublishDir "bin\Release\Publish"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=mailto:centwon@gmail.com
AppSupportURL=mailto:centwon@gmail.com
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; 설치 마법사 설정
AllowNoIcons=yes
DisableProgramGroupPage=yes
; 출력 설정
OutputDir=installer_output
OutputBaseFilename=NewSchoolSetup_{#MyAppVersion}
; 아이콘
SetupIconFile=newschool.ico
UninstallDisplayIcon={app}\newschool.ico
; 압축 (lzma2 = 최고 압축률)
Compression=lzma2/ultra64
SolidCompression=yes
; 권한 (Program Files 설치를 위해 admin)
PrivilegesRequired=admin
; 기타
WizardStyle=modern
ShowLanguageDialog=auto
; 최소 Windows 버전 (Windows 10 1809 이상)
MinVersion=10.0.17763

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 바로가기 만들기"; GroupDescription: "추가 작업:"
Name: "startupicon"; Description: "Windows 시작 시 자동 실행"; GroupDescription: "추가 작업:"; Flags: unchecked

[Files]
; === 앱 파일 ===
Source: "{#PublishDir}\NewSchool.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\NewSchool.pri"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\newschool.ico"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\newschool.png"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\secrets.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; === DLL 파일 ===
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion

; === Assets 폴더 (Jodit, 도움말, 아이콘 등) ===
Source: "{#PublishDir}\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs

; === 런타임 부트스트래퍼 (임시 폴더, 설치 후 삭제) ===
Source: "prerequisites\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall nocompression; Check: NeedsWebView2
Source: "prerequisites\WindowsAppRuntimeInstall-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall nocompression; Check: NeedsWindowsAppRuntime

; === 제외 목록 (아래 파일/폴더는 포함하지 않음) ===
; *.pdb          - 디버그 심볼
; *.xaml         - WinUI 빌드 부산물
; Pages\         - 빈 폴더 (빌드 부산물)
; Controls\      - 빈 폴더 (빌드 부산물)
; Dialogs\       - 빈 폴더 (빌드 부산물)
; Board\         - 빈 폴더 (빌드 부산물)
; Scheduler\     - 빈 폴더 (빌드 부산물)

[Icons]
; 시작 메뉴
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\newschool.ico"
Name: "{group}\{#MyAppName} 제거"; Filename: "{uninstallexe}"
; 바탕화면 (선택 시)
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\newschool.ico"; Tasks: desktopicon

[Registry]
; Windows 시작 시 자동 실행 (선택 시)
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
; Windows App SDK 런타임 설치 (없을 때만)
Filename: "{tmp}\WindowsAppRuntimeInstall-x64.exe"; Parameters: "--quiet"; StatusMsg: "Windows App SDK 런타임 설치 중..."; Check: NeedsWindowsAppRuntime; Flags: waituntilterminated
; WebView2 런타임 설치 (없을 때만)
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "WebView2 런타임 설치 중..."; Check: NeedsWebView2; Flags: waituntilterminated
; 설치 완료 후 앱 실행 옵션
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} 실행"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 앱이 생성한 임시 파일 정리 (사용자 데이터는 %USERPROFILE%\NewSchool에 보존)
Type: filesandordirs; Name: "{app}"

[Code]
// WebView2 런타임 설치 여부 확인
function NeedsWebView2: Boolean;
var
  Version: string;
begin
  // 시스템 전체 설치 확인
  Result := not RegQueryStringValue(HKLM,
    'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'pv', Version);

  if Result then
  begin
    // 사용자별 설치 확인
    Result := not RegQueryStringValue(HKCU,
      'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
      'pv', Version);
  end;

  if Result then
    Log('WebView2 런타임이 필요합니다.')
  else
    Log('WebView2 런타임 설치됨: ' + Version);
end;

// Windows App SDK 런타임 설치 여부 확인
function NeedsWindowsAppRuntime: Boolean;
var
  Version: string;
begin
  // Windows App SDK 1.x 런타임 패키지 확인
  Result := not RegQueryStringValue(HKLM,
    'SOFTWARE\Microsoft\WinAppSDK\Versions\1.8',
    'PackageVersion', Version);

  // 레지스트리에 없으면 패키지 폴더로 확인
  if Result then
  begin
    Result := not DirExists(ExpandConstant('{commonpf}\WindowsApps\Microsoft.WindowsAppRuntime.1.8_*'));
  end;

  if Result then
    Log('Windows App SDK 런타임이 필요합니다.')
  else
    Log('Windows App SDK 런타임 설치됨: ' + Version);
end;
