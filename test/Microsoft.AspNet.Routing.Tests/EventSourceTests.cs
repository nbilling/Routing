using System.Collections.Generic;
using Xunit;
using Microsoft.AspNet.Routing.Logging;
using Newtonsoft.Json.Linq;
using System.Linq;
using System;
using System.Globalization;

namespace Microsoft.AspNet.Routing.Tests
{
    public class EventSourceTests
    {
        public static IEnumerable<object> inputDictionaries = new object[][]
            {
                // Empty
                new object[]
                {
                    new Dictionary<string, object> { },
                },
                // Single element
                new object[]
                {
                    new Dictionary<string, object> { { "foo", "bar" } }
                },
                // Multiple elements
                new object[]
                {
                    new Dictionary<string, object>
                    {
                        { "action", "ActionFoo" },
                        { "controller", "ControllerBar" },
                        { "extra-thing", "BonusData" }
                    }
                },
                // Test escaping
                new object []
                {
                    new Dictionary<string, object>
                    {
                        { "\"", "\"" },
                        { "\\", "\\" },
                        { "adsf\naldkjflak\r\nlkajdf", "\r\n" },
                        { "\n", "\t\t\n" },
                        { "\\\n\\\"\\\\", "\t\r\n\\\r\\\"" },
                        { "\b\t\f\n\r\n\\\"\\\"\r\n\"\\", "\"\\\r\n\t\f\b    \r\n\\\"" },
                        { "\0\0\0\0\0", "\n\0\n\r\0\r\n\0\0" },
                        { "\0asdjkhfjka\0jklhadkjlhf\0lnhadkl;fj\0", "akljhdf\0lkjahdf\0kjadf" },
                        { "asdf\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b", "asdf\0\b\b\\b\b\b\\\0\b\b\b\b\b\b\b\b" },
                        { "{\"afield\":\"avalue\"}", "{\"anotherfield\":\"anothervalue\"}" },
                        { "}", "}" }
                    }
                },
            };

        [Theory]
        [MemberData("inputDictionaries")]
        public void JsonSerialization(Dictionary<string, object> input)
        {
            // Arrange & Act
            var actual = AspNetRoutingEventSource.GetJsonArgumentsFromDictionary(input);

            // Assert
            var deserializedActual = Newtonsoft.Json.JsonConvert.DeserializeObject(actual) as JObject;
            Assert.NotNull(deserializedActual);
            VerifyDictionariesMatch(input, deserializedActual);
        }

        private void VerifyDictionariesMatch(IDictionary<string, object> dict1, IDictionary<string, JToken> dict2)
        {
            // Verify that either both are null, or neither are null + their lengths + elements match.
            if (dict1 != null || dict2 != null)
            {
                Assert.True(dict1 != null && dict2 != null);
                Assert.Equal(dict1.Count, dict2.Count);

                for (int i = 0; i < dict1.Count; i++)
                {
                    Assert.True(CompareKeyValuePairs(dict1.ElementAt(i), dict2.ElementAt(i)));
                }
            }
        }

        private bool CompareKeyValuePairs(KeyValuePair<string, object> kvp1, KeyValuePair<string, JToken> kvp2)
        {
            // Our converter formats formattable input values, so match that.
            return (kvp1.Key == kvp2.Key) &&
                (FormatObject(kvp1.Value) == kvp2.Value.ToString());
            // Note: for some reason using Formatting.None will cause the value to get wrapped in quotes,
            // so we just use the default JToken.ToString here.
        }

        private string FormatObject(object obj)
        {
            var formattable = obj as IFormattable;
            return (formattable == null) ? obj.ToString() : formattable.ToString("G", CultureInfo.InvariantCulture);
        }
    }
}
