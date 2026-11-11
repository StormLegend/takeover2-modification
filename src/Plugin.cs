using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine.TextCore.LowLevel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class TakeoverChinese
{
    private static bool initialized;
    private static Dictionary<string,string> lines, labels;
    private static TMP_FontAsset cjk;
    private static Font legacyFont;
    private static bool ready;
    private static bool settingUp, fontFailed;
    private static bool english = true;
    private static bool trace;
    private static readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

    private static Dictionary<string,string> ReadMap(string name, char separator)
    {
        var result = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(Path.Combine(Path.GetDirectoryName(typeof(TakeoverChinese).Assembly.Location), name)))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            int split = line.IndexOf(separator);
            if (split <= 0) throw new FormatException("Invalid " + name + " entry");
            result.Add(line.Substring(0, split).Replace("\\n", "\n"), line.Substring(split + 1).Replace("\\n", "\n"));
        }
        return result;
    }

    private static bool Initialize()
    {
        if (!initialized)
        {
            initialized = true;
            trace = Array.IndexOf(Environment.GetCommandLineArgs(), "-takeoverzh-check") >= 0;
            try
            {
                lines = ReadMap("zh-CN.txt", '=');
                labels = ReadMap("labels.tsv", '\t');
                Debug.Log("[Takeover2-ZH] Loaded " + lines.Count + " localization keys and " + labels.Count + " additional UI strings.");
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }
        return lines != null && labels != null;
    }

    private static bool SetUpFont()
    {
        if (ready) return true;
        if (settingUp || fontFailed) return false;
        settingUp = true;
        try
        {
            if (trace) Debug.Log("[Takeover2-ZH] Font: loading TMP settings");
            var fallback = TMP_Settings.fallbackFontAssets;
            if (fallback == null) return false;
            if (trace) Debug.Log("[Takeover2-ZH] Font: loading system font file");
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "simhei.ttf");
            cjk = TMP_FontAsset.CreateFontAsset(path, 0, 48, 5, GlyphRenderMode.SDFAA, 2048, 2048);
            if (trace) Debug.Log("[Takeover2-ZH] Font: system font file returned");
            if (!CanRenderChinese(cjk))
            {
                if (trace) Debug.Log("[Takeover2-ZH] Font: trying dynamic OS font");
                legacyFont = Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei", "SimHei", "SimSun"}, 32);
                cjk = TMP_FontAsset.CreateFontAsset(legacyFont);
            }
            if (!CanRenderChinese(cjk))
            {
                fontFailed = true;
                Debug.LogError("[Takeover2-ZH] Could not load usable Chinese font.");
                return false;
            }
            cjk.name = "Takeover2 Chinese Fallback";
            cjk.isMultiAtlasTexturesEnabled = true;
            UnityEngine.Object.DontDestroyOnLoad(cjk);
            fallback.Add(cjk);
            ready = true;
            Debug.Log("[Takeover2-ZH] Chinese font fallback ready.");
            if (trace) Canvas.willRenderCanvases += InspectRendering;
            return true;
        }
        catch (Exception ex) { Debug.LogException(ex); return false; }
        finally { settingUp = false; }
    }

    private static bool CanRenderChinese(TMP_FontAsset asset)
    {
        if (!asset) return false;
        string ignored;
        asset.TryAddCharacters("中文菜单", out ignored);
        return asset.HasCharacter((int)'中') && asset.HasCharacter((int)'单');
    }

    private static int qaFrame = -1;
    private static void InspectRendering()
    {
        if (qaFrame < 0) qaFrame = Time.frameCount;
        if (Time.frameCount - qaFrame < 60) return;
        Canvas.willRenderCanvases -= InspectRendering;
        try
        {
            var glyphs = new HashSet<char>();
            foreach (var map in new[]{lines,labels})
                foreach (var v in map.Values)
                    foreach (var ch in v)
                        if (ch >= '\u3400' && ch <= '\u9fff') glyphs.Add(ch);
            var missing = new List<char>();
            foreach (var ch in glyphs)
            {
                string absent;
                if (!cjk.HasCharacter((int)ch)) cjk.TryAddCharacters(ch.ToString(), out absent);
                if (!cjk.HasCharacter((int)ch)) missing.Add(ch);
            }
            Debug.Log("[Takeover2-ZH] Font validation: " + glyphs.Count + " CJK chars, missing " + missing.Count + " " + new string(missing.ToArray()));
            foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            {
                if (!text.isActiveAndEnabled || !HasChinese(text.text) || text.textInfo == null) continue;
                int actual = 0;
                for (int j = 0; j < text.textInfo.characterCount; j++)
                {
                    var item = text.textInfo.characterInfo[j];
                    if (item.fontAsset == cjk && item.isVisible) actual++;
                }
                if (actual > 0) Debug.Log("[Takeover2-ZH] Rendered: " + text.text.Replace("\n", " ") + " (" + actual + " Chinese glyphs)");
            }
            ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetTempPath(), "takeover2-zh-game.png"));
        }
        catch(Exception ex) { Debug.LogException(ex); }
    }

    public static void OnLanguage(IDictionary values, string path)
    {
        english = path == "Dialogue/English";
        if (!english || !Initialize()) return;
        int count = 0;
        foreach (var item in lines)
            if (values.Contains(item.Key)) { values[item.Key] = item.Value; count++; }
        Debug.Log("[Takeover2-ZH] Applied " + count + "/" + values.Count + " Chinese localization entries.");
    }

    private static string TranslateLabel(string value)
    {
        if (!english || !Initialize() || String.IsNullOrWhiteSpace(value) || value.Length > 250) return value;
        string key = value.Trim();
        string translated;
        if (!labels.TryGetValue(key, out translated))
        {
            if (trace && System.Text.RegularExpressions.Regex.IsMatch(key, "[a-zA-Z]{2,}") &&
                seen.Add(key) && seen.Count <= 450)
                Debug.Log("[Takeover2-ZH] UI trace: " + key.Replace("\n", "\\n"));
            return value;
        }
        int start = value.IndexOf(key, StringComparison.Ordinal);
        return value.Substring(0, start) + translated + value.Substring(start + key.Length);
    }

    private static bool HasChinese(string value)
    {
        if (value == null) return false;
        foreach (char ch in value) if (ch >= '\u3400' && ch <= '\u9fff') return true;
        return false;
    }

    public static void OnText(ref string __0)
    {
        var translated = TranslateLabel(__0);
        if (translated != __0)
        {
            if (SetUpFont()) __0 = translated;
        }
        else if (HasChinese(__0)) SetUpFont();
    }
    public static void OnTmpEnabled(TMP_Text __instance)
    {
        string value = __instance.text;
        string translated = TranslateLabel(value);
        if (translated != value && SetUpFont()) __instance.text = translated;
        else if (HasChinese(value)) SetUpFont();
    }
    public static void OnLegacyText(Text __instance, ref string __0)
    {
        string translated = TranslateLabel(__0);
        if (translated == __0 || !SetUpFont()) return;
        if (!legacyFont)
        {
            legacyFont = Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei", "SimHei", "SimSun"}, 32);
            if (legacyFont) UnityEngine.Object.DontDestroyOnLoad(legacyFont);
        }
        if (legacyFont) { __instance.font = legacyFont; __0 = translated; }
    }
    public static void OnLegacyEnabled(Text __instance)
    {
        string value = __instance.text;
        string translated = value;
        OnLegacyText(__instance, ref translated);
        if (translated != value) __instance.text = translated;
    }
}
