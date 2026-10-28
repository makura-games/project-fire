using Robust.Shared.Graphics;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    public sealed partial class ZEye
    {
        /// <summary>
        /// Original eye whose player-specific effects apply to this projected floor.
        /// </summary>
        public IEye? SourceEye;
    }
}
