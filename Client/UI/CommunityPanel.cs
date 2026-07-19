using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.UI;
using EFT;
using Newtonsoft.Json.Linq;
using TheQuartermaster.Client.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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
        private int _communityTabOriginalSiblingIndex = -1;
        private bool _wasOnQM = false;
        private RectTransform _contentArea;
        private GameObject _communityScreen;
        private Text _statusText;
        private Text _rightSideText;
        private Transform _submissionListContainer;
        private GameObject _listView;
        private GameObject _detailView;
        private Button _refreshButton;
        private Button _linkButton;
        private TMP_Text _linkButtonText;

        private static readonly Queue<Action> _mainThreadActions = new Queue<Action>();
        private static readonly object _mainThreadActionsLock = new object();

        public Component CurrentTraderScreen { get; set; }

        public bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                if (_visible)
                    TryRefreshSubmissions();
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
            CommunityApiClient.OnLinked += OnLinked;
        }

        private void OnDestroy()
        {
            CommunityApiClient.OnStateChanged -= OnStateChanged;
            CommunityApiClient.OnLinked -= OnLinked;
        }

        private void OnLinked()
        {
            RunOnMainThread(() =>
            {
                if (_currentSubmission != null && _communityScreen != null && _communityScreen.activeSelf)
                    ShowSubmissionDetails(_currentSubmission);
            });
        }

        private void Update()
        {
            ProcessMainThreadActions();
        }

        private static void ProcessMainThreadActions()
        {
            Action[] actions;
            lock (_mainThreadActionsLock)
            {
                if (_mainThreadActions.Count == 0)
                    return;
                actions = _mainThreadActions.ToArray();
                _mainThreadActions.Clear();
            }
            foreach (var action in actions)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[TheQuartermaster] Main thread action error: {ex.Message}");
                }
            }
        }

        public static void RunOnMainThread(Action action)
        {
            if (action == null)
                return;
            lock (_mainThreadActionsLock)
                _mainThreadActions.Enqueue(action);
        }

        private void LateUpdate()
        {
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

        public void RefreshCommunityTab()
        {
            if (CurrentTraderScreen == null || CurrentTraderScreen.gameObject == null || !CurrentTraderScreen.gameObject.activeInHierarchy)
            {
                if (_communityTab != null && _communityTab.activeSelf)
                {
                    if (_communityTabComponent != null)
                    {
                        _communityTabComponent.UpdateVisual(false, false);
                        _communityTabComponent.vmethod_0(false);
                    }
                    if (_tabBarParent != null && _communityTabOriginalSiblingIndex >= 0)
                        _communityTab.transform.SetSiblingIndex(_communityTabOriginalSiblingIndex);
                }
                Visible = false;
                return;
            }

            // Detect QM by checking the TraderClass property on TraderScreensGroup
            bool isQuartermaster = false;
            string traderName = null;

            var screensGroup = CurrentTraderScreen as TraderScreensGroup;
            if (screensGroup != null && screensGroup.TraderClass != null)
            {
                var trader = screensGroup.TraderClass;
                var traderId = trader.Id;
                var nickname = trader.LocalizedName;
                traderName = !string.IsNullOrWhiteSpace(nickname) ? nickname : traderId;

                if (!string.IsNullOrWhiteSpace(traderId) && traderId.IndexOf("quartermaster", StringComparison.OrdinalIgnoreCase) >= 0)
                    isQuartermaster = true;
                else if (!string.IsNullOrWhiteSpace(nickname) && nickname.IndexOf("quartermaster", StringComparison.OrdinalIgnoreCase) >= 0)
                    isQuartermaster = true;
            }

            if (isQuartermaster)
            {
                if (!_qmDetected)
                {
                    Plugin.Log.LogInfo($"[TheQuartermaster] Detected Quartermaster trader: {traderName}");
                    _qmDetected = true;
                }
                if (_communityTab == null)
                {
                    AttachCommunityTab(CurrentTraderScreen);
                }
                else
                {
                    if (!_communityTab.activeSelf)
                        _communityTab.SetActive(true);
                    // Re-enable if we were previously not on QM
                    if (!_wasOnQM)
                    {
                        Plugin.Log.LogInfo("[TheQuartermaster] Re-enabling QM tab (transitioning to QM).");
                        if (_communityTabComponent != null)
                        {
                            _communityTabComponent.Interactable = true;
                            _communityTabComponent.vmethod_0(true);
                        }
                        var cg = _communityTab.GetComponent<CanvasGroup>();
                        if (cg != null)
                        {
                            cg.alpha = 1f;
                            cg.interactable = true;
                            cg.blocksRaycasts = true;
                        }
                    }
                }
                _wasOnQM = true;
            }
            else
            {
                _qmDetected = false;
                if (_communityTab != null && _communityTab.activeSelf)
                {
                    if (_wasOnQM)
                        Plugin.Log.LogInfo($"[TheQuartermaster] Not on QM (current: {traderName ?? "unknown"}) — deselecting and greying out QM tab.");
                    if (_communityTabComponent != null)
                    {
                        // Deselect via controller to hide the CommunityScreen
                        _communityTabComponent.Deselect();
                        // Force deselect: set bool_0=false, _uiSelected=false, then update visuals
                        _communityTabComponent.UpdateVisual(false, false);
                        // Grey out and disable interaction
                        _communityTabComponent.Interactable = false;
                        _communityTabComponent.vmethod_0(false);
                    }
                    // Also hide the screen directly in case controller didn't
                    if (_communityScreen != null)
                        _communityScreen.SetActive(false);
                    // Also reset CanvasGroup directly
                    var cg = _communityTab.GetComponent<CanvasGroup>();
                    if (cg != null)
                    {
                        cg.alpha = 0.5f;
                        cg.interactable = false;
                        cg.blocksRaycasts = false;
                    }
                    // Restore original sibling index
                    if (_tabBarParent != null && _communityTabOriginalSiblingIndex >= 0)
                        _communityTab.transform.SetSiblingIndex(_communityTabOriginalSiblingIndex);
                    Visible = false;
                }
                _wasOnQM = false;
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
                    t.text = "VOTING";
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

                // Find the content area — look for one of the screen GameObjects (ServicesScreen, QuestsScreen, TraderDealScreen)
                var screenParent = tabs.parent;
                Transform contentArea = null;
                foreach (Transform child in screenParent)
                {
                    var name = child.name;
                    if (name == "ServicesScreen" || name == "QuestsScreen" || name == "TraderDealScreen" || name == "Content")
                    {
                        contentArea = child as RectTransform;
                        break;
                    }
                }
                if (contentArea == null)
                {
                    // Search deeper — the screens might be nested
                    foreach (Transform child in screenParent)
                    {
                        if (child == tabs) continue;
                        var rt = child as RectTransform;
                        if (rt != null && rt.rect.height > 200f)
                        {
                            contentArea = rt;
                            break;
                        }
                    }
                }
                _contentArea = contentArea as RectTransform;
                Plugin.Log.LogInfo($"[TheQuartermaster] Content area: {(_contentArea?.name ?? "null")}");

                // Clone the ServicesScreen content panel to use as our screen
                // Search recursively — it may not be a direct child of screenParent
                Plugin.Log.LogInfo($"[TheQuartermaster] Searching for ServicesScreen. screenParent={screenParent.name}, childCount={screenParent.childCount}");
                for (int i = 0; i < screenParent.childCount; i++)
                {
                    var child = screenParent.GetChild(i);
                    Plugin.Log.LogInfo($"[TheQuartermaster]   screenParent child[{i}]: {child.name} active={child.gameObject.activeSelf}");
                }

                GameObject servicesScreenObj = null;
                // First try direct children
                foreach (Transform child in screenParent)
                {
                    if (child.name == "ServicesScreen")
                    {
                        servicesScreenObj = child.gameObject;
                        break;
                    }
                }
                // If not found, search recursively in the entire trader screen
                if (servicesScreenObj == null)
                {
                    Plugin.Log.LogInfo("[TheQuartermaster] ServicesScreen not a direct child of screenParent, searching recursively...");
                    foreach (var t in CurrentTraderScreen.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "ServicesScreen")
                        {
                            servicesScreenObj = t.gameObject;
                            Plugin.Log.LogInfo($"[TheQuartermaster] Found ServicesScreen at path: {GetPath(t)}");
                            break;
                        }
                    }
                }
                if (servicesScreenObj != null)
                {
                    // Parent the clone to the same parent as the original ServicesScreen (e.g. Trader Screens Group),
                    // NOT to screenParent (Tab Bar) which is where the tabs live.
                    var screenParentCorrect = servicesScreenObj.transform.parent;
                    var screenClone = Instantiate(servicesScreenObj, screenParentCorrect);
                    screenClone.name = "CommunityScreen";

                    // Strip the ServicesScreen component — we don't want it trying to show services
                    var svcComp = screenClone.GetComponent<ServicesScreen>();
                    if (svcComp != null)
                        Destroy(svcComp);

                    // Deactivate initially
                    screenClone.SetActive(false);
                    _communityScreen = screenClone;

                    // Build our voting UI into the cloned screen
                    BuildCommunityUI(screenClone);

                    // Create a controller and init the tab so Select/Deselect manages our screen
                    var controller = new CommunityScreenController(screenClone);
                    tab.Init(controller);
                    Plugin.Log.LogInfo("[TheQuartermaster] Created CommunityScreen and wired up controller.");
                }
                else
                {
                    Plugin.Log.LogWarning("[TheQuartermaster] Could not find ServicesScreen to clone for content panel.");
                }
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
                _communityTabOriginalSiblingIndex = clone.transform.GetSiblingIndex();
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
            Plugin.Log.LogInfo($"[TheQuartermaster] OnCommunityTabSelectionChanged: selected={selected}");
            if (selected)
            {
                // Select with sendCallback=true so Controller.Show() activates our CommunityScreen
                _communityTabComponent.Select(true, false);
                Plugin.Log.LogInfo($"[TheQuartermaster] CommunityScreen active={(_communityScreen != null && _communityScreen.activeSelf)}");

                // Deselect all sibling tabs (hides their content panels via Controller.TryHide)
                foreach (var siblingTab in _siblingTabs)
                    siblingTab.Deselect();

                // Move to end of siblings so it renders on top (above Services)
                if (_communityTab != null && _tabBarParent != null)
                    _communityTab.transform.SetAsLastSibling();

                // Refresh submission data when tab is opened
                TryRefreshSubmissions();
                PopulateSubmissionList();
            }
            else
            {
                // Restore original sibling index so it renders behind Services again
                if (_communityTab != null && _tabBarParent != null && _communityTabOriginalSiblingIndex >= 0)
                    _communityTab.transform.SetSiblingIndex(_communityTabOriginalSiblingIndex);
            }
        }

        private void OnSiblingTabSelectionChanged(Tab selectedTab, bool selected)
        {
            if (selected && _communityTabComponent != null)
            {
                Plugin.Log.LogInfo($"[TheQuartermaster] Sibling tab {selectedTab.name} selected, deselecting QM tab.");

                // A sibling tab was selected — deselect Community (Controller.TryHide deactivates our screen)
                _communityTabComponent.UpdateVisual(false, false);
                _communityTabComponent.Deselect();

                // Restore original sibling index so it renders behind Services again
                if (_communityTab != null && _tabBarParent != null && _communityTabOriginalSiblingIndex >= 0)
                    _communityTab.transform.SetSiblingIndex(_communityTabOriginalSiblingIndex);
            }
        }

        private GameObject _serviceItemTemplate;
        private GameObject _eftButtonTemplate;
        private JObject _currentSubmission;

        private void BuildCommunityUI(GameObject screenRoot)
        {
            // Based on the known ServicesScreen hierarchy:
            //   ServicesScreen > Services > Ragman > ServicesList > Header > Title (Text)
            //   ServicesScreen > Services > Ragman > ServicesList > List > Scroll View > Content > ServiceItem (template)
            //   ServicesScreen > Services > ArenaEftItemTransferWindow (hide)
            //   ServicesScreen > Services > Ragman > TacticalClothingView (hide)

            // Find the Services container
            var services = FindChild(screenRoot.transform, "Services");
            if (services == null)
            {
                Plugin.Log.LogError("[TheQuartermaster] Could not find 'Services' child in cloned screen.");
                return;
            }

            // Grab DealButton from ArenaEftItemTransferWindow as EFT-style button template before hiding it
            var arena = FindChild(services, "ArenaEftItemTransferWindow");
            if (arena != null)
            {
                var merchantPanel = FindChild(arena, "MerchantPanel");
                if (merchantPanel != null)
                {
                    var buttons = FindChild(merchantPanel, "Buttons");
                    if (buttons != null)
                    {
                        var dealButton = FindChild(buttons, "DealButton");
                        if (dealButton != null)
                        {
                            _eftButtonTemplate = dealButton.gameObject;
                            Plugin.Log.LogInfo("[TheQuartermaster] Stored DealButton as EFT button template.");
                            Plugin.Log.LogInfo("[TheQuartermaster] DealButton hierarchy:");
                            LogHierarchy(dealButton, 0);
                        }
                    }
                }
                arena.gameObject.SetActive(false);
                Plugin.Log.LogInfo("[TheQuartermaster] Hidden ArenaEftItemTransferWindow.");
            }

            // Find Ragman > ServicesList
            var ragman = FindChild(services, "Ragman");
            if (ragman == null)
            {
                Plugin.Log.LogError("[TheQuartermaster] Could not find 'Ragman' child.");
                return;
            }

            // Activate Ragman (parent of ServicesList) — it's inactive by default in the cloned screen
            ragman.gameObject.SetActive(true);
            Plugin.Log.LogInfo("[TheQuartermaster] Activated Ragman container.");

            // Repurpose TacticalClothingView as the right-side quest details panel
            var clothingView = FindChild(ragman, "TacticalClothingView");
            if (clothingView != null)
            {
                clothingView.gameObject.SetActive(true);
                for (int i = 0; i < clothingView.childCount; i++)
                    clothingView.GetChild(i).gameObject.SetActive(false);

                var detailsGO = new GameObject("CommunityDetails", typeof(RectTransform), typeof(Text));
                detailsGO.transform.SetParent(clothingView, false);
                var detailsRT = detailsGO.GetComponent<RectTransform>();
                detailsRT.anchorMin = new Vector2(0.02f, 0.02f);
                detailsRT.anchorMax = new Vector2(0.98f, 0.98f);
                detailsRT.offsetMin = Vector2.zero;
                detailsRT.offsetMax = Vector2.zero;
                _rightSideText = detailsGO.GetComponent<Text>();
                _rightSideText.color = new Color32(220, 220, 220, 255);
                _rightSideText.fontSize = 18;
                _rightSideText.alignment = TextAnchor.UpperLeft;
                _rightSideText.verticalOverflow = VerticalWrapMode.Overflow;
                _rightSideText.horizontalOverflow = HorizontalWrapMode.Wrap;
                _rightSideText.text = "Select a community contract to view details.";
                Plugin.Log.LogInfo("[TheQuartermaster] Prepared right-side details panel.");
            }

            var servicesList = FindChild(ragman, "ServicesList");
            if (servicesList == null)
            {
                Plugin.Log.LogError("[TheQuartermaster] Could not find 'ServicesList'.");
                return;
            }

            // Activate ServicesList so we can see its contents
            servicesList.gameObject.SetActive(true);

            // Add padding to the top of the list so items don't overlap the header
            var listContainer = FindChild(servicesList, "List");
            if (listContainer != null)
            {
                var listRT = listContainer.GetComponent<RectTransform>();
                if (listRT != null)
                {
                    // Push the list down a bit so it doesn't overlap the header
                    listRT.offsetMin = new Vector2(listRT.offsetMin.x, listRT.offsetMin.y);
                    listRT.offsetMax = new Vector2(listRT.offsetMax.x, listRT.offsetMax.y - 40f);
                    Plugin.Log.LogInfo("[TheQuartermaster] Adjusted List position to avoid header overlap.");
                }
            }

            // Find Header > Title and use it as our status text
            var header = FindChild(servicesList, "Header");
            if (header != null)
            {
                header.gameObject.SetActive(true);
                var title = header.Find("Title");
                if (title != null)
                {
                    var titleText = title.GetComponent<Text>();
                    if (titleText != null)
                    {
                        _statusText = titleText;
                        _statusText.text = "Community Contracts";
                        Plugin.Log.LogInfo("[TheQuartermaster] Repurposed ServicesList Header Title as status text.");
                    }

                    if (_rightSideText != null && _statusText != null)
                        _rightSideText.font = _statusText.font;
                }

                // Add Refresh button to the right side of the header (plain simple button that fits)
                if (header is RectTransform headerRT)
                {
                    var refreshBtn = CreateSimpleButton(header, "RefreshButton", "Refresh",
                        new Vector2(-5, 0), new Vector2(90, 28), new Vector2(1, 0.5f));
                    var btn = refreshBtn.GetComponent<Button>();
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(OnRefreshButtonClicked);
                    }
                    Plugin.Log.LogInfo("[TheQuartermaster] Added Refresh button to header.");
                }
            }

            // Find List > Scroll View > Content — this is our submission list container
            var list = FindChild(servicesList, "List");
            if (list != null)
            {
                var scrollView = FindChild(list, "Scroll View");
                if (scrollView != null)
                {
                    var scrollRect = scrollView.GetComponent<ScrollRect>();
                    if (scrollRect != null)
                    {
                        _listView = scrollView.gameObject;
                        _submissionListContainer = scrollRect.content;
                        // Add left padding so items are clamped left with some spacing
                        var vlg = _submissionListContainer.GetComponent<VerticalLayoutGroup>();
                        if (vlg != null)
                        {
                            vlg.padding = new RectOffset(15, 15, 5, 5);
                            vlg.spacing = 5;
                            Plugin.Log.LogInfo("[TheQuartermaster] Added padding to submission list container.");
                        }
                        Plugin.Log.LogInfo($"[TheQuartermaster] Repurposed scroll content '{_submissionListContainer.name}' as submission list.");
                    }

                    // Find ServiceItem template in Content
                    if (_submissionListContainer != null)
                    {
                        var serviceItem = FindChild(_submissionListContainer, "ServiceItem");
                        if (serviceItem != null)
                        {
                            _serviceItemTemplate = serviceItem.gameObject;
                            _serviceItemTemplate.SetActive(false);
                            Plugin.Log.LogInfo("[TheQuartermaster] Found ServiceItem template, stored and hidden.");
                        }
                    }
                }
            }

            Plugin.Log.LogInfo("[TheQuartermaster] Repurposed existing ServicesScreen UI successfully.");
        }

        private static Transform FindChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name)
                    return child;
            }
            return null;
        }

        private void LogHierarchy(Transform t, int depth)
        {
            var indent = new string(' ', depth * 2);
            var components = t.GetComponents<Component>();
            var compNames = components.Select(c => c.GetType().Name);
            Plugin.Log.LogInfo($"[TheQuartermaster] {indent}{t.name} [{string.Join(", ", compNames)}] active={t.gameObject.activeSelf}");
            for (int i = 0; i < t.childCount; i++)
                LogHierarchy(t.GetChild(i), depth + 1);
        }

        private static string GetPath(Transform t)
        {
            var path = t.name;
            var parent = t.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private void PopulateSubmissionList()
        {
            if (_submissionListContainer == null) return;

            if (_rightSideText != null)
                _rightSideText.text = "Select a community contract to view details.";

            // Clear existing rows (keep the template)
            for (int i = _submissionListContainer.childCount - 1; i >= 0; i--)
            {
                var child = _submissionListContainer.GetChild(i);
                if (child.gameObject == _serviceItemTemplate) continue;
                Destroy(child.gameObject);
            }

            var submissions = CommunityApiClient.Submissions;
            if (submissions.Count == 0)
            {
                if (_statusText != null)
                    _statusText.text = "No pending community submissions.";
                return;
            }

            if (_statusText != null)
                _statusText.text = $"Pending Submissions ({submissions.Count})";

            foreach (var submission in submissions)
            {
                CreateSubmissionRow(submission);
            }
        }

        private void CreateSubmissionRow(JObject submission)
        {
            var title = submission["title"]?.ToString() ?? "Untitled";
            var author = submission["created_by"]?.ToString() ?? "Unknown";
            var upvotes = submission["upvotes"]?.ToObject<int>() ?? 0;
            var downvotes = submission["downvotes"]?.ToObject<int>() ?? 0;
            var ratio = submission["approval_ratio"]?.ToObject<float>() ?? 0f;
            var total = upvotes + downvotes;

            // Clone the ServiceItem template
            if (_serviceItemTemplate == null)
            {
                Plugin.Log.LogWarning("[TheQuartermaster] No ServiceItem template, cannot create submission row.");
                return;
            }

            var rowObj = Instantiate(_serviceItemTemplate, _submissionListContainer, false);
            rowObj.name = "SubmissionRow_" + (submission["id"]?.ToString() ?? "?");
            rowObj.SetActive(true);

            // Set the ServiceName text
            var serviceName = FindChild(rowObj.transform, "ServiceName");
            if (serviceName != null)
            {
                var nameText = serviceName.GetComponent<TextMeshProUGUI>();
                if (nameText != null)
                    nameText.text = $"{title}  by {author}  |  Support: {ratio:F0}% ({upvotes}/{total})";
            }

            // Hide SelectedArrow
            var arrow = FindChild(rowObj.transform, "SelectedArrow");
            if (arrow != null) arrow.gameObject.SetActive(false);

            // Hide ServiceIcon stuff
            var iconBg = FindChild(rowObj.transform, "ServiceIconBackground");
            if (iconBg != null) iconBg.gameObject.SetActive(false);

            // Wire up click to show details
            var btn = rowObj.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                AttachClickSound(rowObj, btn);
                btn.onClick.AddListener(() => ShowSubmissionDetails(submission));
            }
        }

        private void ShowSubmissionDetails(JObject submission)
        {
            _currentSubmission = submission;
            var title = submission["title"]?.ToString() ?? "Untitled";
            var author = submission["created_by"]?.ToString() ?? "Unknown";
            var description = submission["description"]?.ToString() ?? string.Empty;
            var upvotes = submission["upvotes"]?.ToObject<int>() ?? 0;
            var downvotes = submission["downvotes"]?.ToObject<int>() ?? 0;
            var ratio = submission["approval_ratio"]?.ToObject<float>() ?? 0f;
            var id = submission["id"]?.ToString() ?? string.Empty;

            // Update header with selected contract title
            if (_statusText != null)
                _statusText.text = $"Selected: {title}";

            // Populate the right-side details panel
            if (_rightSideText != null)
            {
                var objectives = submission["objectives"] as JArray;
                var objText = "";
                if (objectives != null && objectives.Count > 0)
                {
                    objText = "\n\nObjectives:";
                    foreach (var obj in objectives.OfType<JObject>())
                    {
                        var desc = obj["description"]?.ToString() ?? obj.ToString();
                        objText += $"\n- {desc}";
                    }
                }

                var rewards = submission["rewards"];
                var rewardText = FormatRewards(rewards);

                _rightSideText.text = $"{title}\nby {author}  |  Support: {ratio:F0}%  Up: {upvotes}  Down: {downvotes}\n\n{description}{objText}{rewardText}";
            }

            // Replace list with action buttons — all EFT style for consistency
            if (_submissionListContainer != null && _eftButtonTemplate != null)
            {
                for (int i = _submissionListContainer.childCount - 1; i >= 0; i--)
                {
                    var child = _submissionListContainer.GetChild(i);
                    if (child.gameObject == _serviceItemTemplate) continue;
                    Destroy(child.gameObject);
                }

                // Back button (EFT style)
                var backBtnObj = CreateEftButton(_submissionListContainer, "BackButton", "< Back to list", new Vector2(15, 0), new Vector2(0, 1));
                WireEftButtonClick(backBtnObj, () => { PopulateSubmissionList(); });

                // Upvote button (EFT style)
                var upvoteBtn = CreateEftButton(_submissionListContainer, "UpvoteButton", "Upvote", new Vector2(15, -45), new Vector2(0, 1));
                WireEftButtonClick(upvoteBtn, () =>
                {
                    if (_currentSubmission == null)
                        return;

                    if (!CommunityApiClient.IsLinked)
                    {
                        ShowErrorPopup("You must link Discord to vote.");
                        return;
                    }

                    var sid = _currentSubmission["id"]?.ToString() ?? "";
                    CommunityApiClient.CastVote(sid, true, result =>
                    {
                        RunOnMainThread(() =>
                        {
                            UpdateVoteFromResult(_currentSubmission, result);
                            ShowSubmissionDetails(_currentSubmission);
                        });
                    });
                    Plugin.Log.LogInfo($"[TheQuartermaster] Upvoted submission {sid}");
                });

                // Downvote button (EFT style, next to upvote)
                var downvoteBtn = CreateEftButton(_submissionListContainer, "DownvoteButton", "Downvote", new Vector2(145, -45), new Vector2(0, 1));
                WireEftButtonClick(downvoteBtn, () =>
                {
                    if (_currentSubmission == null)
                        return;

                    if (!CommunityApiClient.IsLinked)
                    {
                        ShowErrorPopup("You must link Discord to vote.");
                        return;
                    }

                    var sid = _currentSubmission["id"]?.ToString() ?? "";
                    CommunityApiClient.CastVote(sid, false, result =>
                    {
                        RunOnMainThread(() =>
                        {
                            UpdateVoteFromResult(_currentSubmission, result);
                            ShowSubmissionDetails(_currentSubmission);
                        });
                    });
                    Plugin.Log.LogInfo($"[TheQuartermaster] Downvoted submission {sid}");
                });

                // Link Discord / Unlink button (EFT style, below vote buttons)
                if (CommunityApiClient.IsLinked)
                {
                    var unlinkBtn = CreateEftButton(_submissionListContainer, "UnlinkButton",
                        "Unlink Discord",
                        new Vector2(15, -90), new Vector2(0, 1));
                    WireEftButtonClick(unlinkBtn, () =>
                    {
                        CommunityApiClient.Unlink();
                        ShowSubmissionDetails(_currentSubmission);
                        Plugin.Log.LogInfo("[TheQuartermaster] Unlinked Discord account.");
                    });
                    _linkButtonText = unlinkBtn.GetComponentInChildren<TextMeshProUGUI>(true);
                }
                else
                {
                    var linkBtn = CreateEftButton(_submissionListContainer, "LinkDiscordButton",
                        "Link Discord",
                        new Vector2(15, -90), new Vector2(0, 1));
                    WireEftButtonClick(linkBtn, OnLinkButtonClicked);
                    _linkButtonText = linkBtn.GetComponentInChildren<TextMeshProUGUI>(true);
                }

                Plugin.Log.LogInfo("[TheQuartermaster] Added Back, Upvote, Downvote, and Link/Unlink buttons to detail view.");
            }
        }

        private static void UpdateVoteFromResult(JObject submission, JObject result)
        {
            if (result["upvotes"] != null)
                submission["upvotes"] = result["upvotes"];
            if (result["downvotes"] != null)
                submission["downvotes"] = result["downvotes"];
            if (result["approval_ratio"] != null)
                submission["approval_ratio"] = result["approval_ratio"];
        }

        private static void WireEftButtonClick(GameObject btnObj, UnityEngine.Events.UnityAction callback)
        {
            var eftBtn = btnObj.GetComponent<DefaultUIButton>();
            if (eftBtn != null)
            {
                eftBtn.OnClick.AddListener(callback);
                return;
            }
            // Fallback to plain Button if available
            var btn = btnObj.GetComponent<Button>();
            if (btn == null)
                btn = btnObj.GetComponentInChildren<Button>(true);
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(callback);
            }
            else
            {
                Plugin.Log.LogWarning($"[TheQuartermaster] No clickable component found on '{btnObj.name}'.");
            }
        }

        private GameObject CreateEftButton(Transform parent, string name, string label, Vector2 pos, Vector2 anchor)
        {
            var btn = Instantiate(_eftButtonTemplate, parent, false);
            btn.name = name;
            btn.SetActive(true);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(120, 35);
            rt.anchoredPosition = pos;

            // Remove LayoutElement so our sizeDelta is respected
            var le = btn.GetComponent<LayoutElement>();
            if (le != null) Destroy(le);

            // Get the EFT button component and set raw text (no localization)
            var eftBtn = btn.GetComponent<DefaultUIButton>();
            if (eftBtn != null)
            {
                eftBtn.SetRawText(label, 18);
                eftBtn.Interactable = true;
            }
            else
            {
                Plugin.Log.LogWarning($"[TheQuartermaster] No DefaultUIButton on {name}.");
            }

            // Make sure every TMP label in the button states shows our label (fixes hover text showing old key paths)
            var allLabels = btn.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var lbl in allLabels)
                lbl.text = label;

            // Destroy ALL LocalizedText components recursively so they don't override our labels
            var allLocTexts = btn.GetComponentsInChildren<LocalizedText>(true);
            foreach (var lt in allLocTexts)
                Destroy(lt);

            // Hide extra children that aren't part of the button visual
            // Keep Default/Hover/Pressed/Disabled state containers and Background/Label
            var keep = new HashSet<string> { "Default", "Hover", "Pressed", "Disabled", "Background", "Label", "IconContainer" };
            for (int i = 0; i < btn.transform.childCount; i++)
            {
                var child = btn.transform.GetChild(i);
                if (!keep.Contains(child.name))
                {
                    child.gameObject.SetActive(false);
                    Plugin.Log.LogInfo($"[TheQuartermaster] Hidden extra child '{child.name}' on {name}.");
                }
            }

            // Inside state containers, hide TooltipTarget/Pattern/Icon if present
            foreach (Transform state in btn.transform)
            {
                HideNamedChildren(state, new[] { "TooltipTarget", "Pattern", "Icon", "SizeLabel" });
            }

            Plugin.Log.LogInfo($"[TheQuartermaster] Created EFT button '{name}' with label '{label}'.");
            return btn;
        }

        private GameObject CreateSimpleButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var img = go.GetComponent<Image>();
            img.color = new Color32(45, 45, 45, 230);

            var txtGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtGO.transform.SetParent(go.transform, false);
            var tRT = txtGO.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = Vector2.zero;
            tRT.offsetMax = Vector2.zero;

            var txt = txtGO.GetComponent<Text>();
            txt.text = label;
            txt.color = new Color32(220, 220, 220, 255);
            txt.alignment = TextAnchor.MiddleCenter;
            txt.fontSize = 14;
            if (_statusText != null)
                txt.font = _statusText.font;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor = new Color32(60, 60, 60, 255);
            cb.highlightedColor = new Color32(80, 80, 80, 255);
            cb.pressedColor = new Color32(40, 40, 40, 255);
            cb.disabledColor = new Color32(30, 30, 30, 128);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.1f;
            btn.colors = cb;

            AttachClickSound(go, btn);
            return go;
        }

        private void AttachClickSound(GameObject target, Button btn)
        {
            if (_eftButtonTemplate == null || btn == null)
                return;

            var sourceTemplate = _eftButtonTemplate.GetComponentInChildren<AudioSource>(true);
            if (sourceTemplate == null || sourceTemplate.clip == null)
                return;

            var source = target.GetComponent<AudioSource>();
            if (source == null)
            {
                source = target.AddComponent<AudioSource>();
                source.clip = sourceTemplate.clip;
                source.volume = sourceTemplate.volume;
                source.pitch = sourceTemplate.pitch;
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.outputAudioMixerGroup = sourceTemplate.outputAudioMixerGroup;
            }

            btn.onClick.AddListener(() =>
            {
                if (source != null)
                    source.Play();
            });
        }

        private static void HideNamedChildren(Transform t, string[] names)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (names.Contains(child.name))
                    child.gameObject.SetActive(false);
                HideNamedChildren(child, names);
            }
        }

        private static string FormatRewards(JToken rewards)
        {
            if (rewards == null)
                return string.Empty;

            var lines = new List<string>();

            if (rewards is JObject obj)
            {
                int exp = obj["experience"]?.ToObject<int>() ?? 0;
                if (exp > 0)
                    lines.Add($"+{exp:N0} EXP");

                int roubles = obj["roubles"]?.ToObject<int>() ?? 0;
                if (roubles > 0)
                    lines.Add($"+{roubles:N0} Roubles");

                var money = obj["money"];
                if (money != null)
                {
                    int amount = money["amount"]?.ToObject<int>() ?? 0;
                    string currency = money["currency"]?.ToString() ?? "RUB";
                    if (amount > 0)
                        lines.Add($"+{amount:N0} {currency}");
                }

                double standing = obj["trader_standing"]?.ToObject<double>() ?? 0.0;
                if (Math.Abs(standing) > 0.001)
                    lines.Add($"+{standing:F2} trader standing");

                var items = obj["items"] as JArray;
                if (items != null)
                {
                    foreach (var item in items.OfType<JObject>())
                    {
                        string tpl = item["tpl"]?.ToString() ?? "";
                        int count = item["count"]?.ToObject<int>() ?? 1;
                        bool fir = item["found_in_raid"]?.ToObject<bool>() ?? false;
                        string name = ResolveItemName(tpl);
                        lines.Add($"+{count}x {name}{(fir ? " (Found in Raid)" : "")}");
                    }
                }
            }
            else if (rewards is JArray arr)
            {
                foreach (var r in arr.OfType<JObject>())
                {
                    string type = r["type"]?.ToString() ?? "";
                    string value = r["value"]?.ToString() ?? "";

                    switch (type)
                    {
                        case "Experience":
                            if (int.TryParse(value, out int expv) && expv > 0)
                                lines.Add($"+{expv:N0} EXP");
                            break;
                        case "Item":
                            var itemsArr = r["items"] as JArray;
                            if (itemsArr != null)
                            {
                                bool findInRaid = r["findInRaid"]?.ToObject<bool>() ?? false;
                                foreach (var it in itemsArr.OfType<JObject>())
                                {
                                    string tpl = it["_tpl"]?.ToString() ?? "";
                                    int count = it["upd"]?["StackObjectsCount"]?.ToObject<int>() ?? 1;
                                    string name = ResolveItemName(tpl);
                                    lines.Add($"+{count}x {name}{(findInRaid ? " (Found in Raid)" : "")}");
                                }
                            }
                            break;
                        case "TraderStanding":
                            if (double.TryParse(value, out double st) && Math.Abs(st) > 0.001)
                                lines.Add($"+{st:F2} standing");
                            break;
                        case "Skill":
                            lines.Add($"Skill {r["target"]}: +{value}");
                            break;
                        case "Unlock":
                            lines.Add($"Unlock: {r["target"]}");
                            break;
                        default:
                            if (!string.IsNullOrEmpty(value))
                                lines.Add($"+{value} {type}");
                            break;
                    }
                }
            }

            if (lines.Count == 0)
                return string.Empty;

            return "\n\nRewards:\n" + string.Join("\n", lines.Select(l => "- " + l));
        }

        private static string ResolveItemName(string tpl)
        {
            if (string.IsNullOrWhiteSpace(tpl))
                return "item";

            if (_itemNameCache.TryGetValue(tpl, out var cached))
                return cached;

            var name = TryLocalize($"{tpl} Name");
            if (string.IsNullOrWhiteSpace(name))
                name = TryLocalize($"{tpl} ShortName");

            if (string.IsNullOrWhiteSpace(name))
                name = tpl;

            _itemNameCache[tpl] = name;
            return name;
        }

        private static readonly Dictionary<string, string> _itemNameCache = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> _localizationCache = new Dictionary<string, string>();
        private static MethodInfo _localizedMethod;

        private static string TryLocalize(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;
            if (_localizationCache.TryGetValue(key, out var cached))
                return cached;

            if (_localizedMethod == null)
            {
                _localizedMethod = FindStringLocalizedMethod();
                if (_localizedMethod == null)
                    return string.Empty;
            }

            string result = string.Empty;
            try
            {
                result = _localizedMethod.Invoke(null, new object[] { key, null }) as string ?? string.Empty;
            }
            catch { }

            _localizationCache[key] = result;
            return result;
        }

        private static MethodInfo FindStringLocalizedMethod()
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!((assembly.FullName?.StartsWith("Assembly-CSharp") == true) ||
                          (assembly.FullName?.StartsWith("EFT") == true) ||
                          (assembly.FullName?.StartsWith("SPT") == true)))
                        continue;

                    Type[] types;
                    try { types = assembly.GetTypes(); } catch { continue; }
                    foreach (var type in types)
                    {
                        if (!type.IsClass || type.IsAbstract)
                            continue;

                        var method = type.GetMethod("Localized", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
                            new[] { typeof(string), typeof(string) }, null);
                        if (method != null && method.ReturnType == typeof(string))
                            return method;
                    }
                }
            }
            catch { }
            return null;
        }

        private void ShowErrorPopup(string message)
        {
            NotificationManagerClass.DisplayWarningNotification(message);
        }

        private static void SetServiceItemText(GameObject row, string text)
        {
            var serviceName = FindChild(row.transform, "ServiceName");
            if (serviceName != null)
            {
                var nameText = serviceName.GetComponent<TextMeshProUGUI>();
                if (nameText != null)
                    nameText.text = text;
            }
        }

        private static void HideServiceItemDecorations(GameObject row)
        {
            var arrow = FindChild(row.transform, "SelectedArrow");
            if (arrow != null) arrow.gameObject.SetActive(false);
            var iconBg = FindChild(row.transform, "ServiceIconBackground");
            if (iconBg != null) iconBg.gameObject.SetActive(false);
        }

        private void OnRefreshButtonClicked()
        {
            _lastSubmissionsRefresh = -999f;
            TryRefreshSubmissions();
            PopulateSubmissionList();
        }

        private void OnLinkButtonClicked()
        {
            if (CommunityApiClient.IsLinked)
            {
                CommunityApiClient.RequestLinkCode();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(CommunityApiClient.LinkCode))
                    CommunityApiClient.RequestLinkCode();
                else
                    Application.OpenURL($"https://serenity-workshop.netlify.app/link?code={CommunityApiClient.LinkCode}");
            }
            UpdateLinkButton();
        }

        private void UpdateLinkButton()
        {
            if (_linkButtonText == null) return;
            if (CommunityApiClient.IsLinked)
                _linkButtonText.text = "Unlink Discord";
            else
                _linkButtonText.text = "Link Discord";
        }

        private static long UnixMs()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }
    }
}
