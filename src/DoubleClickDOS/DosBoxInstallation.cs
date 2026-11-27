namespace DoubleClickDOS;

public enum DosBoxFlavor
{
    DosBox,
    DosBoxX,
}

/// <summary>Tells DOSBox, DOSBox SVN and DOSBox-X apart, since their command lines differ slightly.</summary>
internal static class DosBoxInstallation
{
    public static DosBoxFlavor DetectFlavor(string executablePath)
    {
        string name = Path.GetFileNameWithoutExtension(executablePath);

        return name.Contains("dosbox-x", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("dosboxx", StringComparison.OrdinalIgnoreCase)
            ? DosBoxFlavor.DosBoxX
            : DosBoxFlavor.DosBox;
    }

    public static string DisplayName(DosBoxFlavor flavor) =>
        flavor == DosBoxFlavor.DosBoxX ? "DOSBox-X" : "DOSBox";
}
