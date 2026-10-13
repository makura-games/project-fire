using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.Shared._Scp.Scp330;

public abstract partial class SharedScp330System : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;

    private const int ExaminePriority = -50;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp330BowlComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<Scp330BowlComponent, InteractHandEvent>(OnActivate);
    }

    #region Event handlers

    private void OnExamined(Entity<Scp330BowlComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
        {
            AddMessage(ent, ref args, Loc.GetString("scp330-cant-see-count"));
            return;
        }

        var container = _container.EnsureContainer<Container>(ent, StorageComponent.ContainerId);
        var message = Loc.GetString("scp330-see-count", ("count", container.Count));

        var taken = ent.Comp.ThiefCounter.GetValueOrDefault(args.Examiner);
        var canTake = Math.Max(0, ent.Comp.PunishmentAfter - taken);
        var message2 = canTake != 0
            ? Loc.GetString("scp330-can-take", ("count", canTake))
            : Loc.GetString("scp330-can-not-take");

        AddMessage(ent, ref args, message, message2);
    }

    private void OnActivate(Entity<Scp330BowlComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryTakeCandy(ent, args.User))
            return;

        args.Handled = true;
    }

    #endregion

    #region Virtuals

    public virtual bool TryTakeCandy(Entity<Scp330BowlComponent> ent, EntityUid user) { return true; }

    protected virtual void ApplyPunishment(Entity<Scp330BowlComponent> ent, EntityUid user) {}

    #endregion

    #region Helpers

    private void AddMessage(Entity<Scp330BowlComponent> ent, ref ExaminedEvent args, params string[] messages)
    {
        using (args.PushGroup(nameof(Scp330BowlComponent)))
        {
            foreach (var message in messages)
            {
                args.PushMarkup(GetColoredMessage(ent, message), ExaminePriority);
            }
        }
    }

    private string GetColoredMessage(Entity<Scp330BowlComponent> ent, string message)
    {
        return $"[color={ent.Comp.ExamineMessageColor.ToHex()}]{message}[/color]";
    }

    #endregion
}
