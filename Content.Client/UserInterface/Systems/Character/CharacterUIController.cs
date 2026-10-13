using System.Linq;
using System.Numerics;
using Content.Client._Scp.UI;
using Content.Client.CharacterInfo;
using Content.Client.Gameplay;
using Content.Client.Mind;
using Content.Client.Roles;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Character.Controls;
using Content.Client.UserInterface.Systems.Character.Windows;
using Content.Client.UserInterface.Systems.Objectives.Controls;
using Content.Shared._Scp.CharacterInfo.AccessLevel;
using Content.Shared._Scp.CharacterInfo.EmployeeClass;
using Content.Shared._Scp.Fear.Components;
using Content.Shared.Input;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input.Binding;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.Client.CharacterInfo.CharacterInfoSystem;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client.UserInterface.Systems.Character;

[UsedImplicitly]
public sealed partial class CharacterUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>, IOnSystemChanged<CharacterInfoSystem>
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    [UISystemDependency] private readonly CharacterInfoSystem _characterInfo = default!;
    [UISystemDependency] private readonly SpriteSystem _sprite = default!;
    // Fire added start
    [Dependency] private ILocalizationManager _loc = default!;
    [UISystemDependency] private EmployeeClassSystem _employeeClass = default!;
    [UISystemDependency] private AccessLevelSystem _accessLevel = default!;
    [UISystemDependency] private JobSystem _job = default!;
    // Fire added end

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MindRoleTypeChangedEvent>(OnRoleTypeChanged);
    }

    private CharacterWindow? _window;
    private MenuButton? CharacterButton => UIManager.GetActiveUIWidgetOrNull<MenuBar.Widgets.GameTopMenuBar>()?.CharacterButton;

    public void OnStateEntered(GameplayState state)
    {
        DebugTools.Assert(_window == null);

        _window = UIManager.CreateWindow<CharacterWindow>();
        LayoutContainer.SetAnchorPreset(_window, LayoutContainer.LayoutPreset.CenterTop);

        _window.OnClose += DeactivateButton;
        _window.OnOpen += ActivateButton;

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenCharacterMenu,
                InputCmdHandler.FromDelegate(_ => ToggleWindow()))
            .Register<CharacterUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        if (_window != null)
        {
            _window.Close();
            _window = null;
        }

        CommandBinds.Unregister<CharacterUIController>();
    }

    public void OnSystemLoaded(CharacterInfoSystem system)
    {
        system.OnCharacterUpdate += CharacterUpdated;
        _player.LocalPlayerDetached += CharacterDetached;
    }

    public void OnSystemUnloaded(CharacterInfoSystem system)
    {
        system.OnCharacterUpdate -= CharacterUpdated;
        _player.LocalPlayerDetached -= CharacterDetached;
    }

    public void UnloadButton()
    {
        if (CharacterButton == null)
        {
            return;
        }

        CharacterButton.OnPressed -= CharacterButtonPressed;
    }

    public void LoadButton()
    {
        if (CharacterButton == null)
        {
            return;
        }

        CharacterButton.OnPressed += CharacterButtonPressed;
    }

    private void DeactivateButton()
    {
        if (CharacterButton == null)
        {
            return;
        }

        CharacterButton.Pressed = false;
    }

    private void ActivateButton()
    {
        if (CharacterButton == null)
        {
            return;
        }

        CharacterButton.Pressed = true;
    }

    private void CharacterUpdated(CharacterData data)
    {
        if (_window == null)
        {
            return;
        }

        var (entity, job, objectives, briefing, entityName) = data;

        _window.SpriteView.SetEntity(entity);

        UpdateRoleType();
        // Fire added start
        UpdateJobInfo(job, ref _window);
        UpdateFears(entity, ref _window);
        UpdateEmployeeClass(entity, ref _window);
        UpdateAccessLevel(entity, ref _window);
        // Fire added end

        _window.NameLabel.Text = entityName;
        // _window.SubText.Text = job; Fire edit
        _window.Objectives.RemoveAllChildren();
        _window.ObjectivesLabel.Visible = objectives.Any();

        foreach (var (groupId, conditions) in objectives)
        {
            var objectiveControl = new CharacterObjectiveControl
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                Modulate = Color.Gray
            };


            var objectiveText = new FormattedMessage();
            objectiveText.TryAddMarkup(groupId, out _);

            var objectiveLabel = new RichTextLabel
            {
                StyleClasses = { StyleClass.TooltipTitle }
            };
            objectiveLabel.SetMessage(objectiveText);

            objectiveControl.AddChild(objectiveLabel);

            foreach (var condition in conditions)
            {
                var conditionControl = new ObjectiveConditionsControl();
                conditionControl.ProgressTexture.Texture = _sprite.Frame0(condition.Icon);
                conditionControl.ProgressTexture.Progress = condition.Progress;
                var titleMessage = new FormattedMessage();
                var descriptionMessage = new FormattedMessage();
                titleMessage.AddText(condition.Title);
                descriptionMessage.AddText(condition.Description);

                conditionControl.Title.SetMessage(titleMessage);
                conditionControl.Description.SetMessage(descriptionMessage);

                objectiveControl.AddChild(conditionControl);
            }

            _window.Objectives.AddChild(objectiveControl);
        }

        if (briefing != null)
        {
            var briefingControl = new ObjectiveBriefingControl();
            var text = new FormattedMessage();
            text.PushColor(Color.Yellow);
            text.AddText(briefing);
            briefingControl.Label.SetMessage(text);
            _window.Objectives.AddChild(briefingControl);
        }

        var controls = _characterInfo.GetCharacterInfoControls(entity);
        foreach (var control in controls)
        {
            _window.Objectives.AddChild(control);
        }

        _window.RolePlaceholder.Visible = false; // Fire edit - не сри мне тут
    }

    private void OnRoleTypeChanged(MindRoleTypeChangedEvent ev, EntitySessionEventArgs _)
    {
        UpdateRoleType();
    }

    private void UpdateRoleType()
    {
        if (_window == null || !_window.IsOpen)
            return;

        if (!_ent.TryGetComponent<MindContainerComponent>(_player.LocalEntity, out var container)
            || container.Mind is null)
            return;

        if (!_ent.TryGetComponent<MindComponent>(container.Mind.Value, out var mind))
            return;

        if (!_prototypeManager.TryIndex(mind.RoleType, out var proto))
            Log.Error($"Player '{_player.LocalSession}' has invalid Role Type '{mind.RoleType}'. Displaying default instead");

        _window.RoleType.Text = Loc.GetString(proto?.Name ?? "role-type-crew-aligned-name");
        _window.RoleType.FontColorOverride = proto?.Color ?? Color.White;
    }

    // Fire added start
    private void UpdateJobInfo(string jobProtoId, ref CharacterWindow window)
    {
        window.JobInfo.Visible = false;
        window.SubText.Visible = false;
        window.Separator.Visible = false;

        if (!_prototypeManager.TryIndex<JobPrototype>(jobProtoId, out var job))
            return;

        window.JobInfo.Visible = true;
        window.JobInfo.Text = $"{job.LocalizedName}: {job.LocalizedDescription}";

        // ФАК Ю, РИЧ ТЕКСТ, КОТОРЫЙ НЕ ХОЧЕТ СТАНОВИТЬСЯ НОРМАЛЬНЫМ
        // ПОКА Я НЕ РЕСАЙЗНУ ОКНО :>
        window.SetSize = new Vector2(window.Size.X + 1, window.Size.Y + 1);
        window.SetSize = new Vector2(window.Size.X - 1, window.Size.Y - 1);

        if (!_job.TryGetDepartment(job.ID, out var department))
            return;

        window.SubText.Text = Loc.GetString(department.Name);
        window.SubText.Visible = true;
        window.Separator.Visible = true;
    }

    private void UpdateFears(EntityUid uid, ref CharacterWindow window)
    {
        window.Fears.RemoveAllChildren();
        window.FearsPlaceholder.Visible = true;

        if (!_ent.TryGetComponent<FearComponent>(uid, out var fear))
            return;

        foreach (var phobia in fear.Phobias)
        {
            if (!_prototypeManager.TryIndex(phobia, out var phobiaProto))
                continue;

            var item = new ColoredInfo
            {
                NameString = _loc.GetString(phobiaProto.Name),
                DescriptionString = _loc.GetString(phobiaProto.Description),
                Color = phobiaProto.Color,
            };

            window.Fears.AddChild(item);
        }

        window.FearsPlaceholder.Visible = false;
    }

    private void UpdateEmployeeClass(EntityUid uid, ref CharacterWindow window)
    {
        window.EmployeeClass.RemoveAllChildren();
        window.EmployeeClassPlaceholder.Visible = true;

        if (!_employeeClass.TryGetName(uid, out var name, false))
            return;

        if (!_employeeClass.TryGetDescription(uid, out var description, false))
            return;

        var item = new ColoredInfo
        {
            NameString = name,
            DescriptionString = description,
            Color = EmployeeClassComponent.InfoColor,
        };

        window.EmployeeClass.AddChild(item);
        window.EmployeeClassPlaceholder.Visible = false;
    }

    private void UpdateAccessLevel(EntityUid uid, ref CharacterWindow window)
    {
        window.AccessLevel.RemoveAllChildren();
        window.AccessLevelPlaceholder.Visible = true;

        if (!_accessLevel.TryGetName(uid, out var name, false))
            return;

        if (!_accessLevel.TryGetDescription(uid, out var description, false))
            return;

        var item = new ColoredInfo
        {
            NameString = name,
            DescriptionString = description,
            Color = AccessLevelComponent.InfoColor,
        };

        window.AccessLevel.AddChild(item);
        window.AccessLevelPlaceholder.Visible = false;
    }

    private void OnResized()
    {
        if (_window == null)
            return;


    }
    // Fire added end

    private void CharacterDetached(EntityUid uid)
    {
        CloseWindow();
    }

    private void CharacterButtonPressed(ButtonEventArgs args)
    {
        ToggleWindow();
    }

    private void CloseWindow()
    {
        _window?.Close();
    }

    public void ToggleWindow()
    {
        if (_window == null)
            return;

        CharacterButton?.SetClickPressed(!_window.IsOpen);

        if (_window.IsOpen)
        {
            CloseWindow();
        }
        else
        {
            _characterInfo.RequestCharacterInfo();
            _window.Open();
        }
    }
}
