using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SystemCoreMonitor.Core.Mvvm
{
    public static class CollectionSync
    {
        /// <summary>
        /// Makes <paramref name="target"/> match <paramref name="source"/> by index, replacing only rows that
        /// differ. A Reset (new collection) would regenerate every container and lose scroll position.
        /// Defaults to reference equality, which already skips rows served from a collector's TimedCache.
        /// </summary>
        public static void SyncInPlace<T>(this ObservableCollection<T> target, IList<T> source, Func<T, T, bool>? isSame = null)
        {
            isSame ??= (a, b) => ReferenceEquals(a, b);
            int count = source.Count;
            for (int i = 0; i < count; i++)
            {
                if (i >= target.Count) target.Add(source[i]);
                else if (!isSame(target[i], source[i])) target[i] = source[i];
            }
            while (target.Count > count) target.RemoveAt(target.Count - 1);
        }
    }
}
