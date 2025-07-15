using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RFI_Manager.Util
{
    public class RibbonTab
    {
        public string Name { get; set; }
        public RibbonPanel[] Panels { get; set; }
    }

    public class RibbonPanel
    {
        public string Name { get; set; }
        public RibbonButton[] Buttons { get; set; }
    }

    public class RibbonButton
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string ClassName { get; set; }
        public bool IsNeedLogin { get; set; } = true;
        public IntPtr ImagePtr { get; set; }
    }
}
