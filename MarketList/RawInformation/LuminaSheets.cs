using System.Collections.Generic;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Linq;

namespace MarketList.RawInformation
{
    public class LuminaSheets
    {
        public static Dictionary<uint, Item>? ItemSheet;
        public static ILookup<string, Item>? ItemLookup;
        
        public static void Init()
        {
            ItemSheet = Svc.Data?.GetExcelSheet<Item>()?
                        .ToDictionary(i => i.RowId, i => i);
            
            ItemLookup = ItemSheet?.Values.ToLookup(i => i.Name.ToString(), i => i);
            
            Svc.Log.Debug("Lumina sheets initialized");
        }

        public static void Dispose()
        {
            var type = typeof(LuminaSheets);
            foreach (var prop in type.GetFields(System.Reflection.BindingFlags.Static))
            {
                prop.SetValue(null, null);
            }
        }
    }
}
