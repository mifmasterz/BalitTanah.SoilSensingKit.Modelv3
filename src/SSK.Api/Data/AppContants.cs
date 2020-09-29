using SSK.Api.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SSK.Api.Data
{
    public class AppContants
    {
        public static string ModelPath { get; set; } = StorageInfo.GetAbsolutePath("ModelML");
    }
}
