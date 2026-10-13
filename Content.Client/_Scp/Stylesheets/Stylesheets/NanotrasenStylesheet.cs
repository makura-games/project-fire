using Content.Client._Scp.Stylesheets.Palette;
using Content.Client.Stylesheets.Palette;

// НЕ МЕНЯТЬ
namespace Content.Client.Stylesheets.Stylesheets;

public partial class NanotrasenStylesheet
{
    // Sunrise-Edit: Замена стандартных палитр на SCP/Grimdark
    public override ColorPalette PrimaryPalette => ScpPalettes.Primary;
    public override ColorPalette SecondaryPalette => ScpPalettes.Secondary;
    public override ColorPalette PositivePalette => ScpPalettes.Green;
    public override ColorPalette NegativePalette => ScpPalettes.Red;
    public override ColorPalette HighlightPalette => ScpPalettes.Red;
}
