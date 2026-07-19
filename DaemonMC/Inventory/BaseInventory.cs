using DaemonMC.Items;
using DaemonMC.Network.Enumerations.Inventory;

namespace DaemonMC.Inventory;

public abstract class BaseInventory
{
    public abstract int DefaultSize { get; }
    
    public abstract ContainerType Type { get; }

    public List<Player> Viewers { get; } = []; 

    public abstract void SetItem(byte slot, Item item);

    public abstract void SetContent(Item[] items);

    public abstract Item? GetItem(byte slot);

    public abstract bool GetContent(out Item[] items);

    public abstract void AddItem(Item item);

    public abstract void RemoveItem(Item item);

    public abstract void Clear();
    
    public abstract byte GetIndexOfItem(Item item);

    public virtual void Open(Player player)
    {
        Viewers.Add(player);
    }

    public virtual void Close(Player player)
    {
        Viewers.Remove(player);
    }
}