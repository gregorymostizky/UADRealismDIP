using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using Il2CppUiExt;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace TweaksAndFixes
{
    internal static class GGAdvancedBattleAIOption
    {
        private const string PrefKey = "gg_advanced_battle_ai_enabled";
        private const string ButtonName = "GGAdvancedBattleAIButton";
        private const string MenuName = "GG Advanced Battle AI";

        private static Button _button;
        private static Image _image;
        private static Outline _outline;
        private static GameObject _menu;
        private static bool _initialized;

        internal static bool Enabled => PlayerPrefs.GetInt(PrefKey, 1) != 0;

        internal static void Start()
        {
            if (_initialized)
                return;

            try
            {
                SetupButton();
            }
            catch (Exception ex)
            {
                Melon<TweaksAndFixes>.Logger.Warning($"GG Advanced Battle AI button skipped. {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static void RefreshButton()
        {
            if (_button == null)
                return;

            _button.interactable = true;
            ApplyButtonState();
        }

        private static void SetupButton()
        {
            GameObject options = ModUtils.GetChildAtPath("Global/Ui/UiMain/Common/Options");
            GameObject helpButton = ModUtils.GetChildAtPath("Global/Ui/UiMain/Common/Options/Help");
            if (options == null || helpButton == null)
                return;

            GameObject existing = options.transform.Find(ButtonName)?.gameObject;
            GameObject buttonObject = existing ?? GameObject.Instantiate(helpButton);
            buttonObject.transform.SetParent(options.transform);
            buttonObject.name = ButtonName;
            buttonObject.SetActive(true);
            MatchButtonSizing(buttonObject, helpButton);

            // Patch intent: expose the player-battle positioning/scout-screen
            // behavior as a small in-game menu instead of another CSV flag.
            // The launcher is available wherever the top-right UI exists.
            buttonObject.TryDestroyComponent<OnEnter>();
            buttonObject.TryDestroyComponent<OnLeave>();
            AddDynamicTooltip(buttonObject);

            Transform imageChild = buttonObject.transform.Find("Image");
            if (imageChild != null && imageChild.TryGetComponent<Image>(out Image img))
            {
                _image = img;
                Sprite sprite = TryLoadButtonSpriteFromGame();
                if (sprite != null)
                {
                    _image.sprite = sprite;
                    _image.preserveAspect = true;
                }

                ScaleLauncherIcon(imageChild);
            }

            _outline = buttonObject.GetComponent<Outline>() ?? buttonObject.AddComponent<Outline>();
            _outline.effectDistance = new Vector2(1f, 1f);

            _button = buttonObject.GetComponent<Button>();
            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                _button.onClick.AddListener(new System.Action(OpenMenu));
            }

            _initialized = true;
            ApplyButtonState();
        }

        private static void OpenMenu()
        {
            if (_menu != null)
            {
                _menu.transform.SetAsLastSibling();
                _menu.SetActive(true);
                if (_button != null)
                    _button.interactable = false;
                return;
            }

            GameObject popupTemplate = ModUtils.GetChildAtPath("Global/Ui/UiMain/Popup/PopupMenu");
            GameObject popupRoot = ModUtils.GetChildAtPath("Global/Ui/UiMain/Popup/");
            if (popupTemplate == null || popupRoot == null)
            {
                Melon<TweaksAndFixes>.Logger.Warning("GG Advanced Battle AI menu skipped. Popup template not found.");
                return;
            }

            _menu = GameObject.Instantiate(popupTemplate);
            _menu.transform.SetParent(popupRoot);
            _menu.name = MenuName;
            _menu.transform.SetScale(1f, 1f, 1f);
            _menu.transform.localPosition = Vector3.zero;

            RectTransform rootRect = _menu.GetComponent<RectTransform>();
            if (rootRect != null)
            {
                rootRect.anchorMin = Vector2.zero;
                rootRect.anchorMax = Vector2.one;
                rootRect.offsetMin = Vector2.zero;
                rootRect.offsetMax = Vector2.zero;
            }

            GameObject bg = _menu.GetChild("Bg");
            if (bg != null)
            {
                bg.transform.SetAsFirstSibling();
                RectTransform bgRect = bg.GetComponent<RectTransform>();
                if (bgRect != null)
                {
                    bgRect.anchorMin = Vector2.zero;
                    bgRect.anchorMax = Vector2.one;
                    bgRect.offsetMin = Vector2.zero;
                    bgRect.offsetMax = Vector2.zero;
                }

                Image bgImage = bg.GetComponent<Image>() ?? bg.AddComponent<Image>();
                bgImage.color = new Color(0f, 0f, 0f, 0.6f);
                bgImage.raycastTarget = true;
            }

            GameObject window = _menu.GetChild("Window");
            if (window == null)
            {
                _menu.TryDestroy();
                _menu = null;
                return;
            }

            ClearWindowButtons(window);
            AddMenuButton(window, "Advanced Battle AI", true, ToggleAdvancedBattleAI);
            AddMenuButton(window, "Close", false, CloseMenu);

            _menu.transform.SetAsLastSibling();
            _menu.SetActive(true);
            if (_button != null)
                _button.interactable = false;
        }

        private static void ToggleAdvancedBattleAI()
        {
            bool enabled = !Enabled;
            PlayerPrefs.SetInt(PrefKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            RefreshMenuLabels();
            ApplyButtonState();
            Melon<TweaksAndFixes>.Logger.Msg($"GG Advanced Battle AI {(enabled ? "enabled" : "disabled")}");
        }

        private static void ApplyButtonState()
        {
            if (_image != null)
                _image.color = Color.white;

            if (_outline != null)
                _outline.effectColor = Color.white;
        }

        private static void AddDynamicTooltip(GameObject buttonObject)
        {
            OnEnter onEnter = buttonObject.AddComponent<OnEnter>();
            onEnter.action = new System.Action(() =>
            {
                if (G.ui == null || _button == null || !_button.interactable)
                    return;

                G.ui.ShowTooltip($"Advanced Battle AI: {(Enabled ? "On" : "Off")}", buttonObject);
            });

            OnLeave onLeave = buttonObject.AddComponent<OnLeave>();
            onLeave.action = new System.Action(() =>
            {
                try { G.ui?.HideTooltip(); }
                catch { }
            });
        }

        private static Sprite TryLoadButtonSpriteFromGame()
        {
            Sprite item = Resources.Load<Sprite>("tabs/tech");
            if (item != null)
                return item.TryCast<Sprite>();

            item = Resources.Load<Sprite>("tabs/fleet");
            return item != null ? item.TryCast<Sprite>() : null;
        }

        private static void MatchButtonSizing(GameObject target, GameObject template)
        {
            target.transform.localScale = template.transform.localScale;

            RectTransform targetRect = target.GetComponent<RectTransform>();
            RectTransform templateRect = template.GetComponent<RectTransform>();
            if (targetRect != null && templateRect != null)
            {
                targetRect.anchorMin = templateRect.anchorMin;
                targetRect.anchorMax = templateRect.anchorMax;
                targetRect.pivot = templateRect.pivot;
                targetRect.sizeDelta = templateRect.sizeDelta;
                targetRect.localScale = templateRect.localScale;
            }

            LayoutElement targetLayout = target.GetComponent<LayoutElement>();
            LayoutElement templateLayout = template.GetComponent<LayoutElement>();
            if (targetLayout != null && templateLayout != null)
            {
                targetLayout.minWidth = templateLayout.minWidth;
                targetLayout.minHeight = templateLayout.minHeight;
                targetLayout.preferredWidth = templateLayout.preferredWidth;
                targetLayout.preferredHeight = templateLayout.preferredHeight;
                targetLayout.flexibleWidth = templateLayout.flexibleWidth;
                targetLayout.flexibleHeight = templateLayout.flexibleHeight;
            }

            Transform targetImage = target.transform.Find("Image");
            Transform templateImage = template.transform.Find("Image");
            if (targetImage == null || templateImage == null)
                return;

            RectTransform targetImageRect = targetImage.GetComponent<RectTransform>();
            RectTransform templateImageRect = templateImage.GetComponent<RectTransform>();
            if (targetImageRect == null || templateImageRect == null)
                return;

            targetImageRect.anchorMin = templateImageRect.anchorMin;
            targetImageRect.anchorMax = templateImageRect.anchorMax;
            targetImageRect.pivot = templateImageRect.pivot;
            targetImageRect.sizeDelta = templateImageRect.sizeDelta;
            targetImageRect.localScale = templateImageRect.localScale;
        }

        private static void ScaleLauncherIcon(Transform imageChild)
        {
            RectTransform rect = imageChild.GetComponent<RectTransform>();
            if (rect != null)
                rect.localScale *= 0.67f;
            else
                imageChild.localScale *= 0.67f;
        }

        private static void ClearWindowButtons(GameObject window)
        {
            for (int i = window.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = window.transform.GetChild(i).gameObject;
                if (child.GetComponent<Button>() != null)
                    child.TryDestroy();
            }
        }

        private static void AddMenuButton(GameObject window, string label, bool showState, System.Action onPress)
        {
            GameObject buttonTemplate = ModUtils.GetChildAtPath("Global/Ui/UiMain/Popup/PopupMenu/Window/ButtonBase");
            if (buttonTemplate == null)
                return;

            GameObject buttonObject = GameObject.Instantiate(buttonTemplate);
            buttonObject.transform.SetParent(window.transform, false);
            buttonObject.name = "GG_AdvancedBattleAI_" + label.Replace(" ", "_");
            buttonObject.SetActive(true);
            buttonObject.transform.localPosition = Vector3.zero;
            buttonObject.transform.localScale = Vector3.one;

            Button button = buttonObject.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(new System.Action(onPress));
            }

            SetMenuButtonText(buttonObject, showState ? OptionLabel(label) : label);
        }

        private static void RefreshMenuLabels()
        {
            if (_menu == null)
                return;

            GameObject option = _menu.GetChild("Window")?.GetChild("GG_AdvancedBattleAI_Advanced_Battle_AI");
            if (option != null)
                SetMenuButtonText(option, OptionLabel("Advanced Battle AI"));
        }

        private static string OptionLabel(string label)
            => $"{label}: {(Enabled ? "On" : "Off")}";

        private static void SetMenuButtonText(GameObject buttonObject, string text)
        {
            GameObject textObject = buttonObject.GetChild("Text (TMP)");
            if (textObject != null && textObject.TryGetComponent<TMP_Text>(out TMP_Text tmp))
            {
                textObject.TryDestroyComponent<LocalizeText>();
                tmp.text = text;
                return;
            }

            Text uiText = buttonObject.GetComponentInChildren<Text>();
            if (uiText != null)
                uiText.text = text;
        }

        private static void CloseMenu()
        {
            if (_menu != null)
                _menu.SetActive(false);

            if (_button != null)
                _button.interactable = true;
        }

    }
}
