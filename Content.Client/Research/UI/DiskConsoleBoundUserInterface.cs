using Content.Shared.Research;
using Content.Shared.Research.Components;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client.Research.UI
{
    public sealed partial class DiskConsoleBoundUserInterface : BoundUserInterface // Fire edit
    {
        [ViewVariables]
        private DiskConsoleMenu? _menu;

        [Dependency] private IPrototypeManager _prototypeManager = default!;


        public DiskConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            _menu = new DiskConsoleMenu(_prototypeManager);

            _menu.OpenCentered();
            _menu.OnClose += Close;

            _menu.OnServerButtonPressed += () =>
            {
                SendMessage(new ConsoleServerSelectionMessage());
            };
            _menu.OnPrintButtonPressed += () =>
            {
                SendMessage(new DiskConsolePrintDiskMessage());
            };
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (state is not DiskConsoleBoundUserInterfaceState msg)
                return;

            _menu?.Update(msg);
        }
    }
}
