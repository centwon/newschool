<#
.SYNOPSIS
    NewSchool(WinUI 3) 앱을 프로그램으로 띄우고 몰기 위한 드라이버.

.DESCRIPTION
    이 앱은 Windows 전용 WinUI 3 데스크톱 앱이라, 브라우저 드라이버도 Electron
    Playwright 도 붙지 않는다. 대신 Windows 자체의 UI Automation(UIA)으로 붙는다 —
    WinUI 3 컨트롤은 UIA 트리에 그대로 올라온다.

    ⚠ 개발 빌드는 시작 메뉴에 등록되지 않아 computer-use 계열 도구가 "설치된 앱"으로
      인식하지 못한다. 그래서 화면 제어 권한을 받는 경로로는 이 앱을 몰 수 없고,
      이 스크립트가 유일한 자동 경로다.

.PARAMETER Command
    launch   앱을 띄우고 UIA 로 붙는다 (이미 떠 있으면 그 창에 붙는다)
    shot     창을 PNG 로 저장한다
    tree     UIA 트리를 덤프한다 (이름/타입/AutomationId)
    find     이름에 <Text> 가 들어간 요소를 찾는다
    click    이름이 <Text> 인 요소를 누른다 (Invoke → SelectionItem → 좌표 클릭 순)
    text     이름에 <Text> 가 들어간 요소들의 값을 읽는다
    quit     앱을 닫는다

.EXAMPLE
    pwsh -File driver.ps1 launch
    pwsh -File driver.ps1 shot -Out artifacts/home.png
    pwsh -File driver.ps1 find -Text 누가
    pwsh -File driver.ps1 click -Text "누가 기록"
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('launch', 'shot', 'tree', 'find', 'click', 'text', 'windows', 'quit',
                 'keys', 'focused', 'tabs', 'nameless')]
    [string]$Command = 'launch',

    [string]$Text = '',
    [string]$Out = '',
    [int]$Depth = 6,
    [int]$TimeoutSec = 30,

    # tabs 명령이 Tab 을 몇 번 누를지.
    [int]$N = 25,

    # 어느 창을 볼지. 제목의 일부만 주면 된다. 비우면 메인 창.
    # ⚠ 이 앱의 기록 편집·일괄 입력은 ContentDialog 가 아니라 별도 Window 다 —
    #   MainWindowHandle 만 보면 그 창들은 영영 안 잡힌다.
    [string]$Window = ''
)

$ErrorActionPreference = 'Stop'

# 저장소 뿌리 = 이 스크립트에서 세 단계 위 (.claude/skills/run-newschool/)
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$Exe = Join-Path $RepoRoot 'bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\NewSchool.exe'
$ArtifactDir = Join-Path $RepoRoot 'artifacts'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

# PrintWindow 는 다른 창에 가려져도 대상 창만 그린다. CopyFromScreen 은 가려지면
# 가린 창이 찍히므로 쓰지 않는다 — 헤드리스가 아닌 실제 데스크톱에서 도는 탓이다.
if (-not ('Native' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Native {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
'@
}

function Get-App {
    Get-Process NewSchool -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } |
        Select-Object -First 1
}

# 이 프로세스의 최상위 창 전부. 편집 창들이 별도 Window 라 메인 창만 봐서는 안 된다.
function Get-Windows {
    $p = Get-App
    if ($null -eq $p) { throw "앱이 떠 있지 않다. 먼저 'launch' 를 실행할 것." }

    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)

    [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $cond)
}

# 메인 창 = 이 프로세스의 최상위 창 중 "소유자가 없는" 것.
#
# ⚠ Process.MainWindowHandle 을 믿으면 안 된다. 기록 편집 창이 열려 있으면 Windows 가
#   그쪽을 MainWindow 로 골라, launch 가 편집 창 제목을 찍고 click 이 메인 화면의
#   버튼을 못 찾는다(실제로 겪었다). 대화 창은 소유자가 있고 메인 창은 없다 —
#   이것이 제목 문자열에 기대지 않는 유일한 구분법이다.
function Get-MainWindow {
    $wins = @(Get-Windows)
    if ($wins.Count -eq 0) { throw "이 프로세스에 최상위 창이 없다." }

    foreach ($w in $wins) {
        $h = [IntPtr]$w.Current.NativeWindowHandle
        if ($h -eq [IntPtr]::Zero) { continue }
        if ([Native]::GetWindow($h, 4) -eq [IntPtr]::Zero) { return $w }   # 4 = GW_OWNER
    }
    return $wins[0]
}

function Get-Root {
    if (-not $Window) { return Get-MainWindow }

    foreach ($w in Get-Windows) {
        if ($w.Current.Name -like "*$Window*") { return $w }
    }
    $names = (Get-Windows | ForEach-Object { $_.Current.Name }) -join ' | '
    throw "'$Window' 제목의 창이 없다. 지금 떠 있는 창: $names"
}

function Invoke-Windows {
    foreach ($w in Get-Windows) {
        '{0}  (HWnd={1})' -f $w.Current.Name, $w.Current.NativeWindowHandle
    }
}

function Invoke-Launch {
    $p = Get-App
    if ($p) { Write-Host "이미 떠 있다: PID=$($p.Id)"; }
    else {
        if (-not (Test-Path $Exe)) {
            throw "빌드 산출물이 없다: $Exe`n먼저 dotnet build NewSchool.csproj -p:Platform=x64 를 실행할 것."
        }
        Start-Process $Exe | Out-Null
    }

    # 창이 뜨고 UIA 트리가 채워질 때까지 기다린다. WinUI 3 는 프로세스가 살아난 뒤에도
    # 몇 초 동안 MainWindowHandle 이 0 이다 — 고정 Sleep 은 느리거나 모자란다.
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $p = Get-App
        if ($p) {
            $root = $null
            try { $root = Get-MainWindow } catch { }
            if ($root) {
                Write-Host "PID=$($p.Id) HWnd=$($root.Current.NativeWindowHandle)"
                Write-Host "Title='$($root.Current.Name)'"
                $others = @(Get-Windows) | Where-Object {
                    $_.Current.NativeWindowHandle -ne $root.Current.NativeWindowHandle }
                foreach ($o in $others) { Write-Host "  (열려 있는 다른 창: '$($o.Current.Name)')" }
                return
            }
        }
        Start-Sleep -Milliseconds 500
    }
    throw "제한 시간 $TimeoutSec 초 안에 창이 뜨지 않았다."
}

function Invoke-Shot {
    $path = if ($Out) { $Out } else {
        Join-Path $ArtifactDir ("shot-{0:yyyyMMdd-HHmmss}.png" -f (Get-Date))
    }
    if (-not [System.IO.Path]::IsPathRooted($path)) { $path = Join-Path $RepoRoot $path }
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null

    # -Window 를 주면 그 창을, 아니면 메인 창을 찍는다.
    $h = [IntPtr](Get-Root).Current.NativeWindowHandle
    if ($h -eq [IntPtr]::Zero) { throw "창 핸들을 못 얻었다." }
    [Native]::ShowWindow($h, 9) | Out-Null      # SW_RESTORE — 최소화돼 있으면 빈 그림이 찍힌다
    [Native]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 400

    $r = New-Object Native+RECT
    [Native]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $ht = $r.B - $r.T
    if ($w -le 0 -or $ht -le 0) { throw "창 크기를 못 읽었다 ($w x $ht)." }

    $bmp = New-Object System.Drawing.Bitmap($w, $ht)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc()
    # flags=2 (PW_RENDERFULLCONTENT) — 이게 없으면 WinUI 3 창이 통째로 까맣게 찍힌다.
    [Native]::PrintWindow($h, $dc, 2) | Out-Null
    $g.ReleaseHdc($dc); $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()

    Write-Host $path
}

function Get-Elements {
    param($root, [int]$maxDepth)

    $out = New-Object System.Collections.ArrayList
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker

    function Walk($el, $depth) {
        if ($null -eq $el -or $depth -gt $maxDepth) { return }
        try {
            $c = $el.Current
            [void]$out.Add([pscustomobject]@{
                Depth = $depth
                Type  = ($c.ControlType.ProgrammaticName -replace '^ControlType\.', '')
                Name  = $c.Name
                Id    = $c.AutomationId
                El    = $el
            })
        } catch { return }

        $child = $walker.GetFirstChild($el)
        while ($null -ne $child) {
            Walk $child ($depth + 1)
            $child = $walker.GetNextSibling($child)
        }
    }

    Walk $root 0
    return $out
}

function Invoke-Tree {
    $items = Get-Elements (Get-Root) $Depth
    foreach ($i in $items) {
        if (-not $i.Name -and -not $i.Id) { continue }   # 이름도 id 도 없는 껍데기는 건너뛴다
        '{0}{1}  Name="{2}"  Id="{3}"' -f ('  ' * $i.Depth), $i.Type, $i.Name, $i.Id
    }
}

function Invoke-Find {
    if (-not $Text) { throw "-Text 가 필요하다." }
    $items = Get-Elements (Get-Root) $Depth
    $hit = $items | Where-Object { $_.Name -like "*$Text*" -or $_.Id -like "*$Text*" }
    if (-not $hit) { Write-Host "없음: '$Text'"; return }
    foreach ($i in $hit) { '{0}  Name="{1}"  Id="{2}"' -f $i.Type, $i.Name, $i.Id }
}

function Invoke-Click {
    if (-not $Text) { throw "-Text 가 필요하다." }
    $items = Get-Elements (Get-Root) $Depth
    $target = $items | Where-Object { $_.Name -eq $Text -or $_.Id -eq $Text } | Select-Object -First 1
    if (-not $target) {
        $target = $items | Where-Object { $_.Name -like "*$Text*" } | Select-Object -First 1
    }
    if (-not $target) { throw "'$Text' 을(를) 못 찾았다. 'tree' 로 이름을 확인할 것." }

    # ⚠ 이름으로 찾으면 대개 Button 이 아니라 그 안의 Text 가 잡힌다. Text 에는 누를
    #   패턴이 없으므로, 패턴이 있는 조상까지 올라가며 찾는다. 이걸 안 하면 좌표 클릭으로
    #   떨어지는데, 화면 배율이 100% 가 아니면 UIA 좌표와 커서 좌표가 어긋나 엉뚱한 데를
    #   누른다(이 프로젝트의 개발 PC 가 150% 다).
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $el = $target.El
    $pat = $null

    for ($hop = 0; $hop -lt 5 -and $null -ne $el; $hop++) {
        if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) {
            $pat.Invoke(); Write-Host "Invoke: $($el.Current.Name) (조상 $hop 단계)"; return
        }
        if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pat)) {
            $pat.Select(); Write-Host "Select: $($el.Current.Name)"; return
        }
        if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pat)) {
            $pat.Expand(); Write-Host "Expand: $($el.Current.Name)"; return
        }
        try { $el = $walker.GetParent($el) } catch { break }
    }
    $el = $target.El

    # 패턴이 없으면 화면 좌표로 누른다.
    $rect = $el.Current.BoundingRectangle
    if ($rect.IsEmpty) { throw "'$Text' 에 누를 방법이 없다(패턴 없음, 화면에도 없음)." }
    $x = [int]($rect.X + $rect.Width / 2); $y = [int]($rect.Y + $rect.Height / 2)
    [Native]::SetForegroundWindow((Get-App).MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($x, $y)
    Start-Sleep -Milliseconds 100
    Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, int e);' -Name M -Namespace W
    [W.M]::mouse_event(0x02, 0, 0, 0, 0); [W.M]::mouse_event(0x04, 0, 0, 0, 0)
    Write-Host "좌표 클릭: $($target.Name) ($x,$y)"
}

function Invoke-Text {
    if (-not $Text) { throw "-Text 가 필요하다." }
    $items = Get-Elements (Get-Root) $Depth
    foreach ($i in $items | Where-Object { $_.Name -like "*$Text*" }) {
        $v = $null
        if ($i.El.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$v)) {
            '{0}: "{1}"' -f $i.Name, $v.Current.Value
        } else {
            '{0}: (값 패턴 없음)' -f $i.Name
        }
    }
}

# ── 키보드 ───────────────────────────────────────────────────────────────
# 54차(키보드만으로 쓸 때) 를 하려고 붙였다. UIA 는 "무엇이 있는가" 만 답하고
# "Tab 을 눌러 거기까지 갈 수 있는가" 는 답하지 못한다 — 그건 눌러 봐야 안다.

# 창을 앞으로 끌어와야 SendKeys 가 들어간다 — SendKeys 는 포그라운드 창에만 간다.
#
# ⚠ SetForegroundWindow 하나로는 안 된다. Windows 는 백그라운드 프로세스가 창을 앞으로
#   끌어오는 것을 막는다 — 조용히 실패하고(반환값만 false) 키는 엉뚱한 창으로 간다.
#   실제로 첫 시도에서 Tab 이 전부 브라우저(MSN)로 갔다. UIA 의 SetFocus() 를 함께 쓰고,
#   끝나고 포그라운드가 정말 우리 창인지 **확인한 뒤에만** 키를 보낸다.
function Set-Foreground {
    $root = Get-Root
    $h = [IntPtr]$root.Current.NativeWindowHandle
    if ($h -eq [IntPtr]::Zero) { throw "창 핸들을 못 얻었다." }

    [Native]::ShowWindow($h, 9) | Out-Null       # SW_RESTORE
    try { $root.SetFocus() } catch { }
    [Native]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 400

    # 그래도 안 오면 지금 앞에 있는 창의 입력 큐에 우리를 붙였다 뗀다. Windows 는 같은
    # 입력 큐에 붙은 스레드끼리는 포그라운드를 넘기도록 허용한다 — 이것이 표준 우회다.
    if ([Native]::GetForegroundWindow() -ne $h) {
        $meTid = [Native]::GetCurrentThreadId()
        $fgTid = [Native]::GetWindowThreadProcessId([Native]::GetForegroundWindow(), [IntPtr]::Zero)
        if ($fgTid -ne 0 -and $fgTid -ne $meTid) {
            [Native]::AttachThreadInput($meTid, $fgTid, $true) | Out-Null
            [Native]::SetForegroundWindow($h) | Out-Null
            [Native]::AttachThreadInput($meTid, $fgTid, $false) | Out-Null
            Start-Sleep -Milliseconds 400
        }
    }

    $fg = [Native]::GetForegroundWindow()
    if ($fg -ne $h) {
        throw ("창을 앞으로 못 끌어왔다(포그라운드=$fg, 원하는 창=$h). " +
               "키를 보내면 엉뚱한 창으로 간다 — 앱 창을 한 번 눌러 앞으로 놓고 다시 할 것.")
    }
}

# 지금 포커스가 있는 요소. 이름이 없으면 그 사실 자체가 결과다.
function Get-Focused {
    try { $el = [System.Windows.Automation.AutomationElement]::FocusedElement } catch { return $null }
    if ($null -eq $el) { return $null }
    try {
        $c = $el.Current
        return [pscustomobject]@{
            Type = ($c.ControlType.ProgrammaticName -replace '^ControlType\.', '')
            Name = $c.Name
            Id   = $c.AutomationId
            Cls  = $c.ClassName
        }
    } catch { return $null }
}

function Format-Focused {
    param($f)
    if ($null -eq $f) { return '(포커스 없음)' }
    $nm = $f.Name
    if (-not $nm) { $nm = '<<이름없음>>' }
    return ('{0}  Name="{1}"  Id="{2}"  Class="{3}"' -f $f.Type, $nm, $f.Id, $f.Cls)
}

function Invoke-Focused {
    Set-Foreground
    Write-Host (Format-Focused (Get-Focused))
}

# SendKeys 문법이다: {TAB} {ENTER} {ESC} {F10} +{TAB}(Shift+Tab) ^s(Ctrl+S) {APPS}(메뉴 키)
function Invoke-Keys {
    if (-not $Text) { throw "-Text 가 필요하다. 예: -Text '{TAB}' / '+{TAB}' / '^s' / '{APPS}'" }
    Set-Foreground
    [System.Windows.Forms.SendKeys]::SendWait($Text)
    Start-Sleep -Milliseconds 400
    Write-Host ("보냄: {0}" -f $Text)
    Write-Host ("포커스: {0}" -f (Format-Focused (Get-Focused)))
}

# Tab 을 N 번 누르며 포커스가 어디로 가는지 적는다. 이것이 곧 Tab 순서다.
# -Text '+{TAB}' 을 주면 거꾸로 돈다.
#
# 멈추는 조건 둘을 구분해서 알린다:
#   · 한 바퀴  — 처음 자리로 되돌아왔다. 정상이다.
#   · 갇힘     — 같은 자리에서 8번 넘게 안 움직인다. Tab 이 거기서 죽은 것이다.
function Invoke-Tabs {
    Set-Foreground
    $key = if ($Text) { $Text } else { '{TAB}' }
    $first = $null
    $prev = $null
    $same = 0
    for ($i = 1; $i -le $N; $i++) {
        [System.Windows.Forms.SendKeys]::SendWait($key)
        Start-Sleep -Milliseconds 220
        $line = Format-Focused (Get-Focused)
        '{0,3}. {1}' -f $i, $line

        $before = $prev
        if ($line -eq $prev) {
            $same++
            if ($same -ge 8) { Write-Host "  ⚠ 갇혔다 — 같은 자리에서 $same 번 움직이지 않는다."; break }
        } else { $same = 0 }
        $prev = $line

        if ($i -eq 1) { $first = $line; continue }
        # ⚠ 이름 없는 컨트롤이 나란히 둘 있으면 줄이 똑같아 "한 바퀴"로 오인한다.
        #   세 번째부터, 그리고 **바로 앞 자리**가 첫 자리와 다를 때만 한 바퀴로 친다.
        if ($i -ge 3 -and $line -eq $first -and $before -ne $first) {
            Write-Host "  → 한 바퀴 돌았다($i 번째에서 처음으로 되돌아옴)."; break
        }
    }
}

# tree 가 감추는 것들을 본다 — Invoke-Tree 는 이름도 id 도 없는 요소를 건너뛴다.
# 아이콘만 있는 버튼이 바로 거기 숨는다.
function Invoke-Nameless {
    $items = Get-Elements (Get-Root) $Depth
    $bad = $items | Where-Object {
        $_.Type -in @('Button', 'CheckBox', 'RadioButton', 'ComboBox', 'Hyperlink', 'MenuItem') -and
        -not $_.Name
    }
    if (-not $bad) { Write-Host "이름 없는 조작 요소: 없음"; return }
    Write-Host ("이름 없는 조작 요소: {0} 개" -f @($bad).Count)
    foreach ($i in $bad) { '{0}{1}  Id="{2}"' -f ('  ' * $i.Depth), $i.Type, $i.Id }
}

# ⚠ 인스턴스를 전부 닫는다. 한 개만 닫으면 옛 빌드가 돌고 있는 인스턴스가 남아,
#   다음 launch 가 그쪽에 붙어 "고친 것이 안 보인다"가 된다(실제로 겪었다).
function Invoke-Quit {
    $procs = @(Get-Process NewSchool -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { Write-Host "떠 있지 않다."; return }

    foreach ($p in $procs) {
        try {
            $p.CloseMainWindow() | Out-Null
            Start-Sleep -Milliseconds 1500
            if (-not $p.HasExited) { $p.Kill() }
            Write-Host "닫음: PID=$($p.Id)"
        } catch {
            Write-Host "닫기 실패: PID=$($p.Id) — $($_.Exception.Message)"
        }
    }
}

switch ($Command) {
    'launch' { Invoke-Launch }
    'shot'   { Invoke-Shot }
    'tree'   { Invoke-Tree }
    'find'   { Invoke-Find }
    'click'  { Invoke-Click }
    'text'   { Invoke-Text }
    'windows'{ Invoke-Windows }
    'quit'   { Invoke-Quit }
    'keys'     { Invoke-Keys }
    'focused'  { Invoke-Focused }
    'tabs'     { Invoke-Tabs }
    'nameless' { Invoke-Nameless }
}
