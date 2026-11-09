// Licensed under the MIT License.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CalculatorApp.ViewModel.DataLoaders;
using Windows.Data.Json;

namespace Calculator.Tests
{
    [TestClass]
    public class CurrencyRatesTests
    {
        [TestMethod]
        public void ConvertRatesResponse_MapsRatesToLoaderFormat()
        {
            const string body = @"{ ""result"": ""success"", ""base_code"": ""USD"",
                ""rates"": { ""USD"": 1, ""EUR"": 0.89048, ""JPY"": 151.2, ""BAD"": ""x"", ""ZERO"": 0 } }";

            string json = CurrencyHttpClient.ConvertRatesResponse(body);
            JsonArray array = JsonArray.Parse(json);

            var map = array.Select(v => v.GetObject())
                           .ToDictionary(o => o.GetNamedString("An"), o => o.GetNamedNumber("Rt"));
            Assert.AreEqual(3, map.Count, "non-numeric and zero rates are dropped");
            Assert.AreEqual(1.0, map["USD"]);
            Assert.AreEqual(0.89048, map["EUR"], 1e-12);
            Assert.AreEqual(151.2, map["JPY"], 1e-12);
        }

        [TestMethod]
        public void ConvertRatesResponse_RejectsErrorsAndGarbage()
        {
            Assert.IsNull(CurrencyHttpClient.ConvertRatesResponse(@"{ ""result"": ""error"", ""error-type"": ""rate-limited"" }"));
            Assert.IsNull(CurrencyHttpClient.ConvertRatesResponse("not json"));
            Assert.IsNull(CurrencyHttpClient.ConvertRatesResponse(null));
            Assert.IsNull(CurrencyHttpClient.ConvertRatesResponse(@"{ ""result"": ""success"", ""rates"": { } }"));
        }

        [TestMethod]
        public void CurrencyNames_CoverCommonCurrenciesOnce()
        {
            var codes = CurrencyNames.All.Select(r => r[0]).ToList();
            CollectionAssert.AllItemsAreUnique(codes);
            foreach (var code in new[] { "USD", "EUR", "GBP", "CAD", "MXN", "JPY", "CNY", "INR", "AUD", "CHF" })
            {
                CollectionAssert.Contains(codes, code);
            }
            Assert.IsTrue(CurrencyNames.All.All(r => r.Length == 4 && r.All(s => !string.IsNullOrEmpty(s))));
        }
    }
}
