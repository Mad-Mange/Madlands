using Madlands.Content;
using Madlands.Shared;
using Madlands.Shared.Game;

namespace Madlands.Server.Items;

/// <summary>Some of one item in one slot.</summary>
public sealed class ItemStack
{
    public ItemStack(ItemDefinition item, int count)
    {
        Item = item;
        Count = count;
    }

    public ItemDefinition Item { get; }
    public int Count { get; set; }
    public int Room => Item.StackSize - Count;

    public override string ToString() => $"{Count} x {Item}";
}

/// <summary>
/// A player's items and money (ADR 0010): the equipment (container 0), the backpack (1) and up to
/// four bags (2-5) whose size comes from the bag item in their bag slot. Pure rules, no networking:
/// every change is recorded in <see cref="Changed"/> for the item system to send.
/// </summary>
public sealed class Inventory
{
    public const int EquipmentSlots = (int)EquipSlot.Bag4 + 1;

    private readonly ItemStack?[][] _containers = new ItemStack?[ItemPos.ContainerCount][];
    private readonly HashSet<ItemPos> _changed = new();

    public Inventory()
    {
        _containers[ItemPos.Equipment] = new ItemStack?[EquipmentSlots];
        _containers[ItemPos.Backpack] = new ItemStack?[GameConstants.BackpackSlots];
        for (int c = ItemPos.FirstBag; c < ItemPos.ContainerCount; c++) _containers[c] = Array.Empty<ItemStack?>();
    }

    /// <summary>Copper.</summary>
    public long Money { get; private set; }
    /// <summary>Positions changed since <see cref="ClearChanges"/>.</summary>
    public IReadOnlyCollection<ItemPos> Changed => _changed;
    /// <summary>Something worn changed (stats and looks need updating).</summary>
    public bool EquipmentChanged { get; private set; }
    /// <summary>Something in the bags changed (quest item counts need updating).</summary>
    public bool BagsChanged { get; private set; }

    public void ClearChanges()
    {
        _changed.Clear();
        EquipmentChanged = false;
        BagsChanged = false;
    }

    public int ContainerSize(byte container) => container < ItemPos.ContainerCount ? _containers[container].Length : 0;
    public bool IsValid(ItemPos pos) => pos.Container < ItemPos.ContainerCount && pos.Slot < _containers[pos.Container].Length;
    public ItemStack? Get(ItemPos pos) => IsValid(pos) ? _containers[pos.Container][pos.Slot] : null;
    public ItemStack? Equipped(EquipSlot slot) => _containers[ItemPos.Equipment][(int)slot];

    /// <summary>Every occupied position: equipment first, then the bags.</summary>
    public IEnumerable<(ItemPos Pos, ItemStack Stack)> All()
    {
        for (byte c = 0; c < ItemPos.ContainerCount; c++)
        {
            var container = _containers[c];
            for (byte s = 0; s < container.Length; s++)
                if (container[s] is { } stack) yield return (new ItemPos(c, s), stack);
        }
    }

    /// <summary>Bag positions in fill order: the backpack, then bags 1-4.</summary>
    public IEnumerable<ItemPos> BagPositions()
    {
        for (byte c = ItemPos.Backpack; c < ItemPos.ContainerCount; c++)
            for (byte s = 0; s < _containers[c].Length; s++)
                yield return new ItemPos(c, s);
    }

    public bool IsContainerEmpty(byte container) => _containers[container].All(s => s is null);

    // ------------------------------------------------------------------ money

    /// <summary>Adds (or with a negative amount, takes) money within 0..MaxMoney. Returns the actual change.</summary>
    public long AddMoney(long amount)
    {
        long before = Money;
        Money = Math.Clamp(Money + amount, 0, GameConstants.MaxMoney);
        return Money - before;
    }

    // ------------------------------------------------------------------ counting and adding

    /// <summary>How many of an item are in the bags (equipped items do not count).</summary>
    public int Count(uint itemId)
    {
        int n = 0;
        foreach (var pos in BagPositions())
            if (Get(pos) is { } s && s.Item.Id == itemId) n += s.Count;
        return n;
    }

    /// <summary>How many more of <paramref name="item"/> fit into the bags.</summary>
    public int SpaceFor(ItemDefinition item)
    {
        long space = 0;
        foreach (var pos in BagPositions())
        {
            var s = Get(pos);
            if (s is null) space += item.StackSize;
            else if (s.Item == item) space += s.Room;
        }
        return (int)Math.Min(space, int.MaxValue);
    }

    /// <summary>True if all of these fit into the bags at once (rewards, multi-item loot).</summary>
    public bool CanAddAll(IEnumerable<(ItemDefinition Item, int Count)> items)
    {
        int emptySlots = BagPositions().Count(p => Get(p) is null);
        foreach (var group in items.GroupBy(i => i.Item))
        {
            var item = group.Key;
            int count = group.Sum(i => i.Count);
            int partial = BagPositions().Select(Get).Where(s => s is not null && s.Item == item).Sum(s => s!.Room);
            int rest = Math.Max(0, count - partial);
            emptySlots -= (rest + item.StackSize - 1) / item.StackSize;
            if (emptySlots < 0) return false;
        }
        return true;
    }

    /// <summary>Puts up to <paramref name="count"/> into the bags: onto existing stacks first, then into empty slots. Returns how many went in.</summary>
    public int Add(ItemDefinition item, int count)
    {
        int left = count;
        if (item.StackSize > 1)
        {
            foreach (var pos in BagPositions())
            {
                if (left == 0) break;
                if (Get(pos) is not { } s || s.Item != item || s.Room <= 0) continue;
                int n = Math.Min(left, s.Room);
                s.Count += n;
                left -= n;
                Touch(pos);
            }
        }
        foreach (var pos in BagPositions())
        {
            if (left == 0) break;
            if (Get(pos) is not null) continue;
            int n = Math.Min(left, item.StackSize);
            Set(pos, new ItemStack(item, n));
            left -= n;
        }
        return count - left;
    }

    /// <summary>Takes up to <paramref name="count"/> of an item out of the bags, smallest stacks first. Returns how many were taken.</summary>
    public int Remove(uint itemId, int count)
    {
        int left = count;
        var stacks = BagPositions().Where(p => Get(p)?.Item.Id == itemId).OrderBy(p => Get(p)!.Count).ToList();
        foreach (var pos in stacks)
        {
            if (left == 0) break;
            var s = Get(pos)!;
            int n = Math.Min(left, s.Count);
            s.Count -= n;
            left -= n;
            if (s.Count == 0) Set(pos, null);
            else Touch(pos);
        }
        return count - left;
    }

    /// <summary>Removes one from the stack at <paramref name="pos"/> (using a consumable).</summary>
    public void Consume(ItemPos pos, int count = 1)
    {
        if (Get(pos) is not { } s) return;
        s.Count -= Math.Min(count, s.Count);
        if (s.Count == 0) Set(pos, null);
        else Touch(pos);
    }

    /// <summary>Empties a slot. A bag in a bag slot must be empty first.</summary>
    public ActionError Destroy(ItemPos pos)
    {
        if (Get(pos) is null) return ActionError.ItemNotFound;
        if (pos.IsBagSlot && !IsContainerEmpty(ItemPos.ContainerOf((EquipSlot)pos.Slot))) return ActionError.BagNotEmpty;
        Set(pos, null);
        return ActionError.None;
    }

    /// <summary>The first empty bag position, skipping the container <paramref name="exclude"/>.</summary>
    public bool TryFindFreeSlot(out ItemPos free, byte exclude = byte.MaxValue)
    {
        foreach (var pos in BagPositions())
        {
            if (pos.Container == exclude || Get(pos) is not null) continue;
            free = pos;
            return true;
        }
        free = default;
        return false;
    }

    /// <summary>
    /// Puts a saved stack back where it was (loading). Equipment and bag slots must be loaded before
    /// the bags' contents. False if the position does not exist (any more).
    /// </summary>
    public bool Restore(ItemPos pos, ItemStack stack)
    {
        if (!IsValid(pos) || Get(pos) is not null) return false;
        Set(pos, stack);
        return true;
    }

    // ------------------------------------------------------------------ equipping and moving

    /// <summary>Whether a player of this class and level may wear <paramref name="item"/> in <paramref name="slot"/>.</summary>
    public static ActionError CanEquip(ItemDefinition item, EquipSlot slot, int classId, int level, out int value)
    {
        value = 0;
        if (!ItemRules.Fits(item.Slot, slot))
            return slot >= EquipSlot.Bag1 && slot <= EquipSlot.Bag4 ? ActionError.NotABag : ActionError.CantEquipThere;
        if (!item.AllowsClass(classId)) return ActionError.WrongClass;
        if (level < item.RequiredLevel)
        {
            value = item.RequiredLevel;
            return ActionError.LevelTooLow;
        }
        return ActionError.None;
    }

    /// <summary>
    /// Moves a stack (or <paramref name="count"/> of it) to <paramref name="to"/>: into an empty slot, onto
    /// a stack of the same item, or swapping with what is there. Equipment slots check the equip rules;
    /// a two-handed weapon pushes the off-hand item into the bags.
    /// </summary>
    public ActionError Move(ItemPos from, ItemPos to, int count, int classId, int level, out int value)
    {
        value = 0;
        if (!IsValid(from) || !IsValid(to)) return ActionError.ItemNotFound;
        if (from == to) return ActionError.None;
        if (Get(from) is not { } src) return ActionError.ItemNotFound;
        var dst = Get(to);
        int amount = count <= 0 || count >= src.Count ? src.Count : count;

        if (amount < src.Count)
        {
            // Splitting a stack: into an empty slot or onto a stack of the same item.
            if (from.IsEquipment || to.IsEquipment) return ActionError.CantEquipThere;
            if (dst is null)
            {
                src.Count -= amount;
                Touch(from);
                Set(to, new ItemStack(src.Item, amount));
                return ActionError.None;
            }
            if (dst.Item != src.Item || dst.Room <= 0) return ActionError.CantDoThatNow;
            int n = Math.Min(amount, dst.Room);
            dst.Count += n;
            src.Count -= n;
            Touch(from);
            Touch(to);
            return ActionError.None;
        }

        // Merging onto a stack of the same item (a full stack swaps instead).
        if (dst is not null && dst.Item == src.Item && dst.Room > 0 && !from.IsEquipment && !to.IsEquipment)
        {
            int n = Math.Min(src.Count, dst.Room);
            dst.Count += n;
            src.Count -= n;
            if (src.Count == 0) Set(from, null);
            else Touch(from);
            Touch(to);
            return ActionError.None;
        }

        // Swapping (or moving into an empty slot).
        var error = CanPlace(src, to, classId, level, out value);
        if (error != ActionError.None) return error;
        if (dst is not null)
        {
            error = CanPlace(dst, from, classId, level, out value);
            if (error != ActionError.None) return error;
        }
        // A bag can only leave its bag slot empty, and never into itself.
        if (from.IsBagSlot && !IsContainerEmpty(ItemPos.ContainerOf((EquipSlot)from.Slot))) return ActionError.BagNotEmpty;
        if (to.IsBagSlot && dst is not null && !IsContainerEmpty(ItemPos.ContainerOf((EquipSlot)to.Slot))) return ActionError.BagNotEmpty;
        if (from.IsBagSlot && to.Container == ItemPos.ContainerOf((EquipSlot)from.Slot)) return ActionError.BagInItself;
        if (to.IsBagSlot && dst is not null && from.Container == ItemPos.ContainerOf((EquipSlot)to.Slot)) return ActionError.BagInItself;

        // Hands: a two-handed weapon needs the off hand free; nothing goes into the off hand beside one.
        var mainHand = ItemPos.Equip(EquipSlot.MainHand);
        var offHand = ItemPos.Equip(EquipSlot.OffHand);
        if (to == offHand && from != mainHand && Get(mainHand) is { Item.Slot: InventoryType.TwoHand }) return ActionError.TwoHandEquipped;
        if (from == offHand && to == mainHand && dst is { Item.Slot: InventoryType.TwoHand }) return ActionError.TwoHandEquipped;
        ItemPos? offHandTo = null;
        if (to == mainHand && src.Item.Slot == InventoryType.TwoHand && from != offHand && Get(offHand) is not null)
        {
            if (TryFindFreeSlot(out var free) && free != from) offHandTo = free;
            else if (dst is null) offHandTo = from;
            else return ActionError.InventoryFull;
        }

        if (offHandTo is { } target)
        {
            var shield = Get(offHand)!;
            Set(offHand, null);
            if (target == from)
            {
                Set(to, src);
                Set(from, shield);
                return ActionError.None;
            }
            Set(target, shield);
        }
        Set(from, null);
        Set(to, src);
        if (dst is not null) Set(from, dst);
        return ActionError.None;
    }

    private static ActionError CanPlace(ItemStack stack, ItemPos pos, int classId, int level, out int value)
    {
        value = 0;
        return pos.IsEquipment ? CanEquip(stack.Item, (EquipSlot)pos.Slot, classId, level, out value) : ActionError.None;
    }

    // ------------------------------------------------------------------ low level

    private void Set(ItemPos pos, ItemStack? stack)
    {
        _containers[pos.Container][pos.Slot] = stack;
        Touch(pos);
        // A bag slot decides the size of its container (the bag leaving it is empty, see Move).
        if (pos.IsBagSlot)
        {
            byte container = ItemPos.ContainerOf((EquipSlot)pos.Slot);
            _containers[container] = new ItemStack?[stack?.Item.BagSlots ?? 0];
        }
    }

    private void Touch(ItemPos pos)
    {
        _changed.Add(pos);
        if (pos.IsEquipment && !pos.IsBagSlot) EquipmentChanged = true;
        else BagsChanged = true;
    }
}
