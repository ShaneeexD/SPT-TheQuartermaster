using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TheQuartermaster.Client.Services
{
    public static class CommunityApiClient
    {
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static ConfigEntry<string> _cacheUrl;
        private static ConfigEntry<string> _apiBaseUrl;
        private static string _storagePath;
        private static string _localSubmissionsPath = string.Empty;
        private static Timer _pollTimer;
        private static Timer _tokenRefreshTimer;

        public static string Uuid { get; private set; } = string.Empty;
        public static string IdToken { get; private set; } = string.Empty;
        public static string DisplayName { get; private set; } = string.Empty;
        public static string LinkCode { get; private set; } = string.Empty;
        public static long LinkCodeExpiresAt { get; private set; } = 0;
        public static long TokenExpiresAt { get; private set; } = 0;
        public static string LastError { get; set; } = string.Empty;
        public static bool IsLinked => !string.IsNullOrWhiteSpace(IdToken);
        public static string LocalSubmissionsModPath { get; private set; } = string.Empty;

        public static List<JObject> Submissions { get; } = new List<JObject>();
        public static JObject SelectedSubmission { get; set; }

        public static event Action OnStateChanged;
        public static event Action OnLinked;

        public static void Init(ConfigFile config, string pluginFolder)
        {
            _cacheUrl = config.Bind("Community", "CacheUrl", "http://144.21.60.21/contracts/community_submissions.json", "URL for the VM community submissions cache");
            _apiBaseUrl = config.Bind("Community", "ApiBaseUrl", "https://serenity-workshop.netlify.app/.netlify/functions", "Base URL for the community API");

            _storagePath = Path.Combine(pluginFolder, "TheQuartermaster", "community_auth.json");
            _localSubmissionsPath = ResolveLocalSubmissionsPath(pluginFolder);
            LocalSubmissionsModPath = Path.GetDirectoryName(_localSubmissionsPath) ?? string.Empty;
            Directory.CreateDirectory(Path.GetDirectoryName(_storagePath) ?? string.Empty);
            LoadAuth();
        }

        private static string ResolveLocalSubmissionsPath(string pluginFolder)
        {
            try
            {
                var dir = new DirectoryInfo(pluginFolder);
                while (dir != null && !string.Equals(dir.Name, "BepInEx", StringComparison.OrdinalIgnoreCase))
                {
                    dir = dir.Parent;
                }

                var gameRoot = dir?.Parent?.FullName;
                if (!string.IsNullOrWhiteSpace(gameRoot))
                {
                    return Path.Combine(gameRoot, "user", "mods", "TheQuartermaster", "client_submissions.json");
                }
            }
            catch { }
            return string.Empty;
        }

        private static void LoadAuth()
        {
            try
            {
                if (File.Exists(_storagePath))
                {
                    var json = File.ReadAllText(_storagePath);
                    var auth = JsonConvert.DeserializeObject<CommunityAuthFile>(json);
                    if (auth != null)
                    {
                        Uuid = auth.Uuid ?? Guid.NewGuid().ToString("N");
                        IdToken = auth.IdToken ?? string.Empty;
                        DisplayName = auth.DisplayName ?? string.Empty;
                        TokenExpiresAt = auth.TokenExpiresAt;
                        if (IsLinked && TokenExpiresAt == 0)
                            TokenExpiresAt = ParseTokenExpiry(IdToken);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[TheQuartermaster] Failed to load community auth: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(Uuid))
            {
                Uuid = Guid.NewGuid().ToString("N");
                SaveAuth();
            }
            else if (IsLinked)
            {
                StartTokenRefresh();
            }
        }

        private static void SaveAuth()
        {
            try
            {
                var auth = new CommunityAuthFile
                {
                    Uuid = Uuid,
                    IdToken = IdToken,
                    DisplayName = DisplayName,
                    TokenExpiresAt = TokenExpiresAt
                };
                File.WriteAllText(_storagePath, JsonConvert.SerializeObject(auth, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[TheQuartermaster] Failed to save community auth: {ex.Message}");
            }
        }

        private static string ApiUrl(string path)
        {
            var baseUrl = _apiBaseUrl?.Value?.TrimEnd('/') ?? string.Empty;
            return string.IsNullOrWhiteSpace(baseUrl) ? string.Empty : $"{baseUrl}/{path}";
        }

        private static void SetError(string message)
        {
            LastError = message;
            Plugin.Log.LogWarning($"[TheQuartermaster] {message}");
            OnStateChanged?.Invoke();
        }

        public static void RequestLinkCode()
        {
            var url = ApiUrl("link-request");
            if (string.IsNullOrWhiteSpace(url))
            {
                SetError("Community ApiBaseUrl not configured.");
                return;
            }

            var body = JsonConvert.SerializeObject(new { uuid = Uuid });
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            HttpClient.PostAsync(url, content).ContinueWith((Task<HttpResponseMessage> t) =>
            {
                try
                {
                    if (t.IsFaulted || t.IsCanceled)
                    {
                        SetError(t.Exception?.InnerException?.Message ?? "Link request failed.");
                        return;
                    }

                    using var response = t.Result;
                    var json = response.Content.ReadAsStringAsync().Result;
                    if (response.IsSuccessStatusCode)
                    {
                        var result = JsonConvert.DeserializeObject<CommunityLinkResponse>(json);
                        LinkCode = result?.Code ?? string.Empty;
                        LinkCodeExpiresAt = result?.ExpiresAt ?? 0;
                        LastError = string.Empty;
                        StartPolling();
                    }
                    else
                    {
                        SetError($"Link request error: {(int)response.StatusCode} {json}");
                    }
                }
                catch (Exception ex)
                {
                    SetError($"Link request error: {ex.Message}");
                }
            });
        }

        private static void StartPolling()
        {
            _pollTimer?.Dispose();
            _pollTimer = new Timer(_ => PollLinkStatus(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public static void StopPolling()
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
        }

        public static void Unlink()
        {
            IdToken = string.Empty;
            DisplayName = string.Empty;
            LinkCode = string.Empty;
            LinkCodeExpiresAt = 0;
            TokenExpiresAt = 0;
            SaveAuth();
            StopPolling();
            _tokenRefreshTimer?.Dispose();
            _tokenRefreshTimer = null;
            OnStateChanged?.Invoke();
        }

        public static void PollLinkStatus()
        {
            var url = ApiUrl($"link-status?uuid={Uri.EscapeDataString(Uuid)}");
            if (string.IsNullOrWhiteSpace(url))
                return;

            HttpClient.GetAsync(url).ContinueWith((Task<HttpResponseMessage> t) =>
            {
                try
                {
                    if (t.IsFaulted || t.IsCanceled)
                    {
                        SetError(t.Exception?.InnerException?.Message ?? "Link status failed.");
                        return;
                    }

                    using var response = t.Result;
                    var json = response.Content.ReadAsStringAsync().Result;
                    if (!response.IsSuccessStatusCode)
                    {
                        SetError($"Link status error: {(int)response.StatusCode} {json}");
                        return;
                    }

                    var status = JsonConvert.DeserializeObject<CommunityLinkStatusResponse>(json);
                    if (status != null && status.Linked)
                    {
                        UpdateAuthFromStatus(status, false);
                    }
                    else if (status != null)
                    {
                        LinkCode = status.Code ?? LinkCode;
                        LinkCodeExpiresAt = status.ExpiresAt;
                    }
                }
                catch (Exception ex)
                {
                    SetError($"Link status error: {ex.Message}");
                }
            });
        }

        private static void UpdateAuthFromStatus(CommunityLinkStatusResponse status, bool isRefresh)
        {
            if (status == null || !status.Linked)
                return;

            bool firstLink = string.IsNullOrWhiteSpace(IdToken);
            IdToken = status.IdToken ?? IdToken;
            DisplayName = status.DisplayName ?? DisplayName;
            LinkCode = string.Empty;
            LinkCodeExpiresAt = 0;
            TokenExpiresAt = ParseTokenExpiry(IdToken);
            SaveAuth();
            StartTokenRefresh();

            if (isRefresh)
            {
                Plugin.Log.LogInfo("[TheQuartermaster] Discord auth token refreshed.");
                return;
            }

            if (firstLink)
            {
                StopPolling();
                OnLinked?.Invoke();
                LoadSubmissions();
                LastError = string.Empty;
            }
        }

        private static long ParseTokenExpiry(string idToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(idToken))
                    return 0;

                var parts = idToken.Split('.');
                if (parts.Length < 2)
                    return 0;

                var payload = parts[1];
                payload = payload.Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }

                var bytes = Convert.FromBase64String(payload);
                var json = Encoding.UTF8.GetString(bytes);
                var obj = JObject.Parse(json);
                var exp = obj["exp"]?.ToObject<long>() ?? 0;
                return exp * 1000;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[TheQuartermaster] Failed to parse id_token expiry: {ex.Message}");
                return 0;
            }
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static void StartTokenRefresh(long delayMs = -1)
        {
            _tokenRefreshTimer?.Dispose();
            if (!IsLinked)
                return;

            if (delayMs < 0)
            {
                delayMs = TokenExpiresAt - NowMs() - 5 * 60 * 1000; // refresh 5 minutes before expiry
                if (delayMs < 0) delayMs = 0;
            }

            _tokenRefreshTimer = new Timer(_ => RefreshToken(), null, TimeSpan.FromMilliseconds(delayMs), Timeout.InfiniteTimeSpan);
        }

        private static void RefreshToken()
        {
            var url = ApiUrl($"link-status?uuid={Uri.EscapeDataString(Uuid)}");
            if (string.IsNullOrWhiteSpace(url))
                return;

            Plugin.Log.LogInfo("[TheQuartermaster] Refreshing Discord auth token...");
            HttpClient.GetAsync(url).ContinueWith((Task<HttpResponseMessage> t) =>
            {
                try
                {
                    if (t.IsFaulted || t.IsCanceled)
                    {
                        Plugin.Log.LogWarning($"[TheQuartermaster] Token refresh failed: {t.Exception?.InnerException?.Message}");
                        StartTokenRefresh(60 * 1000); // retry in 1 minute
                        return;
                    }

                    using var response = t.Result;
                    var json = response.Content.ReadAsStringAsync().Result;
                    if (!response.IsSuccessStatusCode)
                    {
                        Plugin.Log.LogWarning($"[TheQuartermaster] Token refresh returned {(int)response.StatusCode}: {json}");
                        StartTokenRefresh(60 * 1000);
                        return;
                    }

                    var status = JsonConvert.DeserializeObject<CommunityLinkStatusResponse>(json);
                    if (status != null && status.Linked)
                    {
                        UpdateAuthFromStatus(status, true);
                    }
                    else
                    {
                        Plugin.Log.LogInfo("[TheQuartermaster] Link status reports not linked, clearing auth.");
                        Unlink();
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[TheQuartermaster] Token refresh error: {ex.Message}");
                    StartTokenRefresh(60 * 1000);
                }
            });
        }

        public static void LoadSubmissions()
        {
            var localPath = _localSubmissionsPath;
            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            {
                Task.Run(() =>
                {
                    try
                    {
                        var json = File.ReadAllText(localPath);
                        var doc = JObject.Parse(json);
                        var items = doc["submissions"] as JArray ?? new JArray();
                        Submissions.Clear();
                        foreach (var item in items.OfType<JObject>())
                            Submissions.Add(item);
                        LastError = string.Empty;
                        Plugin.Log.LogInfo($"[TheQuartermaster] Loaded {Submissions.Count} pending submissions from local server file.");
                    }
                    catch (Exception ex)
                    {
                        SetError($"Local submissions load error: {ex.Message}");
                    }
                    OnStateChanged?.Invoke();
                });
                return;
            }

            var url = _cacheUrl?.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                SetError("Community CacheUrl not configured.");
                return;
            }

            Plugin.Log.LogInfo($"[TheQuartermaster] Loading community submissions from {url}");

            HttpClient.GetAsync(url).ContinueWith((Task<HttpResponseMessage> t) =>
            {
                try
                {
                    if (t.IsFaulted || t.IsCanceled)
                    {
                        var message = t.Exception?.InnerException?.Message ?? "Failed to load submissions.";
                        Plugin.Log.LogError($"[TheQuartermaster] Submissions request failed: {message}");
                        SetError(message);
                        return;
                    }

                    using var response = t.Result;
                    var json = response.Content.ReadAsStringAsync().Result;
                    if (!response.IsSuccessStatusCode)
                    {
                        Plugin.Log.LogError($"[TheQuartermaster] Submissions returned {(int)response.StatusCode}: {json}");
                        SetError($"Failed to load submissions: {(int)response.StatusCode} {json}");
                        return;
                    }

                    var doc = JObject.Parse(json);
                    if (doc["submissions"] == null)
                    {
                        Plugin.Log.LogWarning($"[TheQuartermaster] Submissions cache missing 'submissions' key. Keys: {string.Join(", ", doc.Properties().Select(p => p.Name))}");
                        SetError("Submissions cache missing 'submissions' key.");
                        return;
                    }

                    var items = doc["submissions"] as JArray ?? new JArray();
                    Submissions.Clear();
                    foreach (var item in items)
                    {
                        if (item is JObject obj)
                            Submissions.Add(obj);
                    }
                    LastError = string.Empty;
                    Plugin.Log.LogInfo($"[TheQuartermaster] Loaded {Submissions.Count} pending submissions.");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[TheQuartermaster] Load submissions error: {ex.Message}");
                    SetError($"Load submissions error: {ex.Message}");
                }
                OnStateChanged?.Invoke();
            });
        }

        public static void CastVote(string submissionId, bool support, Action<JObject> onSuccess = null)
        {
            var url = ApiUrl("contract-vote");
            if (string.IsNullOrWhiteSpace(url) || !IsLinked)
            {
                SetError("Not linked or API URL not configured.");
                return;
            }

            var body = JsonConvert.SerializeObject(new
            {
                submission_id = submissionId,
                is_upvote = support
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", IdToken);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            HttpClient.SendAsync(request).ContinueWith((Task<HttpResponseMessage> t) =>
            {
                try
                {
                    if (t.IsFaulted || t.IsCanceled)
                    {
                        SetError(t.Exception?.InnerException?.Message ?? "Vote failed.");
                        return;
                    }

                    using var response = t.Result;
                    var json = response.Content.ReadAsStringAsync().Result;
                    if (response.IsSuccessStatusCode)
                    {
                        var result = JObject.Parse(json);
                        if (SelectedSubmission != null && SelectedSubmission["id"]?.ToString() == submissionId)
                        {
                            if (result["upvotes"] != null)
                                SelectedSubmission["upvotes"] = result["upvotes"];
                            if (result["downvotes"] != null)
                                SelectedSubmission["downvotes"] = result["downvotes"];
                            if (result["approval_ratio"] != null)
                                SelectedSubmission["approval_ratio"] = result["approval_ratio"];
                        }
                        onSuccess?.Invoke(result);
                        LoadSubmissions();
                        LastError = string.Empty;
                    }
                    else
                    {
                        SetError($"Vote failed: {(int)response.StatusCode} {json}");
                    }
                }
                catch (Exception ex)
                {
                    SetError($"Vote error: {ex.Message}");
                }
            });
        }

        private class CommunityAuthFile
        {
            public string Uuid { get; set; } = string.Empty;
            public string IdToken { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public long TokenExpiresAt { get; set; }
        }

        private class CommunityLinkResponse
        {
            [JsonProperty("code")]
            public string Code { get; set; } = string.Empty;

            [JsonProperty("expires_at")]
            public long ExpiresAt { get; set; }
        }

        private class CommunityLinkStatusResponse
        {
            [JsonProperty("linked")]
            public bool Linked { get; set; }

            [JsonProperty("code")]
            public string Code { get; set; } = string.Empty;

            [JsonProperty("expires_at")]
            public long ExpiresAt { get; set; }

            [JsonProperty("id_token")]
            public string IdToken { get; set; } = string.Empty;

            [JsonProperty("display_name")]
            public string DisplayName { get; set; } = string.Empty;
        }
    }
}
