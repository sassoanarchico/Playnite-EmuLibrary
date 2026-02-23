using EmuLibrary.PlayniteCommon;
using EmuLibrary.Settings;
using EmuLibrary.Util;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace EmuLibrary.RomTypes.RyujinxCopy
{
    /// <summary>
    /// Scanner for RyujinxCopy ROM type.
    /// Scans for top-level game folders containing NSP/XCI files.
    /// Each game folder may have:
    ///   - Base game NSP/XCI at the root (e.g. "GameName[TitleID][Region][v0].nsp")
    ///   - Update subfolder(s) with update NSP files
    ///   - DLC subfolder(s) with DLC NSP files
    /// The entire folder tree is copied on install.
    /// </summary>
    internal class RyujinxCopyScanner : RomTypeScanner
    {
        private readonly IPlayniteAPI _playniteAPI;

        private static readonly string[] s_switchExtensions = { ".nsp", ".xci" };

        // Pattern to extract a clean game name from folder names like:
        // "ONE PIECE PIRATE WARRIORS 4 (NSP)(EU)(Base Game)"
        // "Mario Kart 8 Deluxe (NSP)(EU)(Base Game + Update 3.0.3 + 48 DLCs)"
        private static readonly Regex s_folderTagPattern = new Regex(@"\s*\((?:NSP|XCI|EU|US|JP|GB|Base Game|Update|DLC|\d+\s*DLCs?|[vV][\d.]+|[+\s])*\)\s*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public override RomType RomType => RomType.RyujinxCopy;
        public override Guid LegacyPluginId => EmuLibrary.PluginId;

        public RyujinxCopyScanner(IEmuLibrary emuLibrary) : base(emuLibrary)
        {
            _playniteAPI = emuLibrary.Playnite;
        }

        public override IEnumerable<GameMetadata> GetGames(EmulatorMapping mapping, LibraryGetGamesArgs args)
        {
            var srcPath = mapping.SourcePath;
            var dstPath = mapping.DestinationPathResolved;

            if (string.IsNullOrEmpty(srcPath) || string.IsNullOrEmpty(dstPath))
                yield break;

            #region Import "installed" games
            if (Directory.Exists(dstPath))
            {
                foreach (var dir in SafeGetDirectories(dstPath))
                {
                    if (args.CancelToken.IsCancellationRequested)
                        yield break;

                    var baseGame = FindBaseGameFile(dir.FullName);
                    if (baseGame == null)
                        continue;

                    var info = new RyujinxCopyGameInfo()
                    {
                        MappingId = mapping.MappingId,
                        SourceFolderName = dir.Name,
                        BaseGameFileName = baseGame.Name,
                    };

                    var gameName = CleanGameName(dir.Name);

                    yield return new GameMetadata()
                    {
                        Source = EmuLibrary.SourceName,
                        Name = gameName,
                        IsInstalled = true,
                        GameId = info.AsGameId(),
                        InstallDirectory = dir.FullName,
                        Platforms = new HashSet<MetadataProperty>() { new MetadataNameProperty(mapping.Platform.Name) },
                        Regions = FileNameUtils.GuessRegionsFromRomName(dir.Name).Select(r => new MetadataNameProperty(r)).ToHashSet<MetadataProperty>(),
                        InstallSize = GetDirectorySize(dir.FullName),
                        Roms = new List<GameRom>() { new GameRom(gameName, Path.Combine(dir.FullName, baseGame.Name)) },
                        GameActions = new List<GameAction>() { new GameAction()
                        {
                            Name = $"Play in {mapping.Emulator.Name}",
                            Type = GameActionType.Emulator,
                            EmulatorId = mapping.EmulatorId,
                            EmulatorProfileId = mapping.EmulatorProfileId,
                            IsPlayAction = true,
                        } }
                    };
                }
            }
            #endregion

            #region Import "uninstalled" games
            if (Directory.Exists(srcPath))
            {
                foreach (var dir in SafeGetDirectories(srcPath))
                {
                    if (args.CancelToken.IsCancellationRequested)
                        yield break;

                    var baseGame = FindBaseGameFile(dir.FullName);
                    if (baseGame == null)
                        continue;

                    // Skip if already installed (equivalent folder exists at destination)
                    var equivalentInstalledPath = Path.Combine(dstPath, dir.Name);
                    if (Directory.Exists(equivalentInstalledPath))
                        continue;

                    var info = new RyujinxCopyGameInfo()
                    {
                        MappingId = mapping.MappingId,
                        SourceFolderName = dir.Name,
                        BaseGameFileName = baseGame.Name,
                    };

                    var gameName = CleanGameName(dir.Name);

                    yield return new GameMetadata()
                    {
                        Source = EmuLibrary.SourceName,
                        Name = gameName,
                        IsInstalled = false,
                        GameId = info.AsGameId(),
                        Platforms = new HashSet<MetadataProperty>() { new MetadataNameProperty(mapping.Platform.Name) },
                        Regions = FileNameUtils.GuessRegionsFromRomName(dir.Name).Select(r => new MetadataNameProperty(r)).ToHashSet<MetadataProperty>(),
                        InstallSize = GetDirectorySize(dir.FullName),
                        GameActions = new List<GameAction>() { new GameAction()
                        {
                            Name = $"Play in {mapping.Emulator.Name}",
                            Type = GameActionType.Emulator,
                            EmulatorId = mapping.EmulatorId,
                            EmulatorProfileId = mapping.EmulatorProfileId,
                            IsPlayAction = true,
                        } }
                    };
                }
            }
            #endregion
        }

        public override bool TryGetGameInfoBaseFromLegacyGameId(Game game, EmulatorMapping mapping, out ELGameInfo gameInfo)
        {
            // No legacy format to support for this new type
            gameInfo = null;
            return false;
        }

        public override IEnumerable<Game> GetUninstalledGamesMissingSourceFiles(CancellationToken ct)
        {
            return _playniteAPI.Database.Games.TakeWhile(g => !ct.IsCancellationRequested)
                .Where(g =>
                {
                    if (g.PluginId != EmuLibrary.PluginId || g.IsInstalled)
                        return false;

                    var info = g.GetELGameInfo();
                    if (info.RomType != RomType.RyujinxCopy)
                        return false;

                    return !Directory.Exists((info as RyujinxCopyGameInfo).SourceFullPath);
                });
        }

        #region Helper methods

        /// <summary>
        /// Find the base game NSP/XCI file at the root of the given folder.
        /// Prefers files containing "[v0]" or title IDs ending in "000" (base game convention).
        /// Falls back to any NSP/XCI at the root level.
        /// </summary>
        private static FileInfo FindBaseGameFile(string folderPath)
        {
            var rootFiles = new DirectoryInfo(folderPath)
                .EnumerateFiles()
                .Where(f => s_switchExtensions.Contains(f.Extension.ToLower()))
                .ToList();

            if (rootFiles.Count == 0)
                return null;

            // Prefer base game: contains [v0] in the name
            var baseByVersion = rootFiles.FirstOrDefault(f => f.Name.Contains("[v0]"));
            if (baseByVersion != null)
                return baseByVersion;

            // Or title ID ends in 000 (Switch base game convention)
            var baseByTitleId = rootFiles.FirstOrDefault(f =>
            {
                var match = Regex.Match(f.Name, @"\[([0-9A-Fa-f]{16})\]");
                return match.Success && match.Groups[1].Value.EndsWith("000");
            });
            if (baseByTitleId != null)
                return baseByTitleId;

            // Fallback: largest file at root level
            return rootFiles.OrderByDescending(f => f.Length).First();
        }

        /// <summary>
        /// Clean the folder name to extract a presentable game name.
        /// Removes tags like (NSP), (EU), (Base Game), etc.
        /// </summary>
        private static string CleanGameName(string folderName)
        {
            var cleaned = s_folderTagPattern.Replace(folderName, " ").Trim();
            // If cleaning removed everything, fall back to the original
            if (string.IsNullOrWhiteSpace(cleaned))
                cleaned = folderName;
            return StringExtensions.NormalizeGameName(cleaned);
        }

        /// <summary>
        /// Get total size of all files in a directory tree.
        /// </summary>
        private static ulong GetDirectorySize(string path)
        {
            try
            {
                return (ulong)new DirectoryInfo(path)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Safely enumerate top-level directories.
        /// </summary>
        private static IEnumerable<DirectoryInfo> SafeGetDirectories(string path)
        {
            try
            {
                return new DirectoryInfo(path).EnumerateDirectories();
            }
            catch
            {
                return Enumerable.Empty<DirectoryInfo>();
            }
        }

        #endregion
    }
}
