using System;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace NewSchool.Helpers;

/// <summary>
/// 자료 파일(SQLite)이 막혔을 때의 예외를 <b>사용자가 할 수 있는 말</b>로 옮긴다.
/// 파일·폴더 쪽을 맡는 <see cref="FileErrorText"/> 의 짝이다.
///
/// <para>이 축(51차, 스키마 세대)에서 가장 중요한 것은 <b>"no such column"</b> 이다.
/// 이 프로젝트는 <b>ALTER 마이그레이션을 두지 않기로</b> 했으므로(Board.cs 의 결정 주석),
/// 새 판이 열을 하나 더 쓰기 시작하면 <b>옛 판이 만든 자료 파일에서는 그 화면이 통째로
/// 실패한다</b>. 그때 사용자가 보던 것은 <c>SQLite Error 1: 'no such column: IsPinned'</c>
/// — 무엇이 잘못됐는지도, 자기 자료가 무사한지도 알 수 없는 문장이었다.</para>
///
/// <para>원문은 버리지 않는다. 부르는 쪽이 괄호로 덧붙인다(49차와 같은 규칙).</para>
/// </summary>
public static class DbErrorText
{
    // SQLite 결과 코드 — https://www.sqlite.org/rescode.html
    private const int Error = 1;        // SQLITE_ERROR (문법·이름 오류 전반)
    private const int Busy = 5;         // SQLITE_BUSY
    private const int Locked = 6;       // SQLITE_LOCKED
    private const int ReadOnly = 8;     // SQLITE_READONLY
    private const int Corrupt = 11;     // SQLITE_CORRUPT
    private const int Full = 13;        // SQLITE_FULL
    private const int CantOpen = 14;    // SQLITE_CANTOPEN
    private const int NotADb = 26;      // SQLITE_NOTADB

    /// <summary>
    /// 해 줄 말이 있으면 그 문장, 없으면 null(그때는 원문을 그대로 쓴다).
    /// </summary>
    public static string? Explain(Exception? ex)
    {
        // 같은 상황이 두 길로 온다 — 쿼리가 칸 이름을 직접 대면 SQLite 가 막고(아래),
        // SELECT * 로 읽어 매핑에서 찾으면 이 형(型)으로 온다.
        if (ex is MissingColumnException missingColumn)
            return OldFileText(missingColumn.ColumnName, "칸");

        if (ex is not SqliteException sql) return null;

        // ⚠ 이름이 없는 열·테이블은 코드가 전부 SQLITE_ERROR(1) 라 <b>문구로</b> 갈라야 한다.
        string? missing = MissingName(sql.Message, "no such column");
        if (missing != null) return OldFileText(missing, "칸");

        missing = MissingName(sql.Message, "no such table");
        if (missing != null) return OldFileText(missing, "항목");

        return sql.SqliteErrorCode switch
        {
            Busy or Locked =>
                "자료 파일을 다른 곳에서 쓰고 있어 기다리다 실패했습니다. 잠시 뒤 다시 시도하세요.\n" +
                "OneDrive 같은 동기화 폴더에 자료를 두었다면 동기화가 끝난 뒤에 하세요.",

            ReadOnly =>
                "자료 폴더가 읽기 전용이라 저장하지 못했습니다. 폴더 권한을 확인하거나 다른 폴더를 쓰세요.",

            Full =>
                "저장할 공간이 부족합니다. 디스크를 정리한 뒤 다시 시도하세요.",

            CantOpen =>
                "자료 파일을 열지 못했습니다. 파일이 옮겨졌거나 폴더 권한이 막혔는지 확인하세요.",

            Corrupt or NotADb =>
                "자료 파일이 손상되었습니다. [설정] > [백업/복원] 에서 백업을 복원하세요.",

            // SQLITE_ERROR 중 위에서 걸리지 않은 것(문법 오류 등)은 사용자가 할 수 있는 일이 없다.
            Error => null,

            _ => null,
        };
    }

    /// <summary>
    /// 옛 자료 파일에서 무엇이 빠졌을 때의 말. <b>자료가 지워진 것이 아니라는 문장</b>이
    /// 반드시 함께 가야 한다 — 그 말이 없으면 사용자는 백업 복원부터 시도한다.
    /// </summary>
    private static string OldFileText(string name, string kind) =>
        $"자료 파일에 '{name}' {kind}이 없습니다. 지금 판보다 오래된 자료 파일일 때 이렇게 됩니다.\n" +
        "담긴 자료가 지워진 것은 아닙니다 — 이 화면만 열리지 않습니다.";

    /// <summary>
    /// <c>no such column: IsPinned</c> 처럼 뒤에 붙는 이름을 뽑는다.
    /// 표(<c>Post.IsPinned</c>)로 올 때는 뒷마디만 남긴다 — 사용자에게는 칸 이름이면 충분하다.
    /// </summary>
    private static string? MissingName(string message, string marker)
    {
        var m = Regex.Match(message, Regex.Escape(marker) + @":\s*([A-Za-z0-9_.]+)");
        if (!m.Success) return null;

        string name = m.Groups[1].Value.TrimEnd('.', '\'', '"');
        int dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
    }
}
