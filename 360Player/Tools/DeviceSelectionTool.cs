using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace Bivrost.Bivrost360Player.Tools
{
    public class DeviceSelectionTool
    {

        public static void EnumerateGraphicCards()
        {
            using (Factory1 dxgiFactory = new Factory1())
            {
                string adapters = "";
                foreach (var adapter in dxgiFactory.Adapters)
                {
                    using (adapter)
                    {
                        adapters += adapter.Description.Description + "\n";
                    }
                }
            }
        }

    }
}
