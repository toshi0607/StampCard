using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.WindowsAzure.MobileServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace StampCard
{
    class CalendarDate
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }
        public enum Status
        {
            Reviewing,
            Approved,
            Rejected,
        }
        [JsonConverter(typeof(StringEnumConverter))]
        public Status Type { get; set; }

        [JsonProperty(PropertyName = "stampAt")]
        public DateTime StampAt { get; set; }
        [Version]
        public string Version { get; set; }
    }
}
