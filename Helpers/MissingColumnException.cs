using System;

namespace NewSchool.Helpers;

/// <summary>
/// 자료 파일에 지금 판이 읽으려는 <b>칸이 없을 때</b> 던진다 — 51차(스키마 세대 축).
///
/// <para>같은 실패가 두 길로 온다. 쿼리가 칸 이름을 직접 대면 SQLite 가
/// <c>no such column</c> 으로 막고, <c>SELECT *</c> 로 읽어 매핑에서 찾으면 사전에서
/// 키를 못 찾는다. 뒤쪽은 예전에 <see cref="System.Collections.Generic.KeyNotFoundException"/>
/// 로 새어 나갔는데, 그 문장만 보고는 자료 파일 문제인지조차 알 수 없었다.
/// 두 길이 <see cref="DbErrorText"/> 에서 같은 말을 하도록 형(型)을 준다.</para>
/// </summary>
public sealed class MissingColumnException : Exception
{
    public MissingColumnException(string columnName)
        : base($"자료 파일에 '{columnName}' 칸이 없습니다.")
        => ColumnName = columnName;

    public MissingColumnException() { }

    public MissingColumnException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>없는 칸 이름.</summary>
    public string ColumnName { get; } = string.Empty;
}
