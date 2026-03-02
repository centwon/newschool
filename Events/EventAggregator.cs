using System;
using System.Collections.Generic;
using System.Linq;

namespace NewSchool.Events
{
    /// <summary>
    /// 이벤트 집계자 - Pub/Sub 패턴 구현 (Native AOT 호환)
    /// </summary>
    public class EventAggregator
    {
        private static readonly Lazy<EventAggregator> _instance = new(() => new EventAggregator());
        public static EventAggregator Instance => _instance.Value;

        private readonly Dictionary<Type, List<Delegate>> _subscribers;
        private readonly object _lockObject = new();

        private EventAggregator()
        {
            _subscribers = new Dictionary<Type, List<Delegate>>();
        }

        #region Subscribe

        /// <summary>
        /// 이벤트 구독
        /// </summary>
        public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
        {
            lock (_lockObject)
            {
                var eventType = typeof(TEvent);

                if (!_subscribers.ContainsKey(eventType))
                {
                    _subscribers[eventType] = new List<Delegate>();
                }

                _subscribers[eventType].Add(handler);
                System.Diagnostics.Debug.WriteLine($"[Event] 구독: {eventType.Name}");
            }
        }

        /// <summary>
        /// 이벤트 구독 해제
        /// </summary>
        public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class
        {
            lock (_lockObject)
            {
                var eventType = typeof(TEvent);

                if (_subscribers.ContainsKey(eventType))
                {
                    _subscribers[eventType].Remove(handler);

                    if (_subscribers[eventType].Count == 0)
                    {
                        _subscribers.Remove(eventType);
                    }

                    System.Diagnostics.Debug.WriteLine($"[Event] 구독 해제: {eventType.Name}");
                }
            }
        }

        #endregion

        #region Publish

        /// <summary>
        /// 이벤트 발행
        /// </summary>
        public void Publish<TEvent>(TEvent eventData) where TEvent : class
        {
            List<Delegate> handlers;

            lock (_lockObject)
            {
                var eventType = typeof(TEvent);

                if (!_subscribers.ContainsKey(eventType))
                {
                    return;
                }

                // 복사본 생성 (이벤트 핸들러 실행 중 구독 변경 방지)
                handlers = _subscribers[eventType].ToList();
            }

            // 모든 핸들러 실행
            foreach (var handler in handlers)
            {
                try
                {
                    ((Action<TEvent>)handler)(eventData);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Event] 핸들러 실행 오류: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[Event] 발행: {typeof(TEvent).Name} - {handlers.Count}개 핸들러 실행");
        }

        #endregion

        #region Clear

        /// <summary>
        /// 모든 구독 해제
        /// </summary>
        public void ClearAll()
        {
            lock (_lockObject)
            {
                _subscribers.Clear();
                System.Diagnostics.Debug.WriteLine("[Event] 모든 구독 해제");
            }
        }

        /// <summary>
        /// 특정 이벤트의 모든 구독 해제
        /// </summary>
        public void Clear<TEvent>() where TEvent : class
        {
            lock (_lockObject)
            {
                var eventType = typeof(TEvent);
                if (_subscribers.ContainsKey(eventType))
                {
                    _subscribers.Remove(eventType);
                    System.Diagnostics.Debug.WriteLine($"[Event] 구독 해제: {eventType.Name}");
                }
            }
        }

        #endregion
    }

    #region Board Events

    /// <summary>
    /// Post 생성 이벤트
    /// </summary>
    public class PostCreatedEvent
    {
        public int PostNo { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Post 수정 이벤트
    /// </summary>
    public class PostUpdatedEvent
    {
        public int PostNo { get; set; }
        public string Category { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Post 삭제 이벤트
    /// </summary>
    public class PostDeletedEvent
    {
        public int PostNo { get; set; }
        public string Category { get; set; } = string.Empty;
        public DateTime DeletedAt { get; set; }
    }

    /// <summary>
    /// Comment 생성 이벤트
    /// </summary>
    public class CommentCreatedEvent
    {
        public int CommentNo { get; set; }
        public int PostNo { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Comment 삭제 이벤트
    /// </summary>
    public class CommentDeletedEvent
    {
        public int CommentNo { get; set; }
        public int PostNo { get; set; }
        public DateTime DeletedAt { get; set; }
    }

    /// <summary>
    /// 데이터 변경 이벤트 (일반)
    /// </summary>
    public class DataChangedEvent
    {
        public string EntityType { get; set; } = string.Empty;
        public int EntityId { get; set; }
        public ChangeType ChangeType { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    public enum ChangeType
    {
        Created,
        Updated,
        Deleted
    }

    #endregion

    #region Usage Example

    public class EventUsageExample
    {
        public void SetupEventHandlers()
        {
            var events = EventAggregator.Instance;

            // Post 생성 이벤트 구독
            events.Subscribe<PostCreatedEvent>(e =>
            {
                System.Diagnostics.Debug.WriteLine($"새 게시글: {e.Title} (#{e.PostNo})");
                // UI 업데이트, 알림 등
            });

            // Comment 생성 이벤트 구독
            events.Subscribe<CommentCreatedEvent>(e =>
            {
                System.Diagnostics.Debug.WriteLine($"새 댓글 (Post #{e.PostNo})");
                // 댓글 개수 업데이트 등
            });

            // 데이터 변경 이벤트 구독
            events.Subscribe<DataChangedEvent>(e =>
            {
                System.Diagnostics.Debug.WriteLine($"데이터 변경: {e.EntityType} #{e.EntityId} - {e.ChangeType}");
                // 캐시 무효화 등
            });
        }

        public void PublishEvents()
        {
            var events = EventAggregator.Instance;

            // 이벤트 발행
            events.Publish(new PostCreatedEvent
            {
                PostNo = 123,
                Category = "공지사항",
                Title = "새 게시글",
                CreatedAt = DateTime.Now
            });
        }
    }

    #endregion
}
