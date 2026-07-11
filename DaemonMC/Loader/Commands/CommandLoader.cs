using DaemonMC.Commands.Core;

namespace DaemonMC.Loader.Commands;

public static class CommandLoader
{
    public static void Load()
    {
        GamemodeCommand.Register();
    }
}