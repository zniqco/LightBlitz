using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace LightBlitz
{
    public class Champions
    {
        public class Champion
        {
            [JsonProperty("id")]
            public int ID { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("alias")]
            public string Alias { get; set; }
        }

        private Dictionary<int, Champion> champions = new Dictionary<int, Champion>();

        public async Task Load(HttpClient httpClient)
        {
            champions.Clear();

            try
            {
                var response = await httpClient.GetAsync("lol-game-data/assets/v1/champion-summary.json");

                if (!response.IsSuccessStatusCode)
                    return;

                var result = await response.Content.ReadAsStringAsync();

                foreach (var champion in JsonConvert.DeserializeObject<Champion[]>(result))
                    champions[champion.ID] = champion;
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

        public string GetName(int id)
        {
            if (champions.TryGetValue(id, out var value))
                return value.Name ?? string.Empty;

            return string.Empty;
        }

        public string GetAlias(int id)
        {
            if (champions.TryGetValue(id, out var value))
                return value.Alias ?? string.Empty;

            return string.Empty;
        }
    }
}
