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
        private static Timer _pollTimer;

        public static string Uuid { get; private set; } = string.Empty;
        public static string IdToken { get; private set; } = string.Empty;
        public static string DisplayName { get; private set; } = string.Empty;
        public static string LinkCode { get; private set; } = string.Empty;
        public static long LinkCodeExpiresAt { get; private set; } = 0;
        public static string LastError { get; set; } = string.Empty;
        public static bool IsLinked => !string.IsNullOrWhiteSpace(IdToken);

        public static List<JObject> Submissions { get; } = new List<JObject>();
        public static JObject SelectedSubmission { get; set; }

        public static event Action OnStateChanged;

        public static void Init(ConfigFile config, string pluginFolder)
        {
            _cacheUrl = config.Bind("Community", "CacheUrl", "http://144.21.60.21/contracts/community_submissions.json", "URL for the VM community submissions cache");
            _apiBaseUrl = config.Bind("Community", "ApiBaseUrl", "https://serenity-workshop.netlify.app/.netlify/functions", "Base URL for the community API");

            _storagePath = Path.Combine(pluginFolder, "TheQuartermaster", "community_auth.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_storagePath) ?? string.Empty);
            LoadAuth();
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
        }

        private static void SaveAuth()
        {
            try
            {
                var auth = new CommunityAuthFile
                {
                    Uuid = Uuid,
                    IdToken = IdToken,
                    DisplayName = DisplayName
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
                        IdToken = status.IdToken ?? string.Empty;
                        DisplayName = status.DisplayName ?? string.Empty;
                        LinkCode = string.Empty;
                        LinkCodeExpiresAt = 0;
                        SaveAuth();
                        StopPolling();
                        LoadSubmissions();
                        LastError = string.Empty;
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

        public static void LoadSubmissions()
        {
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

        public static void CastVote(string submissionId, bool support)
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
