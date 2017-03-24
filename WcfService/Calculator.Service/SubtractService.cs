using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Calculator.Service
{
    using System.Fabric;
    using Calculator.Common;
    class SubtarctService : ISubtract
    {
        private readonly ServiceContext context;

        public SubtarctService(ServiceContext context)
        {
            this.context = context;
        }

        public Task<double> Subtract(double n1, double n2)
        {
            var result = n1 - n2;
            ServiceEventSource.Current.ServiceMessage(this.context, "Received Add({0},{1})", n1, n2);
            ServiceEventSource.Current.ServiceMessage(this.context, "Return: {0}", result);
            return Task.FromResult(result);
        }
    }
}
