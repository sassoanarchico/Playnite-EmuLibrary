using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using EmuLibrary.Util.FileCopier;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EmuLibrary.RomTypes.RyujinxCopy
{
    /// <summary>
    /// Install controller for RyujinxCopy ROM type.
    /// Copies the entire game folder (base + updates + DLC) from source to destination.
    /// Sets the ROM path to the base game NSP/XCI at the root of the copied folder.
    /// </summary>
    class RyujinxCopyInstallController : BaseInstallController
    {
        internal RyujinxCopyInstallController(Game game, IEmuLibrary emuLibrary) : base(game, emuLibrary)
        { }

        public override void Install(InstallActionArgs args)
        {
            var info = Game.GetRyujinxCopyGameInfo();

            var dstPathBase = info.Mapping?.DestinationPathResolved ??
                throw new Exception("Mapped emulator data cannot be found. Please try removing and re-adding.");

            _watcherToken = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    var source = new DirectoryInfo(info.SourceFullPath);
                    var destination = new DirectoryInfo(Path.Combine(dstPathBase, source.Name));

                    // Copy the entire game folder tree (base game + updates + DLC subfolders)
                    await CreateFileCopier(source, destination).CopyAsync(_watcherToken.Token);

                    // Register updates and DLC in Ryujinx's per-game config
                    // Pass both source and destination to enable path rewriting of existing configs
                    RyujinxConfigHelper.RegisterUpdatesAndDlc(source.FullName, destination.FullName, info.BaseGameFileName);

                    var installDir = destination.FullName;
                    var gamePath = Path.Combine(installDir, info.BaseGameFileName);

                    if (_emuLibrary.Playnite.ApplicationInfo.IsPortable)
                    {
                        installDir = installDir.Replace(_emuLibrary.Playnite.Paths.ApplicationPath, ExpandableVariables.PlayniteDirectory);
                        gamePath = gamePath.Replace(_emuLibrary.Playnite.Paths.ApplicationPath, ExpandableVariables.PlayniteDirectory);
                    }

                    InvokeOnInstalled(new GameInstalledEventArgs(new GameInstallationData()
                    {
                        InstallDirectory = installDir,
                        Roms = new List<GameRom>() { new GameRom(Game.Name, gamePath) },
                    }));
                }
                catch (Exception ex)
                {
                    if (!(ex is WindowsCopyDialogClosedException))
                    {
                        _emuLibrary.Playnite.Notifications.Add(Game.GameId, $"Failed to install {Game.Name}.{Environment.NewLine}{Environment.NewLine}{ex}", NotificationType.Error);
                    }
                    Game.IsInstalling = false;
                    throw;
                }
            });
        }
    }
}
