using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Calculator.Common
{
    using System.ServiceModel;

    [ServiceContract]
    public interface ISubtract
    {
        [OperationContract]
        Task<double> Subtract(double n1, double n2);
    }
}
