using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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
                    [JsonRequired]
                    public int id { get; set; }

                    [JsonRequired]
                    public int mapId { get; set; }
                }

                [JsonRequired]
                public Queue queue { get; set; }
            }

            [JsonRequired]
            public GameData gameData { get; set; }

            [JsonRequired]
            public string phase { get; set; }
        }

        public class LeagueSummonerCurrentSummoner
        {
            [JsonRequired]
            public int summonerId { get; set; }

            [JsonRequired]
            public int summonerLevel { get; set; }
        }

        public class LeagueChampSelectSession
        {
            public class MyTeam
            {
                [JsonRequired]
                public int cellId { get; set; }

                [JsonRequired]
                public int championId { get; set; }

                [JsonRequired]
                public int summonerId { get; set; }

                [JsonRequired]
                public int selectedSkinId { get; set; }

                [JsonRequired]
                public int spell1Id { get; set; }

                [JsonRequired]
                public int spell2Id { get; set; }

                [JsonRequired]
                public int wardSkinId { get; set; }
            }

            [JsonRequired]
            public int localPlayerCellId { get; set; }

            [JsonRequired]
            public MyTeam[] myTeam { get; set; }
        }

        public class LeagueChampSelectGridChampions
        {
            [JsonRequired]
            public string name { get; set; }
        }

        public class LeagueChampSelectSessionMySelectionPatch
        {
            [JsonRequired]
            public int selectedSkinId { get; set; }

            [JsonRequired]
            public int spell1Id { get; set; }

            [JsonRequired]
            public int spell2Id { get; set; }

            [JsonRequired]
            public int wardSkinId { get; set; }
        }

        public class LeaguePerksPage
        {
            [JsonRequired]
            public int id { get; set; }

            [JsonRequired]
            public bool isEditable { get; set; }

            [JsonRequired]
            public string name { get; set; }
        }

        public class LeaguePerksPagePost
        {
            [JsonRequired]
            public string name { get; set; }

            [JsonRequired]
            public int primaryStyleId { get; set; }

            [JsonRequired]
            public int[] selectedPerkIds { get; set; }

            [JsonRequired]
            public int subStyleId { get; set; }
        }

        public class BlitzChampions
        {
            public class Data
            {
                public class Stats
                {
                    public class Builds
                    {
                        // HACK: JsonRequired attribute raises an error (value is exists), Newtonsoft.Json bug?
                        public int[] build { get; set; }
                    }

                    [JsonRequired]
                    public Builds runes { get; set; }

                    [JsonRequired]
                    public Builds rune_stat_shards { get; set; }

                    [JsonRequired]
                    public Builds spells { get; set; }

                    [JsonRequired]
                    public Builds starting_items { get; set; }

                    [JsonRequired]
                    public Builds core_builds { get; set; }

                    [JsonRequired]
                    public Builds big_item_builds { get; set; }
                }

                [JsonRequired]
                public string role { get; set; }

                [JsonRequired]
                public Stats stats { get; set; }
            }

            [JsonRequired]
            public Data[] data { get; set; }
        }

        public class BlitzChampionsRole
        {
            [JsonRequired]
            public string role { get; set; }
        }

        public class BlitzPatchesCurrent
        {
            public class Data
            {
                [JsonRequired]
                public string patch { get; set; }
            }

            [JsonRequired]
            public Data data { get; set; }
        }

        public class IngameItemRecommended
        {
            public class Block
            {
                public class Item
                {
                    public int count { get; set; }
                    public string id { get; set; }

                    public Item()
                    {
                        count = 1;
                    }

                    public Item(string id) : this()
                    {
                        this.id = id;
                    }
                }

                public string hideIfSummonerSpell { get; set; }
                public Item[] items { get; set; }
                public int maxSummonerLevel { get; set; }
                public int minSummonerLevel { get; set; }
                public bool recMath { get; set; }
                public string showIfSummonerSpell { get; set; }
                public string type { get; set; }

                public Block()
                {
                    hideIfSummonerSpell = string.Empty;
                    maxSummonerLevel = -1;
                    minSummonerLevel = -1;
                    recMath = false;
                    showIfSummonerSpell = string.Empty;
                }
            }

            public List<Block> blocks { get; set; }
            public string map { get; set; }
            public string mode { get; set; }
            public bool priority { get; set; }
            public int sortrank { get; set; }
            public string title { get; set; }
            public string type { get; set; }

            public IngameItemRecommended()
            {
                map = "any";
                mode = "any";
                priority = false;
                sortrank = -1;
                type = "custom";
                blocks = new List<Block>();
            }

            public void AddBlock(string name, IEnumerable<int> indexes)
            {
                var block = new Block();

                block.items = indexes.Select(x => new Block.Item(x.ToString())).ToArray();
                block.type = name;

                blocks.Add(block);
            }
        }

        private const string runePagePrefix = "LightBlitz: ";
        private const string itemBuildsFileName = "@lightblitz.json";
        private const string mapSummonersRift = "SR";
        private const string mapHowlingAbyss = "HA";

        private readonly HttpClient httpClient = new HttpClient();
        private readonly HttpClient leagueHttpClient = new HttpClient();
        private readonly Champions champions = new Champions();
        private readonly Maps maps = new Maps();

        private Thread thread;
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
            httpClient.Timeout = TimeSpan.FromSeconds(20.0);
            leagueHttpClient.Timeout = TimeSpan.FromSeconds(5.0);
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
                if (!GetLeagueClientInformation(out var process, out var path))
                {
                    await Task.Delay(5000);
                    continue;
                }

                var championBaseFolder = Path.Combine(path, "Config/Champions");

                if (Directory.Exists(championBaseFolder))
                {
                    var championFolders = Directory.GetDirectories(championBaseFolder);

                    foreach (var championFolder in championFolders)
                    {
                        var itemBuildsPath = Path.Combine(championFolder, "Recommended/" + itemBuildsFileName);

                        if (File.Exists(itemBuildsPath))
                            File.Delete(itemBuildsPath);
                    }
                }

                await champions.Load(leagueHttpClient);
                await maps.Load(leagueHttpClient);

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

                        var mapStringId = maps.GetMapStringID(gameflowSession.gameData.queue.mapId);

                        if ((mapStringId == mapSummonersRift && Settings.Current.MapSummonersLift) || (mapStringId == mapHowlingAbyss && Settings.Current.MapHowlingAbyss))
                        {
                            var championId = await GetSelectedChampionId();

                            if (latestChampionId != championId)
                            {
                                var currentVersion = await GetBlitzCurrentVersion();
                                var summonerData = await GetSummoner();
                                var recommendedRole = await GetBlitzRecommendedRole(championId);
                                var recommendedData = await GetBlitzRecommendedData(gameflowSession, championId, currentVersion);

                                if (recommendedData != null && recommendedData.data.Length >= 1)
                                {
                                    var recommendedDataWithRole = recommendedData.data.FirstOrDefault(x => x.role == recommendedRole);

                                    if (recommendedDataWithRole == null)
                                        recommendedDataWithRole = recommendedData.data[0];

                                    Debug.WriteLine("championId={0}, recommendedRole={1}", championId, recommendedRole);

                                    if (Settings.Current.ApplySpells)
                                        await SetSpells(summonerData, recommendedDataWithRole);

                                    if (Settings.Current.ApplyRunes && summonerData.summonerLevel >= 10)
                                        await SetRunes(championId, recommendedDataWithRole);

                                    if (Settings.Current.ApplyItemBuilds)
                                        SetItemBuilds(championId, recommendedDataWithRole, path);

                                    latestChampionId = championId;
                                }
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
            var result = await LeagueRequestRaw(HttpMethod.Get, "lol-champ-select/v1/current-champion");

            if (result != null && int.TryParse(result, out int champion))
                return champion;

            return 0;
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
                var mapStringId = maps.GetMapStringID(gameflowSession.gameData.queue.mapId);

                // Resolve queueId for custom game
                if (mapStringId == mapSummonersRift)
                    queueId = 420;
                else if (mapStringId == mapHowlingAbyss)
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
            var championName = champions.GetName(championId);

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

        private bool SetItemBuilds(int championId, BlitzChampions.Data recommendedData, string path)
        {
            try
            {
                string configPath = Path.Combine(path, "Config/Champions/" + champions.GetAlias(championId) + "/Recommended");

                if (!Directory.Exists(configPath))
                    Directory.CreateDirectory(configPath);

                var itemBuilds = new IngameItemRecommended();

                itemBuilds.title = "LightBlitz";

                itemBuilds.AddBlock("Starting Items & Trinkets", recommendedData.stats.starting_items.build);
                itemBuilds.AddBlock("Consumables", new int[] { 2003, 2031, 2033, 2138, 2139, 2140 });
                itemBuilds.AddBlock("Core Build Path", recommendedData.stats.core_builds.build);
                itemBuilds.AddBlock("Core Final Build", recommendedData.stats.big_item_builds.build);

                var json = JsonConvert.SerializeObject(itemBuilds);

                File.WriteAllText(Path.Combine(configPath, itemBuildsFileName), json);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.ToString());
                return false;
            }

            return true;
        }

        private async Task<T> LeagueRequest<T>(HttpMethod method, string url, HttpContent content = null)
        {
            var result = await LeagueRequestRaw(method, url, content);

            if (result != null)
            {
                try
                {
                    return JsonConvert.DeserializeObject<T>(result);
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
                var requestMessage = new HttpRequestMessage(method, url);

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
                    string result = await response.Content.ReadAsStringAsync();

                    Debug.WriteLine(result);

                    try
                    {
                        return JsonConvert.DeserializeObject<T>(result);
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

        private bool GetLeagueClientInformation(out Process process, out string path)
        {
            // Get connect information
            var processes = Process.GetProcessesByName("LeagueClient");

            process = null;
            path = string.Empty;

            foreach (var p in processes)
            {
                string directory = Path.GetDirectoryName(p.MainModule.FileName);
                string lockfilePath = Path.Combine(directory, "lockfile");

                path = directory;

                if (File.Exists(lockfilePath))
                {
                    using (var stream = new FileStream(lockfilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        var splitted = reader.ReadToEnd().Split(':');

                        if (splitted.Length != 5)
                            continue;

                        Debug.WriteLine("port={0}, password={1}", splitted[2], splitted[3]);

                        leagueHttpClient.BaseAddress = new Uri(string.Format("https://127.0.0.1:{0}", splitted[2]));
                        leagueHttpClient.DefaultRequestHeaders.Remove("Authorization");
                        leagueHttpClient.DefaultRequestHeaders.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Format("riot:{0}", splitted[3]))));

                        process = p;

                        return true;
                    }
                }
            }

            return false;
        }

        private bool ServerCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }
    }
}
