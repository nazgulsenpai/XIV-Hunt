using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.Serialization;

namespace FFXIV_GameSense.Properties
{
    public class ObservableHashSet<T> : INotifyCollectionChanged, ICollection<T>, IEnumerable<T>, IReadOnlyCollection<T>, ISet<T>, IDeserializationCallback, ISerializable
    {
        private HashSet<T> HashSet { get; set; } = new HashSet<T>();

        public ObservableHashSet() : this(EqualityComparer<T>.Default) { }

        public ObservableHashSet(IEqualityComparer<T> comparer)
        {
            HashSet = new HashSet<T>(comparer);
        }

        public int Count => HashSet.Count;

        public bool IsReadOnly => ((ICollection<T>)HashSet).IsReadOnly;

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        private void OnNotifyCollectionChanged(NotifyCollectionChangedEventArgs args) => CollectionChanged?.Invoke(this, args);
        private void OnNotifyCollectionChanged(IList newItems, IList oldItems) => OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newItems, oldItems));

        public void Add(T item)
        {
            if(HashSet.Add(item))
                OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item));
        }

        public void Clear()
        {
            if (HashSet.Count == 0)
                return;
            List<T> oldItems = HashSet.ToList();
            HashSet.Clear();
            OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset, oldItems));
        }

        public bool Contains(T item) => HashSet.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => HashSet.CopyTo(array, arrayIndex);

        public bool Remove(T item)
        {
            if(HashSet.Remove(item))
                OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item));
            return false;
        }

        public IEnumerator<T> GetEnumerator() => HashSet.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        bool ISet<T>.Add(T item)
        {
            bool t = HashSet.Add(item);
            if (t)
                OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item));
            return t;
        }

        public bool AddRange(IEnumerable<T> items)
        {
            List<T> itemsToAdd = items.Where(x => !HashSet.Contains(x)).ToList();
            if (!itemsToAdd.Any())
                return false;
            int countBeforeAdd = Count;
            foreach (T i in itemsToAdd)
                HashSet.Add(i);
            OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, itemsToAdd));
            return Count == countBeforeAdd + itemsToAdd.Count();
        }

        public bool RemoveRange(IEnumerable<T> items)
        {
            List<T> itemsToRemove = items.Where(x => HashSet.Contains(x)).ToList(); ;
            if (!itemsToRemove.Any())
                return false;
            int countBeforeRemove = Count;
            foreach (T i in itemsToRemove)
                HashSet.Remove(i);
            OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, itemsToRemove));
            return Count == countBeforeRemove + itemsToRemove.Count();
        }

        public void UnionWith(IEnumerable<T> other)
        {
            int c = Count;
            var uniqueItemsToAdd = other.Where(x => !HashSet.Contains(x));
            HashSet.UnionWith(other);
            if (Count != c)
                OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, uniqueItemsToAdd));
        }

        public void IntersectWith(IEnumerable<T> other)
        {
            List<T> itemsToBeRemoved = HashSet.Where(x => !other.Contains(x)).ToList();
            if (itemsToBeRemoved.Any())
            {
                HashSet.IntersectWith(other);
                OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, itemsToBeRemoved));
            }
        }

        public void ExceptWith(IEnumerable<T> other)
        {
            List<T> itemsToBeRemoved = HashSet.Where(x => other.Contains(x)).ToList();
            if (itemsToBeRemoved.Any())
            {
                HashSet.ExceptWith(other);
                OnNotifyCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, itemsToBeRemoved));
            }
        }

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            IEnumerable<T> itemsToBeRemoved = HashSet.Where(x => other.Contains(x));
            IEnumerable<T> itemsToBeAdded = HashSet.Where(x => other.Contains(x));
            if (!itemsToBeAdded.Any() && !itemsToBeRemoved.Any())
                return;
            RemoveRange(itemsToBeRemoved);
            AddRange(itemsToBeAdded);
        }

        public bool IsSubsetOf(IEnumerable<T> other) => HashSet.IsSubsetOf(other);

        public bool IsSupersetOf(IEnumerable<T> other) => HashSet.IsSupersetOf(other);

        public bool IsProperSupersetOf(IEnumerable<T> other) => HashSet.IsProperSupersetOf(other);

        public bool IsProperSubsetOf(IEnumerable<T> other) => HashSet.IsProperSubsetOf(other);

        public bool Overlaps(IEnumerable<T> other) => HashSet.Overlaps(other);

        public bool SetEquals(IEnumerable<T> other) => HashSet.SetEquals(other);

        public void OnDeserialization(object sender) => HashSet.OnDeserialization(sender);

        public void GetObjectData(SerializationInfo info, StreamingContext context) => HashSet.GetObjectData(info, context);
    }
}
