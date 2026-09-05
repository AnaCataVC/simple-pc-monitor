using System;

namespace SimplePCMonitor.Core
{
    public class TimedCache<T>
    {
        private readonly TimeSpan _duration;
        private T _value;
        private bool _hasValue;
        private DateTime _lastRefresh = DateTime.MinValue;

        public TimedCache(TimeSpan duration)
        {
            _duration = duration;
        }

        public T LastValue { get { return _value; } }

        public bool TryGetFresh(bool forceRefresh, out T value)
        {
            if (!forceRefresh && _hasValue && (DateTime.UtcNow - _lastRefresh) < _duration)
            {
                value = _value;
                return true;
            }
            value = default(T);
            return false;
        }

        public T Store(T value)
        {
            _value = value;
            _hasValue = true;
            _lastRefresh = DateTime.UtcNow;
            return value;
        }
    }
}
