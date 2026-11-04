using System;
using System.Globalization;
using System.Management;

namespace DeviceDossier.Core
{
    public static class Fmt
    {
        public const string NA = "N/A";
        public const string NeedsAdmin = "N/A - requires administrator";

        public static string Text(object value)
        {
            if (value == null) return "";
            if (value is string) return (string)value;
            if (value is double) return ((double)value).ToString("0.##", CultureInfo.InvariantCulture);
            if (value is float) return ((float)value).ToString("0.##", CultureInfo.InvariantCulture);
            if (value is decimal) return ((decimal)value).ToString("0.##", CultureInfo.InvariantCulture);
            if (value is DateTime) return ((DateTime)value).ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture);
            if (value is bool) return (bool)value ? "True" : "False";
            if (value is IFormattable) return ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
            return value.ToString();
        }

        public static bool IsNumber(object value)
        {
            return value is int || value is long || value is uint || value is ulong || value is short || value is ushort
                || value is byte || value is double || value is float || value is decimal;
        }

        public static double ToDouble(object value)
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        public static string Str(object value)
        {
            if (value == null) return "";
            var arr = value as Array;
            if (arr != null)
            {
                var parts = new string[arr.Length];
                for (int i = 0; i < arr.Length; i++) parts[i] = Convert.ToString(arr.GetValue(i), CultureInfo.InvariantCulture);
                return string.Join(" / ", parts);
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }

        public static string OrNA(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? NA : value;
        }

        public static double Gb(double bytes)
        {
            return Math.Round(bytes / 1073741824.0, 2);
        }

        public static string CimDate(object value, string format)
        {
            var s = value as string;
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                return ManagementDateTimeConverter.ToDateTime(s).ToString(format, CultureInfo.InvariantCulture);
            }
            catch
            {
                return s;
            }
        }

        public static string Size(double gb)
        {
            if (gb >= 1000) return (gb / 1024).ToString("0.#", CultureInfo.InvariantCulture) + " TB";
            return gb.ToString("0.#", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
