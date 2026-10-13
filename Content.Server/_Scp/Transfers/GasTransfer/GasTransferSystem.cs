using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos.Components;

namespace Content.Server._Scp.Transfers.GasTransfer;

public sealed partial class GasTransferSystem : EntitySystem
{
    private const float BalanceThreshold = 0.001f;
    private const float SmallTransferThreshold = 0.1f;

    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private NodeContainerSystem _nodeContainer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasTransferComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GasTransferComponent, AtmosDeviceUpdateEvent>(OnGasTransferUpdated);
    }

    private void OnMapInit(Entity<GasTransferComponent> ent, ref MapInitEvent args)
    {
        TryFindPartner(ent.AsNullable());
    }

    private void OnGasTransferUpdated(Entity<GasTransferComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        if (string.IsNullOrEmpty(ent.Comp.LinkId))
            return;

        if (!ent.Comp.IsActive)
            return;

        if (!ValidatePartner(ent, out var partnerPipe))
            return;

        TransferGas(ent, partnerPipe, args.dt);
    }

    private bool ValidatePartner(Entity<GasTransferComponent> ent, out PipeNode partnerPipe)
    {
        partnerPipe = default!;

        if (!Exists(ent.Comp.Partner) || !ent.Comp.Partner.HasValue)
        {
            if (!TryFindPartner(ent.AsNullable()))
                return false;
        }

        if (ent.Comp.Partner!.Value.Comp.Deleted)
        {
            if (!TryFindPartner(ent.AsNullable()))
                return false;
        }

        var partnerUid = ent.Comp.Partner!.Value.Owner;

        if (ent.Comp.PartnerPipe == null)
        {
            if (!_nodeContainer.TryGetNode<PipeNode>(partnerUid, ent.Comp.Partner.Value.Comp.InletName, out partnerPipe!))
            {
                ent.Comp.PartnerPipe = null;
                return false;
            }
            ent.Comp.PartnerPipe = partnerPipe;
        }
        else
        {
            partnerPipe = ent.Comp.PartnerPipe;
        }

        return true;
    }

    private bool TryFindPartner(Entity<GasTransferComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return false;

        if (string.IsNullOrEmpty(ent.Comp.LinkId))
            return false;

        var transferQuery = EntityQueryEnumerator<GasTransferComponent>();
        while (transferQuery.MoveNext(out var otherUid, out var otherComp))
        {
            if (otherUid == ent.Owner)
                continue;

            if (string.IsNullOrEmpty(otherComp.LinkId))
                continue;

            if (otherComp.LinkId == ent.Comp.LinkId)
            {
                ent.Comp.Partner = (otherUid, otherComp);
                otherComp.Partner = (ent.Owner, ent.Comp);
                return true;
            }
        }
        return false;
    }

    private void TransferGas(Entity<GasTransferComponent> ent, PipeNode partnerPipe, float dt)
    {
        if (!_nodeContainer.TryGetNode<PipeNode>(ent.Owner, ent.Comp.InletName, out var ourPipe))
            return;

        var maxTransfer = ent.Comp.TransferRate * dt;

        if (float.IsNaN(maxTransfer) || float.IsInfinity(maxTransfer))
            return;

        if (maxTransfer < 0)
            return;

        switch (ent.Comp.Mode)
        {
            case GasTransferMode.Balance:
                BalanceGas(ourPipe, partnerPipe, maxTransfer);
                break;

            case GasTransferMode.Send:
                SendGas(ourPipe, partnerPipe, maxTransfer);
                break;

            case GasTransferMode.Receive:
                ReceiveGas(ourPipe, partnerPipe, maxTransfer);
                break;
        }
    }

    private void BalanceGas(PipeNode inlet, PipeNode outlet, float maxTransfer)
    {
        var totalMolesA = inlet.Air.TotalMoles;
        var totalMolesB = outlet.Air.TotalMoles;

        var diff = totalMolesA - totalMolesB;
        var absDiff = Math.Abs(diff);
        if (absDiff < BalanceThreshold)
            return;

        var transferAmount = Math.Min(absDiff / 2f, maxTransfer);

        var fromNode = diff > 0 ? inlet : outlet;
        var toNode = diff > 0 ? outlet : inlet;

        TransferGasMixture(fromNode, toNode, transferAmount);
    }

    private void SendGas(PipeNode inlet, PipeNode outlet, float maxTransfer)
    {
        TransferDirectionalGas(inlet, outlet, maxTransfer);
    }

    private void ReceiveGas(PipeNode inlet, PipeNode outlet, float maxTransfer)
    {
        TransferDirectionalGas(outlet, inlet, maxTransfer);
    }

    private void TransferDirectionalGas(PipeNode source, PipeNode destination, float maxTransfer)
    {
        if (source.Air.TotalMoles <= 0)
            return;

        var transferAmount = CalculateTransferAmount(source.Air.TotalMoles, maxTransfer);
        TransferGasMixture(source, destination, transferAmount);
    }

    private float CalculateTransferAmount(float availableMoles, float maxTransfer)
    {
        return Math.Min(maxTransfer, availableMoles);
    }

    private void TransferGasMixture(PipeNode fromNode, PipeNode toNode, float moles)
    {
        if (fromNode.Air.TotalMoles <= 0)
            return;

        var removed = fromNode.Air.Remove(moles);

        _atmosphereSystem.Merge(toNode.Air, removed);
    }
}
