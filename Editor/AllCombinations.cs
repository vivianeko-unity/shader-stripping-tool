using System.Collections.Generic;
using System.Linq;

namespace ShaderStrippingTool
{
    public static class AllCombinations
    {
        public static List<List<T>> GetAllCombinations<T>(this List<T> list)
        {
            List<List<T>> result = new() { new List<T>() };
            result.Last().Add(list[0]);
            if (list.Count == 1)
                return result;
            var tailCombos = GetAllCombinations(list.Skip(1).ToList());
            foreach (var combo in tailCombos)
            {
                result.Add(new List<T>(combo));
                combo.Add(list[0]);
                result.Add(new List<T>(combo));
            }

            return result;
        }
    }
}
