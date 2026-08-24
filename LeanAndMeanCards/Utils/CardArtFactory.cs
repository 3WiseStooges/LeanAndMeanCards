using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace LeanAndMeanCards.Utils
{
    internal sealed class MmCardArtTag : MonoBehaviour
    {
        public string ArtName;
    }

    internal static class CardArtFactory
    {
        private static readonly HashSet<string> RegisteredCardNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> CardNameToArt =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> MissingMiniLogged =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ArtNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static string _artFolder;
        private static bool _artFolderResolved;
        private static bool _artIndexBuilt;
        private static bool _folderLogged;
        private static Sprite _vanillaTemplateSprite;
        private static Type _nullCardInfoType;
        private static bool _nullCardInfoResolved;

        private static string DllDir =>
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";

        private static string ArtFolder
        {
            get
            {
                if (_artFolderResolved) return _artFolder;
                _artFolderResolved = true;
                var dllDir = DllDir;
                var besideDll = Path.Combine(dllDir, "Art");
                // r2modman often extracts Art/*.png next to the DLL instead of in Art/.
                if (DirHasPng(besideDll)) _artFolder = besideDll;
                else if (DirHasPng(dllDir)) _artFolder = dllDir;
                else
                {
                    var parent = Path.Combine(Directory.GetParent(dllDir)?.FullName ?? dllDir, "Art");
                    _artFolder = DirHasPng(parent) ? parent : besideDll;
                }

                if (!_folderLogged)
                {
                    _folderLogged = true;
                    Plugin.Instance?.Log($"Card art folder: {_artFolder}");
                }

                return _artFolder;
            }
        }

        private static readonly Dictionary<string, Sprite> FullSprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> MiniSprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();

        internal static GameObject Create(string artName)
        {
            if (string.IsNullOrEmpty(artName)) return null;
            if (Templates.TryGetValue(artName, out var cached) && cached != null) return cached;

            var sprite = GetFullSprite(artName);
            if (sprite == null) return null;

            // Toggle Cards / card-bar hover / picks parent art under a UI RectTransform.
            // SpriteRenderer never draws there — use Unity UI Image (same pattern as MADGEIOS).
            // No Canvas on the template → invisible on the main menu until Instantiated onto a card.
            var root = new GameObject("MM_CardArt_" + artName, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(root);

            var tag = root.AddComponent<MmCardArtTag>();
            tag.ArtName = artName;

            var image = root.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;

            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            // Shake / moving bg / glow scale — see CardArtFx + CardVisualsFxPatch.
            CardArtFx.AttachToTemplate(root, artName);

            Templates[artName] = root;
            return root;
        }

        internal static Sprite GetMiniSprite(string artName)
        {
            if (string.IsNullOrEmpty(artName)) return null;
            if (MiniSprites.TryGetValue(artName, out var cached) && cached != null) return cached;

            var path = ResolvePng(artName + "_mini.png") ?? ResolvePng(artName + ".png");
            if (path == null)
            {
                if (MissingMiniLogged.Add(artName))
                    Plugin.Instance?.LogWarn($"No mini PNG for '{artName}' in {ArtFolder} or next to the DLL");
                return null;
            }

            try
            {
                var texture = LoadTexture(path);
                if (texture == null) return null;
                var sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                MiniSprites[artName] = sprite;
                return sprite;
            }
            catch
            {
                return null;
            }
        }

        internal static bool IsNullPlaceholder(CardInfo card)
        {
            if (card == null) return false;
            var nullType = NullCardInfoType();
            if (nullType != null && nullType.IsInstanceOfType(card)) return true;
            if (GameObjectHasNullCardInfo(card.gameObject)) return true;

            var name = (card.cardName ?? "").Trim();
            if (name.StartsWith("[]", StringComparison.Ordinal)) return true;
            if (name.Equals("null", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals("NullCard", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals("Null Card", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static bool GameObjectHasNullCardInfo(GameObject go)
        {
            if (go == null) return false;
            var nullType = NullCardInfoType();
            if (nullType == null) return false;
            return go.GetComponent(nullType) != null;
        }

        private static Type NullCardInfoType()
        {
            if (_nullCardInfoResolved) return _nullCardInfoType;
            _nullCardInfoResolved = true;
            _nullCardInfoType = AccessTools.TypeByName("Nullmanager.NullCardInfo");
            return _nullCardInfoType;
        }

        internal static void TryAssignSprite(CardInfo info)
        {
            try
            {
                if (info == null || IsNullPlaceholder(info)) return;
                var artName = ArtNameFor(info);
                if (string.IsNullOrEmpty(artName)) return;

                RegisterCard(info, artName);
                var mini = GetMiniSprite(artName);
                if (mini == null) return;

                var old = info.sprite;
                if (_vanillaTemplateSprite == null && old != null && old != mini)
                    _vanillaTemplateSprite = old;

                info.sprite = mini;
                CardBarMiniIcons.AttachFancyIcon(info);
                StampPickIcons(info, mini, old);
            }
            catch (Exception ex)
            {
                // Called from CardInfo.Awake. Never throw into Photon spawn.
                Plugin.Instance?.LogWarn($"TryAssignSprite skipped: {ex.Message}");
            }
        }

        internal static void RegisterCard(CardInfo info, string artName)
        {
            if (info == null || string.IsNullOrEmpty(artName)) return;
            if (IsNullPlaceholder(info)) return;
            if (!string.IsNullOrEmpty(info.cardName))
            {
                RegisteredCardNames.Add(info.cardName);
                CardNameToArt[info.cardName] = artName;
            }

            // Stamp the CardInfo itself so Photon / local clones still identify as ours
            // after cardArt loses the runtime tag.
            var tag = info.GetComponent<MmCardArtTag>();
            if (tag == null) tag = info.gameObject.AddComponent<MmCardArtTag>();
            if (string.IsNullOrEmpty(tag.ArtName)) tag.ArtName = artName;
        }

        internal static bool IsRegisteredCardName(string cardName)
        {
            return !string.IsNullOrEmpty(cardName) && CardNameToArt.ContainsKey(cardName);
        }

        internal static string ArtNameFor(CardInfo info)
        {
            if (info == null || IsNullPlaceholder(info)) return null;
            var tag = info.GetComponent<MmCardArtTag>();
            if (tag != null && !string.IsNullOrEmpty(tag.ArtName)) return tag.ArtName;
            if (info.cardArt != null)
            {
                var artTag = info.cardArt.GetComponent<MmCardArtTag>();
                if (artTag != null && !string.IsNullOrEmpty(artTag.ArtName)) return artTag.ArtName;
            }

            if (info.sourceCard != null && info.sourceCard != info)
            {
                var fromSource = ArtNameFor(info.sourceCard);
                if (!string.IsNullOrEmpty(fromSource)) return fromSource;
            }

            return GuessArtName(info.cardName);
        }

        internal static string GuessArtName(string cardName)
        {
            if (string.IsNullOrEmpty(cardName)) return null;
            var trimmed = cardName.Trim();
            if (trimmed.StartsWith("[]", StringComparison.Ordinal)) return null;
            if (CardNameToArt.TryGetValue(cardName, out var mapped)) return mapped;

            EnsureArtIndex();
            var compact = Compact(cardName);
            if (ArtNames.Contains(compact)) return compact;
            if (compact == "sandbagsimulator" && ArtNames.Contains("sandbag")) return "sandbag";
            return null;
        }

        internal static void BindLoadedCardInfos()
        {
            foreach (var info in Resources.FindObjectsOfTypeAll<CardInfo>())
                TryAssignSprite(info);
        }

        /// <summary>
        /// Vanilla copies CardInfo.sprite onto small corner Images at prefab build.
        /// Changing the field later does not retarget those Images, and ChangeSelected
        /// tints them HDR white so bloom washes the mini. Push the PNG onto them and lock color.
        /// </summary>
        private static void StampPickIcons(CardInfo info, Sprite mini, Sprite oldSprite)
        {
            if (info == null || mini == null) return;

            foreach (var img in info.GetComponentsInChildren<Image>(true))
            {
                if (!IsStampableImage(img)) continue;
                if (!SpriteMatches(img.sprite, mini, oldSprite)) continue;
                ApplyMini(img, mini);
            }

            foreach (var sr in info.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null) continue;
                if (sr.GetComponent<MmCardArtTag>() != null) continue;
                if (!SpriteMatches(sr.sprite, mini, oldSprite)) continue;
                sr.sprite = mini;
                sr.color = MmArtColorLock.ArtColor;
                if (sr.GetComponent<MmArtColorLock>() == null)
                    sr.gameObject.AddComponent<MmArtColorLock>();
            }

            StampCornerCluster(info, mini);
        }

        private static void StampCornerCluster(CardInfo info, Sprite mini)
        {
            Transform edge = null;
            foreach (var t in info.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == "EdgePart (2)")
                {
                    edge = t;
                    break;
                }
            }

            if (edge == null) return;

            // Only this corner. Searching the whole card face would retint stats / rarity gems.
            Image first = null;
            foreach (var img in edge.GetComponentsInChildren<Image>(true))
            {
                if (!IsStampableImage(img)) continue;
                if (img.transform.name.StartsWith("EdgePart", StringComparison.Ordinal)) continue;

                var rt = img.rectTransform;
                var w = Mathf.Abs(rt.rect.width);
                var h = Mathf.Abs(rt.rect.height);
                if (w > 140f || h > 140f) continue;
                if (w < 8f && h < 8f) continue;

                if (first == null)
                {
                    first = img;
                    ApplyMini(img, mini);
                }
                else if (SpriteMatches(img.sprite, mini, _vanillaTemplateSprite) || img.sprite == first.sprite)
                {
                    img.enabled = false;
                }
            }
        }

        private static bool IsStampableImage(Image img)
        {
            if (img == null) return false;
            if (img.GetComponent<MmCardArtTag>() != null) return false;
            if (img.GetComponent<MmMovingCardBackground>() != null) return false;
            var n = img.gameObject.name;
            if (n.StartsWith("MM_", StringComparison.Ordinal)) return false;
            if (n == "ModNameText") return false;
            return true;
        }

        private static bool SpriteMatches(Sprite current, Sprite mini, Sprite oldSprite)
        {
            if (current == null) return false;
            if (current == mini) return true;
            if (oldSprite != null && current == oldSprite) return true;
            return _vanillaTemplateSprite != null && current == _vanillaTemplateSprite;
        }

        private static void ApplyMini(Image img, Sprite mini)
        {
            img.sprite = mini;
            img.overrideSprite = mini;
            img.preserveAspect = true;
            img.color = MmArtColorLock.ArtColor;
            img.enabled = true;
            if (img.GetComponent<MmArtColorLock>() == null)
                img.gameObject.AddComponent<MmArtColorLock>();
        }

        private static Sprite GetFullSprite(string artName)
        {
            if (FullSprites.TryGetValue(artName, out var cached) && cached != null) return cached;

            var path = ResolvePng(artName + ".png");
            if (path == null) return null;

            try
            {
                var texture = LoadTexture(path);
                if (texture == null) return null;
                var sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                FullSprites[artName] = sprite;
                return sprite;
            }
            catch
            {
                return null;
            }
        }

        private static string ResolvePng(string fileName)
        {
            var inArt = Path.Combine(ArtFolder, fileName);
            if (File.Exists(inArt)) return inArt;

            var dllDir = DllDir;
            var nextToDll = Path.Combine(dllDir, fileName);
            if (File.Exists(nextToDll)) return nextToDll;

            var inDllArt = Path.Combine(dllDir, "Art", fileName);
            if (File.Exists(inDllArt)) return inDllArt;

            return null;
        }

        private static bool DirHasPng(string dir)
        {
            try
            {
                return Directory.Exists(dir) && Directory.GetFiles(dir, "*.png").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureArtIndex()
        {
            if (_artIndexBuilt) return;
            _artIndexBuilt = true;
            try
            {
                IndexDir(ArtFolder);
                IndexDir(DllDir);
                IndexDir(Path.Combine(DllDir, "Art"));
            }
            catch
            {
            }
        }

        private static void IndexDir(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrEmpty(name)) continue;
                if (name.EndsWith("_mini", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - 5);
                ArtNames.Add(name);
            }
        }

        private static string Compact(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }

        private static Texture2D LoadTexture(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }

            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            UnityEngine.Object.DontDestroyOnLoad(texture);
            return texture;
        }
    }
}
