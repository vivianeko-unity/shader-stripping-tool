using System.Collections.Generic;
using System.Linq;

namespace ShaderStrippingTool
{
    public static class AllCombinations
    {
        public static List<List<T>> GetAllCombinations<T>(this List<T> list)
        {
            var result = new List<List<T>>();
            for (var i = 1; i < (1 << list.Count) -1; ++i)
            {
                var combination = list.Where((_, j) => (i & (1 << j)) == 0).ToList();
                result.Add(combination);
            }
            
            return result;
        }
    }
}
