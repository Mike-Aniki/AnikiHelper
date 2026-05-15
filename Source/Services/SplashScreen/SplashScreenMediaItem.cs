using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AnikiHelper.Services.SplashScreen
{
    internal class SplashScreenMediaItem
    {
        public string FilePath { get; set; }
        public SplashScreenScope Scope { get; set; }
        public bool IsVideo { get; set; }
    }
}
