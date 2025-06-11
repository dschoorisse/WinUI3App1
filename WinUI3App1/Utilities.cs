using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinUI3App1
{
    internal class Utilities
    {
    }

    public class IntToPercentConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int intValue) { return $"{intValue}%"; }
            if (value is double doubleValue) { return $"{Math.Round(doubleValue)}%"; } // Handle double if sliders use it
            return value;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is string stringValue && stringValue.EndsWith("%"))
            {
                if (int.TryParse(stringValue.TrimEnd('%'), out int result)) { return result; }
                if (double.TryParse(stringValue.TrimEnd('%'), out double doubleResult)) { return doubleResult; }
            }
            return value;
        }
    }

}
