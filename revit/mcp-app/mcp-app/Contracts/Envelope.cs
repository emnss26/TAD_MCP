using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

namespace mcp_app.Contracts
{
    internal class MCPEnvelope
    {
        public string action { get; set; }
        public JObject args { get; set; }
    }

    internal class  MCPResponse
    {
        public bool ok { get; set; } = true;
        public string message { get; set; } = "ok";
        public object data { get; set; }
    }
}
