using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <b>삼킨 예외의 유일한 기록이 <c>Debug.WriteLine</c> 이면 그것은 기록이 아니다.</b>
///
/// <para><c>Debug.WriteLine</c> 은 <c>[Conditional("DEBUG")]</c> 이라 <b>배포본에서 통째로
/// 사라진다</b>. 48차(자동 백업)와 49차(사용자 대면 오류)에서 같은 병을 한 자리씩 고쳤고,
/// 50차에 전수로 훑었다 — 자동 동기화가 몇 달째 멎어 있어도, 학생 명단을 못 읽어
/// 화면이 비어 있어도, 사용자가 "오류가 났다" 고 말해도 로그에는 아무 흔적이 없었다.</para>
///
/// <para>그래서 규칙을 세운다: <b>예외를 삼키는 자리는 <c>Logging.Log</c>·<c>FileLogger</c> 로
/// 남기거나, 사용자에게 알리거나, 다시 던진다.</b> 아무것도 안 하고 <c>Debug.WriteLine</c> 만
/// 찍는 것은 안 된다.</para>
///
/// <para>예외가 정당한 자리는 아래 <see cref="Allowed"/> 에 <b>이유와 함께</b> 적어 둔다.
/// 늘어나는 것만 막으면 된다 — 새 자리가 생기면 이 시험이 먼저 걸린다.</para>
/// </summary>
public class SilentFailureGuardTests
{
    /// <summary>
    /// <c>Debug.WriteLine</c> 만 남겨도 되는 자리와 그 이유. 값은 <b>허용 최대 개수</b>다.
    /// </summary>
    private static readonly Dictionary<string, (int Count, string Why)> Allowed = new()
    {
        // 로거 자신이 실패한 자리 — 로그로 알리면 같은 실패를 되풀이하거나 재귀에 빠진다.
        ["Logging/FileLogger.cs"] = (4, "로거 자신의 실패는 로그로 알릴 수 없다"),

        // 잠금 판정·신호 대기는 FileLogger 보다 먼저 도는 시작 경로다. 기록하려고 로거를
        // 만들면 그 생성자가 폴더를 만들다 또 터진다(49차).
        ["Helpers/SingleInstance.cs"] = (1, "로거보다 먼저 도는 시작 경로"),

        // 낱낱의 동기화 실패는 SyncResult 로 모여 화면(InfoBar)에 뜨고, 파일에는
        // SyncAllAsync 끝에서 한 줄로 모아 남긴다. 15분마다 도는 배경 작업이라
        // 낱낱이 남기면 같은 문장이 로그를 채운다.
        ["Google/GoogleSyncService.cs"] = (10, "실패는 결과에 모아 한 줄로 남긴다"),

        // 사용자가 브라우저를 닫은 정상 취소도 이 길로 온다.
        ["Google/GoogleAuthService.cs"] = (1, "정상 취소가 섞이는 자리"),

        // 네트워크·타임아웃은 화면에 그대로 안내하고 스스로 낫는다. 알 수 없는 오류만 남긴다.
        ["Dialogs/SchoolSearchDialog.xaml.cs"] = (3, "흔하고 스스로 낫는 네트워크 실패"),

        // 임시 파일 정리·창 크기·끌기 표식 — 실패해도 하려던 일이 그대로 되고, 다음에 다시 한다.
        ["Controls/RichTextEditor.xaml.cs"] = (1, "임시 인쇄 파일 정리(다음 인쇄 때 재시도)"),
        ["Controls/CourseTimetableBoard.xaml.cs"] = (1, "끌기 표식 생략 — 끌기 자체는 된다"),
        ["Dialogs/StudentLogDialog.xaml.cs"] = (1, "창 크기 지정 실패 — 기본 크기로 뜬다"),

        // WAL 체크포인트 실패는 백업을 막지 않고, DB 판정 실패는 '복원 대상 아님' 이라는 답이다.
        ["Settings.cs"] = (2, "백업 체크포인트·설정 DB 판정"),
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>기록·알림·재던지기 중 하나라도 하고 있으면 이 자리는 조용하지 않다.</summary>
    private static readonly Regex Records = new(
        @"FileLogger|Log\.(Error|Warning|Info|Debug|Critical)|LogError|LogWarning|" +
        @"UserErrorReporter|ReportAsync|MessageBox|InfoBar|ShowError|ShowWarning|ShowSectionError|" +
        @"ShowGlobalWarning|ShowTimetableFailure|ShowMessage|ContentDialog|LoadError|ShowSyncFailure|throw");

    private static readonly Regex CatchStart = new(@"catch\s*(\([^)]*\))?\s*\{");

    /// <summary>catch 본문(중괄호 짝을 세어 자른다)만 모은다.</summary>
    private static IEnumerable<string> CatchBodies(string source)
    {
        foreach (Match m in CatchStart.Matches(source))
        {
            int i = m.Index + m.Length, depth = 1, j = i;
            while (j < source.Length && depth > 0)
            {
                if (source[j] == '{') depth++;
                else if (source[j] == '}') depth--;
                j++;
            }
            yield return source.Substring(i, Math.Max(0, j - 1 - i));
        }
    }

    [Fact]
    public void 예외를_삼킬_때_Debug_WriteLine_만_남기지_않는다()
    {
        string root = RepoRoot();
        var offenders = new List<string>();
        var counts = new Dictionary<string, int>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.StartsWith("NewSchool.Tests/"))
                continue;

            string source = File.ReadAllText(file);

            foreach (var body in CatchBodies(source))
            {
                if (!body.Contains("Debug.WriteLine")) continue;   // 기록이 아예 없는 것은 별개 문제
                if (Records.IsMatch(body)) continue;               // 남기거나 알리거나 던진다

                counts[rel] = counts.GetValueOrDefault(rel) + 1;

                if (!Allowed.ContainsKey(rel))
                    offenders.Add($"{rel} — {string.Join(' ', body.Split()).Substring(0, Math.Min(80, body.Trim().Length))}");
            }
        }

        Assert.True(offenders.Count == 0,
            "예외를 삼키면서 Debug.WriteLine 만 남기는 자리가 생겼다(배포본에서는 흔적이 사라진다).\n" +
            "Logging.Log 로 남기거나 사용자에게 알리거나, 정당한 이유가 있으면 이 시험의 Allowed 에 적을 것:\n  " +
            string.Join("\n  ", offenders));

        // 허용한 자리도 늘어나면 안 된다 — 줄어드는 것은 언제나 환영이다.
        foreach (var (rel, count) in counts)
        {
            if (!Allowed.TryGetValue(rel, out var allowed)) continue;
            Assert.True(count <= allowed.Count,
                $"{rel} 에서 Debug.WriteLine 만 남기는 catch 가 {allowed.Count}개에서 {count}개로 늘었다 " +
                $"(허용 이유: {allowed.Why}).");
        }
    }
}
