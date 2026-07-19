using System;
using System.Collections.Generic;
using System.Linq;
using EFT.UI;
using Newtonsoft.Json.Linq;
using TheQuartermaster.Client.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
        private GameObject _communityTab;
        private Tab _communityTabComponent;
        private Transform _tabBarParent;
        private bool _qmDetected = false;
        private List<Tab> _siblingTabs = new List<Tab>();

        public Component CurrentTraderScreen { get; set; }

        public bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                if (_visible)
                {
                    _cachedSubmissions = null;
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

            RefreshCommunityTab();
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

        private List<JObject> _cachedSubmissions;

        private void DrawList()
        {
            // Cache submissions at start of draw to avoid layout mismatch if list changes between Layout/Repaint
            if (_cachedSubmissions == null)
                _cachedSubmissions = CommunityApiClient.Submissions.ToList();

            _listScroll = GUILayout.BeginScrollView(_listScroll);
            if (_cachedSubmissions.Count == 0)
            {
                GUILayout.Label("No pending community submissions.");
            }
            else
            {
                GUILayout.Label($"Pending Submissions ({_cachedSubmissions.Count})");
                foreach (var submission in _cachedSubmissions)
                {
                    DrawSubmissionRow(submission);
                    GUILayout.Space(4);
                }
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

        public void RefreshCommunityTab()
        {
            if (CurrentTraderScreen == null || CurrentTraderScreen.gameObject == null || !CurrentTraderScreen.gameObject.activeInHierarchy)
            {
                if (_communityTab != null && _communityTab.activeSelf)
                    _communityTab.SetActive(false);
                Visible = false;
                return;
            }

            bool isQuartermaster = false;
            GameObject matched = null;
            string matchedText = null;

            // 1. Check transform names
            foreach (Transform t in CurrentTraderScreen.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && !string.IsNullOrEmpty(t.name) && t.name.IndexOf("quartermaster", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isQuartermaster = true;
                    matched = t.gameObject;
                    matchedText = t.name;
                    break;
                }
            }

            // 2. Check displayed text (TMP_Text / Text)
            if (!isQuartermaster)
            {
                foreach (var txt in CurrentTraderScreen.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (txt != null && !string.IsNullOrEmpty(txt.text) && txt.text.IndexOf("quartermaster", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isQuartermaster = true;
                        matched = txt.gameObject;
                        matchedText = txt.text;
                        break;
                    }
                }
            }

            if (!isQuartermaster)
            {
                foreach (var txt in CurrentTraderScreen.GetComponentsInChildren<Text>(true))
                {
                    if (txt != null && !string.IsNullOrEmpty(txt.text) && txt.text.IndexOf("quartermaster", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isQuartermaster = true;
                        matched = txt.gameObject;
                        matchedText = txt.text;
                        break;
                    }
                }
            }

            if (isQuartermaster)
            {
                if (!_qmDetected)
                {
                    Plugin.Log.LogInfo($"[TheQuartermaster] Detected Quartermaster from {matched?.name ?? "unknown"}: {matchedText}");
                    _qmDetected = true;
                }
                if (_communityTab == null)
                    AttachCommunityTab(CurrentTraderScreen);
                else if (!_communityTab.activeSelf)
                    _communityTab.SetActive(true);
            }
            else
            {
                _qmDetected = false;
                if (_communityTab != null && _communityTab.activeSelf)
                    _communityTab.SetActive(false);
                Visible = false;
            }
        }

        public void AttachCommunityTab(Component traderScreen)
        {
            if (_communityTab != null)
                return;

            if (traderScreen == null)
                return;

            GameObject services = null;

            // Search the opened trader screen for the existing Services tab.
            foreach (Transform t in traderScreen.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == "Services")
                {
                    services = t.gameObject;
                    break;
                }
            }

            if (services == null)
            {
                Plugin.Log.LogWarning("[TheQuartermaster] Could not find 'Services' tab under the trader screen to clone.");
                return;
            }

            try
            {
                var tabs = services.transform.parent;

                // Log the parent and siblings for debugging
                Plugin.Log.LogInfo($"[TheQuartermaster] Tab bar parent: {tabs.name}, childCount={tabs.childCount}");
                for (int i = 0; i < tabs.childCount; i++)
                {
                    var child = tabs.GetChild(i);
                    Plugin.Log.LogInfo($"[TheQuartermaster]   Tab[{i}]: {child.name} active={child.gameObject.activeSelf}");
                }

                // Temporarily activate the source so the clone inherits active children
                bool wasActive = services.activeSelf;
                services.SetActive(true);

                var clone = Instantiate(services, tabs);
                clone.name = "CommunityTab";

                // Restore original Services tab state
                services.SetActive(wasActive);

                // Force the clone and all children active
                clone.SetActive(true);
                foreach (Transform child in clone.transform)
                {
                    child.gameObject.SetActive(true);
                }

                // The tab bar layout arranges right-to-left by sibling index (index 0 = rightmost).
                // To place Community to the RIGHT of Services, insert at servicesIndex (before Services).
                int servicesIndex = services.transform.GetSiblingIndex();
                clone.transform.SetSiblingIndex(servicesIndex);

                // Log Services and clone RectTransform info for debugging
                var servicesRT = services.transform as RectTransform;
                var cloneRT = clone.transform as RectTransform;
                if (servicesRT != null && cloneRT != null)
                {
                    Plugin.Log.LogInfo($"[TheQuartermaster] Services RT: pos={servicesRT.anchoredPosition} size={servicesRT.rect.size} sibling={servicesIndex}");
                    Plugin.Log.LogInfo($"[TheQuartermaster] Clone RT: pos={cloneRT.anchoredPosition} size={cloneRT.rect.size} sibling={cloneRT.GetSiblingIndex()}");
                }

                // Log all components on the clone for debugging
                var comps = clone.GetComponents<Component>();
                Plugin.Log.LogInfo($"[TheQuartermaster] Clone components: {string.Join(", ", comps.Select(c => c.GetType().Name))}");

                // Destroy LocalizedText so it doesn't override our text, then set text on ALL TMP_Text components
                var locText = clone.GetComponent<LocalizedText>();
                if (locText != null)
                {
                    GameObject.Destroy(locText);
                    Plugin.Log.LogInfo("[TheQuartermaster] Destroyed LocalizedText on CommunityTab.");
                }
                var allTexts = clone.GetComponentsInChildren<TMP_Text>(true);
                foreach (var t in allTexts)
                {
                    t.text = "QM";
                }
                Plugin.Log.LogInfo($"[TheQuartermaster] Set 'QM' on {allTexts.Length} TMP_Text components.");

                // Get the Tab component directly
                var tab = clone.GetComponent<Tab>();
                if (tab == null)
                {
                    Plugin.Log.LogError("[TheQuartermaster] No Tab component found on clone.");
                    return;
                }
                _communityTabComponent = tab;

                // Set Interactable=true so CanHandlePointerClick works (it returns !bool_0 && Interactable)
                tab.Interactable = true;

                // Call vmethod_0(true) to fully enable the tab — sets image alpha to 1f and canvas group unlock status
                tab.vmethod_0(true);

                // Also manually reset CanvasGroup since vmethod_0 may not cover all cases
                var cg = clone.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 1f;
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                    Plugin.Log.LogInfo("[TheQuartermaster] Reset CanvasGroup: alpha=1, interactable=true, blocksRaycasts=true");
                }

                // Deselect initially — UpdateVisual(false, false) sets bool_0=false, _uiSelected=false
                tab.UpdateVisual(false, false);

                // Store tab bar parent and collect sibling tabs
                _tabBarParent = tabs;
                _siblingTabs.Clear();
                foreach (Transform sibling in tabs)
                {
                    if (sibling.gameObject == clone)
                        continue;
                    var siblingTab = sibling.GetComponent<Tab>();
                    if (siblingTab != null)
                    {
                        _siblingTabs.Add(siblingTab);
                        // Subscribe to OnSelectionChanged so we can deselect Community when another tab is clicked
                        siblingTab.OnSelectionChanged += OnSiblingTabSelectionChanged;
                    }
                }
                Plugin.Log.LogInfo($"[TheQuartermaster] Subscribed to {_siblingTabs.Count} sibling tabs.");

                // Subscribe to our own tab's OnSelectionChanged — the Tab handles its own clicks via IPointerClickHandler.
                // HandlePointerClick fires OnSelectionChanged(this, !isSelectedNow).
                _communityTabComponent.OnSelectionChanged += OnCommunityTabSelectionChanged;
                Plugin.Log.LogInfo("[TheQuartermaster] Subscribed to Community tab OnSelectionChanged.");

                // Force layout rebuild so the tab appears in the correct position
                var tabsRT = tabs as RectTransform;
                if (tabsRT != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(tabsRT);
                if (cloneRT != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(cloneRT);

                // Start coroutine to rebuild layout next frame (layout groups sometimes need a frame)
                StartCoroutine(RebuildLayoutNextFrame(tabs, clone, services));

                _communityTab = clone;
                Plugin.Log.LogInfo($"[TheQuartermaster] Injected Community tab at sibling index {clone.transform.GetSiblingIndex()} (Services at {servicesIndex}).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[TheQuartermaster] Failed to inject Community tab: {ex.Message}");
            }
        }

        private System.Collections.IEnumerator RebuildLayoutNextFrame(Transform parent, GameObject clone, GameObject services)
        {
            yield return null; // wait one frame
            var rt = parent as RectTransform;
            if (rt != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                Plugin.Log.LogInfo($"[TheQuartermaster] Layout rebuilt next frame for {parent.name}, childCount={parent.childCount}");

                // Log all tab positions after rebuild
                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i) as RectTransform;
                    if (child != null)
                        Plugin.Log.LogInfo($"[TheQuartermaster]   After rebuild Tab[{i}]: {child.name} pos={child.anchoredPosition} size={child.rect.size}");
                }

                // Manually position the clone to the right of Services if layout didn't do it correctly
                var servicesRT = services.transform as RectTransform;
                var cloneRT = clone.transform as RectTransform;
                if (servicesRT != null && cloneRT != null)
                {
                    float servicesX = servicesRT.anchoredPosition.x;
                    float cloneX = cloneRT.anchoredPosition.x;
                    float distance = Mathf.Abs(cloneX - servicesX);

                    // If the clone is too close to Services (overlapping), manually offset it
                    // Calculate the step from two adjacent original tabs
                    float step = 0f;
                    if (parent.childCount >= 3)
                    {
                        // Find two original tabs (not the clone) to measure spacing
                        var positions = new List<float>();
                        for (int i = 0; i < parent.childCount; i++)
                        {
                            var child = parent.GetChild(i) as RectTransform;
                            if (child != null && child.gameObject != clone)
                                positions.Add(child.anchoredPosition.x);
                        }
                        if (positions.Count >= 2)
                        {
                            positions.Sort();
                            step = positions[1] - positions[0];
                        }
                    }

                    if (step > 0f && distance < step * 0.5f)
                    {
                        // Place clone to the right of Services (higher x in this layout)
                        Vector2 newPos = cloneRT.anchoredPosition;
                        newPos.x = servicesX + step;
                        cloneRT.anchoredPosition = newPos;
                        Plugin.Log.LogInfo($"[TheQuartermaster] Manually repositioned CommunityTab from x={cloneX} to x={newPos.x} (Services at x={servicesX}, step={step})");
                    }
                }
            }
        }

        private void OnCommunityTabSelectionChanged(Tab tab, bool selected)
        {
            if (selected)
            {
                // Select our tab visually (sendCallback=false: don't call Controller.Show on cloned controller)
                _communityTabComponent.Select(false, false);

                // Deselect all sibling tabs (hides their content panels via Controller.TryHide)
                foreach (var siblingTab in _siblingTabs)
                    siblingTab.Deselect();

                Visible = true;
            }
            else
            {
                Visible = false;
            }
        }

        private void OnSiblingTabSelectionChanged(Tab selectedTab, bool selected)
        {
            if (selected && _communityTabComponent != null)
            {
                // A sibling tab was selected — deselect Community and hide panel
                _communityTabComponent.UpdateVisual(false, false);
                Visible = false;
            }
        }

        private static long UnixMs()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }
    }
}
