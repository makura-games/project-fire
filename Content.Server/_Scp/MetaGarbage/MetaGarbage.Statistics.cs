using System.Text;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Server._Scp.MetaGarbage;

public sealed partial class MetaGarbageSystem
{
    [Dependency] private IPrototypeManager _prototype = default!;

    public string GetStatistics()
    {
        StringBuilder result = new();

        if (CachedGarbage.Count == 0)
        {
            result.Append(Loc.GetString("meta-garbage-no-any-garbage"));
            result.Append("\n");

            return result.ToString();
        }

        foreach (var (stationProto, dataList) in CachedGarbage)
        {
            result.Append(GetStationStatistics(stationProto, dataList));
        }

        return result.ToString();
    }

    private string GetStationStatistics(EntProtoId stationProto, List<StationMetaGarbageData> dataList)
    {
        StringBuilder result = new();
        result.Append(Loc.GetString("meta-garbage-statistics"));
        result.AppendLine();

        result.Append("[bold]");
        result.Append(stationProto);
        result.Append("[/bold]: ");
        result.AppendLine();

        result.AppendJoin("\n", GetItemCountStatistics(dataList), GetLiquidCountStatistics(dataList));

        return result.ToString();
    }

    /// <summary>
    /// Собирает красивую строку о количестве каждого предмета, который сохранен.
    /// </summary>
    private string GetItemCountStatistics(List<StationMetaGarbageData> dataList)
    {
        StringBuilder result = new();

        var prototypeCount = new Dictionary<EntProtoId, int>();
        foreach (var data in dataList)
        {
            if (prototypeCount.TryGetValue(data.Prototype, out var count))
                prototypeCount[data.Prototype] = count + 1;
            else
                prototypeCount[data.Prototype] = 1;
        }

        if (prototypeCount.Count == 0)
        {
            result.Append(Loc.GetString("meta-garbage-no-item-garbage"));
            result.AppendLine();

            return result.ToString();
        }

        result.Append(Loc.GetString("meta-garbage-found-items"));
        result.AppendLine();

        foreach (var (item, count) in prototypeCount)
        {
            var itemName = _prototype.Index(item).Name;

            result.Append(" - [bold]");
            result.Append(itemName);
            result.Append("[/bold]: ");
            result.Append(count);
            result.AppendLine();
        }

        return result.ToString();
    }

    /// <summary>
    /// Собирает красивую строку об объеме каждого реагента поименно.
    /// </summary>
    private string GetLiquidCountStatistics(List<StationMetaGarbageData> dataList)
    {
        StringBuilder result = new();

        var reagentVolume = new Dictionary<ProtoId<ReagentPrototype>, int>();

        // Собираем данные в удобный для вывода словарь, где хранится только нужная информация.
        foreach (var data in dataList)
        {
            if (data.LiquidData == null)
                continue;

            foreach (var solution in data.LiquidData.Values)
            {
                foreach (var reagentQuantity in solution.Contents)
                {
                    if (reagentVolume.TryGetValue(reagentQuantity.Reagent.Prototype, out var volume))
                        reagentVolume[reagentQuantity.Reagent.Prototype] = volume + reagentQuantity.Quantity.Int();
                    else
                        reagentVolume[reagentQuantity.Reagent.Prototype] = reagentQuantity.Quantity.Int();
                }
            }
        }

        if (reagentVolume.Count == 0)
        {
            result.Append(Loc.GetString("meta-garbage-no-liquid-garbage"));
            result.AppendLine();

            return result.ToString();
        }

        result.Append(Loc.GetString("meta-garbage-found-liquid"));
        result.AppendLine();

        // Добавляем в результат строку о каждом реагенте и его объеме в красивом формате
        foreach (var (id, volume) in reagentVolume)
        {
            var name = _prototype.Index(id).LocalizedName;

            result.Append(" - [bold]");
            result.Append(name);
            result.Append("[/bold]: ");
            result.Append(volume);
            result.AppendLine();
        }

        return result.ToString();
    }
}
