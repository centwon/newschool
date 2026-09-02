---
name: run-newschool
description: NewSchool(WinUI 3 학교관리 앱)을 빌드하고 실행하고 화면을 자동으로 몬다. 앱 실행·시작·구동, 스크린샷 찍기, 화면 확인, UI 변경이 실제로 보이는지 검증, 버튼 누르기, 창 목록 확인, 테스트 실행에 쓴다. run/start/build/test/screenshot the app.
---

# NewSchool 실행·조작

Windows 전용 **WinUI 3(WinAppSDK)** 데스크톱 앱이다. 브라우저 드라이버도 Electron
Playwright 도 붙지 않는다. **Windows UI Automation(UIA)** 으로 붙는 PowerShell
드라이버를 쓴다:

    .claude/skills/run-newschool/driver.ps1

아래 경로는 모두 **저장소 뿌리 기준**이다.

> ⚠ **computer-use 계열 도구로는 이 앱을 몰 수 없다.** 개발 빌드는 시작 메뉴에
> 등록되지 않아 `request_access` 가 `"NewSchool" doesn't match any installed or
> running application` 으로 거절한다(창 제목인 `미리벌중학교` 로 넣어도 같다).
> 이 드라이버가 유일한 자동 경로다.

## 준비

Windows 11 + .NET 10 SDK + WinAppSDK 워크로드. 별도 설치는 필요 없었다.

## 빌드

```bash
dotnet build NewSchool.csproj -p:Platform=x64
```

`-p:Platform=x64` 는 **필수**다. 빼면 복원부터 깨진다.

빌드 로그를 읽을 때 두 가지를 조심할 것:

- `dotnet build | tail` 은 **종료 코드를 삼킨다.** `경고 0개 / 오류 0개` 줄을 눈으로 볼 것.
- C# 오류가 나면 XAML 컴파일러가 `WMC0001 Unknown type ...` · `WMC9999 개체 참조가...`
  를 무더기로 쏟는다. **그건 위장이다** — 목록 맨 위의 `error CS####` 하나가 진짜다.

## 실행 (에이전트 경로) — 이걸 쓸 것

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .claude/skills/run-newschool/driver.ps1 launch
```

| 명령 | 하는 일 |
|---|---|
| `launch` | 띄우고 UIA 로 붙는다(이미 떠 있으면 그 창에 붙는다) |
| `windows` | 이 프로세스의 최상위 창 전부 |
| `shot -Out <경로>` | 창을 PNG 로 저장 (기본 `artifacts/shot-<시각>.png`) |
| `tree` | UIA 트리 덤프 (타입/Name/AutomationId) |
| `find -Text <말>` | 이름·id 에 그 말이 든 요소 |
| `click -Text <말>` | 그 요소를 누른다 |
| `text -Text <말>` | 그 요소들의 값을 읽는다 |
| `quit` | 닫는다 |

**편집 창처럼 별도 Window 를 볼 때는 `-Window <제목 일부>` 를 붙인다.**

### 실제로 돈 한 바퀴 (누가기록 편집 창까지)

**앱은 언제나 홈 화면에서 시작한다.** 화면 이동과 학생 선택을 건너뛸 수 없다 —
학생이 안 골라져 있으면 `[추가]` 는 안내만 띄우고 창을 열지 않는다.

이 블록을 PowerShell 에 그대로 넣으면 된다(이 순서로 실제로 돌았다):

```powershell
$D = ".claude\skills\run-newschool\driver.ps1"
function Drive { param([string[]]$a) & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $D @a }

Drive @('quit')                     # 옛 빌드가 남아 있으면 그쪽에 붙는다
Drive @('launch')
Drive @('click','-Text','학급')      # 상단 메뉴를 편다
Start-Sleep 2
Drive @('click','-Text','누가 기록')
Start-Sleep 4
Drive @('click','-Text','신민경')     # ← 학생 이름. 반이 다르면 tree 로 확인할 것
Start-Sleep 3
Drive @('click','-Text','추가')
Start-Sleep 3
Drive @('windows')
Drive @('shot','-Window','기록 편집','-Out','artifacts/attach.png')
Drive @('find','-Window','기록 편집','-Text','첨부')
Drive @('click','-Window','기록 편집','-Text','취소')   # ⚠ 저장하지 말 것
```

마지막 `find` 가 낸 것:

```
Text  Name="첨부파일 — '+' 로 붙입니다"  Id="HeaderTextBlock"
Text  Name="첨부한 파일이 없습니다."      Id="EmptyTextBlock"
```

**찍은 PNG 를 반드시 눈으로 볼 것.** 까맣거나 비어 있으면 띄우기부터 실패한 것이다.
`find` 가 이름을 찾았다는 것과 그것이 **화면에 제대로 배치됐다**는 것은 다른 얘기다.

### 다른 화면으로 가는 법

상단 메뉴는 `TabItem` 이라 `click` 이 `SelectionItemPattern` 으로 연다. 먼저 큰 메뉴
(`학급`·`수업`·`업무`·`설정`)를 누르고, 하위 항목 이름을 `tree` 로 확인해 누른다.
`학급` 아래에는 이런 것들이 있다:

```
TabItem  Name="학급 일지"      TabItem  Name="학생 정보"
TabItem  Name="누가 기록"      TabItem  Name="학생부 기록"
TabItem  Name="자리 배정"      TabItem  Name="학생정보 출력"
```

## 실행 (사람 경로)

```bash
powershell.exe -Command "Start-Process bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\NewSchool.exe"
```

창이 뜬다. 자동 확인에는 못 쓴다.

## 테스트

```bash
dotnet test NewSchool.Tests/NewSchool.Tests.csproj -p:Platform=x64
```

## Gotchas — 이 앱에서 실제로 걸린 것들

- **`click -Text` 는 대개 Button 이 아니라 그 안의 Text 를 잡는다.** Text 에는 누를
  패턴이 없다. 드라이버가 **조상으로 5단계까지 올라가며** `InvokePattern` 을 찾는다.
  출력의 `(조상 N 단계)` 가 몇 단계 올라갔는지 알려 준다.
- **좌표 클릭은 믿지 말 것.** 이 개발 PC 는 화면 배율이 **150%** 라 UIA 가 주는 좌표와
  커서 좌표가 어긋나 엉뚱한 데를 누른다. 실제로 `추가` 를 (1607,759) 로 눌렀더니
  아무 일도 일어나지 않았다. 드라이버가 좌표로 떨어지는 것은 **최후 수단**이고,
  그때는 결과를 의심할 것. (배율 문제는 WinUI 프레임워크 버그이므로 앱 코드로 고치지 말 것.)
- **기록 편집·일괄 입력은 `ContentDialog` 가 아니라 별도 `Window` 다.** `windows` 로
  확인하고 `-Window` 로 지정할 것. 반대로 **안내·확인 대화상자는 `ContentDialog` 라
  창이 늘지 않는다** — `windows` 에 아무것도 안 늘었는데 클릭이 먹은 것 같으면
  안내가 떴을 가능성이 크다. 스크린샷으로 확인할 것.
- **`Process.MainWindowHandle` 을 믿으면 안 된다.** 편집 창이 열려 있으면 Windows 가
  그쪽을 "메인 창"으로 골라, `launch` 가 편집 창 제목을 찍고 `click` 이 메인 화면의
  버튼을 못 찾는다. 드라이버는 **소유자(`GW_OWNER`)가 없는 창**을 메인으로 잡는다 —
  제목 문자열에 기대지 않는 유일한 구분법이다.
- **`quit` 은 인스턴스를 전부 닫는다.** 하나만 닫으면 옛 빌드가 도는 인스턴스가 남아,
  다음 `launch` 가 그쪽에 붙어 **"고친 것이 안 보인다"** 가 된다. 실제로 이것 때문에
  한 바퀴를 헛돌았다. **코드를 고쳤으면 `quit` → `build` → `launch` 순서를 지킬 것.**
- **앱은 언제나 홈 화면에서 시작한다.** 이전 세션의 화면 상태가 남아 있는 것처럼 보이면
  그것은 **닫지 않은 옛 인스턴스**다.
- **스크린샷은 `PrintWindow(flags=2)` 여야 한다.** `PW_RENDERFULLCONTENT` 를 빼면 WinUI 3
  창이 **통째로 까맣게** 찍힌다. `CopyFromScreen` 은 다른 창에 가리면 그것이 찍히므로 안 쓴다.
- **띄운 직후 몇 초간 `MainWindowHandle` 이 0 이다.** 드라이버가 최대 30초 폴링한다.
  고정 `Sleep` 로는 느리거나 모자란다.
- **PowerShell 5.1 이라 `&&`·`??`·삼항이 없다.** 파서 오류가 난다.
- **한글 출력이 콘솔 코드페이지에서 깨져 보인다**(`?대? ???덈떎`). 스크립트는 정상이다.
  판단은 깨지지 않는 부분(경로·PID·`Invoke:`)으로 할 것.
- **⚠ 이 앱은 사용자의 실제 데이터를 연다.** 학생 명단·기록이 진짜다. 확인만 할 때는
  **저장을 누르지 말고 [취소]로 닫을 것.**

## Troubleshooting

| 증상 | 원인 · 처방 |
|---|---|
| `Unexpected token ',' in expression or statement` | 해시테이블 안의 `-replace` 는 괄호로 감쌀 것 — `Type = ($x -replace 'a','b')` |
| `앱이 떠 있지 않다` | `launch` 먼저. 앱이 조용히 죽었으면 `dotnet build` 부터 다시 |
| `'<제목>' 제목의 창이 없다` | 오류 메시지가 지금 떠 있는 창 목록을 함께 낸다. 거기서 골라 `-Window` 에 넣을 것 |
| `click` 이 `(조상 0 단계)` 인데 아무 일도 없음 | 이름이 여럿에 걸렸다. `find` 로 확인하고 `AutomationId` 로 지정할 것 |
| 스크린샷이 까맣다 | `PrintWindow` flags 가 2 인지 확인. 창이 최소화돼 있으면 `ShowWindow(h,9)` 가 먼저 |
