using System;
using System.IO;

namespace NewSchool.Helpers;

/// <summary>
/// 파일·폴더 작업이 막혔을 때의 예외를 <b>사용자가 할 수 있는 말</b>로 옮긴다.
///
/// <para>지금까지는 예외 문구를 그대로 보여 줬다. "다른 프로세스에서 사용 중이므로 프로세스가
/// 파일에 액세스할 수 없습니다", "경로에 대한 액세스가 거부되었습니다" — 무슨 일이 일어났는지는
/// 적혀 있지만 <b>무엇을 하면 되는지가 없다</b>. 대부분은 그 파일을 엑셀에서 닫으면 끝나는
/// 일이었다.</para>
///
/// <para>원문을 버리지는 않는다. 부르는 쪽이 괄호로 덧붙여, 도움을 청할 때 쓸 수 있게 한다.</para>
/// </summary>
public static class FileErrorText
{
    /// <summary>
    /// 이 예외에 해 줄 말이 있으면 그 문장, 없으면 null(그때는 원문을 그대로 쓴다).
    /// </summary>
    public static string? Explain(Exception? ex) => ex switch
    {
        null => null,

        // 권한은 형(型)만으로 확실하다.
        UnauthorizedAccessException =>
            "이 폴더에 파일을 쓸 권한이 없습니다. 다른 폴더를 쓰거나, 컴퓨터 관리자에게 문의하세요.",

        PathTooLongException =>
            "경로가 너무 깁니다. 파일 이름이나 상위 폴더 이름을 줄여 보세요.",

        DirectoryNotFoundException =>
            "폴더를 찾을 수 없습니다. 폴더가 옮겨졌거나 지워졌는지 확인하세요.",

        FileNotFoundException =>
            "파일을 찾을 수 없습니다. 파일이 옮겨졌거나 지워졌는지 확인하세요.",

        // 나머지 IOException 은 안에 든 Win32 오류 번호로 갈린다.
        IOException io => ExplainIoError(Win32CodeOf(io)),

        _ => null,
    };

    private static string? ExplainIoError(int win32Code) => win32Code switch
    {
        ErrorSharingViolation or ErrorLockViolation =>
            "파일이 다른 프로그램에서 열려 있습니다. 엑셀·한글 등에서 그 파일을 닫고 다시 시도하세요.",

        ErrorDiskFull or ErrorHandleDiskFull =>
            "저장할 공간이 부족합니다. 디스크를 정리한 뒤 다시 시도하세요.",

        ErrorAccessDenied =>
            "이 폴더에 파일을 쓸 권한이 없습니다. 다른 폴더를 쓰거나, 컴퓨터 관리자에게 문의하세요.",

        ErrorFilenameExcedRange =>
            "경로가 너무 깁니다. 파일 이름이나 상위 폴더 이름을 줄여 보세요.",

        _ => null,
    };

    /// <summary>
    /// HRESULT 에서 Win32 오류 번호를 꺼낸다. Win32 에서 온 것이 아니면 -1.
    /// (<c>0x8007xxxx</c> 의 <c>xxxx</c> 가 오류 번호다.)
    /// </summary>
    private static int Win32CodeOf(Exception ex)
        => (ex.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000)
            ? ex.HResult & 0xFFFF
            : -1;

    private const int ErrorAccessDenied = 5;          // ERROR_ACCESS_DENIED
    private const int ErrorSharingViolation = 32;     // ERROR_SHARING_VIOLATION — 다른 프로그램이 열어 둠
    private const int ErrorLockViolation = 33;        // ERROR_LOCK_VIOLATION
    private const int ErrorHandleDiskFull = 39;       // ERROR_HANDLE_DISK_FULL
    private const int ErrorDiskFull = 112;            // ERROR_DISK_FULL
    private const int ErrorFilenameExcedRange = 206;  // ERROR_FILENAME_EXCED_RANGE
}
