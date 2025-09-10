using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace xgather.Tasks.Gather;

public class ManyItem : GatherBase
{
    public readonly Dictionary<uint, uint> Items = [];

    public ManyItem(Dictionary<uint, uint> items, bool preFilterItems = true)
    {
        if (preFilterItems)
        {
            Items = items.Where(k => k.Value > 0 && Svc.ItemDB.CanGather(k.Key)).ToDictionary();
            Svc.Log.Debug(JsonConvert.SerializeObject(Items));
        }
        else
            Items = items;
    }

    protected override async Task Execute()
    {
        foreach (var (itemId, quantity) in Items)
            await RunSubtask(new OneItem(itemId, quantity));
    }
}
