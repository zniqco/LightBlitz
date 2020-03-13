using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace LightBlitz
{
    public class Maps
    {
        public class Map
        {
            [JsonProperty("id")]
            public int ID { get; set; }

            [JsonProperty("mapStringId")]
            public string MapStringID { get; set; }
        }

        private Dictionary<int, Map> maps = new Dictionary<int, Map>();

        public async Task Load(HttpClient httpClient)
        {
            maps.Clear();

            try
            {
                var response = await httpClient.GetAsync("lol-maps/v2/maps");

                if (!response.IsSuccessStatusCode)
                    return;

                var result = await response.Content.ReadAsStringAsync();

                foreach (var map in JsonConvert.DeserializeObject<Map[]>(result))
                    maps[map.ID] = map;
            }
            catch (OperationCanceledException)
            {
            }
            catch (HttpRequestException)
            {
            }
            catch (JsonSerializationException)
            {
            }
        }

        public string GetMapStringID(int id)
        {
            if (maps.TryGetValue(id, out var value))
                return value.MapStringID ?? string.Empty;

            return string.Empty;
        }
    }
}
