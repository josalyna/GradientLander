using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GradientLander
{
    [DisallowMultipleComponent]
    public sealed class LunarLanderController : MonoBehaviour
    {
        private enum RunState { Selecting, Ready, Flying, Reviewing, MissionComplete }

        private const string BankKey = "GradientLander.Bank";
        private const string DogSkinOwnedKey = "GradientLander.Skin.Dog.Owned";
        private const string CatSkinOwnedKey = "GradientLander.Skin.Cat.Owned";
        private const string EquippedSkinKey = "GradientLander.Skin.Equipped";
        private const float DogSkinPrice = 5f;
        private const float CatSkinPrice = 7.5f;

        private static readonly Color Ink = new Color32(235, 242, 250, 255);
        private static readonly Color Muted = new Color32(147, 164, 190, 255);
        private static readonly Color Panel = new Color32(15, 26, 49, 244);
        private static readonly Color PanelRaised = new Color32(24, 39, 66, 246);
        private static readonly Color Accent = new Color32(255, 184, 75, 255);
        private static readonly Color Cyan = new Color32(91, 205, 232, 255);
        private static readonly Color Green = new Color32(92, 214, 151, 255);
        private static readonly Color Red = new Color32(239, 91, 109, 255);

        private Font uiFont;
        private RectTransform gameRoot;
        private RectTransform selectionRoot;
        private RectTransform shopRoot;
        private RectTransform reviewPanel;
        private RectTransform fieldRect;
        private RectTransform landerRect;
        private LandingFieldGraphic fieldGraphic;
        private LanderGraphic landerGraphic;
        private InputField weightInput;
        private InputField biasInput;
        private Button launchButton;
        private Text launchButtonText;
        private Text helperText;
        private Text statusText;
        private Text telemetryText;
        private Text bankText;
        private Text resultBadgeText;
        private Image resultBadge;
        private Text resultsText;
        private Text budgetText;
        private Text historyText;
        private Text titleText;
        private Text missionHeadingText;
        private Text reviewTitleText;
        private Text reviewLegendText;
        private Text selectionBankText;
        private Text shopBankText;
        private Text shopMessageText;
        private Button classicSkinButton;
        private Button dogSkinButton;
        private Button catSkinButton;
        private readonly List<GameObject> gameplayChrome = new List<GameObject>();

        private RunState state;
        private int skippedFrames;
        private float currentX;
        private float currentWorldDepth;
        private float activeWeight;
        private float activeBias;
        private float flightElapsed;
        private float flightDuration;
        private float segmentStartX;
        private float segmentStartDepth;
        private float segmentEndX;
        private float segmentEndDepth;
        private float guidanceEndDepth;
        private float nextDepthBoundary;
        private float cumulativeCost;
        private float remainingBudget;
        private float bank;
        private int coinsCollected;
        private int nextGraphRouteStep;
        private bool graphRouteValid;
        private bool finalMissionSuccess;
        private MissionMode selectedMode = MissionMode.Random;
        private bool willLandOnPlatform;
        private bool willLandOnGround;
        private bool automatedCapture;
        private bool dogSkinOwned;
        private bool catSkinOwned;
        private ShipSkin equippedSkin;
        private MissionLayout missionLayout;
        private ThrustSolution activeThrust;
        private PlatformCollision activeCollision;
        private LanderRunResult currentResult;
        private readonly List<LanderRunResult> landingHistory = new List<LanderRunResult>();
        private readonly List<Vector2> flightPath = new List<Vector2>();
        private AudioSource audioSource;
        private AudioClip coinDing;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            bank = PlayerPrefs.GetFloat(BankKey, 0f);
            dogSkinOwned = PlayerPrefs.GetInt(DogSkinOwnedKey, 0) == 1;
            catSkinOwned = PlayerPrefs.GetInt(CatSkinOwnedKey, 0) == 1;
            equippedSkin = (ShipSkin)Mathf.Clamp(PlayerPrefs.GetInt(EquippedSkinKey, 0), 0, 2);
            if ((equippedSkin == ShipSkin.Dog && !dogSkinOwned) ||
                (equippedSkin == ShipSkin.Cat && !catSkinOwned)) equippedSkin = ShipSkin.Classic;
            automatedCapture = HasAutomatedCaptureArgument();
            audioSource = gameObject.AddComponent<AudioSource>();
            coinDing = CreateCoinDing();
            BuildInterface();
            if (HasArgument("--capture-selection") || HasArgument("--capture-shop")) ShowSelection();
            else if (automatedCapture) SelectMission(HasGraphCaptureArgument() ? MissionMode.GraphChallenge : MissionMode.Random);
            else ShowSelection();
            TryStartAutomatedCapture();
        }

        private void Update()
        {
            if (state == RunState.Flying)
            {
                flightElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(flightElapsed / flightDuration);
                UpdateFlightVisual(t);
                if (t >= 1f) FinishTraversal();
                return;
            }

            if (state == RunState.Ready &&
                (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) &&
                !weightInput.isFocused && !biasInput.isFocused)
            {
                BeginDescent();
            }
        }

        private void BuildInterface()
        {
            GameObject canvasObject = new GameObject("Gradient Lander UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;

            if (EventSystem.current == null)
            {
                GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                eventSystem.transform.SetParent(transform, false);
            }

            gameRoot = CreatePanel("Game Screen", canvasObject.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color32(5, 11, 25, 255));
            gameRoot.SetAsFirstSibling();
            BuildLandingField(gameRoot);
            BuildMissionPanel(gameRoot);
            BuildSelectionScreen(canvasObject.transform);
            BuildShopScreen(canvasObject.transform);
            BuildReviewControls();
        }

        private void BuildLandingField(RectTransform parent)
        {
            fieldRect = CreateRect("Long Descent Map", parent, new Vector2(.018f, .035f), new Vector2(.702f, .965f), Vector2.zero, Vector2.zero);
            fieldRect.gameObject.AddComponent<RectMask2D>();
            RectTransform mapLayer = CreateRect("Clipped Map Rendering", fieldRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            fieldGraphic = mapLayer.gameObject.AddComponent<LandingFieldGraphic>();
            fieldGraphic.raycastTarget = false;

            titleText = CreateText("Title", fieldRect, new Vector2(.03f, .928f), new Vector2(.59f, .987f), 34, FontStyle.Bold, TextAnchor.MiddleLeft, Ink);
            titleText.text = "GRADIENT LANDER: OPEN DESCENT";
            Text subtitle = CreateText("Subtitle", fieldRect, new Vector2(.032f, .868f), new Vector2(.72f, .935f), 15, FontStyle.Normal, TextAnchor.MiddleLeft, Muted);
            subtitle.text = "Descend to the lowest point and reach the ground target while minimizing total cost.\n" +
                            "Each platform landing calculates a cost and subtracts it from your available reward.";

            RectTransform statusPill = CreatePanel("Status", fieldRect, new Vector2(.755f, .925f), new Vector2(.97f, .975f), Vector2.zero, Vector2.zero, new Color32(31, 51, 78, 238));
            statusText = CreateText("Status Text", statusPill, new Vector2(.04f, .05f), new Vector2(.96f, .95f), 13, FontStyle.Bold, TextAnchor.MiddleCenter, Cyan);

            RectTransform controls = CreatePanel("Flight Controls", fieldRect, new Vector2(.03f, .765f), new Vector2(.97f, .885f), Vector2.zero, Vector2.zero, Panel);
            Text controlsLabel = CreateText("Controls Label", controls, new Vector2(.025f, .62f), new Vector2(.18f, .91f), 13, FontStyle.Bold, TextAnchor.MiddleLeft, Muted);
            controlsLabel.text = "FLIGHT INPUT";

            Text weightLabel = CreateText("Weight Label", controls, new Vector2(.205f, .58f), new Vector2(.32f, .91f), 14, FontStyle.Bold, TextAnchor.MiddleLeft, Ink);
            weightLabel.text = "Weight  w";
            weightInput = CreateInput("Weight Input", controls, new Vector2(.205f, .14f), new Vector2(.385f, .58f), ".80");

            Text biasLabel = CreateText("Bias Label", controls, new Vector2(.415f, .58f), new Vector2(.53f, .91f), 14, FontStyle.Bold, TextAnchor.MiddleLeft, Ink);
            biasLabel.text = "Base bias  b";
            biasInput = CreateInput("Bias Input", controls, new Vector2(.415f, .14f), new Vector2(.595f, .58f), ".55");

            launchButton = CreateButton("Launch Button", controls, new Vector2(.625f, .16f), new Vector2(.805f, .78f), Accent, new Color32(255, 203, 118, 255));
            launchButtonText = launchButton.GetComponentInChildren<Text>();
            launchButtonText.text = "DESCEND";
            launchButtonText.color = new Color32(18, 27, 43, 255);
            launchButton.onClick.AddListener(BeginDescent);

            Button resetButton = CreateButton("Reset Button", controls, new Vector2(.825f, .16f), new Vector2(.97f, .78f), new Color32(45, 64, 91, 255), new Color32(57, 80, 111, 255));
            Text resetLabel = resetButton.GetComponentInChildren<Text>();
            resetLabel.text = "RETRY MAP";
            resetLabel.color = Ink;
            resetButton.onClick.AddListener(ResetMission);

            helperText = CreateText("Helper", controls, new Vector2(.025f, .08f), new Vector2(.19f, .56f), 13, FontStyle.Normal, TextAnchor.MiddleLeft, Muted);
            helperText.text = "Enter -3 to 3\nthen descend";

            RectTransform telemetry = CreatePanel("Telemetry", fieldRect, new Vector2(.745f, .61f), new Vector2(.97f, .75f), Vector2.zero, Vector2.zero, new Color32(13, 24, 46, 228));
            Text telemetryHeader = CreateText("Telemetry Header", telemetry, new Vector2(.08f, .74f), new Vector2(.92f, .93f), 13, FontStyle.Bold, TextAnchor.MiddleLeft, Cyan);
            telemetryHeader.text = "LIVE TELEMETRY";
            telemetryText = CreateText("Telemetry Values", telemetry, new Vector2(.08f, .08f), new Vector2(.92f, .74f), 15, FontStyle.Normal, TextAnchor.UpperLeft, Ink);
            telemetryText.supportRichText = true;

            RectTransform lesson = CreatePanel("Collision Note", fieldRect, new Vector2(.03f, .16f), new Vector2(.36f, .285f), Vector2.zero, Vector2.zero, new Color32(16, 30, 53, 224));
            Text lessonTitle = CreateText("Collision Title", lesson, new Vector2(.07f, .62f), new Vector2(.93f, .91f), 13, FontStyle.Bold, TextAnchor.MiddleLeft, Accent);
            lessonTitle.text = "OPTIONAL LANDINGS - BUDGET 4.0000";
            Text lessonBody = CreateText("Collision Body", lesson, new Vector2(.07f, .08f), new Vector2(.93f, .63f), 13, FontStyle.Normal, TextAnchor.UpperLeft, Muted);
            lessonBody.text = "Only platform overlaps stop the ship and deduct cost. Otherwise the same trajectory scrolls continuously to the next landing.";

            gameplayChrome.Add(titleText.gameObject);
            gameplayChrome.Add(subtitle.gameObject);
            gameplayChrome.Add(statusPill.gameObject);
            gameplayChrome.Add(controls.gameObject);
            gameplayChrome.Add(telemetry.gameObject);
            gameplayChrome.Add(lesson.gameObject);

            landerRect = CreateRect("Lander", fieldRect, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-60f, -80f), new Vector2(60f, 80f));
            landerGraphic = landerRect.gameObject.AddComponent<LanderGraphic>();
            landerGraphic.raycastTarget = false;
        }

        private void BuildMissionPanel(RectTransform parent)
        {
            RectTransform sidebar = CreatePanel("Mission Ledger", parent, new Vector2(.716f, .035f), new Vector2(.982f, .965f), Vector2.zero, Vector2.zero, Panel);
            Text eyebrow = CreateText("Eyebrow", sidebar, new Vector2(.07f, .92f), new Vector2(.57f, .97f), 13, FontStyle.Bold, TextAnchor.MiddleLeft, Cyan);
            eyebrow.text = "MISSION LEDGER";
            bankText = CreateText("Bank", sidebar, new Vector2(.53f, .92f), new Vector2(.93f, .97f), 13, FontStyle.Bold, TextAnchor.MiddleRight, Accent);

            missionHeadingText = CreateText("Heading", sidebar, new Vector2(.07f, .845f), new Vector2(.93f, .925f), 30, FontStyle.Bold, TextAnchor.MiddleLeft, Ink);
            missionHeadingText.text = "Open Descent";

            resultBadge = CreatePanel("Result Badge", sidebar, new Vector2(.07f, .795f), new Vector2(.93f, .845f), Vector2.zero, Vector2.zero, new Color32(37, 55, 80, 255)).GetComponent<Image>();
            resultBadgeText = CreateText("Result Badge Text", resultBadge.transform, new Vector2(.04f, .05f), new Vector2(.96f, .95f), 14, FontStyle.Bold, TextAnchor.MiddleCenter, Muted);

            resultsText = CreateText("Landing Results", sidebar, new Vector2(.07f, .47f), new Vector2(.93f, .785f), 16, FontStyle.Normal, TextAnchor.UpperLeft, Ink);
            resultsText.supportRichText = true;
            resultsText.lineSpacing = 1.10f;

            RectTransform budgetCard = CreatePanel("Budget Card", sidebar, new Vector2(.07f, .34f), new Vector2(.93f, .455f), Vector2.zero, Vector2.zero, PanelRaised);
            budgetText = CreateText("Budget Values", budgetCard, new Vector2(.05f, .08f), new Vector2(.95f, .92f), 14, FontStyle.Normal, TextAnchor.MiddleLeft, Ink);
            budgetText.supportRichText = true;

            Text historyLabel = CreateText("History Label", sidebar, new Vector2(.07f, .285f), new Vector2(.93f, .33f), 13, FontStyle.Bold, TextAnchor.MiddleLeft, Cyan);
            historyLabel.text = "LANDING HISTORY";
            historyText = CreateText("History", sidebar, new Vector2(.07f, .08f), new Vector2(.93f, .285f), 14, FontStyle.Normal, TextAnchor.UpperLeft, Muted);
            historyText.supportRichText = true;
            historyText.lineSpacing = 1.28f;

            Text footer = CreateText("Footer", sidebar, new Vector2(.07f, .02f), new Vector2(.93f, .07f), 12, FontStyle.Normal, TextAnchor.MiddleCenter, new Color32(97, 115, 143, 255));
            footer.text = "Only completed landings add cost";
        }

        private void BuildSelectionScreen(Transform canvas)
        {
            selectionRoot = CreatePanel("Map Selection", canvas, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color32(5, 11, 25, 255));
            Text title = CreateText("Selection Title", selectionRoot, new Vector2(.08f, .86f), new Vector2(.92f, .96f), 45, FontStyle.Bold, TextAnchor.MiddleCenter, Ink);
            title.text = "CHOOSE A DESCENT MAP";
            Text intro = CreateText("Selection Intro", selectionRoot, new Vector2(.16f, .79f), new Vector2(.84f, .87f), 18, FontStyle.Normal, TextAnchor.MiddleCenter, Muted);
            intro.text = "Test a known gradient-descent landscape or explore a map with no predetermined solution.";

            BuildMapCard(selectionRoot, new Vector2(.08f, .12f), new Vector2(.47f, .77f),
                MissionMode.GraphChallenge, "QUARTIC VALLEY", "PREDETERMINED GRADIENT PATH",
                "Three checkpoint coins mark exact gradient-descent steps.\nThe solution stays hidden until the route review.", "SELECT CHALLENGE");
            BuildMapCard(selectionRoot, new Vector2(.53f, .12f), new Vector2(.92f, .77f),
                MissionMode.Random, "RANDOM", "NO PREDETERMINED PATH",
                "Platforms and the ground target are randomized.\nExperiment freely with weight and bias.", "SELECT RANDOM");

            selectionBankText = CreateText("Selection Bank", selectionRoot, new Vector2(.34f, .04f), new Vector2(.62f, .09f), 16, FontStyle.Bold, TextAnchor.MiddleCenter, Accent);
            selectionBankText.text = $"CURRENT BANK  {bank:0.0000}";

            Button shop = CreateButton("Open Skin Shop", selectionRoot, new Vector2(.73f, .035f), new Vector2(.91f, .095f),
                new Color32(137, 75, 150, 255), new Color32(167, 94, 181, 255));
            shop.GetComponentInChildren<Text>().text = "SHIP SKIN SHOP";
            shop.onClick.AddListener(ShowShop);
        }

        private void BuildShopScreen(Transform canvas)
        {
            shopRoot = CreatePanel("Ship Skin Shop", canvas, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color32(5, 11, 25, 255));
            Text title = CreateText("Shop Title", shopRoot, new Vector2(.08f, .87f), new Vector2(.72f, .96f), 45, FontStyle.Bold, TextAnchor.MiddleLeft, Ink);
            title.text = "SHIP SKIN SHOP";
            Text intro = CreateText("Shop Intro", shopRoot, new Vector2(.08f, .80f), new Vector2(.75f, .87f), 17, FontStyle.Normal, TextAnchor.MiddleLeft, Muted);
            intro.text = "Spend banked landing rewards on a new look. Purchased skins stay unlocked.";
            shopBankText = CreateText("Shop Bank", shopRoot, new Vector2(.72f, .88f), new Vector2(.92f, .95f), 18, FontStyle.Bold, TextAnchor.MiddleRight, Accent);

            classicSkinButton = BuildSkinCard(shopRoot, new Vector2(.055f, .17f), new Vector2(.315f, .77f),
                ShipSkin.Classic, "CLASSIC", "ORIGINAL LANDER", "The standard silver gradient lander.", 0f);
            dogSkinButton = BuildSkinCard(shopRoot, new Vector2(.37f, .17f), new Vector2(.63f, .77f),
                ShipSkin.Dog, "SPOTTY DOG", "DOG EARS + POLKA DOTS", "Floppy dog ears, a warm cream hull, and playful spots.", DogSkinPrice);
            catSkinButton = BuildSkinCard(shopRoot, new Vector2(.685f, .17f), new Vector2(.945f, .77f),
                ShipSkin.Cat, "COMET CAT", "CAT EARS + TAIL", "Pointed cat ears, a lavender hull, and a curled tail.", CatSkinPrice);

            shopMessageText = CreateText("Shop Message", shopRoot, new Vector2(.23f, .07f), new Vector2(.77f, .14f), 16, FontStyle.Bold, TextAnchor.MiddleCenter, Muted);
            Button back = CreateButton("Back to Maps", shopRoot, new Vector2(.055f, .055f), new Vector2(.205f, .125f),
                new Color32(45, 64, 91, 255), new Color32(57, 80, 111, 255));
            back.GetComponentInChildren<Text>().text = "BACK TO MAPS";
            back.onClick.AddListener(ShowSelection);
            shopRoot.gameObject.SetActive(false);
        }

        private Button BuildSkinCard(
            Transform parent, Vector2 min, Vector2 max, ShipSkin skin,
            string name, string tag, string description, float price)
        {
            RectTransform card = CreatePanel(name + " Skin Card", parent, min, max, Vector2.zero, Vector2.zero, Panel);
            Text heading = CreateText("Skin Name", card, new Vector2(.07f, .86f), new Vector2(.93f, .96f), 27, FontStyle.Bold, TextAnchor.MiddleCenter, Ink);
            heading.text = name;
            Text label = CreateText("Skin Tag", card, new Vector2(.07f, .79f), new Vector2(.93f, .86f), 12, FontStyle.Bold, TextAnchor.MiddleCenter, skin == ShipSkin.Classic ? Cyan : Accent);
            label.text = tag;

            RectTransform preview = CreatePanel("Skin Preview", card, new Vector2(.12f, .38f), new Vector2(.88f, .77f), Vector2.zero, Vector2.zero, new Color32(8, 18, 36, 255));
            RectTransform ship = CreateRect("Preview Lander", preview, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-60f, -80f), new Vector2(60f, 80f));
            LanderGraphic graphic = ship.gameObject.AddComponent<LanderGraphic>();
            graphic.raycastTarget = false;
            graphic.SetSkin(skin);
            graphic.SetState(.55f, .45f, false, 0f);

            Text body = CreateText("Skin Description", card, new Vector2(.08f, .20f), new Vector2(.92f, .36f), 14, FontStyle.Normal, TextAnchor.UpperCenter, Muted);
            body.text = description + (price > 0f ? $"\n\nPRICE  {price:0.0000}" : "\n\nALWAYS AVAILABLE");
            Button action = CreateButton("Skin Action", card, new Vector2(.08f, .06f), new Vector2(.92f, .17f),
                skin == ShipSkin.Classic ? new Color32(34, 121, 151, 255) : new Color32(137, 75, 150, 255),
                skin == ShipSkin.Classic ? new Color32(48, 151, 181, 255) : new Color32(167, 94, 181, 255));
            action.onClick.AddListener(() => HandleSkinAction(skin, price));
            return action;
        }

        private void BuildMapCard(
            Transform parent, Vector2 min, Vector2 max, MissionMode mode,
            string name, string tag, string description, string buttonLabel)
        {
            RectTransform card = CreatePanel(name + " Card", parent, min, max, Vector2.zero, Vector2.zero, Panel);
            Text heading = CreateText("Map Name", card, new Vector2(.07f, .85f), new Vector2(.93f, .95f), 29, FontStyle.Bold, TextAnchor.MiddleLeft, Ink);
            heading.text = name;
            Text label = CreateText("Map Tag", card, new Vector2(.07f, .79f), new Vector2(.93f, .86f), 13, FontStyle.Bold, TextAnchor.MiddleLeft, mode == MissionMode.GraphChallenge ? Accent : Cyan);
            label.text = tag;
            RectTransform previewRect = CreateRect("Graph Preview", card, new Vector2(.07f, .34f), new Vector2(.93f, .76f), Vector2.zero, Vector2.zero);
            MapPreviewGraphic preview = previewRect.gameObject.AddComponent<MapPreviewGraphic>();
            preview.Mode = mode;
            preview.raycastTarget = false;
            Text body = CreateText("Map Description", card, new Vector2(.07f, .19f), new Vector2(.93f, .32f), 15, FontStyle.Normal, TextAnchor.UpperLeft, Muted);
            body.text = description;
            Button choose = CreateButton("Choose " + name, card, new Vector2(.07f, .06f), new Vector2(.93f, .17f),
                mode == MissionMode.GraphChallenge ? new Color32(190, 121, 48, 255) : new Color32(34, 121, 151, 255),
                mode == MissionMode.GraphChallenge ? new Color32(222, 151, 68, 255) : new Color32(48, 151, 181, 255));
            choose.GetComponentInChildren<Text>().text = buttonLabel;
            choose.onClick.AddListener(() => SelectMission(mode));
        }

        private void BuildReviewControls()
        {
            reviewPanel = CreatePanel("Route Review", fieldRect, new Vector2(.03f, .03f), new Vector2(.97f, .97f), Vector2.zero, Vector2.zero, new Color32(7, 15, 30, 18));
            reviewTitleText = CreateText("Review Title", reviewPanel, new Vector2(.04f, .91f), new Vector2(.96f, .98f), 28, FontStyle.Bold, TextAnchor.MiddleLeft, Ink);
            reviewLegendText = CreateText("Review Legend", reviewPanel, new Vector2(.04f, .84f), new Vector2(.74f, .91f), 15, FontStyle.Bold, TextAnchor.MiddleLeft, Muted);
            Button retry = CreateButton("Try Again", reviewPanel, new Vector2(.60f, .025f), new Vector2(.78f, .095f), Accent, new Color32(255, 203, 118, 255));
            retry.GetComponentInChildren<Text>().text = "TRY AGAIN";
            retry.GetComponentInChildren<Text>().color = new Color32(18, 27, 43, 255);
            retry.onClick.AddListener(RetrySelectedMap);
            Button maps = CreateButton("Select Map", reviewPanel, new Vector2(.80f, .025f), new Vector2(.97f, .095f), new Color32(45, 64, 91, 255), new Color32(57, 80, 111, 255));
            maps.GetComponentInChildren<Text>().text = "SELECT MAP";
            maps.onClick.AddListener(ShowSelection);
            reviewPanel.gameObject.SetActive(false);
        }

        private void SelectMission(MissionMode mode)
        {
            selectedMode = mode;
            selectionRoot.gameObject.SetActive(false);
            if (shopRoot != null) shopRoot.gameObject.SetActive(false);
            gameRoot.gameObject.SetActive(true);
            ShowGameplayChrome(true);
            reviewPanel.gameObject.SetActive(false);
            ResetMission();
        }

        private void RetrySelectedMap()
        {
            ShowGameplayChrome(true);
            reviewPanel.gameObject.SetActive(false);
            landerRect.gameObject.SetActive(true);
            ResetMission();
        }

        private void ShowSelection()
        {
            StopAllCoroutines();
            state = RunState.Selecting;
            if (gameRoot != null) gameRoot.gameObject.SetActive(false);
            if (shopRoot != null) shopRoot.gameObject.SetActive(false);
            if (selectionRoot != null)
            {
                if (selectionBankText != null) selectionBankText.text = $"CURRENT BANK  {bank:0.0000}";
                selectionRoot.gameObject.SetActive(true);
                selectionRoot.SetAsLastSibling();
            }
        }

        private void ShowShop()
        {
            StopAllCoroutines();
            state = RunState.Selecting;
            if (gameRoot != null) gameRoot.gameObject.SetActive(false);
            if (selectionRoot != null) selectionRoot.gameObject.SetActive(false);
            RefreshShop("Choose a skin to purchase or equip.");
            shopRoot.gameObject.SetActive(true);
            shopRoot.SetAsLastSibling();
        }

        private void HandleSkinAction(ShipSkin skin, float price)
        {
            bool owned = skin == ShipSkin.Classic ||
                         (skin == ShipSkin.Dog && dogSkinOwned) ||
                         (skin == ShipSkin.Cat && catSkinOwned);
            if (!owned)
            {
                if (bank + .00005f < price)
                {
                    RefreshShop($"You need {price - bank:0.0000} more banked reward for {GetSkinName(skin)}.");
                    return;
                }

                bank -= price;
                if (skin == ShipSkin.Dog)
                {
                    dogSkinOwned = true;
                    PlayerPrefs.SetInt(DogSkinOwnedKey, 1);
                }
                else if (skin == ShipSkin.Cat)
                {
                    catSkinOwned = true;
                    PlayerPrefs.SetInt(CatSkinOwnedKey, 1);
                }
                PlayerPrefs.SetFloat(BankKey, bank);
            }

            equippedSkin = skin;
            PlayerPrefs.SetInt(EquippedSkinKey, (int)equippedSkin);
            PlayerPrefs.Save();
            if (landerGraphic != null) landerGraphic.SetSkin(equippedSkin);
            RefreshShop(owned ? $"{GetSkinName(skin)} equipped." : $"{GetSkinName(skin)} purchased and equipped.");
        }

        private void RefreshShop(string message)
        {
            if (shopBankText != null) shopBankText.text = $"BANK  {bank:0.0000}";
            if (selectionBankText != null) selectionBankText.text = $"CURRENT BANK  {bank:0.0000}";
            UpdateSkinButton(classicSkinButton, ShipSkin.Classic, true, 0f);
            UpdateSkinButton(dogSkinButton, ShipSkin.Dog, dogSkinOwned, DogSkinPrice);
            UpdateSkinButton(catSkinButton, ShipSkin.Cat, catSkinOwned, CatSkinPrice);
            if (shopMessageText != null) shopMessageText.text = message;
        }

        private void UpdateSkinButton(Button button, ShipSkin skin, bool owned, float price)
        {
            if (button == null) return;
            Text label = button.GetComponentInChildren<Text>();
            if (equippedSkin == skin)
            {
                label.text = "EQUIPPED";
                button.interactable = false;
            }
            else if (owned)
            {
                label.text = "EQUIP";
                button.interactable = true;
            }
            else
            {
                label.text = bank + .00005f >= price ? $"BUY  {price:0.0000}" : $"NEED  {price:0.0000}";
                button.interactable = bank + .00005f >= price;
            }
        }

        private static string GetSkinName(ShipSkin skin)
        {
            return skin == ShipSkin.Dog ? "Spotty Dog" : skin == ShipSkin.Cat ? "Comet Cat" : "Classic";
        }

        private void ShowGameplayChrome(bool visible)
        {
            for (int i = 0; i < gameplayChrome.Count; i++) gameplayChrome[i].SetActive(visible);
        }

        private void BeginDescent()
        {
            if (state != RunState.Ready) return;
            if (!TryReadNumber(weightInput.text, out activeWeight) || !TryReadNumber(biasInput.text, out activeBias))
            {
                SetInputMessage("Enter valid numbers for both w and b.", Red);
                return;
            }
            if (Mathf.Abs(activeWeight) > 3f || Mathf.Abs(activeBias) > 3f)
            {
                SetInputMessage("Keep both values between -3 and 3.", Red);
                return;
            }

            SetInputsEnabled(false);
            helperText.text = "Inputs remain locked\nuntil a landing";
            helperText.color = Muted;
            StartContinuousTraversal();
        }

        private void StartContinuousTraversal()
        {
            segmentStartX = currentX;
            segmentStartDepth = currentWorldDepth;
            float guidanceTarget = LanderMath.GetGuidanceTarget(missionLayout, currentWorldDepth);
            guidanceEndDepth = LanderMath.GetGuidanceDepth(missionLayout, currentWorldDepth);
            activeThrust = LanderMath.CalculateThrust(activeWeight, activeBias, currentX, guidanceTarget);
            willLandOnPlatform = LanderMath.TryFindNextPlatformCollision(
                missionLayout, currentWorldDepth, currentX, activeThrust.EndX, guidanceEndDepth, out activeCollision);
            willLandOnGround = !willLandOnPlatform;

            if (willLandOnPlatform)
            {
                segmentEndX = activeCollision.ShipX;
                segmentEndDepth = activeCollision.WorldDepth;
            }
            else
            {
                segmentEndX = activeThrust.EndX;
                segmentEndDepth = missionLayout.TotalDepth;
            }

            float depthSpan = segmentEndDepth - segmentStartDepth;
            flightDuration = Mathf.Max(.85f, depthSpan * 2.35f) *
                             Mathf.Lerp(.92f, 1.12f, Mathf.Clamp01(Mathf.Abs(activeThrust.TotalPower) / 3f));
            flightElapsed = 0f;
            nextDepthBoundary = Mathf.Floor(segmentStartDepth + .0001f) + 1f;
            state = RunState.Flying;
            launchButtonText.text = "DESCENDING";
            statusText.text = "CONTINUOUS DESCENT ACTIVE";
            statusText.color = Accent;
            resultBadge.color = new Color32(50, 58, 77, 255);
            resultBadgeText.text = willLandOnPlatform ? $"ALIGNMENT FOUND: {activeCollision.Platform.Label}" : "NO PLATFORM ON CURRENT PATH";
            resultBadgeText.color = willLandOnPlatform ? Green : Accent;
            resultsText.text = BuildPendingResults();
            UpdateFlightVisual(0f);
        }

        private void UpdateFlightVisual(float t)
        {
            float eased = LanderMath.SmoothStep(t);
            float depth = Mathf.Lerp(segmentStartDepth, segmentEndDepth, eased);
            float x = LanderMath.EvaluatePathX(
                segmentStartX, activeThrust.EndX, segmentStartDepth, guidanceEndDepth, depth);
            RecordFlightPoint(x, depth);
            float y = LanderMath.GetShipScreenY(depth) + Mathf.Sin(t * Mathf.PI) * .012f;
            SetLanderPosition(x, y);
            float direction = Mathf.Sign(activeThrust.EndX - segmentStartX);
            float turn = -direction * Mathf.Sin(t * Mathf.PI) * Mathf.Min(16f, Mathf.Abs(activeThrust.EndX - segmentStartX) * 42f);
            float wobble = Mathf.Sin(t * Mathf.PI * 7f) * (1f - t) * 1.5f;
            landerRect.localRotation = Quaternion.Euler(0f, 0f, turn + wobble);
            float flamePulse = (Mathf.Sin(Time.time * 15f) + 1f) * .5f;
            landerGraphic.SetState(activeThrust.LeftPower, activeThrust.RightPower, true, flamePulse);
            fieldGraphic.SetFlight(missionLayout, depth, segmentStartDepth, segmentEndDepth,
                segmentStartX, activeThrust.EndX, guidanceEndDepth, t);

            while (nextDepthBoundary < missionLayout.TotalDepth && depth >= nextDepthBoundary)
            {
                skippedFrames++;
                nextDepthBoundary += 1f;
            }

            telemetryText.text =
                $"<color=#{ToHex(Muted)}>LEFT COMMAND</color>   {activeThrust.LeftPower,7:0.000}\n" +
                $"<color=#{ToHex(Muted)}>RIGHT COMMAND</color>  {activeThrust.RightPower,7:0.000}\n" +
                $"<color=#{ToHex(Muted)}>HORIZONTAL X</color>   {x,7:0.000}";
        }

        private void FinishTraversal()
        {
            currentX = segmentEndX;
            currentWorldDepth = segmentEndDepth;
            RecordFlightPoint(currentX, currentWorldDepth, true);
            landerRect.localRotation = Quaternion.identity;
            landerGraphic.SetState(activeThrust.LeftPower, activeThrust.RightPower, false, 0f);
            SetLanderPosition(currentX, LanderMath.GetShipScreenY(currentWorldDepth));

            if (willLandOnPlatform)
            {
                currentResult = LanderMath.ScorePlatformLanding(activeThrust, activeCollision);
                bool collectedCoin = TryCollectCheckpointCoin(activeCollision);
                if (selectedMode == MissionMode.GraphChallenge)
                {
                    bool correctNextStop = activeCollision.Platform.IsCorrectRoute &&
                                           activeCollision.Platform.RouteStep == nextGraphRouteStep;
                    bool checkpointSatisfied = !activeCollision.Platform.IsCheckpoint || collectedCoin;
                    if (correctNextStop && checkpointSatisfied) nextGraphRouteStep++;
                    else graphRouteValid = false;
                }
                RegisterLanding(currentResult);
                PauseOnPlatform();
                return;
            }

            if (willLandOnGround)
            {
                currentResult = LanderMath.ScoreGroundLanding(activeThrust, missionLayout);
                RegisterLanding(currentResult);
                CompleteMission();
                return;
            }

            // Every uninterrupted flight resolves at a platform or at the ground.
        }

        private void PauseOnPlatform()
        {
            state = RunState.Ready;
            fieldGraphic.SetReady(missionLayout, currentWorldDepth, currentX);
            SetInputsEnabled(true);
            launchButtonText.text = "CONTINUE";
            helperText.text = activeCollision.Platform.IsCheckpoint
                ? "Checkpoint cost deducted\nretune for the next step"
                : "Platform cost deducted\nretune or continue";
            helperText.color = Muted;
            statusText.text = $"LANDED ON {activeCollision.Platform.Label}";
            statusText.color = Green;
            resultBadge.color = new Color32(28, 82, 71, 255);
            bool coin = activeCollision.Platform.HasCoin &&
                        activeCollision.Platform.CoinIndex >= 0 &&
                        missionLayout.CoinCollected[activeCollision.Platform.CoinIndex];
            resultBadgeText.text = coin
                ? $"{activeCollision.Platform.Label}: COIN SECURED"
                : $"PLATFORM LANDING: {activeCollision.Platform.Label}";
            resultBadgeText.color = Green;
            resultsText.text = BuildLandingResults(currentResult, false);
            UpdateLedger();
        }

        private void RegisterLanding(LanderRunResult result)
        {
            cumulativeCost += result.Cost;
            remainingBudget = Mathf.Max(0f, LanderMath.MissionBudget - cumulativeCost);
            landingHistory.Add(result);
            budgetText.text = BuildBudgetText(false, 0f);
            UpdateHistory();
            RefreshTelemetry(currentX);
        }

        private bool TryCollectCheckpointCoin(PlatformCollision collision)
        {
            PlatformDefinition platform = collision.Platform;
            if (!platform.HasCoin || platform.CoinIndex < 0 ||
                platform.CoinIndex >= missionLayout.CoinCollected.Length ||
                missionLayout.CoinCollected[platform.CoinIndex]) return false;
            if (Mathf.Abs(collision.ShipX - platform.CoinX) > LanderMath.CoinTolerance) return false;

            missionLayout.CoinCollected[platform.CoinIndex] = true;
            coinsCollected++;
            if (audioSource != null && coinDing != null) audioSource.PlayOneShot(coinDing, .85f);
            return true;
        }

        private void RecordFlightPoint(float x, float depth, bool force = false)
        {
            Vector2 point = new Vector2(x, depth);
            if (!force && flightPath.Count > 0 && Vector2.Distance(flightPath[flightPath.Count - 1], point) < .025f) return;
            flightPath.Add(point);
        }

        private void CompleteMission()
        {
            state = RunState.Reviewing;
            bool targetHit = currentResult.HitFinalTarget &&
                             (selectedMode != MissionMode.GraphChallenge ||
                              (graphRouteValid && nextGraphRouteStep == LanderMath.GraphLandingCount));
            finalMissionSuccess = targetHit;
            float coinBonus = coinsCollected * LanderMath.CoinReward;
            float reward = targetHit ? remainingBudget + coinBonus : 0f;
            if (targetHit)
            {
                bank += reward;
                if (!automatedCapture)
                {
                    PlayerPrefs.SetFloat(BankKey, bank);
                    PlayerPrefs.Save();
                }
            }

            SetInputsEnabled(false);
            launchButtonText.text = "MISSION COMPLETE";
            helperText.text = targetHit ? "Reward deposited\nin the bank" : "No bank deposit\nfor this mission";
            helperText.color = targetHit ? Green : Red;
            fieldGraphic.SetComplete(missionLayout, currentWorldDepth, currentX);
            bankText.text = $"BANK  {bank:0.0000}";
            resultsText.text = BuildLandingResults(currentResult, true);
            budgetText.text = BuildBudgetText(targetHit, reward);

            if (targetHit)
            {
                statusText.text = "TARGET HIT - REWARD BANKED";
                statusText.color = Green;
                resultBadge.color = new Color32(28, 82, 71, 255);
                resultBadgeText.text = $"SUCCESS: +{reward:0.0000} BANKED";
                resultBadgeText.color = Green;
            }
            else
            {
                statusText.text = selectedMode == MissionMode.GraphChallenge && currentResult.HitFinalTarget
                    ? "GROUND REACHED - CORRECT ROUTE MISSED"
                    : "GROUND REACHED - TARGET MISSED";
                statusText.color = Red;
                resultBadge.color = new Color32(83, 39, 54, 255);
                resultBadgeText.text = selectedMode == MissionMode.GraphChallenge && currentResult.HitFinalTarget
                    ? "INVALID GRADIENT ROUTE: NO DEPOSIT"
                    : "MISSION COMPLETE: NO DEPOSIT";
                resultBadgeText.color = Red;
            }
            StartCoroutine(ShowRouteReview(targetHit, reward));
        }

        private IEnumerator ShowRouteReview(bool targetHit, float reward)
        {
            yield return new WaitForSecondsRealtime(.65f);
            ShowGameplayChrome(false);
            landerRect.gameObject.SetActive(false);
            reviewPanel.gameObject.SetActive(true);
            reviewTitleText.text = targetHit
                ? $"ROUTE REVIEW  -  {missionLayout.DisplayName.ToUpperInvariant()}  -  +{reward:0.0000} BANKED"
                : $"ROUTE REVIEW  -  {missionLayout.DisplayName.ToUpperInvariant()}  -  TARGET MISSED";
            reviewLegendText.text = missionLayout.Mode == MissionMode.GraphChallenge
                ? "<color=#5BCDE8>SHIP PATH</color>     <color=#FFB84B>CORRECT GRADIENT PATH</color>     Coins collected " + coinsCollected + "/3"
                : "<color=#5BCDE8>SHIP PATH</color>     Random maps have no predetermined solution";
            reviewLegendText.supportRichText = true;

            const float duration = 1.35f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                fieldGraphic.SetReview(missionLayout, flightPath, missionLayout.CorrectPath, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            fieldGraphic.SetReview(missionLayout, flightPath, missionLayout.CorrectPath, 1f);
            state = RunState.MissionComplete;
        }

        private void ResetMission()
        {
            state = RunState.Ready;
            skippedFrames = 0;
            missionLayout = selectedMode == MissionMode.GraphChallenge
                ? LanderMath.GenerateGraphChallengeLayout()
                : LanderMath.GenerateRandomLayout(automatedCapture
                    ? LanderMath.DemonstrationSeed
                    : unchecked(System.Environment.TickCount ^ System.Guid.NewGuid().GetHashCode()));
            currentX = missionLayout.StartX;
            currentWorldDepth = 0f;
            cumulativeCost = 0f;
            remainingBudget = LanderMath.MissionBudget;
            coinsCollected = 0;
            nextGraphRouteStep = 0;
            graphRouteValid = true;
            finalMissionSuccess = false;
            landingHistory.Clear();
            flightPath.Clear();
            flightPath.Add(new Vector2(currentX, 0f));
            fieldGraphic.SetPersistentPath(flightPath);
            if (weightInput != null) weightInput.text = selectedMode == MissionMode.GraphChallenge ? "1.00" : ".80";
            if (biasInput != null) biasInput.text = selectedMode == MissionMode.GraphChallenge ? ".25" : ".55";
            SetInputsEnabled(true);
            launchButtonText.text = "DESCEND";
            helperText.text = "Enter -3 to 3\nthen descend";
            helperText.color = Muted;
            statusText.text = "HIGH ALTITUDE - READY";
            statusText.color = Cyan;
            resultBadge.color = new Color32(37, 55, 80, 255);
            resultBadgeText.text = selectedMode == MissionMode.GraphChallenge
                ? "HIDDEN GRADIENT ROUTE READY"
                : "RANDOMIZED DESCENT READY";
            resultBadgeText.color = Muted;
            resultsText.text = BuildEmptyResults();
            historyText.text = selectedMode == MissionMode.GraphChallenge
                ? "No landings yet.\n\nCheckpoint coins hint at the hidden gradient route."
                : "No landings yet.\n\nMissing every platform automatically advances the descent.";
            bankText.text = $"BANK  {bank:0.0000}";
            budgetText.text = BuildBudgetText(false, 0f);
            fieldGraphic.SetReady(missionLayout, currentWorldDepth, currentX);
            titleText.text = selectedMode == MissionMode.GraphChallenge
                ? "GRADIENT LANDER: QUARTIC VALLEY"
                : "GRADIENT LANDER: OPEN DESCENT";
            missionHeadingText.text = missionLayout.DisplayName;
            reviewPanel.gameObject.SetActive(false);
            landerRect.gameObject.SetActive(true);
            landerRect.localRotation = Quaternion.identity;
            landerGraphic.SetSkin(equippedSkin);
            landerGraphic.SetState(0f, 0f, false, 0f);
            Canvas.ForceUpdateCanvases();
            SetLanderPosition(currentX, LanderMath.GetShipScreenY(currentWorldDepth));
            RefreshTelemetry(currentX);
        }

        private string BuildEmptyResults()
        {
            return ResultLine("DESCENT PATH", selectedMode == MissionMode.GraphChallenge ? "hidden gradient route" : "randomized platforms") +
                   ResultLine("LEFT THRUSTER", "-") +
                   ResultLine("RIGHT THRUSTER", "-") +
                   ResultLine("LOCATION ERROR", "-") +
                   ResultLine("LAST LANDING COST", "-") +
                   $"\n<size=14><color=#{ToHex(Muted)}>CUMULATIVE COST</color></size>\n<size=29><b>0.0000</b></size>";
        }

        private string BuildPendingResults()
        {
            string destination = willLandOnPlatform ? activeCollision.Platform.Label : "Ground";
            return ResultLine("PATH RESULT", destination) +
                   ResultLine("LEFT THRUSTER", activeThrust.LeftPower.ToString("0.000")) +
                   ResultLine("RIGHT THRUSTER", activeThrust.RightPower.ToString("0.000")) +
                   ResultLine("PLATFORM ALIGNMENT", willLandOnPlatform ? "collision" : "none") +
                   ResultLine("LANDING COST", willLandOnPlatform || willLandOnGround ? "pending" : "none") +
                   $"\n<size=14><color=#{ToHex(Muted)}>CUMULATIVE COST</color></size>\n<size=29><b>{cumulativeCost:0.0000}</b></size>";
        }

        private string BuildLandingResults(LanderRunResult result, bool final)
        {
            string platformName = selectedMode == MissionMode.GraphChallenge && result.IsCheckpoint
                ? $"Checkpoint {result.CheckpointIndex + 1}"
                : $"Platform F{result.StageIndex + 1}-{(char)('A' + result.PlatformIndex)}";
            string destination = final
                ? (finalMissionSuccess ? "Target hit" :
                    (selectedMode == MissionMode.GraphChallenge && result.HitFinalTarget ? "Invalid route" : "Target missed"))
                : platformName;
            Color totalColor = final ? (finalMissionSuccess ? Green : Red) : Ink;
            return ResultLine("LANDING", destination) +
                   ResultLine("LEFT THRUSTER", result.LeftPower.ToString("0.000")) +
                   ResultLine("RIGHT THRUSTER", result.RightPower.ToString("0.000")) +
                   ResultLine("LOCATION ERROR", result.Distance.ToString("0.000")) +
                   ResultLine("LANDING COST", result.Cost.ToString("0.0000")) +
                   $"\n<size=14><color=#{ToHex(Muted)}>CUMULATIVE COST</color></size>\n" +
                   $"<size=29><b><color=#{ToHex(totalColor)}>{cumulativeCost:0.0000}</color></b></size>";
        }

        private string BuildBudgetText(bool success, float reward)
        {
            bool finished = state == RunState.Reviewing || state == RunState.MissionComplete;
            float coinBonus = coinsCollected * LanderMath.CoinReward;
            string footer = finished
                ? (success ? $"<color=#{ToHex(Green)}>BANK DEPOSIT  +{reward:0.0000}</color>" : $"<color=#{ToHex(Red)}>BANK DEPOSIT  0.0000</color>")
                : $"<color=#{ToHex(Muted)}>FLIGHT MODE</color>  continuous scroll";
            return $"<color=#{ToHex(Muted)}>STARTING REWARD</color>  {LanderMath.MissionBudget:0.0000}\n" +
                   $"<color=#{ToHex(Muted)}>LANDING COSTS</color>  {cumulativeCost:0.0000}\n" +
                   $"<color=#{ToHex(Muted)}>COIN BONUS IF TARGET</color>  +{coinBonus:0.0000}\n" +
                   $"<b><color=#{ToHex(Accent)}>AVAILABLE</color>  {remainingBudget + coinBonus:0.0000}</b>\n" + footer;
        }

        private string ResultLine(string label, string value)
        {
            return $"<size=13><color=#{ToHex(Muted)}>{label}</color></size>\n<size=19><b>{value}</b></size>\n";
        }

        private void UpdateLedger()
        {
            bankText.text = $"BANK  {bank:0.0000}";
            budgetText.text = BuildBudgetText(false, 0f);
            UpdateHistory();
            RefreshTelemetry(currentX);
        }

        private void UpdateHistory()
        {
            if (landingHistory.Count == 0)
            {
                historyText.text = "No landings yet.";
                return;
            }

            StringBuilder builder = new StringBuilder();
            int first = Mathf.Max(0, landingHistory.Count - 6);
            for (int i = first; i < landingHistory.Count; i++)
            {
                LanderRunResult result = landingHistory[i];
                string label = result.IsGroundLanding ? "GROUND" :
                    (selectedMode == MissionMode.GraphChallenge && result.IsCheckpoint
                        ? $"CHECK {result.CheckpointIndex + 1}"
                        : $"F{result.StageIndex + 1}-{(char)('A' + result.PlatformIndex)}");
                builder.Append($"<color=#{ToHex(i == landingHistory.Count - 1 ? Ink : Muted)}>{i + 1}. {label,-7} cost {result.Cost,7:0.0000}  x {result.LandingX:0.000}</color>");
                if (i < landingHistory.Count - 1) builder.Append("\n");
            }
            historyText.text = builder.ToString();
        }

        private void RefreshTelemetry(float x)
        {
            string left = landingHistory.Count == 0 ? "-" : landingHistory[landingHistory.Count - 1].LeftPower.ToString("0.000");
            string right = landingHistory.Count == 0 ? "-" : landingHistory[landingHistory.Count - 1].RightPower.ToString("0.000");
            telemetryText.text =
                $"<color=#{ToHex(Muted)}>LAST LEFT</color>       {left}\n" +
                $"<color=#{ToHex(Muted)}>LAST RIGHT</color>      {right}\n" +
                $"<color=#{ToHex(Muted)}>HORIZONTAL X</color>   {x,7:0.000}";
        }

        private void SetInputsEnabled(bool enabled)
        {
            weightInput.interactable = enabled;
            biasInput.interactable = enabled;
            launchButton.interactable = enabled;
        }

        private void SetLanderPosition(float xNormalized, float yNormalized)
        {
            if (fieldRect == null || landerRect == null) return;
            Rect r = fieldRect.rect;
            landerRect.anchoredPosition = new Vector2((xNormalized - .5f) * r.width, (yNormalized - .5f) * r.height);
        }

        private void SetInputMessage(string message, Color color)
        {
            helperText.text = message;
            helperText.color = color;
            statusText.text = "CHECK PARAMETERS";
            statusText.color = color;
        }

        private static bool TryReadNumber(string text, out float value)
        {
            string normalized = (text ?? string.Empty).Trim().Replace(',', '.');
            return float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                   !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
            return rect;
        }

        private RectTransform CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            RectTransform rect = CreateRect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return rect;
        }

        private Text CreateText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, int size, FontStyle style, TextAnchor alignment, Color color)
        {
            RectTransform rect = CreateRect(name, parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = uiFont;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private InputField CreateInput(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, string initialValue)
        {
            RectTransform rect = CreatePanel(name, parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero, new Color32(7, 17, 34, 255));
            InputField input = rect.gameObject.AddComponent<InputField>();
            Text value = CreateText("Value", rect, new Vector2(.08f, .08f), new Vector2(.92f, .92f), 20, FontStyle.Bold, TextAnchor.MiddleLeft, Ink);
            value.raycastTarget = true;
            Text placeholder = CreateText("Placeholder", rect, new Vector2(.08f, .08f), new Vector2(.92f, .92f), 18, FontStyle.Normal, TextAnchor.MiddleLeft, Muted);
            placeholder.text = "0.00";
            input.textComponent = value;
            input.placeholder = placeholder;
            input.text = initialValue;
            input.lineType = InputField.LineType.SingleLine;
            input.contentType = InputField.ContentType.Standard;
            input.characterLimit = 8;
            input.caretColor = Accent;
            input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, .35f);
            return input;
        }

        private Button CreateButton(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color normal, Color highlighted)
        {
            RectTransform rect = CreatePanel(name, parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero, normal);
            Button button = rect.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.pressedColor = Color.Lerp(normal, Color.black, .18f);
            colors.selectedColor = highlighted;
            colors.disabledColor = new Color32(55, 63, 76, 190);
            button.colors = colors;
            Text label = CreateText("Label", rect, new Vector2(.04f, .05f), new Vector2(.96f, .95f), 15, FontStyle.Bold, TextAnchor.MiddleCenter, Ink);
            label.raycastTarget = false;
            return button;
        }

        private static string ToHex(Color color)
        {
            return ColorUtility.ToHtmlStringRGB(color);
        }

        private static AudioClip CreateCoinDing()
        {
            const int sampleRate = 44100;
            const float duration = .32f;
            int samples = Mathf.RoundToInt(sampleRate * duration);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float time = i / (float)sampleRate;
                float envelope = Mathf.Exp(-time * 8.5f) * Mathf.Clamp01(time * 90f);
                float tone = Mathf.Sin(2f * Mathf.PI * 880f * time) * .62f +
                             Mathf.Sin(2f * Mathf.PI * 1320f * time) * .28f;
                data[i] = tone * envelope * .45f;
            }
            AudioClip clip = AudioClip.Create("Checkpoint Coin Ding", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void TryStartAutomatedCapture()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                bool selection = args[i] == "--capture-selection";
                bool shop = args[i] == "--capture-shop";
                bool preview = args[i] == "--capture-preview" || args[i] == "--capture-graph-preview";
                bool skip = args[i] == "--capture-skip";
                bool platform = args[i] == "--capture-platform" || args[i] == "--capture-graph-platform";
                bool results = args[i] == "--capture-results" || args[i] == "--capture-graph-results";
                if (!selection && !shop && !preview && !skip && !platform && !results) continue;
                automatedCapture = true;
                string path = i + 1 < args.Length
                    ? args[i + 1]
                    : System.IO.Path.Combine(Application.dataPath, "../GradientLanderPreview.png");
                if (shop)
                {
                    ShowShop();
                    StartCoroutine(CaptureCurrentFrame(path));
                }
                else if (selection || preview) StartCoroutine(CaptureCurrentFrame(path));
                else if (skip) StartCoroutine(CaptureFirstSkip(path));
                else if (platform) StartCoroutine(CaptureFirstLanding(path));
                else StartCoroutine(CaptureAfterMission(path));
                break;
            }
        }

        private static bool HasAutomatedCaptureArgument()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--capture-selection" || args[i] == "--capture-shop" || args[i] == "--capture-preview" ||
                    args[i] == "--capture-skip" || args[i] == "--capture-platform" ||
                    args[i] == "--capture-results" || args[i] == "--capture-graph-preview" ||
                    args[i] == "--capture-graph-platform" || args[i] == "--capture-graph-results")
                    return true;
            }
            return false;
        }

        private IEnumerator CaptureCurrentFrame(string path)
        {
            yield return null;
            yield return SaveScreenshotAndQuit(path);
        }

        private IEnumerator CaptureFirstSkip(string path)
        {
            yield return null;
            BeginDescent();
            while (skippedFrames == 0) yield return null;
            yield return new WaitForSecondsRealtime(.20f);
            yield return SaveScreenshotAndQuit(path);
        }

        private IEnumerator CaptureFirstLanding(string path)
        {
            yield return null;
            PrepareAutomatedGraphInputs();
            BeginDescent();
            if (selectedMode == MissionMode.GraphChallenge)
            {
                while (coinsCollected == 0)
                {
                    if (state == RunState.Ready) BeginDescent();
                    yield return null;
                }
            }
            else
            {
                while (!(state == RunState.Ready && landingHistory.Count > 0)) yield return null;
            }
            yield return new WaitForSecondsRealtime(.2f);
            yield return SaveScreenshotAndQuit(path);
        }

        private IEnumerator CaptureAfterMission(string path)
        {
            yield return null;
            PrepareAutomatedGraphInputs();
            while (state != RunState.MissionComplete)
            {
                if (state == RunState.Ready) BeginDescent();
                yield return null;
            }
            yield return SaveScreenshotAndQuit(path);
        }

        private void PrepareAutomatedGraphInputs()
        {
            if (selectedMode != MissionMode.GraphChallenge) return;
            weightInput.text = "1.00";
            biasInput.text = ".25";
        }

        private static bool HasArgument(string expected)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++) if (args[i] == expected) return true;
            return false;
        }

        private static bool HasGraphCaptureArgument()
        {
            return HasArgument("--capture-graph-preview") || HasArgument("--capture-graph-platform") ||
                   HasArgument("--capture-graph-results");
        }

        private IEnumerator SaveScreenshotAndQuit(string path)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path, 1);
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }
    }
}
