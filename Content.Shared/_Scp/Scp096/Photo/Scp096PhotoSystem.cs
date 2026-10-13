using Content.Shared._Scp.Scp096.Main.Components;
using Content.Shared._Scp.Scp096.Main.Systems;
using Content.Shared._Scp.ScpMask;
using Content.Shared.Examine;

namespace Content.Shared._Scp.Scp096.Photo;

/// <summary>
/// Система управляющая фотографиями SCP-096.
/// Отвечает за действия при осмотре фотографии.
/// </summary>
public sealed partial class Scp096PhotoSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedScp096System _scp096 = default!;
    [Dependency] private ScpMaskSystem _scpMask = default!;

    private const int Priority = -80;
    private static readonly Color TextColor = Color.Gray;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp096PhotoComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<Scp096PhotoComponent, ExaminedEvent>(OnExamined);
    }

    private void OnInit(Entity<Scp096PhotoComponent> photo, ref ComponentInit args)
    {
        // Меняем спрайт на проявленный
        _appearance.SetData(photo, Scp096PhotoVisualLayers.Base, true);
    }

    private void OnExamined(Entity<Scp096PhotoComponent> photo, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
        {
            args.PushMarkup(GetMessage("scp096-photo-not-in-details-range"), Priority);
            return;
        }

        var triggeredAny = false;
        var query = EntityQueryEnumerator<Scp096Component>();
        while (query.MoveNext(out var uid, out var scp096))
        {
            if (!_scp096.TryAddTarget((uid, scp096), args.Examiner, true, true))
                continue;

            _scpMask.TryTear(uid);
            triggeredAny = true;
        }

        var message = triggeredAny ? "scp096-photo-triggered" : "scp096-photo-not-triggered";
        args.PushMarkup(GetMessage(message), Priority);
    }

    private string GetMessage(string message)
    {
        return $"\n[color={TextColor.ToHex()}]{Loc.GetString(message)}[/color]";
    }
}
