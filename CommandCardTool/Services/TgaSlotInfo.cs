namespace LauncherWinUI.Services;

/// <summary>
/// Identifies which TGA/DDS entry from the winning BIG in load order contains
/// the button icon for a given command-card slot, and the pixel coordinates of
/// the icon within the atlas (on a 512-pixel grid, same as MappedImage INI coords).
/// </summary>
internal sealed record TgaSlotInfo(
    string EntryName,   // e.g. "Data\English\Art\Textures\SAUserInterface512_001.tga"
    int    Left,
    int    Top,
    int    Right,
    int    Bottom);
