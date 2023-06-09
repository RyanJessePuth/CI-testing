﻿using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Object.Synchronizing;
using SS3D.Core.Behaviours;
using SS3D.Systems.Inventory.Items;
using UnityEngine;

namespace SS3D.Systems.Inventory.Containers
{
    /// <summary>
    /// Stores items in a 2 dimensional container
    /// </summary>
    public sealed class Container : NetworkActor
    {
        /// <summary>
        /// Called when the contents of the container change
        /// </summary>
        public event ContainerContentsHandler OnContentsChanged;
        public delegate void ContainerContentsHandler(Container container, IEnumerable<Item> oldItems,IEnumerable<Item> newItems, ContainerChangeType type);

        /// <summary>
        /// The size of this container
        /// </summary>
        public Vector2Int Size;
        /// <summary>
        /// An optional reference to an attached container
        /// </summary>
        public AttachedContainer AttachedTo { get; set; }
        /// <summary>
        /// The items stored in this container, including information on how they are stored
        /// </summary>
        [SyncObject]
        private readonly SyncList<StoredItem> _storedItems = new();
        /// <summary>
        /// Server sole purpose of locking code execution while an operation is outgoing
        /// </summary>
        private readonly object _modificationLock = new();
        /// <summary>
        /// The last time the contents of this container were changed
        /// </summary>
        public float LastModification { get; private set; }

        public SyncList<StoredItem> StoredItems => _storedItems;
        /// <summary>
        /// Is this container empty
        /// </summary>
        public bool Empty => ItemCount == 0;
        /// <summary>
        /// How many items are in this container
        /// </summary>
        public int ItemCount => StoredItems.Count;
        /// <summary>
        /// The items stored in this container
        /// </summary>
        public IEnumerable<Item> Items => StoredItems.Select(x => x.Item);

        protected override void OnAwake()
        {
            base.OnAwake();

            StoredItems.OnChange += HandleStoredItemsChanged;
        }

        ~Container()
        {
            StoredItems.OnChange -= HandleStoredItemsChanged;
        }

        /// <summary>
        /// Runs when the container was changed, networked
        /// </summary>
        /// <param name="op">Type of change</param>
        /// <param name="index">Which element was changed</param>
        /// <param name="oldItem">Element before the change</param>
        /// <param name="newItem">Element after the change</param>
        private void HandleStoredItemsChanged(SyncListOperation op, int index, StoredItem oldItem, StoredItem newItem, bool asServer)
        {
            ContainerChangeType changeType;

            switch (op)
            {
                case SyncListOperation.Add:
                    changeType = ContainerChangeType.Add;
                    break;
                case SyncListOperation.Insert:
                case SyncListOperation.Set:
                    changeType = ContainerChangeType.Move;
                    break;
                case SyncListOperation.RemoveAt:
                case SyncListOperation.Clear:
                    changeType = ContainerChangeType.Remove;
                    break;
                case SyncListOperation.Complete:
                    changeType = ContainerChangeType.Move;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(op), op, null);
            }

            OnContentsChanged?.Invoke(this, new []{oldItem.Item} ,new []{newItem.Item}, changeType);
        }

        /// <summary>
        /// Places an item into this container in the first available position
        /// </summary>
        /// <param name="item">The item to place</param>
        /// <returns>If the item was added</returns>
        public bool AddItem(Item item)
        {
            if (ContainsItem(item))
            {
                return true;
            }

            if (!CanStoreItem(item))
            {
                return false;
            }

            Vector2Int itemSize = item.Size;
            int maxX = Size.x - itemSize.x;
            int maxY = Size.y - itemSize.y;

            // TODO: Use a more efficient algorithm
            for (int y = 0; y <= maxY; y++)
            {
                for (int x = 0; x <= maxX; x++)
                {
                    Vector2Int itemPosition = new Vector2Int(x, y);
                    if (AddItemPosition(item, itemPosition))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Tries to add an item at the specified position
        /// </summary>
        /// <param name="storedItem">The item to add</param>
        /// <param name="position">The target position in the container</param>
        /// <returns>If the item was added</returns>
        public bool AddItemPosition(Item item, Vector2Int position)
        {
            int itemIndex = FindItem(item);
            if (itemIndex != -1)
            {
                StoredItem existingItem = StoredItems[itemIndex];
                // Try to move existing item
                if (existingItem.Position == position)
                {
                    return true;
                }

                if (!IsAreaFreeExcluding(new RectInt(position, item.Size), item))
                {
                    return false;
                }

                StoredItem storedItem = new(item, position);
                StoredItems.Set(itemIndex, storedItem);

                return true;

                // Item at same position, nothing to do
            }

            if (!CanStoreItem(item))
            {
                return false;
            }

            bool wasAdded = false;
            lock (_modificationLock)
            {
                if (IsAreaFree(new RectInt(position, item.Size)))
                {
                    AddItemUnchecked(item, position);
                    wasAdded = true;
                }
            }

            if (!wasAdded)
            {
                return false;
            }

            item.SetContainer(this, true, false);

            return true;
        }

        /// <summary>
        /// Adds an item to the container without any checks (but ensuring there are no duplicates)
        /// </summary>
        /// <param name="item">The item to add</param>
        /// <param name="position">Where the item should go, make sure this position is valid and free!</param>
        private void AddItemUnchecked(Item item, Vector2Int position)
        {
            StoredItem newItem = new(item, position);

            // Move it if it is already in the container
            if (MoveItemUnchecked(newItem))
            {
                return;
            }

            StoredItems.Add(newItem);
            LastModification = Time.time;
        }

        /// <summary>
        /// Adds a stored item without checking any validity
        /// <param name="storedItem">The item to store</param>
        /// </summary>
        public void AddItemUnchecked(StoredItem storedItem)
        {
            AddItemUnchecked(storedItem.Item, storedItem.Position);
        }

        /// <summary>
        /// Add an array of items without performing checks
        /// </summary>
        /// <param name="items"></param>
        public void AddItemsUnchecked(StoredItem[] items)
        {
            foreach (StoredItem storedItem in items)
            {
                AddItemUnchecked(storedItem);
            }
        }

        /// <summary>
        /// Checks if a given area in the container is free
        /// </summary>
        /// <param name="area">The area to check</param>
        /// <returns>If the given area is free</returns>
        public bool IsAreaFree(RectInt area)
        {
            if (area.xMin < 0 || area.xMax < 0)
            {
                return false;
            }

            if (area.xMax > Size.x || area.yMax > Size.y)
            {
                return false;
            }

            foreach (StoredItem storedItem in StoredItems)
            {
                if (storedItem.IsExcludedOfFreeAreaComputation) continue;
                RectInt storedItemPlacement = new(storedItem.Position, storedItem.Item.Size);
                if (area.Overlaps(storedItemPlacement))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if a given area in the container is free, while excluding an item
        /// </summary>
        /// <param name="area">The area to check</param>
        /// <param name="item">The item to exclude from the check</param>
        /// <returns>If the given area is free</returns>
        public bool IsAreaFreeExcluding(RectInt area, Item item)
        {
            int itemIndex = FindItem(item);
            StoredItem storedItem = default;
            if (itemIndex != -1)
            {
                storedItem = StoredItems[itemIndex];
                StoredItems[itemIndex] = new StoredItem(storedItem.Item, storedItem.Position, true);
            }

            bool areaFree = IsAreaFree(area);

            if (itemIndex != -1)
            {
                StoredItems[itemIndex] = new StoredItem(storedItem.Item, storedItem.Position, false);
                StoredItems[itemIndex] = storedItem;
            }

            return areaFree;
        }

        /// <summary>
        /// Removes an item from the container
        /// </summary>
        /// <param name="item">The item to remove</param>
        public void RemoveItem(Item item)
        {
            for (int i = 0; i < StoredItems.Count; i++)
            {
                if (StoredItems[i].Item != item)
                {
                    continue;
                }

                RemoveItemAt(i);
                return;
            }
        }

        /// <summary>
        /// Moves an item without performing validation
        /// </summary>
        /// <param name="item">The item to move</param>
        /// <returns>If the item was moved</returns>
        private bool MoveItemUnchecked(StoredItem item)
        {
            for (int i = 0; i < StoredItems.Count; i++)
            {
                StoredItem x = StoredItems[i];
                if (x.Item != item.Item)
                {
                    continue;
                }

                if (x.Position == item.Position)
                {
                    return true;
                }

                StoredItems[i] = item;
                LastModification = Time.time;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Moves multiple items without performing validation
        /// </summary>
        /// <param name="items">The items to move</param>
        public void MoveItemsUnchecked(StoredItem[] items)
        {
            foreach (StoredItem storedItem in items)
            {
                MoveItemUnchecked(storedItem);
            }
        }

        /// <summary>
        /// Finds an item at a position
        /// </summary>
        /// <param name="position">The position to check</param>
        /// <returns>The item at the position, or null if there is none</returns>
        public Item ItemAt(Vector2Int position)
        {
            foreach (StoredItem storedItem in StoredItems)
            {
                RectInt storedItemPlacement = new(storedItem.Position, storedItem.Item.Size);
                if (storedItemPlacement.Contains(position))
                {
                    return storedItem.Item;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the position of an item in the container
        /// </summary>
        /// <param name="item">The item to look for</param>
        /// <returns>The item's position or (-1, -1)</returns>
        public Vector2Int PositionOf(Item item)
        {
            foreach (StoredItem storedItem in StoredItems)
            {
                if (storedItem.Item == item)
                {
                    return storedItem.Position;
                }
            }
            
            return new Vector2Int(-1, -1);
        }

        private void RemoveItemAt(int index)
        {
            StoredItem storedItem = StoredItems[index];
            lock (_modificationLock)
            {
                StoredItems.RemoveAt(index);
            }

            LastModification = Time.time;
            storedItem.Item.SetContainerUnchecked(null);
        }

        /// <summary>
        /// Empties the container, removing all items
        /// </summary>
        public void Dump()
        {
            Item[] oldItems = StoredItems.Select(x => x.Item).ToArray();
            for (int i = 0; i < oldItems.Length; i++)
            {
                oldItems[i].Container = null;
            }
            StoredItems.Clear();

            LastModification = Time.time;
        }

        /// <summary>
        /// Destroys all items in this container
        /// </summary>
        public void Purge()
        {
            foreach (StoredItem item in StoredItems)
            {
                item.Item.Delete();
            }
            StoredItems.Clear();

            LastModification = Time.time;
        }

        /// <summary>
        /// Checks if this container contains the item
        /// </summary>
        /// <param name="item">The item to search for</param>
        /// <returns>If it is in this container</returns>
        public bool ContainsItem(Item item)
        {
            foreach (StoredItem storedItem in StoredItems)
            {
                if (storedItem.Item == item)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if this item could be stored (traits etc.) without considering size
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool CanStoreItem(Item item)
        {
            // Do not store if the item is the container itself
            if (AttachedTo.GetComponent<Item>() == item)
            {
                return false;
            }

            Filter filter = AttachedTo.ContainerDescriptor.StartFilter;
            if (filter != null)
            {
                return filter.CanStore(item);
            }

            return true;
        }

        /// <summary>
        /// Checks if this item fits inside the container
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool CanHoldItem(Item item)
        {
            Vector2Int itemSize = item.Size;
            int maxX = Size.x - itemSize.x;
            int maxY = Size.y - itemSize.y;

            // TODO: Use a more efficient algorithm
            for (int y = 0; y <= maxY; y++)
            {
                for (int x = 0; x <= maxX; x++)
                {
                    if (IsAreaFreeExcluding(new RectInt(new Vector2Int(x, y), item.Size), item))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if this item can be stored and fits inside the container
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool CanContainItem(Item item)
        {
            return (CanStoreItem(item) && CanHoldItem(item));
        }

        /// <summary>
        /// Finds the index of an item
        /// </summary>
        /// <param name="item">The item to look for</param>
        /// <returns>The index of the item or -1 if not found</returns>
        public int FindItem(Item item)
        {
            for (int i = 0; i < StoredItems.Count; i++)
            {
                StoredItem storedItem = StoredItems[i];
                if (storedItem.Item == item)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}