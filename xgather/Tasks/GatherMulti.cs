using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


namespace xgather.Tasks;

public class GatherMulti : GatherBase
{
    public readonly Dictionary<uint, uint> Items = [];

    public GatherMulti(Dictionary<uint, uint> items, bool preFilterItems = true)
    {
        if (preFilterItems)
        {
            Items = items.Where(k => k.Value > 0 && Svc.ItemDB.CanGather(k.Key)).ToDictionary();
            Svc.Log.Debug(JsonConvert.SerializeObject(Items));
        }
        else
        {
            Items = items;
        }
    }

    protected override async Task Execute()
    {
        foreach (var (itemId, quantity) in Items)
        {
            await RunSubtask(new GatherItem(itemId, quantity), s =>
            {
                Status = $"{s}\n{Util.ItemName(itemId)}";
            });
        }
    }
}
