using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NewSchool.Helpers;

/// <summary>
/// 이 데이터 폴더를 쓰는 앱이 <b>한 번에 하나만</b> 뜨게 한다.
///
/// <para>예전에는 아무 제한이 없어 같은 컴퓨터에서 앱이 두 개 떴다. 겉보기에는 창이 둘일
/// 뿐이지만, <see cref="SettingsDb"/> 는 설정을 <b>프로세스 메모리에 캐시</b>하기 때문에
/// 서로의 저장을 보지 못한다 — 한쪽에서 담임 학급을 고치고 다른 쪽에서 아무 설정이나
/// 저장하면, 캐시에 남아 있던 옛 값이 방금 고친 것을 덮는다. 같은 DB 파일에 두 프로세스가
/// 계속 쓰는 것도 그 자체로 이득이 없다.</para>
///
/// <para><b>데이터 폴더별로</b> 잠근다. 설치본과 포터블본은 서로 다른 폴더를 보므로 함께
/// 띄워도 부딪히지 않는다 — 그 둘을 막으면 자료를 옮겨 보는 정상적인 사용이 막힌다.</para>
///
/// <para>이름은 <c>Local\</c> 로 시작해 <b>로그인 세션 안에서만</b> 유효하다. 한 PC 를 여러
/// 교사가 각자 계정으로 쓰는 경우(전환 사용자), 서로의 실행을 막지 않는다.</para>
/// </summary>
public static class SingleInstance
{
    private static Mutex? _mutex;
    private static EventWaitHandle? _showSignal;
    private static Action? _onShowRequested;

    /// <summary>
    /// 잠금을 잡는다. <b>false 면 이미 다른 창이 떠 있다</b>는 뜻이므로 조용히 끝내야 한다
    /// (이때 먼저 그 창을 앞으로 불러낸다 — 아이콘을 눌렀는데 아무 일도 없으면 고장으로 보인다).
    /// </summary>
    public static bool TryAcquire(string dataPath)
    {
        var key = KeyFor(dataPath);

        try
        {
            _mutex = new Mutex(initiallyOwned: false, $"Local\\NewSchool.Instance.{key}");
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\NewSchool.Show.{key}");
        }
        catch (Exception ex)
        {
            // 잠금을 만들지 못하면 막지 않는다 — 두 개 뜨는 것보다 아예 안 뜨는 쪽이 나쁘다.
            Debug.WriteLine($"[SingleInstance] 잠금 생성 실패: {ex.Message}");
            return true;
        }

        // 앞 프로세스가 막 끝나는 중일 수 있다. 특히 [복원] 뒤 자동 재시작
        // (AppInstance.Restart)은 옛 프로세스가 사라지자마자 새것을 띄우므로,
        // 한 번 보고 포기하면 재시작이 실패한 것처럼 보인다. 잠깐 기다려 본다.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (_mutex.WaitOne(TimeSpan.Zero)) return true;
            }
            catch (AbandonedMutexException)
            {
                // 앞 프로세스가 잠금을 든 채 죽었다 — 이제 우리 것이다.
                return true;
            }

            Thread.Sleep(200);
        }

        RequestShowExistingWindow();
        return false;
    }

    /// <summary>
    /// 뒤늦게 실행된 쪽이 보내는 "창 좀 띄워 달라" 신호를 받기 시작한다.
    /// 창이 만들어진 뒤에 부른다(그 전에는 띄울 창이 없다).
    /// </summary>
    public static void ListenForShowRequests(Action onShowRequested)
    {
        if (_showSignal == null) return;

        _onShowRequested = onShowRequested;

        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    _showSignal.WaitOne();
                    _onShowRequested?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SingleInstance] 신호 처리 실패: {ex.Message}");
                    return;
                }
            }
        })
        {
            IsBackground = true,   // 앱이 끝날 때 함께 사라진다
            Name = "SingleInstance.ShowListener",
        };

        thread.Start();
    }

    /// <summary>이미 떠 있는 창을 앞으로 불러낸다.</summary>
    private static void RequestShowExistingWindow()
    {
        try
        {
            // 포그라운드 권한을 먼저 넘겨준다 — 이 프로세스가 지금 앞에 있으므로,
            // 이것 없이는 상대가 SetForegroundWindow 를 불러도 작업 표시줄만 깜박인다.
            AllowSetForegroundWindow(ASFW_ANY);
            _showSignal?.Set();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SingleInstance] 기존 창 호출 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 데이터 폴더 → 잠금 이름. 폴더 경로를 그대로 쓸 수 없어(구분자·길이·대소문자) 해시한다.
    /// 대소문자와 끝의 구분자는 무시한다 — 같은 폴더를 다르게 적었다고 잠금이 갈리면 안 된다.
    /// </summary>
    internal static string KeyFor(string dataPath)
    {
        var normalized = (dataPath ?? "")
            .Trim()
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash, 0, 8);   // 16자면 충분히 갈린다
    }

    private const uint ASFW_ANY = 0xFFFFFFFF;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);
}
