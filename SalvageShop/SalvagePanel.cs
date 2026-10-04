using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GameModeForge.SalvageShop
{
    internal enum SalvageMode
    {
        Salvage,
        Upgrade
    }

    // THE SALVAGE SCREEN (3.1, 13.1, 14.1): a third sub-tab, SALVAGE, beside
    // SHOP and VAULT on the grid tab's right panel, at every station. Inside it
    // two MODES and one SLOT:
    //
    //   SALVAGE   drop a weapon or gadget from the vault into the slot; it asks
    //             to confirm (it does NOT say what you will get), then scraps it
    //             for Ex and maybe Bond. QUICK SALVAGE skips the ask - except for
    //             an upgraded item or a ranked gun, which always asks.
    //   UPGRADE   drop any module you own (vault or equipped) into the slot; it
    //             stays there while you buy as many steps as you like, picking
    //             a stat a step, and goes back where it came from (it never left).
    //
    // ★ THE VAULT STAYS ON SCREEN. You drag FROM it, so the panel takes the top
    // of the right-hand panel and the game's own vault widget is shortened
    // underneath it - the same widget, so its scrolling, selection and drag all
    // stay the game's.
    //
    // ★ BUILT FROM THE GAME'S OWN PIECES at runtime: the tab is a clone of the
    // SHOP tab button, the mode buttons clones of the VAULT tab button, the
    // texts clones of a tab caption (font, material, rich text), the slot's
    // icon an instance of the vault's own module icon. A failure anywhere
    // leaves the grid screen exactly as the game made it and says so.
    //
    // Lives on the GridScreen object, beside ModuleGridScreen and
    // ModuleGridInput, so either can find it with GetComponent.
    internal sealed class SalvagePanel : MonoBehaviour
    {
        // ---- the game's pieces (private fields, read once) ------------------

        private static readonly FieldInfo FInput = AccessTools.Field(typeof(ModuleGridScreen), "input");
        private static readonly FieldInfo FGrid = AccessTools.Field(typeof(ModuleGridScreen), "gridWidget");
        private static readonly FieldInfo FVaultWidget = AccessTools.Field(typeof(ModuleGridScreen), "vaultGridWidget");
        private static readonly FieldInfo FShop = AccessTools.Field(typeof(ModuleGridScreen), "shopWidget");
        private static readonly FieldInfo FRight = AccessTools.Field(typeof(ModuleGridScreen), "rightPanelTransform");
        private static readonly FieldInfo FHovered = AccessTools.Field(typeof(ModuleGridScreen), "hoveredModuleInfo");
        private static readonly FieldInfo FVaultTab = AccessTools.Field(typeof(ModuleGridScreen), "vaultButtonImage");
        private static readonly FieldInfo FShopTab = AccessTools.Field(typeof(ModuleGridScreen), "shopButtonImage");
        private static readonly FieldInfo FSelBg = AccessTools.Field(typeof(ModuleGridScreen), "selectedTabBg");
        private static readonly FieldInfo FUnselBg = AccessTools.Field(typeof(ModuleGridScreen), "unselectedTabBg");
        private static readonly FieldInfo FTabs = AccessTools.Field(typeof(ModuleGridScreen), "tabsObject");
        private static readonly FieldInfo FFailSfx = AccessTools.Field(typeof(ModuleGridScreen), "placementFailedSfx");
        private static readonly FieldInfo FMoveSfx = AccessTools.Field(typeof(ModuleGridScreen), "startMoveSfx");
        private static readonly FieldInfo FCurrentGrid = AccessTools.Field(typeof(ModuleGridScreen), "currentGrid");
        private static readonly MethodInfo MHoverInfo = AccessTools.Method(typeof(ModuleGridScreen), "UpdateHoveredModuleIndfo");
        private static readonly PropertyInfo PShip = AccessTools.Property(typeof(ShipMenuTab), "Ship");
        private static readonly PropertyInfo PStation = AccessTools.Property(typeof(ShipMenuTab), "Station");

        private static readonly FieldInfo FActive = AccessTools.Field(typeof(ModuleGridInput), "activeModuleWidget");
        private static readonly MethodInfo MSetActive = AccessTools.PropertySetter(typeof(ModuleGridInput), "ActiveModuleWidget");
        private static readonly FieldInfo FPlayerInput = AccessTools.Field(typeof(ModuleGridInput), "playerInput");
        private static readonly FieldInfo FSelectSfx = AccessTools.Field(typeof(ModuleGridInput), "selectionMoveSfx");
        private static readonly FieldInfo FCancelSfx = AccessTools.Field(typeof(ModuleGridInput), "cancelMoveSfx");
        private static readonly FieldInfo FIconPrefab = AccessTools.Field(typeof(VaultGridWidget), "gridItemPrefab");
        private static readonly FieldInfo FCellSize = AccessTools.Field(typeof(VaultGridWidget), "gridSize");

        internal ModuleGridScreen screen;
        internal ModuleGridInput input;
        internal SalvageSlot slot;

        private Vault _vault;
        private ModuleGridWidget _grid;
        private VaultGridWidget _vaultWidget;
        private ShopWidget _shop;
        private HoveredModuleInfo _hovered;
        private Image _vaultTab, _shopTab, _myTab;
        private Sprite _selBg, _unselBg;
        private GameObject _tabButton, _root;
        private RectTransform _vaultRect;
        private Vector2 _vaultTop;
        private bool _shrunk;
        private Image[] _modeImages = new Image[2];
        private ModuleIconWidget _icon;
        private Image _iconFallback;
        private Component _title, _status, _quick, _footer;
        private readonly List<Component> _rows = new List<Component>();

        // Everything on the panel that takes a click (the mode buttons, the
        // quick salvage line, the stat lines), switched off while a module is
        // carried - see TakesClicks.
        private readonly List<CanvasGroup> _clickables = new List<CanvasGroup>();
        private bool _clickOn = true;

        // The stock VAULT and SHOP tabs as the game laid them out, put back if
        // the build fails part-way.
        private readonly List<KeyValuePair<RectTransform, Vector4>> _stockTabs =
            new List<KeyValuePair<RectTransform, Vector4>>();

        internal bool Built;

        // ---- state -----------------------------------------------------------

        public bool IsOpen { get; private set; }
        public SalvageMode Mode { get; private set; }
        public Module Slotted { get; private set; }

        private ModuleContainerWidget _from;
        private List<string> _offers = new List<string>();
        private int _cursor, _top;
        private bool _armed;
        private string _note;          // the last thing that happened, until the next slot
        private bool _noteBad;

        private const int VisibleRows = 5;
        private const float PanelHeight = 384f;
        private const string Orange = "#E47D1E";
        private const string Red = "#FF5A4E";
        private const string Grey = "#8C8C8C";

        // ---- building ---------------------------------------------------------

        internal static SalvagePanel For(Component c)
        {
            return c != null ? c.GetComponent<SalvagePanel>() : null;
        }

        // Once per grid screen instance (a new scene is a new screen).
        internal static SalvagePanel Ensure(ModuleGridScreen screen)
        {
            if (screen == null)
                return null;

            SalvagePanel p = For(screen);

            if (p != null)
                return p.Built ? p : null;

            p = screen.gameObject.AddComponent<SalvagePanel>();
            p.screen = screen;

            if (!p.Build())
            {
                p.Teardown();
                return null;
            }

            p.Built = true;
            return p;
        }

        private bool Build()
        {
            try
            {
                input = FInput != null ? FInput.GetValue(screen) as ModuleGridInput : null;
                _grid = FGrid != null ? FGrid.GetValue(screen) as ModuleGridWidget : null;
                _vaultWidget = FVaultWidget != null ? FVaultWidget.GetValue(screen) as VaultGridWidget : null;
                _shop = FShop != null ? FShop.GetValue(screen) as ShopWidget : null;
                _hovered = FHovered != null ? FHovered.GetValue(screen) as HoveredModuleInfo : null;
                _vaultTab = FVaultTab != null ? FVaultTab.GetValue(screen) as Image : null;
                _shopTab = FShopTab != null ? FShopTab.GetValue(screen) as Image : null;
                _selBg = FSelBg != null ? FSelBg.GetValue(screen) as Sprite : null;
                _unselBg = FUnselBg != null ? FUnselBg.GetValue(screen) as Sprite : null;

                var right = FRight != null ? FRight.GetValue(screen) as RectTransform : null;
                var tabs = FTabs != null ? FTabs.GetValue(screen) as GameObject : null;

                ServiceLocator.TryGet<Vault>(out _vault);

                if (input == null || _grid == null || _vaultWidget == null || _shop == null || right == null ||
                    tabs == null || _vault == null || MSetActive == null || FActive == null)
                {
                    SalvageShopPlugin.Log.LogError(
                        "the SALVAGE tab was NOT built: the grid screen is not shaped the way this build " +
                        "expects (a field it reads is missing). The shop and vault work as normal.");
                    return false;
                }

                Transform shopButton = tabs.transform.Find("ShopButton");
                Transform vaultButton = tabs.transform.Find("VaultButton");

                if (shopButton == null || vaultButton == null)
                {
                    SalvageShopPlugin.Log.LogError(
                        "the SALVAGE tab was NOT built: no ShopButton / VaultButton beside the shop tab. " +
                        "The shop and vault work as normal.");
                    return false;
                }

                // -- the SALVAGE tab, a clone of SHOP's ------------------------
                _tabButton = SalvageUi.Clone(shopButton.gameObject, tabs.transform, "SalvageButton");
                StripHint(_tabButton);
                _tabButton.transform.SetSiblingIndex(shopButton.GetSiblingIndex() + 1);

                Component tabText = SalvageUi.FirstText(_tabButton);
                SalvageUi.SetText(tabText, "SALVAGE");
                SalvageUi.Fit(tabText, 44f, 16f);

                Button tabBtn = _tabButton.GetComponentInChildren<Button>(true);
                SalvageUi.Rewire(tabBtn, OnTabClicked);
                _myTab = tabBtn != null ? tabBtn.targetGraphic as Image : null;

                if (_myTab != null && _unselBg != null)
                    _myTab.sprite = _unselBg;

                // Three tabs in the span the two filled.
                foreach (Transform t in new[] { vaultButton, shopButton })
                {
                    var trt = t as RectTransform;

                    if (trt != null)
                        _stockTabs.Add(new KeyValuePair<RectTransform, Vector4>(trt, new Vector4(
                            trt.anchoredPosition.x, trt.anchoredPosition.y, trt.sizeDelta.x, trt.sizeDelta.y)));
                }

                LayTabs(vaultButton as RectTransform, shopButton as RectTransform,
                        _tabButton.transform as RectTransform);

                // -- the panel, over the top of the vault ----------------------
                _vaultRect = _vaultWidget.transform as RectTransform;
                _vaultTop = _vaultRect.offsetMax;

                RectTransform root = SalvageUi.NewRect("SalvagePanel", right);
                _root = root.gameObject;
                root.anchorMin = new Vector2(0f, 1f);
                root.anchorMax = new Vector2(1f, 1f);
                root.pivot = new Vector2(0.5f, 1f);
                root.offsetMin = new Vector2(_vaultRect.offsetMin.x, _vaultTop.y - PanelHeight);
                root.offsetMax = new Vector2(_vaultTop.x, _vaultTop.y);
                root.SetSiblingIndex(_vaultRect.GetSiblingIndex() + 1);

                Image bg = root.gameObject.AddComponent<Image>();
                bg.color = new Color(0f, 0f, 0f, 0.45f);

                if (_unselBg != null)
                {
                    bg.sprite = _unselBg;
                    bg.type = Image.Type.Sliced;
                }

                // -- the two modes: clones of VAULT's tab --------------------
                for (int i = 0; i < 2; i++)
                {
                    GameObject b = SalvageUi.Clone(vaultButton.gameObject, root,
                                                   i == 0 ? "SalvageModeScrap" : "SalvageModeUpgrade");
                    StripHint(b);

                    var rt = (RectTransform)b.transform;
                    rt.anchorMin = new Vector2(i * 0.5f, 1f);
                    rt.anchorMax = new Vector2(i * 0.5f + 0.5f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.offsetMin = new Vector2(8f, -70f);
                    rt.offsetMax = new Vector2(-8f, -8f);

                    Component t = SalvageUi.FirstText(b);
                    SalvageUi.SetText(t, i == 0 ? "SALVAGE" : "UPGRADE");
                    SalvageUi.Fit(t, 34f, 14f);

                    Button mb = b.GetComponentInChildren<Button>(true);
                    SalvageMode mode = i == 0 ? SalvageMode.Salvage : SalvageMode.Upgrade;
                    SalvageUi.Rewire(mb, () => SetMode(mode));
                    _modeImages[i] = mb != null ? mb.targetGraphic as Image : null;
                    TakesClicks(b.transform);
                }

                // -- the slot ------------------------------------------------
                RectTransform slotRect = SalvageUi.NewRect("SalvageSlot", root);
                SalvageUi.Box(slotRect, 10f, 80f, 112f, 112f);

                Image frame = slotRect.gameObject.AddComponent<Image>();

                if (_unselBg != null)
                {
                    frame.sprite = _unselBg;
                    frame.type = Image.Type.Sliced;
                }

                slot = slotRect.gameObject.AddComponent<SalvageSlot>();
                slot.panel = this;
                slot.rect = slotRect;
                slot.frame = frame;
                slot.Paint();

                var prefab = FIconPrefab != null ? FIconPrefab.GetValue(_vaultWidget) as ModuleIconWidget : null;

                if (prefab != null)
                {
                    _icon = Instantiate(prefab, slotRect);
                    var irt = _icon.RectTransform != null ? _icon.RectTransform : (RectTransform)_icon.transform;

                    // The icon at the size the vault draws it (its cell), then
                    // scaled to fit the slot - its children are laid out for
                    // that size, so the size is kept and the scale changed.
                    Vector2 cell = FCellSize != null ? (Vector2)FCellSize.GetValue(_vaultWidget) : Vector2.zero;

                    if (cell.x < 1f || cell.y < 1f)
                        cell = new Vector2(Mathf.Max(1f, irt.rect.width), Mathf.Max(1f, irt.rect.height));

                    if (cell.x < 8f || cell.y < 8f)
                        cell = new Vector2(96f, 96f);

                    irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
                    irt.sizeDelta = cell;
                    irt.anchoredPosition = Vector2.zero;

                    float s = 96f / Mathf.Max(cell.x, cell.y);
                    irt.localScale = new Vector3(s, s, 1f);

                    try { _icon.EnablePowerIndicator(false); } catch (Exception) { }

                    slot.icon = irt;
                    _icon.gameObject.SetActive(false);
                }
                else
                {
                    RectTransform f = SalvageUi.NewRect("SalvageSlotIcon", slotRect);
                    SalvageUi.Box(f, 16f, 16f, 80f, 80f);
                    _iconFallback = f.gameObject.AddComponent<Image>();
                    _iconFallback.preserveAspect = true;
                    _iconFallback.raycastTarget = false;
                    slot.icon = f;
                    f.gameObject.SetActive(false);
                }

                // -- the texts: clones of a tab caption ------------------------
                GameObject captionSource = tabText != null ? tabText.gameObject : null;

                if (captionSource == null)
                {
                    SalvageShopPlugin.Log.LogError(
                        "the SALVAGE tab was NOT built: the tab caption has no text to copy the font from.");
                    return false;
                }

                _title = Text(captionSource, root, "SalvageTitle", 132f, 80f, 34f, 28f, false, false);
                _status = Text(captionSource, root, "SalvageStatus", 132f, 116f, 72f, 21f, true, false);
                _quick = Text(captionSource, root, "SalvageQuick", 132f, 192f, 26f, 21f, false, true);
                AddClick(_quick, ToggleQuick);
                TakesClicks(_quick);

                for (int i = 0; i < VisibleRows; i++)
                {
                    Component row = Text(captionSource, root, "SalvageRow" + i, 12f, 224f + i * 28f, 28f, 21f, false, true);
                    int index = i;
                    AddClick(row, () => RowClicked(index));
                    TakesClicks(row);
                    _rows.Add(row);
                }

                _footer = Text(captionSource, root, "SalvageFooter", 12f, 224f + VisibleRows * 28f + 4f, 28f, 20f, false, false);

                _root.SetActive(false);

                SalvageShopPlugin.Log.LogInfo(
                    "the SALVAGE tab is built - it is beside SHOP and VAULT on the grid tab at every station.");
                return true;
            }
            catch (Exception ex)
            {
                SalvageShopPlugin.Log.LogError(
                    "the SALVAGE tab was NOT built (" + ex.GetType().Name + ": " + ex.Message +
                    "). The shop and vault work as normal. " + ex.StackTrace);
                return false;
            }
        }

        // A failed build leaves nothing behind.
        private void Teardown()
        {
            try
            {
                if (_vaultRect != null && _shrunk)
                    _vaultRect.offsetMax = _vaultTop;

                foreach (KeyValuePair<RectTransform, Vector4> kv in _stockTabs)
                {
                    if (kv.Key == null)
                        continue;

                    kv.Key.anchoredPosition = new Vector2(kv.Value.x, kv.Value.y);
                    kv.Key.sizeDelta = new Vector2(kv.Value.z, kv.Value.w);
                }

                if (_tabButton != null)
                    Destroy(_tabButton);

                if (_root != null)
                    Destroy(_root);
            }
            catch (Exception)
            {
            }

            Destroy(this);
        }

        // The SHOP tab's "new items" badge is the shop's, not ours.
        private static void StripHint(GameObject b)
        {
            Transform hint = SalvageUi.Find(b.transform, "NewHintWidget");

            if (hint != null)
                Destroy(hint.gameObject);
        }

        // VAULT, SHOP, SALVAGE - left to right, across the span VAULT and SHOP
        // filled (measured: 181 wide each, centres -94 and +95).
        private static void LayTabs(RectTransform vault, RectTransform shop, RectTransform mine)
        {
            if (vault == null || shop == null || mine == null)
                return;

            float left = vault.anchoredPosition.x - vault.sizeDelta.x * 0.5f;
            float right = shop.anchoredPosition.x + shop.sizeDelta.x * 0.5f;
            float gap = 6f;
            float w = (right - left - 2f * gap) / 3f;

            RectTransform[] order = { vault, shop, mine };

            for (int i = 0; i < 3; i++)
            {
                RectTransform rt = order[i];
                rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
                rt.anchoredPosition = new Vector2(left + w * 0.5f + i * (w + gap), shop.anchoredPosition.y);

                // The captions were sized for a wider button.
                Component t = SalvageUi.FirstText(rt.gameObject);
                SalvageUi.Fit(t, 44f, 16f);
            }
        }

        private static Component Text(GameObject source, RectTransform parent, string name, float left, float top,
                                      float height, float size, bool wrap, bool clickable)
        {
            GameObject go = SalvageUi.Clone(source, parent, name);
            var rt = (RectTransform)go.transform;
            SalvageUi.Row(rt, left, 12f, top, height);

            Component t = SalvageUi.FirstText(go);
            SalvageUi.SetFontSize(t, size);
            SalvageUi.Align(t, true);
            SalvageUi.RichText(t);

            if (wrap)
                SalvageUi.Wrap(t);
            else
                SalvageUi.OneLine(t);

            // ★ A plain label must not catch the mouse: the screen takes the
            // click that DROPS a dragged module (ModuleGridScreen.OnPointerUp),
            // and a raycast target with a handler of its own would eat it.
            SalvageUi.SetRaycast(t, clickable);
            SalvageUi.SetText(t, "");
            return t;
        }

        private static void AddClick(Component text, UnityEngine.Events.UnityAction action)
        {
            if (text == null)
                return;

            Button b = text.gameObject.GetComponent<Button>() ?? text.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;

            var nav = new Navigation { mode = Navigation.Mode.None };
            b.navigation = nav;

            b.targetGraphic = text as Graphic;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(action);
        }

        // ★ A BUTTON ON THE PANEL EATS THE DROP (found in the audit). The game
        // drops a carried module in the screen's own OnPointerUp, and Unity
        // sends a pointer-up to whatever took the pointer-down - a Selectable
        // under the pointer takes it, so the screen never hears the click and
        // the module stays on the cursor. So everything clickable on the panel
        // stops catching the pointer while a module is carried (Update), and
        // the click falls through to the panel's background and on to the
        // screen, the same path a drop on the background always took.
        private void TakesClicks(Component c)
        {
            if (c == null)
                return;

            CanvasGroup g = c.gameObject.GetComponent<CanvasGroup>() ?? c.gameObject.AddComponent<CanvasGroup>();
            _clickables.Add(g);
        }

        private void Clickable(bool on)
        {
            if (on == _clickOn)
                return;

            _clickOn = on;

            foreach (CanvasGroup g in _clickables)
                if (g != null)
                    g.blocksRaycasts = on;
        }

        // ---- the game's state, read fresh --------------------------------------

        private Ship Ship
        {
            get
            {
                try { return PShip != null ? PShip.GetValue(screen, null) as Ship : null; }
                catch (Exception) { return null; }
            }
        }

        private bool AtStation
        {
            get
            {
                try { return PStation != null && PStation.GetValue(screen, null) != null; }
                catch (Exception) { return false; }
            }
        }

        private bool Pad
        {
            get
            {
                try
                {
                    Ship s = Ship;
                    return s != null && s.shipInput != null && s.shipInput.UsesGamepad;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        private ModuleGrid ThisGrid
        {
            get { return FCurrentGrid != null ? FCurrentGrid.GetValue(screen) as ModuleGrid : null; }
        }

        private bool OnThisShip(Module m)
        {
            ModuleGrid g = ThisGrid;
            return m != null && g != null && g.Contains(m);
        }

        internal ModuleContainerWidget Active
        {
            get { return FActive.GetValue(input) as ModuleContainerWidget; }
        }

        internal void SetActive(ModuleContainerWidget w)
        {
            if (w == null || ReferenceEquals(w, Active))
                return;

            try
            {
                MSetActive.Invoke(input, new object[] { w });
            }
            catch (Exception)
            {
            }
        }

        // ★ THE WHOLE PANEL IS THE SLOT'S TARGET for the mouse. Over any part
        // of it the slot is the active widget, so a click on the panel's
        // background can never pick up a grid module drawn underneath it (the
        // grid is the game's default widget wherever the vault and shop are
        // not), and a module dropped anywhere on the panel lands in the slot.
        internal bool Contains(Vector2 mouse)
        {
            var rt = _root != null ? _root.transform as RectTransform : null;
            return rt != null && _root.activeInHierarchy && rt.ContainsPoint(mouse);
        }

        internal bool IsSlot(ModuleContainerWidget w)
        {
            return w != null && ReferenceEquals(w, slot);
        }

        private void Sfx(FieldInfo f, object owner)
        {
            try
            {
                var id = f != null ? f.GetValue(owner) as string : null;

                if (!string.IsNullOrEmpty(id))
                    AudioManager.PlaySfx(id);
            }
            catch (Exception)
            {
            }
        }

        private static void PlayId(string id)
        {
            try
            {
                if (!string.IsNullOrEmpty(id))
                    AudioManager.PlaySfx(id);
            }
            catch (Exception)
            {
            }
        }

        private void Fail()
        {
            Sfx(FFailSfx, screen);
        }

        // ---- opening and closing ------------------------------------------------

        private bool _opening;

        internal bool Opening
        {
            get { return _opening; }
        }

        private void OnTabClicked()
        {
            Open();
        }

        internal void Open()
        {
            if (!Built || IsOpen || !AtStation)
                return;

            try
            {
                // The vault is what the SALVAGE tab shows under the panel, so
                // the game's own ShowVault opens it (and hides the shop).
                _opening = true;
                screen.ShowVault();
            }
            finally
            {
                _opening = false;
            }

            if (_vaultTab != null && _unselBg != null) _vaultTab.sprite = _unselBg;
            if (_shopTab != null && _unselBg != null) _shopTab.sprite = _unselBg;
            if (_myTab != null && _selBg != null) _myTab.sprite = _selBg;

            if (!_shrunk)
            {
                _vaultRect.offsetMax = new Vector2(_vaultTop.x, _vaultTop.y - PanelHeight - 8f);
                _shrunk = true;
            }

            _root.SetActive(true);
            IsOpen = true;
            Slotted = null;
            _note = null;
            _armed = false;

            SetActive(_vaultWidget);

            try { _vaultWidget.SelectFirstModule(); } catch (Exception) { }

            SalvageRules r = SalvageRules.Current;
            SalvageCheck.Once(Ing(r.common), Ing(r.uncommon));

            Sfx(FSelectSfx, input);
            Redraw();
        }

        internal void Close()
        {
            if (!Built)
                return;

            bool wasSlot = IsSlot(Active);

            IsOpen = false;
            Slotted = null;
            _from = null;
            _armed = false;

            ShowIcon(null);

            if (_root != null)
                _root.SetActive(false);

            if (_shrunk && _vaultRect != null)
            {
                _vaultRect.offsetMax = _vaultTop;
                _shrunk = false;
            }

            if (_myTab != null && _unselBg != null)
                _myTab.sprite = _unselBg;

            if (wasSlot)
                SetActive(_shop != null && _shop.IsOpened ? (ModuleContainerWidget)_shop : _vaultWidget);
        }

        // ---- modes ----------------------------------------------------------------

        internal void ToggleMode()
        {
            SetMode(Mode == SalvageMode.Salvage ? SalvageMode.Upgrade : SalvageMode.Salvage);
        }

        internal void SetMode(SalvageMode mode)
        {
            if (!IsOpen)
                return;

            if (Mode != mode)
            {
                Mode = mode;
                _armed = false;
                _note = null;

                // A module that is not welcome in the new mode comes out.
                if (Slotted != null && !Accepts(Slotted))
                    TakeOut();
                else if (Slotted != null)
                    Offer(Slotted);

                Sfx(FSelectSfx, input);
            }

            Redraw();
        }

        internal void ToggleQuick()
        {
            if (!IsOpen)
                return;

            SalvageShopPlugin.SetQuick(!SalvageShopPlugin.Quick, "the SALVAGE screen");
            Sfx(FSelectSfx, input);
            Redraw();
        }

        // ---- the slot ---------------------------------------------------------------

        internal bool Accepts(Module m)
        {
            if (!IsOpen || m == null)
                return false;

            if (Mode == SalvageMode.Salvage)
                return _vault.Contains(m) && SalvageCatalog.Scrappable(m);

            return (_vault.Contains(m) || OnThisShip(m)) &&
                   SalvageCatalog.Offers(m, SalvageRules.Current).Count > 0;
        }

        private void Offer(Module m)
        {
            _offers = Mode == SalvageMode.Upgrade && m != null
                ? SalvageCatalog.Offers(m, SalvageRules.Current)
                : new List<string>();

            _cursor = Mathf.Clamp(_cursor, 0, Math.Max(0, _offers.Count - 1));
            Window();
        }

        // A module arrives in the slot - dropped by the mouse (the active
        // widget is already the slot) or sent by the pad's A.
        internal void Put(Module m, ModuleContainerWidget from)
        {
            if (!Accepts(m))
                return;

            Slotted = m;
            _from = from;
            _cursor = 0;
            _top = 0;
            _armed = false;
            _note = null;

            Offer(m);
            ShowIcon(m);
            Sfx(FMoveSfx, screen);

            if (Mode == SalvageMode.Salvage && SalvageShopPlugin.Quick && !NeedsWarning(m))
            {
                Scrap();
                return;
            }

            Redraw();
        }

        internal void TakeOut()
        {
            if (Slotted == null)
                return;

            Module m = Slotted;
            Slotted = null;
            _armed = false;
            ShowIcon(null);

            if (IsSlot(Active))
            {
                ModuleContainerWidget back = _from != null && !IsSlot(_from) ? _from : _vaultWidget;

                if (ReferenceEquals(back, _vaultWidget) && !_vault.Contains(m))
                    back = _grid;

                SetActive(back);

                try
                {
                    if (ReferenceEquals(back, _grid) && !OnThisShip(m))
                        back.SelectFirstModule();
                    else
                        back.SelectModule(m);
                }
                catch (Exception)
                {
                }
            }

            _from = null;
            Sfx(FCancelSfx, input);
            Redraw();
        }

        private void ShowIcon(Module m)
        {
            try
            {
                if (_icon != null)
                {
                    if (m != null)
                    {
                        _icon.gameObject.SetActive(true);
                        _icon.Module = m;
                        _icon.DisplayPowerLevel();
                    }
                    else
                    {
                        _icon.gameObject.SetActive(false);
                    }
                }
                else if (_iconFallback != null)
                {
                    _iconFallback.gameObject.SetActive(m != null);

                    if (m != null)
                    {
                        _iconFallback.sprite = m.Icon;
                        _iconFallback.color = m.Color;
                    }
                }
            }
            catch (Exception)
            {
            }

            if (slot != null)
                slot.SelectFirstModule();
        }

        // ★ The slot holds a reference, so the item can be moved out from under
        // it (dragged to the grid, say). Checked every frame while it is open.
        // And while a module is carried, nothing on the panel takes the click
        // that drops it (TakesClicks).
        private void Update()
        {
            if (!IsOpen)
                return;

            try
            {
                Clickable(input == null || !input.IsMovingModule);

                if (Slotted == null)
                    return;

                bool there = Mode == SalvageMode.Salvage
                    ? _vault.Contains(Slotted)
                    : _vault.Contains(Slotted) || OnThisShip(Slotted);

                if (!there)
                {
                    _note = "IT LEFT " + (Mode == SalvageMode.Salvage ? "THE VAULT" : "YOUR SHIP");
                    _noteBad = true;
                    TakeOut();
                }
            }
            catch (Exception)
            {
            }
        }

        // ---- the pad / keyboard / mouse ------------------------------------------

        // The pad's A (and the keyboard's Enter) on the SALVAGE tab: send the
        // highlighted module into the slot. True = handled (the game's own
        // "pick up to move" does not run).
        internal bool PickUp()
        {
            if (!IsOpen || input.IsMovingModule)
                return false;

            ModuleContainerWidget a = Active;

            if (IsSlot(a))
                return true;

            if (!ReferenceEquals(a, _vaultWidget) && !ReferenceEquals(a, _grid))
                return false;

            Module m = a.SelectedModule;

            if (m == null)
                return true;

            if (!Accepts(m))
            {
                _note = Mode == SalvageMode.Salvage
                    ? "ONLY WEAPONS AND GADGETS IN THE VAULT CAN BE SALVAGED"
                    : "NOTHING ON THAT MODULE CAN BE UPGRADED";
                _noteBad = true;
                Fail();
                Redraw();
                return true;
            }

            Put(m, a);

            if (Slotted != null)
            {
                SetActive(slot);
                slot.SelectFirstModule();
            }
            else
            {
                // Quick salvage already scrapped it.
                SetActive(_vaultWidget);
                try { _vaultWidget.SelectFirstModule(); } catch (Exception) { }
            }

            return true;
        }

        // B / Back: the module comes out of the slot. True = handled.
        internal bool Back()
        {
            if (!IsOpen || Slotted == null)
                return false;

            TakeOut();
            return true;
        }

        // A right-click on the slot takes the module out.
        internal bool RightClick(Vector2 mouse)
        {
            if (!IsOpen || Slotted == null || slot == null || !slot.rect.ContainsPoint(mouse))
                return false;

            TakeOut();
            return true;
        }

        // Y (pad), R (keyboard), G (mode), C (quick salvage), X (take out) -
        // read on THIS menu player's own devices, every frame the grid screen
        // updates. Nothing is bound into the game's input.
        internal void Keys()
        {
            if (!IsOpen || input == null || !input.IsEditingEnabled)
                return;

            try
            {
                var pi = FPlayerInput != null ? FPlayerInput.GetValue(input) as PlayerInput : null;

                if (pi == null)
                    return;

                foreach (InputDevice d in pi.devices)
                {
                    var gp = d as Gamepad;

                    if (gp != null && gp.buttonNorth.wasPressedThisFrame)
                        Confirm();

                    var kb = d as Keyboard;

                    if (kb == null)
                        continue;

                    if (kb.rKey.wasPressedThisFrame)
                        Confirm();
                    else if (kb.gKey.wasPressedThisFrame)
                        ToggleMode();
                    else if (kb.cKey.wasPressedThisFrame)
                        ToggleQuick();
                    else if (kb.xKey.wasPressedThisFrame)
                        TakeOut();
                }
            }
            catch (Exception)
            {
            }
        }

        internal void MoveCursor(int delta)
        {
            if (!IsOpen || Slotted == null || Mode != SalvageMode.Upgrade || _offers.Count == 0)
                return;

            int was = _cursor;
            _cursor = Mathf.Clamp(_cursor + delta, 0, _offers.Count - 1);

            if (_cursor != was)
            {
                Window();
                Sfx(FSelectSfx, input);
                Redraw();
            }
        }

        private void Window()
        {
            if (_cursor < _top)
                _top = _cursor;

            if (_cursor >= _top + VisibleRows)
                _top = _cursor - VisibleRows + 1;

            _top = Mathf.Clamp(_top, 0, Math.Max(0, _offers.Count - VisibleRows));
        }

        // A click on a row: the first picks it, a second on the same row buys.
        private void RowClicked(int i)
        {
            if (!IsOpen || Slotted == null)
                return;

            if (Mode == SalvageMode.Salvage)
            {
                if (i == 0)
                    Confirm();
                return;
            }

            int index = _top + i;

            if (index >= _offers.Count)
                return;

            if (index == _cursor)
            {
                Confirm();
                return;
            }

            _cursor = index;
            Window();
            Sfx(FSelectSfx, input);
            Redraw();
        }

        // ---- confirm: scrap, or buy a step -----------------------------------

        internal void Confirm()
        {
            if (!IsOpen)
                return;

            if (Slotted == null)
            {
                Fail();
                return;
            }

            if (Mode == SalvageMode.Salvage)
            {
                // ★ An upgraded item or a ranked gun asks a second time (3.1),
                // quick salvage or not.
                if (NeedsWarning(Slotted) && !_armed)
                {
                    _armed = true;
                    Sfx(FSelectSfx, input);
                    Redraw();
                    return;
                }

                Scrap();
                return;
            }

            if (_offers.Count == 0)
            {
                Fail();
                return;
            }

            Buy(_offers[Mathf.Clamp(_cursor, 0, _offers.Count - 1)]);
        }

        private bool NeedsWarning(Module m)
        {
            SalvageUpgrade u = SalvageRecords.Get(m);
            return (u != null && u.Any) || RankOf(m) > 0;
        }

        private void Scrap()
        {
            Module m = Slotted;
            SalvageRules r = SalvageRules.Current;

            if (m == null || !_vault.Contains(m))
            {
                _note = "IT IS NOT IN THE VAULT ANY MORE";
                _noteBad = true;
                TakeOut();
                return;
            }

            Ingredient common = Ing(r.common);
            Ingredient uncommon = Ing(r.uncommon);

            if (common == null)
            {
                _note = "THE SCRAP INGREDIENT '" + r.common.ToUpperInvariant() + "' IS NOT IN THIS GAME";
                _noteBad = true;
                Fail();
                Redraw();
                return;
            }

            SalvageUpgrade u = SalvageRecords.Get(m);
            int refundEx = u != null ? SalvageMath.Refund(u.spentEx, r.refundPercent) : 0;
            int refundBond = u != null ? SalvageMath.Refund(u.spentBond, r.refundPercent) : 0;
            int rank = RankOf(m);

            int ex = Math.Max(0, r.exPerScrap) + refundEx;
            int bond = (uncommon != null && SalvageMath.BondRoll(r.bondChance, UnityEngine.Random.value)
                           ? Math.Max(0, r.bondPerScrap) : 0) +
                       (uncommon != null ? refundBond : 0);

            string name = SalvageApply.Name(m);

            // ★ "new" first: the vault keeps a set of NEW modules, and one
            // removed while still in it would light the vault's "new" badge for
            // the rest of the run.
            try { _vault.MarkModuleSeen(m); } catch (Exception) { }

            _vault.Remove(m);
            SalvageRecords.Remove(m);

            if (ex > 0)
                _vault.Add(common, ex);

            if (bond > 0)
                _vault.Add(uncommon, bond);

            SalvageShopPlugin.Log.LogInfo(
                "scrapped " + name + ": +" + ex + " " + common.displayName +
                (bond > 0 ? ", +" + bond + " " + uncommon.displayName : "") +
                (refundEx + refundBond > 0
                    ? " (" + refundEx + " " + common.displayName + (uncommon != null ? " and " + refundBond + " " + uncommon.displayName : "") +
                      " of it refunded upgrades)"
                    : "") +
                (rank > 0 ? " - its rank " + rank + " is gone" : "") + ".");

            Slotted = null;
            _from = null;
            _armed = false;
            ShowIcon(null);

            _note = "SCRAPPED " + name;
            _noteBad = false;

            // The card may still be showing the item that is gone.
            try { if (_hovered != null) _hovered.Hide(); } catch (Exception) { }

            try { _vaultWidget.Refresh(); } catch (Exception) { }

            if (IsSlot(Active) || Active == null)
            {
                SetActive(_vaultWidget);
                try { _vaultWidget.SelectFirstModule(); } catch (Exception) { }
            }

            PlayId(m.Data != null ? m.Data.gridPlacementSfx : null);
            Redraw();
        }

        private void Buy(string stat)
        {
            Module m = Slotted;
            SalvageRules r = SalvageRules.Current;

            if (m == null || !(_vault.Contains(m) || OnThisShip(m)))
            {
                TakeOut();
                return;
            }

            Ingredient common = Ing(r.common);
            Ingredient uncommon = Ing(r.uncommon);

            SalvageUpgrade had = SalvageRecords.Get(m);
            int step = (had != null ? had.steps : 0) + 1;
            int ex, bond;
            r.PriceFor(stat, step, out ex, out bond);

            if (common == null || (bond > 0 && uncommon == null))
            {
                _note = "THE PRICE IS IN AN INGREDIENT THIS GAME DOES NOT HAVE";
                _noteBad = true;
                Fail();
                Redraw();
                return;
            }

            if (_vault.AmountOf(common) < ex || (bond > 0 && _vault.AmountOf(uncommon) < bond))
            {
                _note = "NOT ENOUGH SCRAP - IT COSTS " +
                        SalvageMath.Price(ex, bond, common.displayName.ToUpperInvariant(),
                                          uncommon != null ? uncommon.displayName.ToUpperInvariant() : "BOND");
                _noteBad = true;
                Fail();
                Redraw();
                return;
            }

            if (ex > 0)
                _vault.Remove(common, ex);

            if (bond > 0)
                _vault.Remove(uncommon, bond);

            SalvageUpgrade u = SalvageRecords.GetOrAdd(m);
            u.Add(stat, ex, bond);
            SalvageRecords.Set(m, u);

            SalvageApply.Refresh(m);

            string what = SalvageCatalog.StatName(stat, m) + " " + SalvageCatalog.Gives(stat, 1, r, m);

            SalvageShopPlugin.Log.LogInfo(
                "upgraded " + SalvageApply.Name(m) + ": " + what + " (its step " + u.steps + ", paid " +
                SalvageMath.Price(ex, bond, common.displayName, uncommon != null ? uncommon.displayName : "Bond") +
                "; now " + SalvageCatalog.Gives(stat, u.Count(stat), r, m) + " " +
                SalvageCatalog.StatName(stat, m) + ").");

            _note = what;
            _noteBad = false;

            // Show it: the grid's and vault's icons (level pips), the slot's,
            // and the card.
            try
            {
                if (OnThisShip(m))
                    _grid.RefreshModules();
                else
                    _vaultWidget.Refresh();
            }
            catch (Exception)
            {
            }

            ShowIcon(m);
            Offer(m);
            Card();

            PlayId(m.Data != null ? m.Data.gridPlacementSfx : null);
            Redraw();
        }

        // The card shows a module once and then early-returns for the same one,
        // so it is hidden and asked again (what the screen does after a stats
        // recalculation).
        private void Card()
        {
            try
            {
                if (_hovered != null)
                    _hovered.Hide();

                if (MHoverInfo != null)
                    MHoverInfo.Invoke(screen, null);
            }
            catch (Exception)
            {
            }
        }

        // ---- drawing -----------------------------------------------------------

        private string Key(string pad, string kbm)
        {
            return "<color=" + Orange + ">" + (Pad ? pad : kbm) + "</color>";
        }

        internal void Redraw()
        {
            if (!Built || !IsOpen)
                return;

            try
            {
                SalvageRules r = SalvageRules.Current;
                Ingredient common = Ing(r.common);
                Ingredient uncommon = Ing(r.uncommon);
                string exName = common != null ? common.displayName.ToUpperInvariant() : r.common.ToUpperInvariant();
                string bondName = uncommon != null ? uncommon.displayName.ToUpperInvariant() : r.uncommon.ToUpperInvariant();

                for (int i = 0; i < 2; i++)
                {
                    if (_modeImages[i] != null && _selBg != null && _unselBg != null)
                        _modeImages[i].sprite = (int)Mode == i ? _selBg : _unselBg;
                }

                Module m = Slotted;
                string note = _note != null
                    ? "<color=" + (_noteBad ? Red : Orange) + ">" + _note + "</color>\n"
                    : "";

                if (m == null)
                {
                    SalvageUi.SetText(_title, Mode == SalvageMode.Salvage
                        ? "SCRAP A WEAPON OR GADGET"
                        : "UPGRADE A MODULE");

                    SalvageUi.SetText(_status, note + (Mode == SalvageMode.Salvage
                        ? (Pad ? "Highlight one in the vault and press " + Key("A", "") + "."
                               : "Drag one from the vault into the slot.")
                        : (Pad ? "Highlight one in the vault or on your ship and press " + Key("A", "") + "."
                               : "Drag one from the vault or your ship into the slot.")) +
                        "  " + Key("RT", "G") + ": " + (Mode == SalvageMode.Salvage ? "UPGRADE" : "SALVAGE"));
                }
                else
                {
                    SalvageUpgrade u = SalvageRecords.Get(m);
                    int steps = u != null ? u.steps : 0;

                    SalvageUi.SetText(_title, SalvageApply.Name(m) +
                        (steps > 0 ? " <color=" + Grey + ">(" + steps + (steps == 1 ? " STEP" : " STEPS") + ")</color>" : ""));

                    string takeOut = Key("B", "RIGHT-CLICK") + ": TAKE IT OUT";

                    if (Mode == SalvageMode.Salvage)
                    {
                        string warn = Warning(m);

                        SalvageUi.SetText(_status, note + (_armed
                            ? "<color=" + Red + ">" + warn + "</color> " + Key("Y", "R") + " AGAIN TO SCRAP IT.  " + takeOut
                            : "SALVAGE IT?  " + Key("Y", "R") + ": YES   " + takeOut));
                    }
                    else
                    {
                        SalvageUi.SetText(_status, note + (_offers.Count == 0
                            ? "NOTHING ON THIS MODULE CAN BE UPGRADED.  " + takeOut
                            : "STEP " + (steps + 1) + ": PICK A STAT.  " + Key("Y", "R / CLICK") + ": BUY   " + takeOut));
                    }
                }

                SalvageUi.SetText(_quick, "[" + (SalvageShopPlugin.Quick ? "<color=" + Orange + ">X</color>" : " ") +
                                          "] QUICK SALVAGE  <color=" + Grey + ">(" + (Pad ? "X" : "C") + ")</color>");

                for (int i = 0; i < _rows.Count; i++)
                    SalvageUi.SetText(_rows[i], RowText(i, m, r, common, uncommon, exName, bondName));

                SalvageUi.SetText(_footer,
                    "YOU HAVE " + (common != null ? _vault.AmountOf(common) : 0) + " " + exName + " · " +
                    (uncommon != null ? _vault.AmountOf(uncommon) : 0) + " " + bondName +
                    (Mode == SalvageMode.Upgrade && _offers.Count > VisibleRows
                        ? "   <color=" + Grey + ">" + (_cursor + 1) + " / " + _offers.Count + "</color>"
                        : "") +
                    (SalvageRules.Broken ? "   <color=" + Red + ">RULES FILE UNREADABLE - SEE THE LOG</color>" : ""));
            }
            catch (Exception ex)
            {
                SalvageShopPlugin.Log.LogWarning("the SALVAGE panel could not redraw (" + ex.Message + ").");
            }
        }

        private string RowText(int i, Module m, SalvageRules r, Ingredient common, Ingredient uncommon,
                               string exName, string bondName)
        {
            if (m == null)
                return "";

            if (Mode == SalvageMode.Salvage)
                return i == 0 ? "<color=" + Orange + ">> SALVAGE IT</color>" : "";

            int index = _top + i;

            if (index >= _offers.Count)
                return "";

            string id = _offers[index];
            SalvageUpgrade u = SalvageRecords.Get(m);
            int step = (u != null ? u.steps : 0) + 1;
            int ex, bond;
            r.PriceFor(id, step, out ex, out bond);

            bool afford = common != null && _vault.AmountOf(common) >= ex &&
                          (bond <= 0 || (uncommon != null && _vault.AmountOf(uncommon) >= bond));

            int have = u != null ? u.Count(id) : 0;
            bool sel = index == _cursor;

            string text = (sel ? "> " : "  ") + SalvageCatalog.StatName(id, m) + " " +
                          SalvageCatalog.Gives(id, 1, r, m) +
                          (have > 0 ? " <color=" + Grey + ">x" + have + "</color>" : "") + "   " +
                          "<color=" + (afford ? (sel ? Orange : "#FFFFFF") : Red) + ">" +
                          SalvageMath.Price(ex, bond, exName, bondName) + "</color>";

            return sel ? "<color=" + Orange + ">" + text + "</color>" : text;
        }

        private string Warning(Module m)
        {
            SalvageUpgrade u = SalvageRecords.Get(m);
            int rank = RankOf(m);
            var parts = new List<string>();

            if (u != null && u.steps > 0)
                parts.Add("ITS " + u.steps + " UPGRADE" + (u.steps == 1 ? "" : "S") + " GO (" +
                          SalvageMath.Num(SalvageRules.Current.refundPercent) + "% REFUNDED)");

            if (rank > 0)
                parts.Add("RANK " + rank + " IS LOST");

            return string.Join(", ", parts.ToArray()) + ".";
        }

        // ---- ingredients --------------------------------------------------------

        private static readonly Dictionary<string, Ingredient> _ings = new Dictionary<string, Ingredient>();

        // By id through the game's registry (the save uses the same one), then
        // by id, shown name or asset name - so "Ex" and "Fiber" both work.
        internal static Ingredient Ing(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            Ingredient i;

            if (_ings.TryGetValue(name, out i) && i != null)
                return i;

            i = null;

            try
            {
                IRegistry<Ingredient, string> reg;

                if (ServiceLocator.TryGet<IRegistry<Ingredient, string>>(out reg) && reg != null)
                    i = reg.Get(name);
            }
            catch (Exception)
            {
            }

            if (i == null)
            {
                foreach (Ingredient c in UnityEngine.Resources.FindObjectsOfTypeAll<Ingredient>())
                {
                    if (c != null && (string.Equals(c.id, name, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(c.displayName, name, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(c.name, "Ingredient " + name, StringComparison.OrdinalIgnoreCase)))
                    {
                        i = c;
                        break;
                    }
                }
            }

            if (i != null)
                _ings[name] = i;

            return i;
        }

        // ---- a ranked gun (Weapon Forge), by type name ------------------------

        private static MethodInfo _rankTryGet;
        private static bool _rankLooked;

        internal static void ForgetRankLookup()
        {
            if (_rankTryGet == null)
                _rankLooked = false;
        }

        // Its rank, or 0: no Weapon Forge, not a ranked gun, or rank 0.
        internal static int RankOf(Module m)
        {
            if (m == null)
                return 0;

            try
            {
                if (_rankTryGet == null && !_rankLooked)
                {
                    _rankLooked = true;
                    Type t = SalvageUi.FindTypeQuietly("WeaponForge.ForgeRankCopies");

                    if (t != null)
                    {
                        foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        {
                            ParameterInfo[] ps = mi.GetParameters();

                            if (mi.Name == "TryGet" && ps.Length == 2 && ps[0].ParameterType == typeof(Module) &&
                                ps[1].ParameterType.IsByRef)
                            {
                                _rankTryGet = mi;
                                break;
                            }
                        }
                    }
                }

                if (_rankTryGet == null)
                    return 0;

                var args = new object[] { m, null };

                if (!(bool)_rankTryGet.Invoke(null, args) || args[1] == null)
                    return 0;

                object p = Traverse.Create(args[1]).Field("p").GetValue();

                return p != null ? Traverse.Create(p).Field("rank").GetValue<int>() : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
