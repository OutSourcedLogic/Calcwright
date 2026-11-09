// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using CalculatorApp.ViewModel.DataLoaders;

namespace Calculator.Tests
{
    [TestClass]
    public static class TestAssemblySetup
    {
        [AssemblyInitialize]
        public static void Initialize(TestContext context)
        {
            // Currency tests run offline against fixed sample data, not the live exchange-rate service.
            CurrencyHttpClient.UseSampleData = true;
        }
    }
}
