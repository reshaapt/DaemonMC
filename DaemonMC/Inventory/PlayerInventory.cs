using DaemonMC.Items;
using DaemonMC.Items.VanillaItems;
using DaemonMC.Network.Bedrock;
using DaemonMC.Network.Enumerations;
using DaemonMC.Network.Enumerations.Inventory;
using DaemonMC.Utils.Game;
using Log = DaemonMC.Utils.Text.Log;

namespace DaemonMC.Inventory;

public class PlayerInventory : BaseInventory
{
    public override int DefaultSize => 40;
    public override ContainerType Type => ContainerType.Inventory;
    
    public Item Cursor { get; set; } = new Air();
    public Item[] Slots { get; set; } = new Item[40];
    public int HandSlot { get; set; } = 0;
    public Player Holder { get; }
    
    public Item Head => Slots[36];
    public Item Chest => Slots[37];
    public Item Legs => Slots[38];
    public Item Feets => Slots[39];
    
    public delegate void SlotChangedEventHandler(byte index, Item oldItem, Item newItem);
    public event SlotChangedEventHandler? SlotChanged; // -> maybe not needed, but could be useful for Plugins

    public PlayerInventory(Player player)
    {
        Holder = player;
        Clear();
    }

    public void SetHandSlot(byte slot)
    {
        if (slot >= 9)
        {
            Log.warn($"Attempted to set hand slot to {slot}, which is out of bounds for hotbar size 9.");
            return;
        }

        HandSlot = slot;
        SendHandSlot(slot, Viewers);
    }
    
    public Item GetItemInHand()
    {
        return GetItem((byte) HandSlot)!;
    }

    public void SendHandSlot(byte slot, List<Player> viewers)
    {
        foreach (var player in viewers)
        {
            if (player == Holder)
            {
                SendSlot(slot, player);
                
                var mobEquipment = new MobEquipment();
                mobEquipment.EntityId = Holder.EntityID;
                mobEquipment.Item = GetItemInHand();
                mobEquipment.Slot = slot;
                mobEquipment.SelectedSlot = slot;
                
                player.CurrentWorld.Send(mobEquipment, Holder.EntityID);
            }
            else
            {
                // ToDo: Send inventory slot update to other viewers if necessary
            }
        }
    }
    
    public override void SetItem(byte slot, Item item)
    {
        if (slot >= DefaultSize)
        {
            Log.warn($"Attempted to set item at index {slot}, which is out of bounds for inventory size {DefaultSize}.");
            return;
        }

        Slots[slot] = item;
        
        SendSlot(slot, Viewers);
    }

    public void SendSlot(byte slot, List<Player> viewers)
    {
        foreach (var player in viewers)
        {
            SendSlot(slot, player);
        }
    }

    public void SendSlot(byte slot, Player player)
    {
        if (player == Holder)
        {
            var inventorySlot = new InventorySlot();
            inventorySlot.ContainerID = (byte) ContainerId.Inventory;
            inventorySlot.Slot = slot;
            inventorySlot.ContainerName = GetFullContainerName(slot);
            inventorySlot.Item = GetItem(slot)!;
                
            if (slot > 35)
            {
                var mobEquipment = new MobArmorEquipment();
                mobEquipment.EntityId = Holder.EntityID;
                mobEquipment.Head = Head;
                mobEquipment.Chest = Chest;
                mobEquipment.Legs = Legs;
                mobEquipment.Feet = Feets;
                    
                Holder.CurrentWorld.Send(mobEquipment, Holder.EntityID);
            }
                
            player.Send(inventorySlot);
        }
        else
        {
            // ToDo: Send inventory slot update to other viewers if necessary
        }
    }

    public FullContainerName GetFullContainerName(byte slot)
    {
        return slot switch
        {
            < 9 => new FullContainerName(ContainerEnumName.HotbarContainer, (byte) ContainerId.Inventory),
            < 36 => new FullContainerName(ContainerEnumName.InventoryContainer, (byte) ContainerId.Inventory),
            < 40 => new FullContainerName(ContainerEnumName.ArmorContainer, (byte) ContainerId.Inventory),
            _ => throw new ArgumentOutOfRangeException(nameof(slot), $"Slot index {slot} is out of range for player inventory.")
        };
    }

    public override void SetContent(Item[] items)
    {
        if (items.Length != DefaultSize)
        {
            Log.warn($"Attempted to set inventory content with an array of size {items.Length}, but expected size is {DefaultSize}.");
            return;
        }

        Slots = items;
        SendContent();
    }

    public void SendContent()
    {
        foreach (var player in Viewers)
        {
            if (player == Holder)
            {
            }
            else
            {
                // ToDo: Send inventory slot update to other viewers if necessary
            }
        }
    }

    public override Item? GetItem(byte slot)
    {
        if (slot >= DefaultSize)
        {
            Log.warn($"Attempted to get item at index {slot}, which is out of bounds for inventory size {DefaultSize}.");
            return new Air();
        }
        
        return Slots[slot];
    }
    
    public bool TryGetItem(byte slot, out Item item)
    {
        if (slot >= DefaultSize)
        {
            Log.warn($"Attempted to get item at index {slot}, which is out of bounds for inventory size {DefaultSize}.");
            item = new Air();
            return false;
        }

        item = Slots[slot];
        return true;
    }

    public override bool GetContent(out Item[] items)
    {
        throw new NotImplementedException();
    }

    public override void AddItem(Item item)
    {
        throw new NotImplementedException();
    }

    public override void RemoveItem(Item item)
    {
        throw new NotImplementedException();
    }

    public sealed override void Clear()
    {
        for (int i = 0; i < DefaultSize; i++)
        {
            Slots[i] = new Air();
        }
    }

    public override byte GetIndexOfItem(Item item)
    {
        throw new NotImplementedException();
    }

    public override void Open(Player player)
    {
        base.Open(player);

        var containerOpen = new ContainerOpen();
        containerOpen.ContainerId = (byte) ContainerId.Inventory;
        containerOpen.ContainerType = (byte) ContainerType.Inventory;
        containerOpen.Position = player.Position;
        containerOpen.EntityId = player.EntityID;
        player.Send(containerOpen);
    }

    public override void Close(Player player)
    {
        base.Close(player);

        var containerClose = new ContainerClose();
        containerClose.ContainerId = (byte)ContainerId.Inventory;
        containerClose.ContainerType = (byte)ContainerType.Inventory;
        
        player.Send(containerClose);
    }
    
    public void Send(ContainerId containerId, byte slot, Item item, ContainerEnumName containerName = ContainerEnumName.InventoryContainer) // temporary
    {
        var pk = new InventorySlot
        {
            ContainerID = (int)containerId,
            ContainerName = new FullContainerName() { ContainerName = containerName },
            Slot = slot,
            Item = item,
        };
        
        Holder.Send(pk);
    }
}