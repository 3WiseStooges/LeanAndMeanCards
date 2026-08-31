using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// A LOCK button hanging under each offered card, for the sniper only.
    ///
    /// The old version listened for a bare left click anywhere near a card. That raced the
    /// picker: by the time a lock landed the card had usually already been taken, so the
    /// sniper got a "pick another" prompt for a pick that was over. A button is its own
    /// answer to that — it exists only while the offer is live, it says which card it locks,
    /// and it cannot be confused with picking.
    ///
    /// The buttons are built as children of each card's own world-space Canvas, so they
    /// track the card's position and scale for free and die with it. They are created on the
    /// sniper's client only and are never networked, so the picker never sees them.
    /// </summary>
    internal static class DraftSniperLockUi
    {
        private const string ButtonName = "MM_DraftSniperLock";

        private static readonly List<DraftSniperLockButton> Live = new List<DraftSniperLockButton>();

        internal static void Sync(List<GameObject> cards, int locksLeft)
        {
            if (cards == null || cards.Count == 0 || locksLeft <= 0)
            {
                Clear();
                return;
            }

            for (var i = Live.Count - 1; i >= 0; i--)
            {
                var live = Live[i];
                if (live == null || live.Card == null || !cards.Contains(live.Card))
                {
                    Destroy(i);
                }
            }

            foreach (var card in cards)
            {
                if (card == null) continue;
                var button = Find(card) ?? Build(card);
                button?.Refresh(locksLeft);
            }
        }

        internal static void Clear()
        {
            for (var i = Live.Count - 1; i >= 0; i--) Destroy(i);
            Live.Clear();
        }

        private static void Destroy(int index)
        {
            if (index < 0 || index >= Live.Count) return;
            var button = Live[index];
            Live.RemoveAt(index);
            if (button != null) Object.Destroy(button.gameObject);
        }

        private static DraftSniperLockButton Find(GameObject card)
        {
            foreach (var live in Live)
            {
                if (live != null && live.Card == card) return live;
            }

            return null;
        }

        private static DraftSniperLockButton Build(GameObject card)
        {
            try
            {
                // The card's own canvas: same anchor ApplyLockVisual uses, so the button and
                // the locked-out overlay always agree about which object is the card.
                var canvas = card.GetComponentInChildren<Canvas>(true);
                var parent = canvas != null ? canvas.transform as RectTransform : null;
                if (parent == null) return null;

                var go = new GameObject(ButtonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(parent, false);
                go.transform.SetAsLastSibling();

                // Sized from the card so this works whatever units the card prefab uses.
                var height = Mathf.Max(24f, parent.rect.height * 0.115f);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(0.08f, 0f);
                rect.anchorMax = new Vector2(0.92f, 0f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(0f, height);
                rect.anchoredPosition = new Vector2(0f, -height * 0.28f);
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;

                // Solid, not UiGfx.Rounded: that sprite is sliced at a fixed pixel radius and
                // a card's canvas rect is not in the same units as the pack's own overlays.
                var image = UiGfx.Solid(go.GetComponent<Image>(), DraftSniperLockButton.ReadyColor, raycast: true);

                var labelGo = new GameObject("Label", typeof(RectTransform));
                labelGo.transform.SetParent(go.transform, false);
                var labelRect = (RectTransform)labelGo.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                var label = labelGo.AddComponent<TextMeshProUGUI>();
                UiGfx.StyleTmp(label, height * 0.5f, UiGfx.Title, TextAlignmentOptions.Center, wrap: false);
                label.fontStyle = FontStyles.Bold;
                label.enableAutoSizing = true;
                label.fontSizeMin = 6f;
                label.fontSizeMax = height * 0.58f;

                var button = go.AddComponent<Button>();
                button.targetGraphic = image;
                UiGfx.GhostColors(button);

                var behaviour = go.AddComponent<DraftSniperLockButton>();
                behaviour.Bind(card, button, image, label);
                Live.Add(behaviour);
                return behaviour;
            }
            catch
            {
                // A missing button is a card that cannot be sniped, not a broken pick phase.
                return null;
            }
        }
    }

    internal sealed class DraftSniperLockButton : MonoBehaviour
    {
        internal static readonly Color ReadyColor = new Color(0.58f, 0.14f, 0.16f, 0.95f);
        private static readonly Color Pending = new Color(0.36f, 0.30f, 0.12f, 0.95f);
        private static readonly Color Done = new Color(0.10f, 0.10f, 0.12f, 0.92f);

        private Button _button;
        private Image _image;
        private TextMeshProUGUI _label;
        private float _pendingUntil;

        internal GameObject Card { get; private set; }

        internal void Bind(GameObject card, Button button, Image image, TextMeshProUGUI label)
        {
            Card = card;
            _button = button;
            _image = image;
            _label = label;
            _button.onClick.AddListener(OnClick);
        }

        internal void Refresh(int locksLeft)
        {
            if (_label == null || _image == null || _button == null) return;

            if (DraftSniperManager.IsBlocked(Card))
            {
                Set(Done, "LOCKED", interactable: false);
                return;
            }

            // Held briefly after a click: the host is the one that decides, and until its
            // answer arrives a second click would just burn another lock on the same card.
            if (Time.unscaledTime < _pendingUntil)
            {
                Set(Pending, "LOCKING...", interactable: false);
                return;
            }

            Set(ReadyColor, locksLeft > 1 ? $"LOCK ({locksLeft})" : "LOCK", interactable: true);
        }

        private void Set(Color color, string text, bool interactable)
        {
            _image.color = color;
            _label.text = text;
            _label.color = interactable ? new Color(1f, 0.95f, 0.88f) : new Color(0.72f, 0.72f, 0.74f);
            _button.interactable = interactable;
        }

        private void OnClick()
        {
            if (Card == null) return;
            if (!DraftSniperManager.TryLock(Card)) return;
            _pendingUntil = Time.unscaledTime + 0.8f;
        }
    }
}
