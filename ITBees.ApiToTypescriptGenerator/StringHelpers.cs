using System.Collections.Generic;
using System;
using System.Linq;

namespace TimesheetServices.HelperServices
{
    public static class StringHelpers
    {
        public static string ToLowerFirstChar(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return char.ToLower(input[0]) + input.Substring(1);
        }
    }
}