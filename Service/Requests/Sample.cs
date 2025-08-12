using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CreateColumn.Service.Request;

namespace CreateColumn.Service.Requests
{
    public class PoseSample : BaseRequest
    {
        public override string Route { get => "/sample"; }
        public override Method Method => Method.POST;

        [Body]
        public string Name { get; set; }

    }
}
