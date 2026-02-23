using Playnite.SDK.Models;

namespace EmuLibrary.RomTypes.RyujinxCopy
{
    internal static class RyujinxCopyGameInfoExtensions
    {
        static public RyujinxCopyGameInfo GetRyujinxCopyGameInfo(this Game game)
        {
            return ELGameInfo.FromGame<RyujinxCopyGameInfo>(game);
        }
    }
}
