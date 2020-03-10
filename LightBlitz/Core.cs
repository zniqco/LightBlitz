using SimpleJSON;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LightBlitz
{
    class Core
    {
        private struct SummonerData
        {
            public int ID;
            public int Level;
        }

        private struct QueueData
        {
            public int ID;
            public int MapID;
        }

        private const string runePagePrefix = "LightBlitz: ";
        private const int mapSummonersRift = 11;
        private const int mapHowlingAbyss = 12;

        private Thread thread;
        private HttpClient httpClient;
        private HttpClient leagueHttpClient;
        private string leagueApiBaseUrl;
        private bool isBusy;
        private object isBusyLockObject = new object();

        public bool IsBusy
        {
            get
            {
                lock (isBusyLockObject)
                    return isBusy;
            }
            set
            {
                lock (isBusyLockObject)
                    isBusy = value;
            }
        }

        public Core()
        {
            leagueHttpClient = new HttpClient();
            leagueHttpClient.Timeout = TimeSpan.FromSeconds(5.0);

            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(20.0);

            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = ServerCertificateValidationCallback;
        }

        public void Start()
        {
            if (thread == null)
            {
                thread = new Thread(new ThreadStart(new Action(MainLoop)));
                thread.Start();
            }
        }

        public void Stop()
        {
            if (thread != null)
            {
                thread.Abort();
                thread = null;
            }
        }

        private async void MainLoop()
        {
            while (true)
            {
                if (!GetLeagueClientInformation(out var process))
                {
                    await Task.Delay(5000);
                    continue;
                }

                while (true)
                {
                    if (process.HasExited)
                        break;

                    var latestChampionId = 0;
                    var shouldFetchBaseVariables = true;
                    var currentVersion = string.Empty;
                    var queueData = default(QueueData);
                    var summonerData = default(SummonerData);

                    while (await GetGameflowPhase() == "ChampSelect")
                    {
                        IsBusy = true;

                        if (shouldFetchBaseVariables)
                        {
                            currentVersion = await GetCurrentVersion();
                            queueData = await GetQueueData();
                            summonerData = await GetSummonerData();

                            Log("queueData.ID={0}, queueData.MapID={1}, gameVersion={2}", queueData.ID, queueData.MapID, currentVersion);

                            if (queueData.ID == -1) // Custom?
                            {
                                if (queueData.MapID == mapSummonersRift)
                                    queueData.ID = 420;
                                else if (queueData.MapID == mapHowlingAbyss)
                                    queueData.ID = 450;
                            }

                            shouldFetchBaseVariables = false;
                        }

                        if ((queueData.MapID == mapSummonersRift && Settings.Current.MapSummonersLift) || (queueData.MapID == mapHowlingAbyss && Settings.Current.MapHowlingAbyss))
                        {
                            var championId = await GetSelectedChampionId(summonerData.ID);

                            if (latestChampionId != championId)
                            {
                                var recommendedRole = await GetRecommendedRole(championId);
                                var recommendedData = await GetRecommendedData(queueData, championId, currentVersion);
                                var recommendedDataWithRole = recommendedData.Linq.Select(x => x.Value).FirstOrDefault(x => x["role"] == recommendedRole);

                                if (recommendedDataWithRole == null)
                                    recommendedDataWithRole = recommendedData[0];

                                Log("championId={0}, recommendedRole={1}", championId, recommendedRole);

                                if (Settings.Current.ApplySpells)
                                    await SetSpells(summonerData, recommendedDataWithRole);

                                if (Settings.Current.ApplyRunes && summonerData.Level >= 10)
                                    await SetRunes(championId, recommendedDataWithRole);

                                latestChampionId = championId;
                            }

                            await Task.Delay(500);
                        }
                        else
                        {
                            await Task.Delay(2000);
                        }
                    }

                    IsBusy = false;

                    await Task.Delay(2000);
                }                
            }
        }

        private async Task<string> GetGameflowPhase()
        {
            var result = await LeagueRequestRaw(HttpMethod.Get, "lol-gameflow/v1/gameflow-phase");

            if (result != null)
                return result.Trim('\"');

            return string.Empty;
        }

        private async Task<SummonerData> GetSummonerData()
        {
            var result = await LeagueRequest(HttpMethod.Get, "lol-summoner/v1/current-summoner");

            if (result != null)
            {
                return new SummonerData()
                {
                    ID = result["summonerId"].AsInt,
                    Level = result["summonerLevel"].AsInt,
                };
            }

            return new SummonerData();
        }

        private async Task<int> GetSelectedChampionId(int summonerId)
        {
            var result = await LeagueRequest(HttpMethod.Get, "lol-champ-select/v1/session");

            if (result == null)
                return 0;

            var cellId = result["localPlayerCellId"].Value;
            var action = result["actions"][0].Linq.Select(x => x.Value).FirstOrDefault(x => x["actorCellId"].Value == cellId);

            if (action == null)
                return 0;

            if (!action["completed"].AsBool || action["type"].Value != "pick")
                return 0;

            return action["championId"].AsInt;
        }

        private async Task<QueueData> GetQueueData()
        {
            var result = await LeagueRequest(HttpMethod.Get, "lol-gameflow/v1/session");

            if (result != null)
            {
                var queue = result["gameData"]["queue"];

                return new QueueData()
                {
                    ID = queue["id"].AsInt,
                    MapID = queue["mapId"].AsInt,
                };
            }

            return new QueueData();
        }

        private async Task<string> GetCurrentVersion()
        {
            var result = await BlitzRequest("patches/current");

            if (result != null)
                return result["data"]["patch"];

            return string.Empty;
        }

        private async Task<string> GetRecommendedRole(int championId)
        {
            var result = await BlitzRequest(string.Format("champions/{0}/role", championId));

            if (result != null)
                return result["role"];

            return string.Empty;
        }

        private async Task<JSONArray> GetRecommendedData(QueueData queueData, int championId, string version)
        {
            var url = string.Format("champions/{0}?patch={2}&queue={1}&region=world", championId, queueData.ID, version);
            var result = await BlitzRequest(url);

            if (result != null)
                return result["data"].AsArray;

            return new JSONArray();
        }

        private async Task<bool> SetSpells(SummonerData summonerData, JSONNode recommendedData)
        {
            var result = await LeagueRequest(HttpMethod.Get, "lol-champ-select/v1/session");

            if (result == null)
                return false;

            var summoner = result["myTeam"].Linq.Select(x => x.Value).FirstOrDefault(x => x["summonerId"].AsInt == summonerData.ID);

            if (summoner == null)
                return false;

            var spell1 = recommendedData["stats"]["spells"]["build"][0].AsInt;
            var spell2 = recommendedData["stats"]["spells"]["build"][1].AsInt;
            var patchData = new JSONObject();

            patchData["selectedSkinId"] = summoner["selectedSkinId"].AsInt;
            patchData["spell1Id"] = summoner["spell1Id"].AsInt;
            patchData["spell2Id"] = summoner["spell2Id"].AsInt;
            patchData["wardSkinId"] = summoner["wardSkinId"].AsInt;

            // TODO: Get spell datas and check level
            // var spellDatas = await LeagueRequest(HttpMethod.Get, "lol-game-data/assets/v1/summoner-spells.json");

            // Set spells
            if (Settings.Current.BlinkToRight && spell1 == 4)
            {
                spell1 = spell2;
                spell2 = 4;
            }

            patchData["spell1Id"] = spell1;
            patchData["spell2Id"] = spell2;

            result = await LeagueRequestRaw(new HttpMethod("PATCH"), "lol-champ-select/v1/session/my-selection", new StringContent(patchData.ToString(), Encoding.UTF8, "application/json"));

            if (result == null)
                return false;

            return true;
        }

        private async Task<bool> SetRunes(int championId, JSONNode recommendedData)
        {
            // Get champion name
            var result = await LeagueRequest(HttpMethod.Get, "lol-champ-select/v1/grid-champions/" + championId.ToString());

            if (result == null)
                return false;

            var championName = result["name"].Value;

            // Get pages
            result = await LeagueRequest(HttpMethod.Get, "lol-perks/v1/pages");

            if (result == null)
                return false;

            // Set to any default pages
            var pages = result.Linq.Select(x => x.Value).ToArray();
            var anyDefaultPage = pages.FirstOrDefault(x => !x["isEditable"].AsBool);

            await LeagueRequestRaw(HttpMethod.Put, "lol-perks/v1/currentpage", new StringContent(anyDefaultPage["id"].Value, Encoding.UTF8));

            // Delete exist LightBlitz pages
            foreach (var page in pages.Where(x => x["isEditable"].AsBool && x["name"].Value.StartsWith(runePagePrefix)))
                result = await LeagueRequestRaw(HttpMethod.Delete, "lol-perks/v1/pages/" + page["id"].Value);

            // Create page
            var postData = new JSONObject();
            var recommendedRunes = recommendedData["stats"]["runes"]["build"];
            var recommendedRuneShards = recommendedData["stats"]["rune_stat_shards"]["build"];
            var recommendedRole = recommendedData["role"].Value;

            postData["name"] = runePagePrefix + championName + (recommendedRole.Length > 0 ? string.Format(" ({0})", recommendedRole) : string.Empty);
            postData["primaryStyleId"] = recommendedRunes[0];
            postData["selectedPerkIds"] = new JSONArray();
            postData["selectedPerkIds"].Add(recommendedRunes[1]);
            postData["selectedPerkIds"].Add(recommendedRunes[2]);
            postData["selectedPerkIds"].Add(recommendedRunes[3]);
            postData["selectedPerkIds"].Add(recommendedRunes[4]);
            postData["selectedPerkIds"].Add(recommendedRunes[6]);
            postData["selectedPerkIds"].Add(recommendedRunes[7]);
            postData["selectedPerkIds"].Add(recommendedRuneShards[0]);
            postData["selectedPerkIds"].Add(recommendedRuneShards[1]);
            postData["selectedPerkIds"].Add(recommendedRuneShards[2]);
            postData["subStyleId"] = recommendedRunes[5];

            result = await LeagueRequest(HttpMethod.Post, "lol-perks/v1/pages", new StringContent(postData.ToString(), Encoding.UTF8, "application/json"));

            if (result == null)
                return false;

            // Set page
            result = await LeagueRequestRaw(HttpMethod.Put, "lol-perks/v1/currentpage", new StringContent(result["id"].Value, Encoding.UTF8));

            if (result == null)
                return false;

            return true;
        }

        private async Task<JSONNode> LeagueRequest(HttpMethod method, string url, HttpContent content = null)
        {
            var result = await LeagueRequestRaw(method, url, content);

            if (result != null)
                return JSON.Parse(result);
            else
                return null;
        }

        private async Task<string> LeagueRequestRaw(HttpMethod method, string url, HttpContent content = null)
        {
            try
            {
                var requestMessage = new HttpRequestMessage(method, leagueApiBaseUrl + url);

                if (content != null)
                    requestMessage.Content = content;

                var response = await leagueHttpClient.SendAsync(requestMessage);

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync();
                else
                    return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        private async Task<JSONNode> BlitzRequest(string url)
        {
            try
            {
                var response = await httpClient.GetAsync("https://beta.iesdev.com/api/lolstats/" + url);

                if (response.IsSuccessStatusCode)
                    return JSON.Parse(await response.Content.ReadAsStringAsync());
                else
                    return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        private bool GetLeagueClientInformation(out Process process)
        {
            // Get connect information
            var processes = Process.GetProcessesByName("LeagueClient");

            process = null;

            foreach (var p in processes)
            {
                string directory = Path.GetDirectoryName(p.MainModule.FileName);
                string lockfilePath = Path.Combine(directory, "lockfile");

                if (File.Exists(lockfilePath))
                {
                    using (var stream = new FileStream(lockfilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        var splitted = reader.ReadToEnd().Split(':');

                        if (splitted.Length != 5)
                            continue;

                        Log("port={0}, password={1}", splitted[2], splitted[3]);

                        leagueHttpClient.DefaultRequestHeaders.Remove("Authorization");
                        leagueHttpClient.DefaultRequestHeaders.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Format("riot:{0}", splitted[3]))));

                        process = p;
                        leagueApiBaseUrl = string.Format("https://127.0.0.1:{0}/", splitted[2]);

                        return true;
                    }
                }
            }

            return false;
        }

        private void Log(string message)
        {
            Debug.WriteLine("[{0}] {1}", DateTime.Now, message);
        }

        private void Log(object value)
        {
            Debug.WriteLine("[{0}] {1}", DateTime.Now, value);
        }

        private void Log(string format, params object[] args)
        {
            Debug.WriteLine(string.Format("[{0}] {1}", DateTime.Now, format), args);
        }

        private bool ServerCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }
    }
}
