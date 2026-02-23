using LibHac.Fs;
using LibHac.FsSystem;
using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace EmuLibrary.RomTypes.RyujinxCopy
{
    #region JSON Models

    internal class RyujinxUpdatesConfig
    {
        [JsonProperty("selected")]
        public string Selected { get; set; } = "";

        [JsonProperty("paths")]
        public List<string> Paths { get; set; } = new List<string>();
    }

    internal class RyujinxDlcContainer
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("dlc_nca_list")]
        public List<RyujinxDlcNca> DlcNcaList { get; set; } = new List<RyujinxDlcNca>();
    }

    internal class RyujinxDlcNca
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("title_id")]
        public long TitleId { get; set; }

        [JsonProperty("is_enabled")]
        public bool IsEnabled { get; set; } = true;
    }

    #endregion

    /// <summary>
    /// Helper to register/deregister update and DLC NSP files in Ryujinx's per-game
    /// configuration (updates.json, dlc.json) at %APPDATA%\Ryujinx\games\{titleId}\.
    /// 
    /// Primary strategy: PATH REWRITING
    /// If Ryujinx already has existing config entries (from manual setup or a previous
    /// source location), we clone those entries replacing the old source path with the
    /// new installed path. This preserves all NCA metadata and title_ids perfectly.
    /// 
    /// Fallback strategy: DISCOVERY
    /// For update NSPs not yet in config, we add them by path (no NCA parsing needed).
    /// For DLC NSPs not yet in config, we open the PFS0 to list NCA files.
    /// </summary>
    internal static class RyujinxConfigHelper
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private static readonly string RyujinxGamesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Ryujinx", "games");

        // Matches standard 16-hex-digit title IDs like [0100152000022000]
        private static readonly Regex TitleIdRegex = new Regex(
            @"\[([0-9A-Fa-f]{16})\]", RegexOptions.Compiled);

        private static readonly Regex VersionRegex = new Regex(
            @"\[v(\d+)\]", RegexOptions.Compiled);

        /// <summary>
        /// After installing a game folder, register updates and DLC in Ryujinx config.
        /// Uses a two-phase approach:
        /// 1) Rewrite existing config entries from ANY old path to the new installed path
        /// 2) Discover any new update/DLC NSPs not yet in config
        /// </summary>
        /// <param name="sourceFolderPath">The original source folder (e.g. E:\SwitchGames\GameFolder)</param>
        /// <param name="installedFolderPath">The installed destination folder</param>
        /// <param name="baseGameFileName">Filename of the base game NSP/XCI</param>
        public static void RegisterUpdatesAndDlc(string sourceFolderPath, string installedFolderPath, string baseGameFileName)
        {
            try
            {
                var baseTitleId = ExtractTitleId(baseGameFileName);
                if (baseTitleId == null)
                {
                    logger.Warn($"[RyujinxConfig] Cannot extract title ID from: {baseGameFileName}");
                    return;
                }

                var gameConfigDir = Path.Combine(RyujinxGamesPath, baseTitleId.ToLower());
                Directory.CreateDirectory(gameConfigDir);

                // Phase 1: Rewrite existing config entries (path substitution)
                int rewrittenUpdates = RewriteExistingUpdatesConfig(gameConfigDir, sourceFolderPath, installedFolderPath);
                int rewrittenDlc = RewriteExistingDlcConfig(gameConfigDir, sourceFolderPath, installedFolderPath);

                logger.Info($"[RyujinxConfig] Phase 1 - Rewrote {rewrittenUpdates} update path(s) and {rewrittenDlc} DLC path(s)");

                // Phase 2: Discover new update/DLC NSPs not yet in config
                ulong baseTid = ulong.Parse(baseTitleId, NumberStyles.HexNumber);
                DiscoverAndRegisterNew(gameConfigDir, installedFolderPath, baseGameFileName, baseTid);

                logger.Info($"[RyujinxConfig] Registration complete for {baseTitleId}");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[RyujinxConfig] Failed to register updates/DLC");
            }
        }

        /// <summary>
        /// On uninstall, remove Ryujinx config entries that reference paths
        /// inside the game's installed folder.
        /// </summary>
        public static void DeregisterUpdatesAndDlc(string installedFolderPath, string baseGameFileName)
        {
            try
            {
                var baseTitleId = ExtractTitleId(baseGameFileName);
                if (baseTitleId == null) return;

                var gameConfigDir = Path.Combine(RyujinxGamesPath, baseTitleId.ToLower());
                if (!Directory.Exists(gameConfigDir)) return;

                // --- updates.json ---
                var updatesFile = Path.Combine(gameConfigDir, "updates.json");
                if (File.Exists(updatesFile))
                {
                    var config = JsonConvert.DeserializeObject<RyujinxUpdatesConfig>(File.ReadAllText(updatesFile));
                    if (config?.Paths != null)
                    {
                        int before = config.Paths.Count;
                        config.Paths.RemoveAll(p => p.StartsWith(installedFolderPath, StringComparison.OrdinalIgnoreCase));

                        if (config.Selected?.StartsWith(installedFolderPath, StringComparison.OrdinalIgnoreCase) == true)
                            config.Selected = config.Paths.LastOrDefault() ?? "";

                        if (config.Paths.Count == 0)
                            File.Delete(updatesFile);
                        else if (config.Paths.Count < before)
                            File.WriteAllText(updatesFile, JsonConvert.SerializeObject(config, Formatting.Indented));
                    }
                }

                // --- dlc.json ---
                var dlcFile = Path.Combine(gameConfigDir, "dlc.json");
                if (File.Exists(dlcFile))
                {
                    var containers = JsonConvert.DeserializeObject<List<RyujinxDlcContainer>>(File.ReadAllText(dlcFile))
                        ?? new List<RyujinxDlcContainer>();

                    int before = containers.Count;
                    containers.RemoveAll(c => c.Path.StartsWith(installedFolderPath, StringComparison.OrdinalIgnoreCase));

                    if (containers.Count == 0)
                        File.Delete(dlcFile);
                    else if (containers.Count < before)
                        File.WriteAllText(dlcFile, JsonConvert.SerializeObject(containers, Formatting.Indented));
                }

                logger.Info($"[RyujinxConfig] Deregistered updates/DLC for {baseTitleId}");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[RyujinxConfig] Failed to deregister updates/DLC");
            }
        }

        #region Phase 1: Path rewriting

        /// <summary>
        /// Rewrite updates.json: for each path entry that starts with the source folder,
        /// add an equivalent entry pointing to the installed folder.
        /// Also scans ALL existing paths for any that reference the same game folder name
        /// but from a different drive/location (handles moved source folders).
        /// </summary>
        private static int RewriteExistingUpdatesConfig(string gameConfigDir, string sourceFolderPath, string installedFolderPath)
        {
            var configPath = Path.Combine(gameConfigDir, "updates.json");
            if (!File.Exists(configPath)) return 0;

            var config = JsonConvert.DeserializeObject<RyujinxUpdatesConfig>(File.ReadAllText(configPath));
            if (config?.Paths == null) return 0;

            var sourceFolderName = new DirectoryInfo(sourceFolderPath).Name;
            int added = 0;

            // Collect new paths to add
            var newPaths = new List<(string newPath, ulong version)>();
            foreach (var oldPath in config.Paths.ToList())
            {
                // Try direct source→dest substitution
                var newPath = TryRewritePath(oldPath, sourceFolderPath, installedFolderPath);

                // Also try matching by folder name from any location (handles drive changes)
                if (newPath == null)
                    newPath = TryRewritePathByFolderName(oldPath, sourceFolderName, installedFolderPath);

                if (newPath != null && !config.Paths.Any(p => p.Equals(newPath, StringComparison.OrdinalIgnoreCase)))
                {
                    newPaths.Add((newPath, ExtractVersion(Path.GetFileName(newPath))));
                    added++;
                }
            }

            if (added > 0)
            {
                foreach (var (path, _) in newPaths)
                    config.Paths.Add(path);

                // Select the highest-version update from installed paths
                var latestInstalled = newPaths.OrderByDescending(u => u.version).FirstOrDefault();
                if (latestInstalled.newPath != null)
                    config.Selected = latestInstalled.newPath;

                File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
            }

            return added;
        }

        /// <summary>
        /// Rewrite dlc.json: for each DLC container whose path references the source folder,
        /// clone the entry with the installed path (preserving all NCA metadata and title_ids).
        /// </summary>
        private static int RewriteExistingDlcConfig(string gameConfigDir, string sourceFolderPath, string installedFolderPath)
        {
            var configPath = Path.Combine(gameConfigDir, "dlc.json");
            if (!File.Exists(configPath)) return 0;

            var containers = JsonConvert.DeserializeObject<List<RyujinxDlcContainer>>(File.ReadAllText(configPath));
            if (containers == null) return 0;

            var sourceFolderName = new DirectoryInfo(sourceFolderPath).Name;
            int added = 0;

            var newContainers = new List<RyujinxDlcContainer>();
            foreach (var existing in containers)
            {
                var newPath = TryRewritePath(existing.Path, sourceFolderPath, installedFolderPath);
                if (newPath == null)
                    newPath = TryRewritePathByFolderName(existing.Path, sourceFolderName, installedFolderPath);

                if (newPath != null && !containers.Any(c => c.Path.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                                    && !newContainers.Any(c => c.Path.Equals(newPath, StringComparison.OrdinalIgnoreCase)))
                {
                    // Verify the file actually exists at the new path
                    if (File.Exists(newPath))
                    {
                        newContainers.Add(new RyujinxDlcContainer()
                        {
                            Path = newPath,
                            // Clone the NCA list — preserves correct title_ids
                            DlcNcaList = existing.DlcNcaList.Select(nca => new RyujinxDlcNca()
                            {
                                Path = nca.Path,
                                TitleId = nca.TitleId,
                                IsEnabled = nca.IsEnabled,
                            }).ToList(),
                        });
                        added++;
                    }
                }
            }

            if (added > 0)
            {
                containers.AddRange(newContainers);
                File.WriteAllText(configPath, JsonConvert.SerializeObject(containers, Formatting.Indented));
            }

            return added;
        }

        /// <summary>
        /// Direct path prefix substitution: source\relative → dest\relative
        /// </summary>
        private static string TryRewritePath(string oldPath, string sourceFolderPath, string installedFolderPath)
        {
            if (oldPath.StartsWith(sourceFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                var relative = oldPath.Substring(sourceFolderPath.Length).TrimStart('\\');
                return Path.Combine(installedFolderPath, relative);
            }
            return null;
        }

        /// <summary>
        /// Match by game folder name from any location.
        /// E.g. old path "D:\SwitchGames\GameFolder\sub\file.nsp" matches folder name "GameFolder"
        /// and rewrites to "C:\Dest\GameFolder\sub\file.nsp".
        /// </summary>
        private static string TryRewritePathByFolderName(string oldPath, string sourceFolderName, string installedFolderPath)
        {
            // Look for the folder name in the old path
            var marker = "\\" + sourceFolderName + "\\";
            var idx = oldPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var relative = oldPath.Substring(idx + marker.Length);
                return Path.Combine(installedFolderPath, relative);
            }

            // Also handle case where it's at the end (no trailing backslash)
            var markerEnd = "\\" + sourceFolderName;
            if (oldPath.EndsWith(markerEnd, StringComparison.OrdinalIgnoreCase))
            {
                return installedFolderPath;
            }

            return null;
        }

        #endregion

        #region Phase 2: Discovery of new NSPs

        /// <summary>
        /// Find update/DLC NSPs in the installed folder that aren't yet in Ryujinx config,
        /// and add them.
        /// </summary>
        private static void DiscoverAndRegisterNew(string gameConfigDir, string installedFolderPath, string baseGameFileName, ulong baseTid)
        {
            // Find all NSP files except the base game
            List<string> allNsps;
            try
            {
                allNsps = Directory.EnumerateFiles(installedFolderPath, "*.nsp", SearchOption.AllDirectories)
                    .Where(f => !string.Equals(Path.GetFileName(f), baseGameFileName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch
            {
                return;
            }

            if (allNsps.Count == 0) return;

            // Classify NSPs into updates and DLCs
            var updatePaths = new List<(string path, ulong version)>();
            var dlcNspPaths = new List<string>();

            foreach (var nspPath in allNsps)
            {
                var fileName = Path.GetFileName(nspPath);
                var titleIdHex = ExtractTitleId(fileName);

                if (titleIdHex != null)
                {
                    ulong tid = ulong.Parse(titleIdHex, NumberStyles.HexNumber);
                    if (IsUpdate(tid, baseTid))
                    {
                        updatePaths.Add((nspPath, ExtractVersion(fileName)));
                        continue;
                    }
                }

                // Any NSP that isn't the base game and isn't a recognized update
                // is treated as potential DLC (including files with non-standard title IDs)
                dlcNspPaths.Add(nspPath);
            }

            // --- Add new update entries ---
            if (updatePaths.Count > 0)
            {
                var configPath = Path.Combine(gameConfigDir, "updates.json");
                RyujinxUpdatesConfig config;
                if (File.Exists(configPath))
                    config = JsonConvert.DeserializeObject<RyujinxUpdatesConfig>(File.ReadAllText(configPath)) ?? new RyujinxUpdatesConfig();
                else
                    config = new RyujinxUpdatesConfig();

                int added = 0;
                foreach (var (path, _) in updatePaths)
                {
                    if (!config.Paths.Any(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    {
                        config.Paths.Add(path);
                        added++;
                    }
                }

                if (added > 0 || string.IsNullOrEmpty(config.Selected))
                {
                    // Select highest version from all installed paths
                    var allInstalledUpdates = config.Paths
                        .Where(p => p.StartsWith(installedFolderPath, StringComparison.OrdinalIgnoreCase))
                        .Select(p => (path: p, version: ExtractVersion(Path.GetFileName(p))))
                        .OrderByDescending(u => u.version)
                        .ToList();

                    if (allInstalledUpdates.Count > 0)
                        config.Selected = allInstalledUpdates.First().path;

                    File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
                    logger.Info($"[RyujinxConfig] Phase 2 - Added {added} new update path(s)");
                }
            }

            // --- Add new DLC entries ---
            if (dlcNspPaths.Count > 0)
            {
                var configPath = Path.Combine(gameConfigDir, "dlc.json");
                List<RyujinxDlcContainer> containers;
                if (File.Exists(configPath))
                    containers = JsonConvert.DeserializeObject<List<RyujinxDlcContainer>>(File.ReadAllText(configPath)) ?? new List<RyujinxDlcContainer>();
                else
                    containers = new List<RyujinxDlcContainer>();

                int added = 0;
                foreach (var nspPath in dlcNspPaths)
                {
                    if (containers.Any(c => c.Path.Equals(nspPath, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var container = BuildDlcContainer(nspPath);
                    if (container != null)
                    {
                        containers.Add(container);
                        added++;
                    }
                }

                if (added > 0)
                {
                    File.WriteAllText(configPath, JsonConvert.SerializeObject(containers, Formatting.Indented));
                    logger.Info($"[RyujinxConfig] Phase 2 - Added {added} new DLC container(s)");
                }
            }
        }

        /// <summary>
        /// Build a DLC container entry by opening the NSP as PFS0 and listing content NCAs.
        /// PFS0 headers are unencrypted, so no Switch keys are needed for listing.
        /// </summary>
        private static RyujinxDlcContainer BuildDlcContainer(string nspPath)
        {
            var fileName = Path.GetFileName(nspPath);

            // Extract title ID from filename - try strict hex first, then relaxed pattern
            long titleIdLong = 0;
            var titleIdHex = ExtractTitleId(fileName);
            if (titleIdHex != null)
            {
                titleIdLong = long.Parse(titleIdHex, NumberStyles.HexNumber);
            }
            else
            {
                // Try relaxed pattern for non-standard IDs like [010015200002300x]
                var relaxedMatch = Regex.Match(fileName, @"\[([0-9A-Fa-f]{15})[0-9A-Fa-fx]\]");
                if (relaxedMatch.Success)
                {
                    // Replace non-hex chars with '0' for a parseable approximation
                    var approxId = relaxedMatch.Groups[1].Value + "0";
                    titleIdLong = long.Parse(approxId, NumberStyles.HexNumber);
                    logger.Info($"[RyujinxConfig] Using approximate title ID {approxId} for: {fileName}");
                }
                else
                {
                    logger.Warn($"[RyujinxConfig] No title ID in DLC filename: {fileName}");
                    return null;
                }
            }

            try
            {
                var ncaList = new List<RyujinxDlcNca>();

                using (var fileStream = new FileStream(nspPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var pfs = new PartitionFileSystem(fileStream.AsStorage());

                    foreach (var entry in pfs.EnumerateEntries("/", "*.nca"))
                    {
                        // Skip CNMT (metadata) NCAs, only keep content NCAs
                        if (entry.Name.ToLower().Contains("cnmt"))
                            continue;

                        ncaList.Add(new RyujinxDlcNca()
                        {
                            Path = "/" + entry.Name,
                            TitleId = titleIdLong,
                            IsEnabled = true,
                        });
                    }
                }

                if (ncaList.Count == 0)
                {
                    logger.Warn($"[RyujinxConfig] No content NCAs in DLC: {fileName}");
                    return null;
                }

                return new RyujinxDlcContainer()
                {
                    Path = nspPath,
                    DlcNcaList = ncaList,
                };
            }
            catch (Exception ex)
            {
                logger.Warn($"[RyujinxConfig] Failed to parse DLC NSP '{fileName}': {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Title ID helpers

        /// <summary>
        /// Extract 16-character hex title ID from a filename like "Game[0100AABB...][v0].nsp".
        /// </summary>
        internal static string ExtractTitleId(string fileName)
        {
            var match = TitleIdRegex.Match(fileName);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static ulong ExtractVersion(string fileName)
        {
            var match = VersionRegex.Match(fileName);
            return match.Success && ulong.TryParse(match.Groups[1].Value, out var v) ? v : 0;
        }

        /// <summary>
        /// Switch update title IDs share the same application ID (top 51 bits)
        /// and have type bits = 0x800 in the low 13 bits.
        /// </summary>
        private static bool IsUpdate(ulong titleId, ulong baseTitleId)
        {
            return (titleId & 0xFFFFFFFFFFFFE000) == (baseTitleId & 0xFFFFFFFFFFFFE000)
                && (titleId & 0x1FFF) == 0x800;
        }

        /// <summary>
        /// Switch DLC title IDs share the same application ID (top 51 bits)
        /// and have bit 12 set in the type field (0x1000+).
        /// </summary>
        private static bool IsDlc(ulong titleId, ulong baseTitleId)
        {
            return (titleId & 0xFFFFFFFFFFFFE000) == (baseTitleId & 0xFFFFFFFFFFFFE000)
                && (titleId & 0x1000) != 0
                && titleId != baseTitleId;
        }

        #endregion
    }
}
