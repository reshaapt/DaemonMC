using DaemonMC.Utils.Game;

namespace DaemonMC.Commands.Core;

public static class GamemodeCommand
{
    public static void Register()
    {
        CommandManager.Register(new Command("gm", "Changes your gamemode", 0, [new StringP("mode")]), Execute);
    }

    private static void Execute(CommandAction action)
    {
        var player = action.Player;
        var targetMode = action.Data.Length == 1 ? action.Data[0] as string : null;
        
        if (targetMode is null) return;

        switch (targetMode)
        {
            case "0":
            {
                player.SetGameMode(0);
                break;
            }
            case "1":
            {
                player.SetGameMode(1);
                break;
            }
            case "2":
            {
                player.SetGameMode(2);
                break;
            }
        }
    }
}
