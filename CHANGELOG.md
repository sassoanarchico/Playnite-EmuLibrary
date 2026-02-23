# Changelog

All notable changes to EmuLibrary will be documented in this file.

## [3.5.0] - 2026-02-23

### Added
- **New RyujinxCopy ROM type** for Nintendo Switch games via Ryujinx emulator
  - Copies entire game folders (base game + updates + DLC subfolders) from source to destination
  - Ryujinx can open NSP/XCI files directly — no NCA extraction or NAND structure needed
  - Intelligent base game detection: prefers files with `[v0]` in name, then title IDs ending in `000`, then largest file at root level
  - Clean game name extraction: strips tags like `(NSP)`, `(EU)`, `(Base Game)`, etc. from folder names
  - Region detection from folder/file names
- **Automatic Ryujinx update/DLC registration on install**
  - Writes `updates.json` with the highest-version update selected
  - Writes `dlc.json` with NCA contents read directly from each DLC NSP (PFS0 parsing, no keys required)
  - Smart path rewriting: if Ryujinx already has config entries from an old source location (e.g. drive changed from D: to E:), clones those entries with the new installed path — preserving all NCA metadata and title IDs
  - Handles non-standard title IDs (e.g. `010015200002300x`) with relaxed regex fallback
- **Automatic Ryujinx update/DLC deregistration on uninstall**
  - Removes only the entries pointing to the uninstalled folder, leaving other entries intact

### New files
- `RomTypes/RyujinxCopy/RyujinxCopyGameInfo.cs` — ProtoBuf game info with SourceFolderName and BaseGameFileName
- `RomTypes/RyujinxCopy/RyujinxCopyGameInfoExtensions.cs` — Extension method for Game objects
- `RomTypes/RyujinxCopy/RyujinxCopyScanner.cs` — Scanner for source/destination folders
- `RomTypes/RyujinxCopy/RyujinxCopyInstallController.cs` — Folder copy + Ryujinx config registration
- `RomTypes/RyujinxCopy/RyujinxCopyUninstallController.cs` — Folder delete + Ryujinx config cleanup
- `RomTypes/RyujinxCopy/RyujinxConfigHelper.cs` — Ryujinx updates.json/dlc.json read/write helper

## [3.4.1] - 2024-01-13

### Added
- Support for using Windows file copy UI when installing SingleFile or MultiFile games (Alex Tiley)
- Settings validation to prevent invalid configurations

### Changed
- Greatly improved game install speed for SingleFile and MultiFile games (Alex Tiley)

## [3.3.4] - 2024-01-07

### Fixed
- Fixed a case where some files would not get deleted when uninstalling a MultiFile game (Alex Tiley)
- Fixed some cases where a game would not be marked as uninstalled after uninstalling it (Alex Tiley)
- Fixed game being stuck as "Installing" if install process failed for any reason (Alex Tiley)
- Fixed scanning of ROM files that lack a file extension (Alex Tiley)

## [3.3.3] - 2024-01-01

### Changed
- Improved Yuzu RomType scanning speed on collections with many DLC files

### Added
- Added setting of Version field for Yuzu RomType games
