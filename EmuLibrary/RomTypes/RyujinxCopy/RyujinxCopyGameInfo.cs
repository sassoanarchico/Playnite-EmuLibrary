using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using ProtoBuf;
using System.Collections.Generic;
using System.IO;

namespace EmuLibrary.RomTypes.RyujinxCopy
{
    [ProtoContract]
    internal class RyujinxCopyGameInfo : ELGameInfo
    {
        public override RomType RomType => RomType.RyujinxCopy;

        /// <summary>
        /// The name of the top-level game folder, relative to the mapping's SourcePath.
        /// Example: "ONE PIECE PIRATE WARRIORS 4 (NSP)(EU)(Base Game)"
        /// </summary>
        [ProtoMember(1)]
        public string SourceFolderName { get; set; }

        /// <summary>
        /// The filename of the base game ROM (NSP/XCI) found at the root of the folder.
        /// Example: "ONE PIECE PIRATE WARRIORS 4[0100BA400E2F4000][GB][v0].nsp"
        /// </summary>
        [ProtoMember(2)]
        public string BaseGameFileName { get; set; }

        /// <summary>
        /// Full path to the source folder.
        /// </summary>
        public string SourceFullPath => Path.Combine(Mapping?.SourcePath ?? "", SourceFolderName);

        /// <summary>
        /// Full path to the base game ROM file in the source.
        /// </summary>
        public string SourceBaseGameFullPath => Path.Combine(SourceFullPath, BaseGameFileName);

        public override InstallController GetInstallController(Game game, IEmuLibrary emuLibrary) =>
            new RyujinxCopyInstallController(game, emuLibrary);

        public override UninstallController GetUninstallController(Game game, IEmuLibrary emuLibrary) =>
            new RyujinxCopyUninstallController(game, emuLibrary);

        protected override IEnumerable<string> GetDescriptionLines()
        {
            yield return $"{nameof(SourceFolderName)}: {SourceFolderName}";
            yield return $"{nameof(BaseGameFileName)}: {BaseGameFileName}";
            yield return $"{nameof(SourceFullPath)}*: {SourceFullPath}";
        }

        public override void BrowseToSource()
        {
            System.Diagnostics.Process.Start("explorer.exe", $"\"{Path.GetFullPath(SourceFullPath)}\"");
        }
    }
}
