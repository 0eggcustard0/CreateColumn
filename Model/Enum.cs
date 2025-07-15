using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace JoinGeometryUtils.Model
{
    public class Enum : DependencyObject
    {
        public Dictionary<string, EnumItem> SampleEnum { get; set; }
    }

    public class EnumItem
    {
        public string Word { get; set; }
        public int Value { get; set; }
        public int BelongTo { get; set; }

        //public static string ToWord(EnumItem[] enumSet, int value)
        //{
        //    return enumSet.First(e => e.Value == value).Word;
        //}

        //public static int ToValue(EnumItem[] enumSet, string word)
        //{
        //    return enumSet.First(e => e.Word == word).Value;
        //}

        public static int? ToValue(Dictionary<string, EnumItem> enumSet, string word)
        {
            try
            {
                return enumSet.First(e => e.Value.Word == word).Value.Value;

            }
            catch (Exception e)
            {
                return null;
            }
        }

        public static string ToWord(Dictionary<string, EnumItem> enumSet, int value)
        {
            try
            {
                return enumSet.First(e => e.Value.Value == value).Value.Word;

            }
            catch (Exception e)
            {
                return null;
            }
        }
    }
}
