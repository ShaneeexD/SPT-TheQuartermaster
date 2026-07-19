using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TheQuartermaster.Client.Services;
using UnityEngine;

namespace TheQuartermaster.Client.UI
{
    public class CommunityPanel : MonoBehaviour
    {
        public static CommunityPanel Instance { get; private set; }

        private bool _visible = false;
        private bool _showDetails = false;
        private Vector2 _listScroll;
        private string _errorMessage = string.Empty;
        private float _lastSubmissionsRefresh = -999f;
        private const float SubmissionRefreshSeconds = 60f;

        public bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                if (_visible)
                {
                    TryRefreshSubmissions();
                }
            }
        }

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            CommunityApiClient.OnStateChanged += OnStateChanged;
        }

        private void OnDestroy()
        {
            CommunityApiClient.OnStateChanged -= OnStateChanged;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                Visible = !Visible;
            }
        }

        private void OnStateChanged()
        {
            _errorMessage = CommunityApiClient.LastError;
        }

        private void TryRefreshSubmissions()
        {
            if (Time.time - _lastSubmissionsRefresh < SubmissionRefreshSeconds && CommunityApiClient.Submissions.Count > 0)
                return;
            _lastSubmissionsRefresh = Time.time;
            CommunityApiClient.LoadSubmissions();
        }

        private void OnGUI()
        {
            if (!_visible)
                return;

            var width = Mathf.Min(900, Screen.width - 40);
            var height = Mathf.Min(700, Screen.height - 80);
            var windowRect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);

            GUI.Window(0, windowRect, DrawWindow, "The Quartermaster - Community Contracts");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            DrawHeader();
            GUILayout.Space(10);
            if (_showDetails && CommunityApiClient.SelectedSubmission != null)
                DrawDetails();
            else
                DrawList();
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Width(80)))
            {
                _lastSubmissionsRefresh = -999f;
                TryRefreshSubmissions();
            }

            GUILayout.FlexibleSpace();

            if (CommunityApiClient.IsLinked)
            {
                GUILayout.Label($"Linked: {CommunityApiClient.DisplayName}");
                if (GUILayout.Button("Unlink", GUILayout.Width(80)))
                {
                    // Clearing the token would require a new link; for now, just reload the link code.
                    CommunityApiClient.RequestLinkCode();
                }
            }
            else
            {
                if (GUILayout.Button("Link Discord", GUILayout.Width(120)))
                {
                    if (string.IsNullOrWhiteSpace(CommunityApiClient.LinkCode))
                        CommunityApiClient.RequestLinkCode();
                    else
                        Application.OpenURL($"https://serenity-workshop.netlify.app/link?code={CommunityApiClient.LinkCode}");
                }
            }

            if (GUILayout.Button("X", GUILayout.Width(40)))
            {
                Visible = false;
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(CommunityApiClient.LinkCode))
            {
                var remaining = Mathf.Max(0, (CommunityApiClient.LinkCodeExpiresAt - UnixMs()) / 1000f);
                GUILayout.Label($"Link Code: {CommunityApiClient.LinkCode} (expires in {remaining:F0}s)");
                if (GUILayout.Button("Open Link Page"))
                {
                    Application.OpenURL($"https://serenity-workshop.netlify.app/link?code={CommunityApiClient.LinkCode}");
                }
            }

            if (!string.IsNullOrWhiteSpace(_errorMessage))
            {
                GUI.color = Color.red;
                GUILayout.Label($"Error: {_errorMessage}");
                GUI.color = Color.white;
            }
        }

        private void DrawList()
        {
            var submissions = CommunityApiClient.Submissions;
            if (submissions.Count == 0)
            {
                GUILayout.Label("No pending community submissions.");
                return;
            }

            GUILayout.Label($"Pending Submissions ({submissions.Count})");
            _listScroll = GUILayout.BeginScrollView(_listScroll);
            foreach (var submission in submissions)
            {
                DrawSubmissionRow(submission);
                GUILayout.Space(4);
            }
            GUILayout.EndScrollView();
        }

        private void DrawSubmissionRow(JObject submission)
        {
            var id = submission["id"]?.ToString() ?? string.Empty;
            var title = submission["title"]?.ToString() ?? "Untitled";
            var author = submission["created_by"]?.ToString() ?? "Unknown";
            var upvotes = submission["upvotes"]?.ToObject<int>() ?? 0;
            var downvotes = submission["downvotes"]?.ToObject<int>() ?? 0;
            var ratio = submission["approval_ratio"]?.ToObject<float>() ?? 0f;
            var total = upvotes + downvotes;

            GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.ExpandWidth(true));
            GUILayout.BeginVertical();
            GUILayout.Label($"<b>{title}</b>  by {author}");
            GUILayout.Label($"Support: {ratio:F0}% ({upvotes}/{total})");
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(120));
            if (GUILayout.Button("View"))
            {
                CommunityApiClient.SelectedSubmission = submission;
                _showDetails = true;
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawDetails()
        {
            var s = CommunityApiClient.SelectedSubmission;
            if (s == null)
            {
                _showDetails = false;
                return;
            }

            var id = s["id"]?.ToString() ?? string.Empty;
            var title = s["title"]?.ToString() ?? "Untitled";
            var author = s["created_by"]?.ToString() ?? "Unknown";
            var description = s["description"]?.ToString() ?? string.Empty;
            var upvotes = s["upvotes"]?.ToObject<int>() ?? 0;
            var downvotes = s["downvotes"]?.ToObject<int>() ?? 0;
            var ratio = s["approval_ratio"]?.ToObject<float>() ?? 0f;

            _listScroll = GUILayout.BeginScrollView(_listScroll);
            GUILayout.Label($"<size=16><b>{title}</b></size>");
            GUILayout.Label($"Author: {author}");
            GUILayout.Label($"Support: {ratio:F0}%  Upvotes: {upvotes}  Downvotes: {downvotes}");
            GUILayout.Space(10);
            GUILayout.Label("Description:");
            GUILayout.Label(description, GUI.skin.textArea, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            DrawObjectives(s["objectives"] as JArray);
            DrawRewards(s["rewards"]); // may be object or already string

            GUILayout.Space(20);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Support", GUILayout.Height(40)))
            {
                if (CommunityApiClient.IsLinked)
                    CommunityApiClient.CastVote(id, true);
                else
                    _errorMessage = "Link Discord before voting.";
            }
            if (GUILayout.Button("Reject", GUILayout.Height(40)))
            {
                if (CommunityApiClient.IsLinked)
                    CommunityApiClient.CastVote(id, false);
                else
                    _errorMessage = "Link Discord before voting.";
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            if (GUILayout.Button("Back"))
            {
                _showDetails = false;
                CommunityApiClient.SelectedSubmission = null;
            }
            GUILayout.EndScrollView();
        }

        private void DrawObjectives(JArray objectives)
        {
            GUILayout.Label("Objectives:");
            if (objectives == null || objectives.Count == 0)
            {
                GUILayout.Label("None specified.");
                return;
            }
            foreach (var obj in objectives.OfType<JObject>())
            {
                var desc = obj["description"]?.ToString() ?? obj.ToString();
                GUILayout.Label($"- {desc}");
            }
        }

        private void DrawRewards(JToken rewards)
        {
            GUILayout.Label("Rewards:");
            if (rewards == null)
            {
                GUILayout.Label("None specified.");
                return;
            }
            if (rewards.Type == JTokenType.String)
            {
                GUILayout.Label(rewards.ToString(), "box");
            }
            else
            {
                GUILayout.Label(rewards.ToString(), "box");
            }
        }

        private static long UnixMs()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }
    }
}
