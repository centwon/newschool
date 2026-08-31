using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace NewSchool.Board;

/// <summary>
/// Post 모델 - 게시글 데이터
/// </summary>
public class Post : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private int _no = -1;
    public int No
    {
        get => _no;
        set
        {
            if (_no != value)
            {
                _no = value;
                OnPropertyChanged();
            }
        }
    }

    private string _user = string.Empty;
    public string User
    {
        get => _user;
        set
        {
            if (_user != value)
            {
                _user = value;
                OnPropertyChanged();
            }
        }
    }

    private DateTime _dateTime = DateTime.Now;
    public DateTime DateTime
    {
        get => _dateTime;
        set
        {
            if (_dateTime != value)
            {
                _dateTime = value;
                OnPropertyChanged();
            }
        }
    }

    private string _category = string.Empty;
    public string Category
    {
        get => _category;
        set
        {
            if (_category != value)
            {
                _category = value;
                OnPropertyChanged();
            }
        }
    }

    private string _subject = string.Empty;
    public string Subject
    {
        get => _subject;
        set
        {
            if (_subject != value)
            {
                _subject = value;
                OnPropertyChanged();
            }
        }
    }

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
            }
        }
    }

    private byte[] _content = [];
    /// <summary>에디터 내용 (.flow 패키지 바이트). 검색은 <see cref="PlainText"/> 사용.</summary>
    public byte[] Content
    {
        get => _content;
        set
        {
            if (!ReferenceEquals(_content, value))
            {
                _content = value ?? [];
                OnPropertyChanged();
            }
        }
    }

    private string _plainText = string.Empty;
    /// <summary>검색·미리보기용 평문 (.flow Content 에서 추출).</summary>
    public string PlainText
    {
        get => _plainText;
        set
        {
            if (_plainText != value)
            {
                _plainText = value;
                OnPropertyChanged();
            }
        }
    }

    // ── 게시글 답글(스레드) 3종: RefNo · ReplyOrder · Depth ────────────────
    // 고전 게시판의 답글 정렬용 스키마(참조번호·답글순서·들여쓰기 깊이)인데,
    // <b>값을 넣는 코드가 한 곳도 없어 항상 0 이다.</b> 게시글 답글 UI 는 만들지 않았다.
    //
    // 이 보드는 로그인·사용자 구분이 없는 1인용 메모/자료 보관함이라(작성자는 늘
    // Settings.UserName), 여러 사람이 토론하는 답글 트리가 쓸 자리가 없다.
    // "쓴 글에 나중에 덧붙이기" 는 댓글 + 1단계 대댓글(Comment.ParentNo)이 담당한다.
    //
    // DB 컬럼은 DEFAULT 0 이라 두는 비용이 없고, 빼려면 테이블 재작성이 필요해
    // 신규 DB 와 기존 DB 의 스키마가 갈리므로 그대로 둔다.
    // 되살릴 일이 생기면 여기서부터 시작하면 된다.

    private int _refNo;
    public int RefNo
    {
        get => _refNo;
        set
        {
            if (_refNo != value)
            {
                _refNo = value;
                OnPropertyChanged();
            }
        }
    }

    private int _replyOrder;
    public int ReplyOrder
    {
        get => _replyOrder;
        set
        {
            if (_replyOrder != value)
            {
                _replyOrder = value;
                OnPropertyChanged();
            }
        }
    }

    private int _depth;
    public int Depth
    {
        get => _depth;
        set
        {
            if (_depth != value)
            {
                _depth = value;
                OnPropertyChanged();
            }
        }
    }

    private int _readCount;
    public int ReadCount
    {
        get => _readCount;
        set
        {
            if (_readCount != value)
            {
                _readCount = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _hasFile;
    public bool HasFile
    {
        get => _hasFile;
        set
        {
            if (_hasFile != value)
            {
                _hasFile = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FileIconVisibility));
            }
        }
    }

    private bool _hasComment;
    public bool HasComment
    {
        get => _hasComment;
        set
        {
            if (_hasComment != value)
            {
                _hasComment = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CommentIconVisibility));
            }
        }
    }

    private bool _isCompleted;
    /// <summary>
    /// 완료 여부 (메모용)
    /// </summary>
    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted != value)
            {
                _isCompleted = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isPinned;
    /// <summary>
    /// 중요 글 여부. 참이면 목록에서 항상 맨 앞으로 온다
    /// (지금 보고 있는 목록 안에서만 — 카테고리·주제 필터나 검색을 걸면 그 안에서 위로 온다).
    /// </summary>
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PinIconVisibility));
            }
        }
    }

    /// <summary>
    /// 값이 같은 새 <see cref="Post"/> 를 만든다. <b>캐시가 자기 것을 남에게 넘기지 않기 위한</b> 사본이다.
    ///
    /// <para>캐시가 담아 둔 인스턴스를 그대로 돌려주면, 그것을 받은 편집 화면이 제목·본문·
    /// 카테고리를 고치는 순간 <b>저장하지 않아도</b> 캐시가 함께 바뀐다. 취소하고 나와도
    /// 목록·상세에 고친 값이 비친다.</para>
    ///
    /// <para><see cref="Content"/>(.flow 바이트)는 배열 참조를 그대로 나눠 갖는다 —
    /// 수십KB~MB 라 복사가 비싸고, 쓰는 쪽은 언제나 <c>GetFlowBytes()</c> 가 만든
    /// <b>새 배열을 통째로 대입</b>하지 제자리에서 고치지 않기 때문이다.</para>
    ///
    /// <para><see cref="PropertyChanged"/> 구독자는 따라오지 않는다(사본은 새 객체다).</para>
    /// </summary>
    public Post Clone() => new()
    {
        No = No,
        User = User,
        DateTime = DateTime,
        Category = Category,
        Subject = Subject,
        Title = Title,
        Content = Content,
        PlainText = PlainText,
        RefNo = RefNo,
        ReplyOrder = ReplyOrder,
        Depth = Depth,
        ReadCount = ReadCount,
        HasFile = HasFile,
        HasComment = HasComment,
        IsCompleted = IsCompleted,
        IsPinned = IsPinned,
    };

    // UI 바인딩용 속성
    public Visibility FileIconVisibility => HasFile ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CommentIconVisibility => HasComment ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PinIconVisibility => IsPinned ? Visibility.Visible : Visibility.Collapsed;
    public string DateTimeDisplay => DateTime.ToString("M/d HH:mm");

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
