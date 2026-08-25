using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using ExtensibleSaveFormat;
using HarmonyLib;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using Studio;
using UnityEngine;
using UnityEngine.UI;

namespace KK_osr2_sr6_link
{
    /// <summary>
    /// Stores only the shared action profile key in the scene card. The action files stay in the
    /// desktop profile library so every bound scene observes the same saved curves.
    /// </summary>
    public sealed class SceneProfileController : SceneCustomFunctionController
    {
        public const string DataId = "org.bepinex.plugins.osr2_sr6_link.scene-profile";
        private const string ProfileKeyField = "profileKey";
        private static bool registered;
        private static string redirectScenePath = "";

        public static void Register()
        {
            if (registered) return;
            StudioSaveLoadApi.RegisterExtraBehaviour<SceneProfileController>(DataId);
            ExtendedSave.SceneBeingSaved += SceneBeingSaved;
            registered = true;
        }

        /// <summary>Runs the complete Studio save flow while redirecting its generated card path.</summary>
        public static bool TrySaveCurrentCard(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                var studio = Singleton<Studio.Studio>.Instance;
                if (studio == null) return false;
                redirectScenePath = path;
                Osr2_sr6_link.LogInfo("profile card save: invoking Studio.SaveScene");
                studio.SaveScene();
                return true;
            }
            catch (Exception ex)
            {
                Osr2_sr6_link.LogInfo("full Studio card save failed: " + ex);
                return false;
            }
            finally { redirectScenePath = ""; }
        }

        protected override void OnSceneLoad(SceneOperationKind operation, ReadOnlyDictionary<int, ObjectCtrlInfo> loadedItems)
        {
            if (operation == SceneOperationKind.Import)
            {
                Osr2_sr6_link.LogInfo("scene profile load operation=Import profile=" +
                    (string.IsNullOrEmpty(Osr2_sr6_link.CurrentProfileKey) ? "<none>" : Osr2_sr6_link.CurrentProfileKey));
                return;
            }
            if (operation == SceneOperationKind.Clear)
            {
                Osr2_sr6_link.TrySetCurrentProfileKey("");
                Osr2_sr6_link.LogInfo("scene profile load operation=Clear profile=<none>");
                return;
            }

            PluginData data = GetExtendedData();
            string key = "";
            object value;
            if (data != null && data.version == 1 && data.data != null && data.data.TryGetValue(ProfileKeyField, out value))
            {
                string stored = value as string;
                if (!string.IsNullOrEmpty(stored) && Osr2_sr6_link.IsValidProfileKey(stored)) key = stored;
            }
            Osr2_sr6_link.TrySetCurrentProfileKey(key);
            Osr2_sr6_link.LogInfo("scene profile load operation=" + operation + " profile=" +
                (string.IsNullOrEmpty(key) ? "<none>" : key));
            Osr2_sr6_link.SendCurrentSceneMessage();
        }

        protected override void OnSceneSave()
        {
            string key = Osr2_sr6_link.CurrentProfileKey;
            if (string.IsNullOrEmpty(key))
            {
                Osr2_sr6_link.LogInfo("scene card metadata save: clearing profile key");
                SetExtendedData(null);
                return;
            }

            Osr2_sr6_link.LogInfo("scene card metadata save: profile=" + key);
            SetExtendedData(new PluginData
            {
                version = 1,
                data = new Dictionary<string, object> { { ProfileKeyField, key } },
            });
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(SceneInfo), "Save", new[] { typeof(string) })]
        private static void RedirectGeneratedScenePath(ref string __0)
        {
            if (string.IsNullOrEmpty(redirectScenePath)) return;
            Osr2_sr6_link.LogInfo("redirecting SceneInfo.Save path from " + __0 + " to " + redirectScenePath);
            __0 = redirectScenePath;
        }

        private static void SceneBeingSaved(string path)
        {
            try
            {
                string rawPath = Osr2_sr6_link.SceneDataPath(path);
                if (string.IsNullOrEmpty(rawPath))
                {
                    Osr2_sr6_link.LogInfo("scene profile reference save skipped: path unmapped=" + path);
                    return;
                }
                string refPath = Osr2_sr6_link.SceneRefPath(rawPath);
                string key = Osr2_sr6_link.CurrentProfileKey;
                if (string.IsNullOrEmpty(key))
                {
                    if (File.Exists(refPath)) File.Delete(refPath);
                    Osr2_sr6_link.LogInfo("scene profile reference cleared: " + refPath);
                    return;
                }

                string directory = Path.GetDirectoryName(refPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(refPath, key + Environment.NewLine, new UTF8Encoding(false));
                Osr2_sr6_link.LogInfo("scene profile reference saved: " + refPath + " key=" + key);
            }
            catch (Exception ex) { Debug.Log("Osr2 sr6 profile reference save failed: " + ex.Message); }
        }

        // SceneLoadScene creates its page objects after InitInfo and again when SetPage runs.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SceneLoadScene), "InitInfo")]
        private static void InitInfoPostfix(SceneLoadScene __instance) { RefreshBadges(__instance, 0, "InitInfo"); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SceneLoadScene), "SetPage", new[] { typeof(int) })]
        private static void SetPagePostfix(SceneLoadScene __instance, int __0) { RefreshBadges(__instance, __0, "SetPage"); }

        private enum BadgeState { None, Shared, Legacy, Broken }

        private static void RefreshBadges(SceneLoadScene scene, int page, string source)
        {
            try
            {
                object paths = Field(scene, "listPath");
                object buttons = Field(scene, "buttonThumbnail");
                object thumbnailObject = Field(scene, "thumbnailNum");
                int thumbnailCount = ToInt(thumbnailObject);
                int pageNum = ToInt(Field(scene, "pageNum"));
                int selected = ToInt(Field(scene, "select"));
                if (paths == null || buttons == null)
                {
                    Osr2_sr6_link.LogInfo("scene badge refresh source=" + source + " page=" + page +
                        " missing listPath/buttonThumbnail");
                    return;
                }

                int buttonCount = Count(buttons);
                int pageSize = buttonCount;
                Osr2_sr6_link.LogInfo("scene badge refresh source=" + source + " page=" + page +
                    " pageNum=" + pageNum + " select=" + selected + " listCount=" + Count(paths) +
                    " thumbnailCount=" + thumbnailCount + " buttonCount=" + buttonCount +
                    " pageSize=" + pageSize);
                for (int i = 0; i < buttonCount; i++)
                {
                    int absoluteIndex = page * pageSize + i;
                    string path = StringAt(paths, absoluteIndex);

                    GameObject button = AsGameObject(ValueAt(buttons, i));
                    if (button == null)
                    {
                        Osr2_sr6_link.LogInfo("scene badge slot=" + i + " absolute=" + absoluteIndex +
                            " button=<null> path=" + path);
                        continue;
                    }
                    BadgeState state = string.IsNullOrEmpty(path) ? BadgeState.None : GetBadgeState(path);
                    Osr2_sr6_link.LogInfo("scene badge slot=" + i + " absolute=" + absoluteIndex +
                        " path=" + path + " state=" + state);
                    SetBadge(button, state);
                }
            }
            catch (Exception ex)
            {
                Osr2_sr6_link.LogInfo("scene badge refresh failed: " + ex);
            }
        }

        private static BadgeState GetBadgeState(string scenePath)
        {
            string rawPath = Osr2_sr6_link.SceneDataPath(scenePath);
            if (string.IsNullOrEmpty(rawPath))
            {
                Osr2_sr6_link.LogInfo("scene badge path unmapped: " + scenePath);
                return BadgeState.None;
            }

            string refPath = Osr2_sr6_link.SceneRefPath(rawPath);
            if (File.Exists(refPath))
            {
                string key = ReadProfileKey(refPath);
                bool complete = !string.IsNullOrEmpty(key) && HasActionSet(Osr2_sr6_link.ProfileStem(key));
                BadgeState state = complete ? BadgeState.Shared : BadgeState.Broken;
                Osr2_sr6_link.LogInfo("scene badge shared scene=" + scenePath + " raw=" + rawPath +
                    " ref=" + refPath + " key=" + (string.IsNullOrEmpty(key) ? "<invalid>" : key) +
                    " complete=" + complete + " state=" + state);
                return state;
            }

            bool legacy = HasActionSet(rawPath);
            BadgeState legacyState = legacy ? BadgeState.Legacy : BadgeState.None;
            Osr2_sr6_link.LogInfo("scene badge legacy scene=" + scenePath + " raw=" + rawPath +
                " ref=<none> complete=" + legacy + " state=" + legacyState);
            return legacyState;
        }

        private static string ReadProfileKey(string path)
        {
            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length == 0 || lines.Length > 1 || !Osr2_sr6_link.IsValidProfileKey(lines[0])) return "";
                return lines[0];
            }
            catch { return ""; }
        }

        private static bool HasActionSet(string stem)
        {
            string actionStem = stem.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? stem.Substring(0, stem.Length - 4)
                : stem;
            for (int i = 0; i < 6; i++)
            {
                string path = actionStem + AxisSuffix(i) + ".sr6script";
                if (!File.Exists(path))
                {
                    Osr2_sr6_link.LogInfo("scene badge action missing: " + path);
                    return false;
                }
                if (new FileInfo(path).Length == 0)
                {
                    Osr2_sr6_link.LogInfo("scene badge action empty: " + path);
                    return false;
                }
                try
                {
                    string text = File.ReadAllText(path);
                    if (text.IndexOf("\"actions\"", StringComparison.Ordinal) < 0)
                    {
                        Osr2_sr6_link.LogInfo("scene badge action malformed: " + path);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Osr2_sr6_link.LogInfo("scene badge action read failed: " + path + " error=" + ex.Message);
                    return false;
                }
            }

            string cfg = actionStem + ".sr6cfg";
            if (!File.Exists(cfg))
            {
                Osr2_sr6_link.LogInfo("scene badge cfg missing: " + cfg);
                return false;
            }
            if (new FileInfo(cfg).Length == 0)
            {
                Osr2_sr6_link.LogInfo("scene badge cfg empty: " + cfg);
                return false;
            }
            try
            {
                bool valid = File.ReadAllText(cfg).TrimStart().StartsWith("[", StringComparison.Ordinal);
                if (!valid) Osr2_sr6_link.LogInfo("scene badge cfg malformed: " + cfg);
                else Osr2_sr6_link.LogInfo("scene badge action set complete: " + actionStem);
                return valid;
            }
            catch (Exception ex)
            {
                Osr2_sr6_link.LogInfo("scene badge cfg read failed: " + cfg + " error=" + ex.Message);
                return false;
            }
        }

        private static string AxisSuffix(int axis)
        {
            switch (axis)
            {
                case 0: return "";
                case 1: return ".surge";
                case 2: return ".sway";
                case 3: return ".twist";
                case 4: return ".roll";
                default: return ".pitch";
            }
        }

        private static void SetBadge(GameObject thumbnail, BadgeState state)
        {
            Transform existing = thumbnail.transform.Find("KKOsr2Sr6Link.SR6Badge");
            GameObject badge = existing == null ? CreateBadge(thumbnail.transform) : existing.gameObject;
            if (state == BadgeState.None)
            {
                badge.SetActive(false);
                return;
            }

            badge.SetActive(true);
            Image image = badge.GetComponent<Image>();
            Text text = badge.transform.Find("Text").GetComponent<Text>();
            if (state == BadgeState.Shared)
            {
                image.color = new Color(0.1f, 0.65f, 0.2f, 0.95f);
                text.text = "SR6";
            }
            else if (state == BadgeState.Legacy)
            {
                image.color = new Color(0.35f, 0.35f, 0.4f, 0.95f);
                text.text = "SR6";
            }
            else
            {
                image.color = new Color(0.75f, 0.1f, 0.1f, 0.95f);
                text.text = "SR6!";
            }
        }

        private static GameObject CreateBadge(Transform parent)
        {
            var badge = new GameObject("KKOsr2Sr6Link.SR6Badge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(parent, false);
            var rect = badge.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(4, -4);
            rect.sizeDelta = new Vector2(40, 20);
            var labelObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(badge.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 11;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            badge.GetComponent<Image>().raycastTarget = false;
            label.raycastTarget = false;
            return badge;
        }

        private static object Field(object instance, string name)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return field == null ? null : field.GetValue(instance);
        }

        private static int ToInt(object value)
        {
            try { return Convert.ToInt32(value); }
            catch { return 0; }
        }

        private static int Count(object value)
        {
            if (value is ICollection collection) return collection.Count;
            return 0;
        }

        private static object ValueAt(object value, int index)
        {
            if (value is IList list && index >= 0 && index < list.Count) return list[index];
            if (value is Array array && index >= 0 && index < array.Length) return array.GetValue(index);
            return null;
        }

        private static string StringAt(object value, int index)
        {
            object item = ValueAt(value, index);
            return item == null ? "" : item.ToString();
        }

        private static GameObject AsGameObject(object value)
        {
            var thumbnail = value as ThumbnailNode;
            if (thumbnail != null && thumbnail.button != null) return thumbnail.button.gameObject;
            var gameObject = value as GameObject;
            if (gameObject != null) return gameObject;
            var component = value as Component;
            return component == null ? null : component.gameObject;
        }
    }
}
