using Newtonsoft.Json;
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
        public class LeagueGameflowSession
        {
            public class GameData
            {
                public class Queue
                {
                    public int id { get; set; }
                    public int mapId { get; set; }
                }

                public Queue queue { get; set; }
            }

            public GameData gameData { get; set; }
            public string phase { get; set; }
        }

        public class LeagueSummonerCurrentSummoner
        {
            public int summonerId { get; set; }
            public int summonerLevel { get; set; }
        }

        public class LeagueChampSelectSession
        {
            public class Action
            {
                public int actorCellId { get; set; }
                public int championId { get; set; }
                public bool completed { get; set; }
                public string type { get; set; }
            }

            public class MyTeam
            {
                public int summonerId { get; set; }
                public int selectedSkinId { get; set; }
                public int spell1Id { get; set; }
                public int spell2Id { get; set; }
                public int wardSkinId { get; set; }
            }

            public Action[][] actions { get; set; }
            public int localPlayerCellId { get; set; }
            public MyTeam[] myTeam { get; set; }
        }

        public class LeagueChampSelectGridChampions
        {
            public string name { get; set; }
        }

        public class LeagueChampSelectSessionMySelectionPatch
        {
            public int selectedSkinId { get; set; }
            public int spell1Id { get; set; }
            public int spell2Id { get; set; }
            public int wardSkinId { get; set; }
        }

        public class LeaguePerksPage
        {
            public int id { get; set; }
            public bool isEditable { get; set; }
            public string name { get; set; }
        }

        public class LeaguePerksPagePost
        {
            public string name { get; set; }
            public int primaryStyleId { get; set; }
            public int[] selectedPerkIds { get; set; }
            public int subStyleId { get; set; }
        }

        public class BlitzChampions
        {
            public class Data
            {
                public class Stats
                {
                    public class Runes
                    {
                        public int[] build { get; set; }
                    }

                    public class RuneStatShards
                    {
                        public int[] build { get; set; }
                    }

                    public class Spells
                    {
                        public int[] build { get; set; }
                    }

                    public Runes runes { get; set; }
                    public RuneStatShards rune_stat_shards { get; set; }
                    public Spells spells { get; set; }
                }

                public string role { get; set; }
                public Stats stats { get; set; }
            }

            public Data[] data { get; set; }
        }

        public class BlitzChampionsRole
        {
            public string role { get; set; }
        }

        public class BlitzPatchesCurrent
        {
            public class Data
            {
                public string patch { get; set; }
            }

            public Data data { get; set; }
        }

        private const string runePagePrefix = "LightBlitz: ";
        private const int mapSummonersRift = 11;
        private const int mapHowlingAbyss = 12;

        private readonly JsonSerializerSettings jsonSerializerSettings;

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

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = ServerCertificateValidationCallback;

            jsonSerializerSettings = new JsonSerializerSettings()
            {
                ContractResolver = new RequiredContractResolver()
            };
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
                    while (true)
                    {
                        var gameflowSession = await GetGameflowSession();

                        if (gameflowSession == null || gameflowSession.phase != "ChampSelect")
                            break;

                        IsBusy = true;

                        if ((gameflowSession.gameData.queue.mapId == mapSummonersRift && Settings.Current.MapSummonersLift) || (gameflowSession.gameData.queue.mapId == mapHowlingAbyss && Settings.Current.MapHowlingAbyss))
                        {
                            var championId = await GetSelectedChampionId();

                            if (latestChampionId != championId)
                            {
                                var currentVersion = await GetBlitzCurrentVersion();
                                var summonerData = await GetSummoner();
                                var recommendedRole = await GetBlitzRecommendedRole(championId);
                                var recommendedData = await GetBlitzRecommendedData(gameflowSession, championId, currentVersion);
                                var recommendedDataWithRole = recommendedData.data.FirstOrDefault(x => x.role == recommendedRole);

                                if (recommendedDataWithRole == null)
                                    recommendedDataWithRole = recommendedData.data[0];

                                Log("championId={0}, recommendedRole={1}", championId, recommendedRole);

                                if (Settings.Current.ApplySpells)
                                    await SetSpells(summonerData, recommendedDataWithRole);

                                if (Settings.Current.ApplyRunes && summonerData.summonerLevel >= 10)
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

        private async Task<LeagueGameflowSession> GetGameflowSession()
        {
            return await LeagueRequest<LeagueGameflowSession>(HttpMethod.Get, "lol-gameflow/v1/session");
        }

        private async Task<LeagueSummonerCurrentSummoner> GetSummoner()
        {
            return await LeagueRequest<LeagueSummonerCurrentSummoner>(HttpMethod.Get, "lol-summoner/v1/current-summoner");
        }

        private async Task<int> GetSelectedChampionId()
        {
            var result = await LeagueRequest<LeagueChampSelectSession>(HttpMethod.Get, "lol-champ-select/v1/session");

            if (result == null)
                return 0;

            var action = result.actions.SelectMany(x => x).FirstOrDefault(x => x.actorCellId == result.localPlayerCellId);

            if (action == null || !action.completed || action.type != "pick")
                return 0;

            return action.championId;
        }

        private async Task<string> GetChampionName(int championId)
        {
            var result = await LeagueRequest<LeagueChampSelectGridChampions>(HttpMethod.Get, "lol-champ-select/v1/grid-champions/" + championId.ToString());

            if (result == null)
                return string.Empty;

            return result.name;
        }

        private async Task<string> GetBlitzCurrentVersion()
        {
            var result = await BlitzRequest<BlitzPatchesCurrent>("patches/current");

            if (result == null)
                return string.Empty;

            return result.data.patch;
        }

        private async Task<string> GetBlitzRecommendedRole(int championId)
        {
            var result = await BlitzRequest<BlitzChampionsRole>(string.Format("champions/{0}/role", championId));

            if (result == null)
                return string.Empty;

            return result.role;
        }

        private async Task<BlitzChampions> GetBlitzRecommendedData(LeagueGameflowSession gameflowSession, int championId, string version)
        {
            var queueId = gameflowSession.gameData.queue.id;

            if (queueId == -1)
            {
                // Resolve queueId for custom game
                if (gameflowSession.gameData.queue.mapId == mapSummonersRift)
                    queueId = 420;
                else if (gameflowSession.gameData.queue.mapId == mapHowlingAbyss)
                    queueId = 450;
            }

            return await BlitzRequest<BlitzChampions>(string.Format("champions/{0}?patch={2}&queue={1}&region=world", championId, queueId, version));
        }

        private async Task<bool> SetSpells(LeagueSummonerCurrentSummoner summonerData, BlitzChampions.Data recommendedData)
        {
            var result = await LeagueRequest<LeagueChampSelectSession>(HttpMethod.Get, "lol-champ-select/v1/session");

            if (result == null)
                return false;

            var summoner = result.myTeam.FirstOrDefault(x => x.summonerId == summonerData.summonerId);

            if (summoner == null)
                return false;

            var data = new LeagueChampSelectSessionMySelectionPatch();

            data.selectedSkinId = summoner.selectedSkinId;
            data.wardSkinId = summoner.wardSkinId;

            if (Settings.Current.BlinkToRight && recommendedData.stats.spells.build[0] == 4)
            {
                data.spell1Id = recommendedData.stats.spells.build[1];
                data.spell2Id = recommendedData.stats.spells.build[0];
            }
            else
            {
                data.spell1Id = recommendedData.stats.spells.build[0];
                data.spell2Id = recommendedData.stats.spells.build[1];
            }

            var json = JsonConvert.SerializeObject(data);
            var selectResult = await LeagueRequestRaw(new HttpMethod("PATCH"), "lol-champ-select/v1/session/my-selection", new StringContent(json, Encoding.UTF8, "application/json"));

            if (selectResult == null)
                return false;

            return true;
        }

        private async Task<bool> SetRunes(int championId, BlitzChampions.Data recommendedData)
        {
            // Get champion name
            var championName = await GetChampionName(championId);

            // Get pages
            var pages = await LeagueRequest<LeaguePerksPage[]>(HttpMethod.Get, "lol-perks/v1/pages");

            if (pages == null)
                return false;

            // Set to any default pages
            var anyDefaultPage = pages.FirstOrDefault(x => !x.isEditable);

            if (anyDefaultPage == null)
                return false;

            await LeagueRequestRaw(HttpMethod.Put, "lol-perks/v1/currentpage", new StringContent(anyDefaultPage.id.ToString(), Encoding.UTF8));

            // Delete exist LightBlitz pages
            foreach (var page in pages.Where(x => x.isEditable && x.name.StartsWith(runePagePrefix)))
                await LeagueRequestRaw(HttpMethod.Delete, "lol-perks/v1/pages/" + page.id.ToString());

            // Create page
            if (recommendedData.stats.runes.build.Length < 8 || recommendedData.stats.rune_stat_shards.build.Length < 3)
                return false;

            var data = new LeaguePerksPagePost();

            data.name = runePagePrefix + championName + (recommendedData.role.Length > 0 ? string.Format(" ({0})", recommendedData.role) : string.Empty);
            data.primaryStyleId = recommendedData.stats.runes.build[0];

            data.selectedPerkIds = new int[]
            {
                recommendedData.stats.runes.build[1],
                recommendedData.stats.runes.build[2],
                recommendedData.stats.runes.build[3],
                recommendedData.stats.runes.build[4],
                recommendedData.stats.runes.build[6],
                recommendedData.stats.runes.build[7],
                recommendedData.stats.rune_stat_shards.build[0],
                recommendedData.stats.rune_stat_shards.build[1],
                recommendedData.stats.rune_stat_shards.build[2],
            };

            data.subStyleId = recommendedData.stats.runes.build[5];

            var json = JsonConvert.SerializeObject(data);
            var newPage = await LeagueRequest<LeaguePerksPage>(HttpMethod.Post, "lol-perks/v1/pages", new StringContent(json, Encoding.UTF8, "application/json"));

            if (newPage == null)
                return false;

            // Set page
            var setResult = await LeagueRequestRaw(HttpMethod.Put, "lol-perks/v1/currentpage", new StringContent(newPage.id.ToString(), Encoding.UTF8));

            if (setResult == null)
                return false;

            return true;
        }

        private async Task<T> LeagueRequest<T>(HttpMethod method, string url, HttpContent content = null)
        {
            var result = await LeagueRequestRaw(method, url, content);

            if (result != null)
            {
                try
                {
                    return JsonConvert.DeserializeObject<T>(result, jsonSerializerSettings);
                }
                catch (JsonSerializationException)
                {
                }
            }

            return default(T);
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

        private async Task<T> BlitzRequest<T>(string url)
        {
            try
            {
                var response = await httpClient.GetAsync("https://beta.iesdev.com/api/lolstats/" + url);

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        return JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync(), jsonSerializerSettings);
                    }
                    catch (JsonSerializationException)
                    {
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (HttpRequestException)
            {
            }

            return default(T);
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
