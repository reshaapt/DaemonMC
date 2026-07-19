namespace DaemonMC.Network.Enumerations.Inventory;

public enum ContainerId : byte
{
    None = 255, // -1 as int
    Inventory = 0,
    First = 1,
    Last = 100,
    Offhand = 119,
    Armor = 120,
    SelectionSlots = 122,
    PlayerOnlyUi = 124,
    Registry = 125,
}