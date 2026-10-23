using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Reflection;
using HarmonyLib;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[BepInPlugin("local.mcs.ptbr.menus", "Menus PT-BR (teste)", "0.43")]
public class MenusPTBR : BaseUnityPlugin
{
    readonly Dictionary<string, string> translations = new Dictionary<string, string>();
    static MenusPTBR instance;
    // Descriptions such as "受到（attack）点伤害" reach the screen with the number
    // already substituted. Templates are indexed by their text without numbers.
    class Template { public Regex pattern; public string result; }
    readonly Dictionary<string, List<Template>> templates = new Dictionary<string, List<Template>>();
    readonly Dictionary<string, string> templateCache = new Dictionary<string, string>();
    static readonly Regex slot = new Regex(@"（[A-Za-z0-9_*+\-. ]{1,24}）");
    static string Skeleton(string key) { return Regex.Replace(slot.Replace(key, ""), @"[-+]?\d+(?:\.\d+)?", ""); }
    void AddTemplate(string key, string value) {
        AddPTBRFormat(key, value);
        if (!slot.IsMatch(key)) return;
        string pattern = "^";
        int last = 0;
        foreach (Match m in slot.Matches(key)) {
            pattern += Regex.Escape(key.Substring(last, m.Index - last)) + @"([-+]?\d+(?:\.\d+)?)";
            last = m.Index + m.Length;
        }
        pattern += Regex.Escape(key.Substring(last)) + "$";
        string skeleton = Skeleton(key);
        List<Template> list;
        if (!templates.TryGetValue(skeleton, out list)) { list = new List<Template>(); templates[skeleton] = list; }
        Template t = new Template(); t.pattern = new Regex(pattern); t.result = value; list.Add(t);
    }
    string FromTemplate(string source) {
        string key = Key(source);
        if (!Regex.IsMatch(key, @"\d") || !Regex.IsMatch(key, @"[\u4e00-\u9fff]")) return null;
        string cached;
        if (templateCache.TryGetValue(key, out cached)) return cached;
        string found = null;
        List<Template> list;
        if (templates.TryGetValue(Skeleton(key), out list)) {
            foreach (Template t in list) {
                Match m = t.pattern.Match(key);
                if (!m.Success) continue;
                int index = 1;
                Match capture = m;
                found = slot.Replace(t.result, delegate(Match ignored) {
                    return index < capture.Groups.Count ? capture.Groups[index++].Value : ignored.Value;
                });
                break;
            }
        }
        if (templateCache.Count < 5000) templateCache[key] = found;
        return found;
    }
    static FieldInfo sayText, menuText;
    static int hookedSay, hookedMenu, pendingSay;
    static string FromFile(string value) { return value.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\r", ""); }
    // Fungus substitutes {FirstName}, {color=...} and pauses only when the
    // command runs, so the source text is swapped before OnEnter executes.
    static void SayPrefix(object __instance) {
        try {
            string source = (string)sayText.GetValue(__instance);
            string result;
            result = null;
            if (!String.IsNullOrEmpty(source) && !instance.translations.TryGetValue(Key(source), out result)) result = instance.Fuzzy(Key(source));
            if (String.IsNullOrEmpty(source) || result == null) {
                if (!String.IsNullOrEmpty(source) && pendingSay < 300 && Regex.IsMatch(source, @"[\u4e00-\u9fff]")) {
                    pendingSay++;
                    instance.Logger.LogInfo("[PTBR fala pendente] " + source.Replace('\n', ' ').Replace('\r', ' '));
                }
                return;
            }
            sayText.SetValue(__instance, result); hookedSay++;
        } catch (Exception error) { instance.Logger.LogWarning("PT-BR Say: " + error.Message); }
    }
    static void MenuPrefix(object __instance) {
        try {
            string source = (string)menuText.GetValue(__instance);
            string result;
            if (String.IsNullOrEmpty(source)) return;
            if (!instance.translations.TryGetValue(Key(source), out result)) result = instance.Fuzzy(Key(source));
            if (result == null) return;
            menuText.SetValue(__instance, result); hookedMenu++;
        } catch (Exception error) { instance.Logger.LogWarning("PT-BR Menu: " + error.Message); }
    }
    void ApplyHooks() {
        Harmony harmony = new Harmony("local.mcs.ptbr.menus");
        try { ApplyPTBRAnalysisHooks(harmony); } catch (Exception error) { Logger.LogWarning("[PTBR analise] " + error.Message); }
        BindingFlags hookFlags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        try {
            MethodInfo init = AccessTools.Method(typeof(jsonData), "InitLogic");
            if (init != null) {
                harmony.Patch(init, null, new HarmonyMethod(typeof(MenusPTBR).GetMethod("InitLogicPostfix", hookFlags)));
                Logger.LogInfo("[PTBR] Gancho de dados ativo (jsonData.InitLogic).");
            }
        } catch (Exception error) { Logger.LogWarning("[PTBR] Gancho de dados falhou: " + error.Message); }
        Type say = AccessTools.TypeByName("Fungus.Say");
        Type menu = AccessTools.TypeByName("Fungus.Menu");
        BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        if (say != null) {
            sayText = AccessTools.Field(say, "storyText");
            harmony.Patch(AccessTools.Method(say, "OnEnter"), new HarmonyMethod(typeof(MenusPTBR).GetMethod("SayPrefix", flags)));
            Logger.LogInfo("[PTBR] Gancho de diálogos ativo (Fungus.Say).");
        } else Logger.LogWarning("[PTBR] Fungus.Say não encontrado.");
        if (menu != null) {
            menuText = AccessTools.Field(menu, "text");
            harmony.Patch(AccessTools.Method(menu, "OnEnter"), new HarmonyMethod(typeof(MenusPTBR).GetMethod("MenuPrefix", flags)));
            Logger.LogInfo("[PTBR] Gancho de escolhas ativo (Fungus.Menu).");
        } else Logger.LogWarning("[PTBR] Fungus.Menu não encontrado.");
        try {
            MethodInfo setter = AccessTools.PropertySetter(typeof(Text), "text");
            if (setter != null) harmony.Patch(setter, new HarmonyMethod(typeof(MenusPTBR).GetMethod("TextSetPrefix", flags)));
            MethodInfo enable = AccessTools.Method(typeof(Text), "OnEnable");
            if (enable != null) harmony.Patch(enable, null, new HarmonyMethod(typeof(MenusPTBR).GetMethod("TextEnablePostfix", flags)));
            setter = AccessTools.PropertySetter(typeof(UILabel), "text");
            if (setter != null) harmony.Patch(setter, new HarmonyMethod(typeof(MenusPTBR).GetMethod("TextSetPrefix", flags)));
            setter = AccessTools.PropertySetter(typeof(TMP_Text), "text");
            if (setter != null) harmony.Patch(setter, new HarmonyMethod(typeof(MenusPTBR).GetMethod("TextSetPrefix", flags)));
            textField = AccessTools.Field(typeof(Text), "m_Text");
            MethodInfo populate = AccessTools.Method(typeof(Text), "OnPopulateMesh", new Type[] { typeof(VertexHelper) });
            if (populate != null && textField != null) harmony.Patch(populate, new HarmonyMethod(typeof(MenusPTBR).GetMethod("TextPopulatePrefix", flags)));
            Logger.LogInfo("[PTBR] Gancho de textos ativo (rótulos já traduzidos não voltam ao chinês).");
        } catch (Exception error) { Logger.LogWarning("[PTBR] Gancho de textos falhou: " + error.Message); }
        try {
            Type writer = AccessTools.TypeByName("Fungus.Writer");
            MethodInfo write = writer == null ? null : AccessTools.Method(writer, "Write");
            if (write != null) { harmony.Patch(write, new HarmonyMethod(typeof(MenusPTBR).GetMethod("GenderPrefix", flags))); Logger.LogInfo("[PTBR] Gancho de pronomes ativo (Fungus.Writer)."); }
            Type menuDialog = AccessTools.TypeByName("Fungus.MenuDialog");
            MethodInfo add = menuDialog == null ? null : AccessTools.Method(menuDialog, "AddOption");
            if (add != null && add.GetParameters().Length > 0 && add.GetParameters()[0].ParameterType == typeof(string)) harmony.Patch(add, new HarmonyMethod(typeof(MenusPTBR).GetMethod("GenderPrefix", flags)));
        } catch (Exception error) { Logger.LogWarning("[PTBR] Gancho de pronomes falhou: " + error.Message); }
        try {
            // v0.65: the encyclopedia/guide search compares the typed words with the (Chinese) data names.
            Type searcher = AccessTools.TypeByName("YSGame.TuJian.TuJianSearcher");
            MethodInfo contains = searcher == null ? null : AccessTools.Method(searcher, "IsContansSearch");
            if (contains != null) {
                foreach (FieldInfo f in searcher.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    if (f.FieldType == typeof(string[])) { searchKeys = f; break; }
                harmony.Patch(contains, null, new HarmonyMethod(typeof(MenusPTBR).GetMethod("SearchPostfix", flags)));
                Logger.LogInfo("[PTBR] Busca da enciclopedia em portugues ativa.");
            } else Logger.LogWarning("[PTBR] TuJianSearcher.IsContansSearch nao encontrado.");
        } catch (Exception error) { Logger.LogWarning("[PTBR] Gancho da busca falhou: " + error.Message); }
    }
    static FieldInfo searchKeys;
    readonly Dictionary<string, string> searchNames = new Dictionary<string, string>();
    static string Fold(string text) {
        if (String.IsNullOrEmpty(text)) return "";
        string d = text.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder(d.Length);
        foreach (char c in d) if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Replace('\u00A0', ' ').ToLowerInvariant();
    }
    string SearchName(string source) {
        string cached;
        if (searchNames.TryGetValue(source, out cached)) return cached;
        string pt = null;
        try {
            if (HasCjk(source)) {
                pt = Translate(source);
                if (pt == null) { string d; if (dataTranslations.TryGetValue(Key(source), out d)) pt = d; }
            }
        } catch (Exception) { }
        pt = Fold(pt ?? "");
        if (searchNames.Count < 20000) searchNames[source] = pt;
        return pt;
    }
    // Typed words (Portuguese, with or without accents) also match the Portuguese name of an entry.
    static int searchLogged;
    static readonly Dictionary<string, int> searchLogPerKey = new Dictionary<string, int>();
    // v0.68: the game ORs the words and an empty word (trailing space) matches everything. Here the words are
    // cleaned of empties; Chinese keeps the game's rule (any word), Portuguese needs ALL words in the name.
    static void SearchPostfix(object __instance, string __0, ref bool __result) {
        try {
            if (instance == null || searchKeys == null) return;
            string[] keys = searchKeys.GetValue(__instance) as string[];
            if (keys == null || keys.Length == 0 || String.IsNullOrEmpty(__0)) return;
            List<string> words = new List<string>();
            foreach (string key in keys) { string t = key == null ? "" : key.Trim(); if (t.Length > 0) words.Add(t); }
            if (words.Count == 0) return;
            bool before = __result, zh = false, all = true;
            string pt = instance.SearchName(__0);
            string name = Fold(__0) + "|" + pt;
            foreach (string w in words) {
                if (__0.IndexOf(w, StringComparison.Ordinal) >= 0) zh = true;
                if (name.IndexOf(Fold(w), StringComparison.Ordinal) < 0) all = false;
            }
            __result = zh || all;
            string joined = String.Join("|", keys);
            int n; searchLogPerKey.TryGetValue(joined, out n);
            if (n < 4 && searchLogged < 400) {
                searchLogPerKey[joined] = n + 1; searchLogged++;
                instance.Logger.LogInfo("[PTBR busca] chaves=[" + joined + "] arg=" + __0.Substring(0, Math.Min(30, __0.Length)).Replace('\n', ' ') + " pt=" + pt + " jogo=" + before + " final=" + __result);
            }
        } catch (Exception) { }
    }
    // Labels the plugin has translated once: when the game rewrites them every
    // frame (dates, counters), the new value is translated before it is shown.
    static readonly HashSet<int> touched = new HashSet<int>();
    static readonly Dictionary<string, string> setCache = new Dictionary<string, string>();
    // Labels translated at write time still need the sizing/wrapping the scan applies.
    static readonly HashSet<int> pendingStyle = new HashSet<int>();
    float lastScan;
    static void MarkStyle(int id) {
        pendingStyle.Add(id);
        if (instance == null) return;
        float soon = Math.Max(Time.unscaledTime + 0.02f, instance.lastScan + 0.15f);
        if (instance.next > soon) instance.next = soon;
    }
    static FieldInfo textField;
    static void TextPopulatePrefix(Text __instance) {
        try {
            if (instance == null || __instance == null || textField == null) return;
            if (PTBRIsSymbol(__instance)) return; // Use its setter, not m_Text directly.
            string v = (string)textField.GetValue(__instance);
            if (String.IsNullOrEmpty(v) || v.Length >= 2000 || !HasCjk(v)) return;
            string result;
            if (!setCache.TryGetValue(v, out result)) {
                result = instance.Translate(v);
                if (setCache.Count > 4000) setCache.Clear();
                setCache[v] = result;
            }
            if (result == null || __instance.GetComponentInParent<InputField>() != null) return;
            int id = __instance.GetInstanceID();
            if (!touched.Contains(id)) { touched.Add(id); MarkStyle(id); }
            textField.SetValue(__instance, result);
            __instance.SetLayoutDirty();
            Relayout(__instance);
        } catch { }
    }
    // Tooltips and popups are instantiated from prefabs with Chinese text already set:
    // translate them the moment they appear instead of waiting for the next scan.
    static void TextEnablePostfix(Text __instance) {
        try {
            if (instance == null || __instance == null) return;
            string v = __instance.text;
            if (String.IsNullOrEmpty(v) || !HasCjk(v)) return;
            if (__instance.GetComponentInParent<InputField>() != null) return;
            string result;
            if (!setCache.TryGetValue(v, out result)) {
                result = instance.Translate(v);
                if (setCache.Count > 4000) setCache.Clear();
                setCache[v] = result;
            }
            if (result == null) return;
            touched.Add(__instance.GetInstanceID());
            __instance.text = result;
        } catch { }
    }
    // Fixed labels the game glues into tooltips by code (not in the data files).
    static readonly string[,] tokenTable = {
        {"【种类】","【Tipo】"}, {"【属性】","【Atributos】"}, {"【冷却】","【Recarga】"}, {"【进度】","【Progresso】"},
        {"【修炼速度】","【Velocidade de cultivo】"}, {"【耐久】","【Durabilidade】"}, {"【耐药】","【Tolerância】"},
        {"【丹毒】","【Toxicidade】"}, {"【主药】","【Principal】"}, {"【辅药】","【Auxiliar】"}, {"【药引】","【Catalisador】"},
        {"【主动】","【Ativo】"}, {"【被动】","【Passivo】"}, {"主动：","Ativo: "}, {"主动:","Ativo: "}, {"冷却:","Recarga:"},
        {"领悟时间:","Tempo de compreensão:"}, {"领悟条件:","Requisito:"}, {"已有:","Você tem: "}, {"已有：","Você tem: "},
        {"【需求点数】","【Pontos necessários】"}, {"【领悟条件】","【Requisito】"}, {"需求点数：","Pontos necessários: "}, {"领悟条件：","Requisito: "},
        {"领悟时间：","Tempo de compreensão: "}, {"领悟条件：","Requisito: "}, {"悟道提升：","Ganho de Dao: "} };
    static readonly Regex tokenTurnsEnd = new Regex(@"(\d+)\s*回合结束");
    static readonly Regex tokenDaoReach = new Regex(@"对([^，的]+?)的(?:领悟|感悟)达到");
    static readonly Regex tokenDaoGain = new Regex(@"[，,]?领悟后提升([^大]+?)大道感悟");
    static readonly Dictionary<string,string> daoNames = new Dictionary<string,string> {
        {"金","Dao do Metal"},{"木","Dao da Madeira"},{"水","Dao da Água"},{"火","Dao do Fogo"},{"土","Dao da Terra"},{"神","Dao do Espírito"},
        {"体","Dao do Corpo"},{"剑","Dao da Espada"},{"气","Dao do Qi"},{"阵","Dao das Formações"},{"丹","Dao da Alquimia"},{"器","Dao dos Artefatos"} };
    static string DaoName(string x) {
        if (x.IndexOf(',') >= 0 || x.IndexOf('，') >= 0 || x.IndexOf('、') >= 0) {
            string[] parts = x.Split(new char[] { ',', '，', '、' });
            List<string> names = new List<string>();
            for (int i = 0; i < parts.Length; i++) {
                string one = parts[i].Trim();
                if (one.EndsWith("之道")) one = one.Substring(0, one.Length - 2);
                else if (one.EndsWith("道")) one = one.Substring(0, one.Length - 1);
                string el;
                names.Add(daoElement.TryGetValue(one, out el) ? el : one);
            }
            return "Dao de " + String.Join(" e ", names.ToArray());
        }
        Match m = Regex.Match(x, @"^(.)(?:之)?道$");
        string e;
        if (m.Success && daoNames.TryGetValue(m.Groups[1].Value, out e)) return e;
        return x;
    }
    static readonly Regex tokenTurns = new Regex(@"(\d+)回合");
    static readonly Regex tokenTurnNth = new Regex(@"第\s*(\d+)\s*回合");
    static readonly Regex tokenDays = new Regex(@"^(\s*(?:<color=[^>]+>)?)(\d+)日后");
    static readonly Regex tokenFloor = new Regex(@"第(?=[A-ZÀ-Ú])");
    static readonly Regex tokenDao = new Regex(@"对([^，,]+?)的领悟达到([^，,]+?)[，,]领悟后提升(.)大道感悟");
    static readonly Dictionary<string,string> daoElement = new Dictionary<string,string> {
        {"金","Metal"},{"木","Madeira"},{"水","Água"},{"火","Fogo"},{"土","Terra"},{"神","Espírito"},{"体","Corpo"},{"剑","Espada"},{"气","Qi"},{"阵","Formações"},{"丹","Alquimia"},{"器","Artefatos"} };
    static string Tokens(string text) {
        if (String.IsNullOrEmpty(text) || !HasCjk(text)) return text;
        string r = text;
        if (r.IndexOf('【') >= 0 || r.IndexOf('：') >= 0 || r.IndexOf(':') >= 0)
            for (int i = 0; i < tokenTable.GetLength(0); i++) if (r.Contains(tokenTable[i, 0])) r = r.Replace(tokenTable[i, 0], tokenTable[i, 1]);
        if (r.Length < 60 && r.IndexOf("已完成") >= 0) r = r.Replace("已完成", "(concluída)");
        if (r.IndexOf('回') >= 0) { r = tokenTurnsEnd.Replace(r, "Termina em $1 turno(s)"); r = tokenTurnNth.Replace(r, "Turno $1"); r = tokenTurns.Replace(r, "$1 turno(s)"); }
        if (r.IndexOf('日') >= 0) r = tokenDays.Replace(r, "$1daqui a $2 dias");
        if (r.IndexOf('第') >= 0) r = tokenFloor.Replace(r, "");
        if (r.IndexOf('领') >= 0) r = tokenDao.Replace(r, delegate(Match m) {
            string e; daoElement.TryGetValue(m.Groups[3].Value, out e);
            return "compreensão de " + m.Groups[1].Value + " em " + m.Groups[2].Value + "; ao compreender, aumenta o Grande Dao " + (e ?? m.Groups[3].Value);
        });
        if (r.IndexOf("达到") >= 0 || r.IndexOf("领悟后提升") >= 0) {
            r = tokenDaoReach.Replace(r, delegate(Match m) { return DaoName(m.Groups[1].Value) + ": "; });
            r = tokenDaoGain.Replace(r, delegate(Match m) {
                string[] list = m.Groups[1].Value.Split(new char[] { ',', '，', '、' });
                for (int i = 0; i < list.Length; i++) { string e; if (daoElement.TryGetValue(list[i], out e)) list[i] = e; }
                return "; ao compreender, aumenta o Grande Dao " + String.Join(", ", list);
            });
        }
        return r;
    }
    // v0.42: "3年11月29日" abaixo de "Tempo restante" / "Custo de compreensão" é uma duração, não uma data.
    static readonly Regex ymdText = new Regex(@"^(\d+)年(\d+)月(\d+)日$");
    static string Duration(int years, int months, int days) {
        List<string> parts = new List<string>();
        if (years > 0) parts.Add(years + (years == 1 ? " ano" : " anos"));
        if (months > 0) parts.Add(months + (months == 1 ? " mês" : " meses"));
        if (days > 0 || parts.Count == 0) parts.Add(days + (days == 1 ? " dia" : " dias"));
        if (parts.Count == 1) return parts[0];
        return String.Join(", ", parts.GetRange(0, parts.Count - 1).ToArray()) + " e " + parts[parts.Count - 1];
    }
    static bool DurationLabel(Component label) {
        try {
            Transform scope = label.transform.parent;
            if (scope != null && scope.parent != null) scope = scope.parent;
            if (scope == null) return false;
            float y = label.transform.position.y;
            float gap = float.MaxValue;
            string above = null;
            foreach (Text other in scope.GetComponentsInChildren<Text>(true)) {
                if (other.transform == label.transform || String.IsNullOrEmpty(other.text)) continue;
                float d = other.transform.position.y - y;
                if (d <= 0.01f || d >= gap) continue;
                gap = d; above = other.text;
            }
            foreach (TMP_Text other in scope.GetComponentsInChildren<TMP_Text>(true)) {
                if (other.transform == label.transform || String.IsNullOrEmpty(other.text)) continue;
                float d = other.transform.position.y - y;
                if (d <= 0.01f || d >= gap) continue;
                gap = d; above = other.text;
            }
            if (above == null) return false;
            string head = Key(above).TrimEnd(':', '：');
            return head == "剩余时间" || head == "感悟消耗" || head == "Temporestante" || head == "Custodecompreensão";
        } catch { return false; }
    }
    static bool UnderNamed(Component label, string name) {
        for (Transform t = label.transform; t != null; t = t.parent) if (t.name == name) return true;
        return false;
    }
    static string FixDuration(Component label, string source, string value) {
        try {
            if (value == null || source == null || label == null || !value.StartsWith("Ano ", StringComparison.Ordinal)) return value;
            Match ymd = ymdText.Match(Key(source));
            if (!ymd.Success || !(UnderNamed(label, "悟道") || DurationLabel(label))) return value;
            return Duration(int.Parse(ymd.Groups[1].Value), int.Parse(ymd.Groups[2].Value), int.Parse(ymd.Groups[3].Value));
        } catch { return value; }
    }
    static void TextSetPrefix(UnityEngine.Object __instance, ref string value) {
        try {
            // Preserve the complete Chinese key until TranslateCore checks the dictionary.
            if (instance == null || String.IsNullOrEmpty(value) || __instance == null || !HasCjk(value)) return;
            string result;
            if (!setCache.TryGetValue(value, out result)) {
                result = instance.Translate(value);
                if (setCache.Count > 4000) setCache.Clear();
                setCache[value] = result;
            }
            result = FixDuration(__instance as Component, value, result);
            if (result == null) { PTBRTraceResidual(__instance, value); return; }
            int setId = __instance.GetInstanceID();
            if (!touched.Contains(setId)) {
                // First write to this label: never touch text the player is typing.
                Text setText = __instance as Text;
                if (setText != null && setText.GetComponentInParent<InputField>() != null) return;
                touched.Add(setId);
                if (setText != null) MarkStyle(setId);
            }
            value = result;
            PTBRTraceResidual(__instance, value);
        } catch { }
    }
    // O jogo troca {shidi}, {xiaozi}, {ta}... por palavras chinesas depois da tradução;
    // numa fala já em português, essas palavras viram os equivalentes em PT.
    static readonly string[,] genderWords = {
        {"林道友","companheira Lin"}, {"小女子","eu"}, {"在下","eu"}, {"晚辈","eu"}, {"心儿","Xin'er"}, {"沐心","Muxin"},
        {"炼气期","Refino de Qi"}, {"筑基期","Fundação"}, {"金丹期","Núcleo Dourado"}, {"元婴期","Alma Nascente"}, {"化神期","Transformação Espiritual"},
        {"百里兄","irmão Baili"}, {"倪兄","irmão Ni"}, {"师弟","irmão júnior"}, {"师妹","irmã júnior"},
        {"师兄","irmão sênior"}, {"师姐","irmã sênior"}, {"师傅","mestre"}, {"兄弟","irmão"}, {"姑娘","moça"},
        {"公子","jovem senhor"}, {"小友","jovem amigo"}, {"道友","companheiro"}, {"前辈","veterano"},
        {"丫头","menina"}, {"小子","moleque"}, {"她","ela"}, {"他","ele"} };
    static void GenderPrefix(ref string __0) {
        try { __0 = GenderWords(__0); } catch { }
        try { if (instance != null) __0 = instance.FillRuns(__0); } catch { }
    }
    // v0.43: falas já em português que o jogo completa com nomes de itens/ervas em chinês
    // ("Pílula do Coração Gelado precisa de 夜交藤..."): traduz cada trecho chinês que sobrou.
    static readonly Regex ratioRun = new Regex(@"^[一二三四五六七八九十]+(比[一二三四五六七八九十]+)+$");
    string FillRuns(string text) {
        if (String.IsNullOrEmpty(text) || text.Length > 6000 || !HasCjk(text)) return text;
        int cjk = 0, latin = 0;
        foreach (char c in text) { if (c >= '\u4e00' && c <= '\u9fff') cjk++; else if (Char.IsLetter(c)) latin++; }
        if (latin < cjk) return text;
        return cjkRun.Replace(text, delegate(Match m) {
            string run = m.Value, t;
            if (!translations.TryGetValue(run, out t) && !dataTranslations.TryGetValue(run, out t)) t = null;
            if (t == null && ratioRun.IsMatch(run)) {
                string[] parts = run.Split('比');
                for (int i = 0; i < parts.Length; i++) parts[i] = ChineseNumber(parts[i]);
                t = String.Join(":", parts);
            }
            if (t == null) t = NameRun(run);
            if (t == null || HasCjk(t)) return run;
            int a = m.Index, b = m.Index + m.Length;
            bool before = a > 0 && Char.IsLetterOrDigit(text[a - 1]);
            bool after = b < text.Length && Char.IsLetterOrDigit(text[b]);
            return (before ? " " : "") + t + (after ? " " : "");
        });
    }
    static string GenderWords(string text) {
        {
            if (String.IsNullOrEmpty(text)) return text;
            int cjk = 0, latin = 0;
            foreach (char c in text) { if (c >= '\u4e00' && c <= '\u9fff') cjk++; else if (Char.IsLetter(c)) latin++; }
            if (cjk == 0 || latin < cjk * 3) return text;
            for (int i = 0; i < genderWords.GetLength(0); i++) {
                string zh = genderWords[i, 0], pt = genderWords[i, 1];
                int at;
                while ((at = text.IndexOf(zh, StringComparison.Ordinal)) >= 0) {
                    int k = at - 1;
                    while (k >= 0 && (text[k] == ' ' || text[k] == '"' || text[k] == '(' || text[k] == '«')) k--;
                    bool start = k < 0 || text[k] == '.' || text[k] == '!' || text[k] == '?' || text[k] == '\n' || text[k] == '}' || text[k] == '…';
                    string word = start ? Char.ToUpper(pt[0]) + pt.Substring(1) : pt;
                    bool spaceBefore = at > 0 && Char.IsLetterOrDigit(text[at - 1]);
                    bool spaceAfter = at + zh.Length < text.Length && Char.IsLetterOrDigit(text[at + zh.Length]);
                    text = text.Substring(0, at) + (spaceBefore ? " " : "") + word + (spaceAfter ? " " : "") + text.Substring(at + zh.Length);
                }
            }
            return text;
        }
    }

    // ---- Dados do jogo (TextAsset/JSON) traduzidos na memória, como o EN Translation faz ----
    readonly Dictionary<string, string> dataTranslations = new Dictionary<string, string>();
    static int dataChanged;
    static bool dataPending = true, dataStable;
    float nextData;
    class RefComparer : IEqualityComparer<object> {
        public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
        public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
    }
    List<Type> dataTypes;
    readonly Dictionary<Type, FieldInfo[]> stringFields = new Dictionary<Type, FieldInfo[]>();
    FieldInfo joStr, joList;
    string DataText(string value) {
        if (value == null || value.Length < 2 || value.Length > 20000) return null;
        string result;
        if (dataTranslations.TryGetValue(Key(value), out result) && result != value) {
            result = PTBRNormalizeHy(result);
            if (PTBRMarkupSafe(value, result)) return result;
        }
        if (dataMissing.Count < 60000 && HasCjk(value)) dataMissing.Add(value);
        return null;
    }
    readonly HashSet<string> dataMissing = new HashSet<string>();
    int dataMissingWritten;
    bool quietPending;
    // Lists every Chinese string left in the game's data tables that no dictionary covers.
    void DumpMissing() {
        if (dataMissing.Count == dataMissingWritten || dataMissing.Count == 0) return;
        dataMissingWritten = dataMissing.Count;
        List<string> lines = new List<string>();
        quietPending = true;
        try {
            foreach (string value in dataMissing) {
                string known = null;
                try { known = Translate(value); } catch (Exception) { }
                if (known == null) lines.Add(value.Replace("\r", "").Replace("\n", "\\n"));
            }
        } finally { quietPending = false; }
        lines.Sort(StringComparer.Ordinal);
        File.WriteAllLines(Path.Combine(pluginFolder, "pendentes-dados.txt"), lines.ToArray(), new UTF8Encoding(false));
        Logger.LogInfo("[PTBR] pendentes-dados.txt: " + lines.Count + " textos de dados sem tradução.");
    }
    static bool HasCjk(string value) {
        foreach (char c in value) if (c >= '一' && c <= '鿿') return true;
        return false;
    }
    void WalkData(object node, HashSet<object> visited, int depth) {
        if (node == null || depth > 60 || node is string || !visited.Add(node)) return;
        Type type = node.GetType();
        if (type.IsPrimitive || type.IsEnum) return;
        if (type.IsArray && type.GetElementType().IsValueType) return;
        if (type.IsGenericType) {
            Type[] args = type.GetGenericArguments();
            if (args[args.Length - 1].IsValueType) return;
        }
        JToken token = node as JToken;
        if (token != null) {
            List<JValue> values = new List<JValue>();
            JContainer container = token as JContainer;
            if (container != null) values.AddRange(container.DescendantsAndSelf().OfType<JValue>());
            else if (token is JValue) values.Add((JValue)token);
            foreach (JValue value in values) {
                if (value.Type != JTokenType.String) continue;
                string translated = DataText((string)value.Value);
                if (translated != null) { value.Value = translated; dataChanged++; }
            }
            return;
        }
        if (type.Name == "JSONObject") {
            if (joStr == null) { joStr = type.GetField("str"); joList = type.GetField("list"); }
            if (joStr != null) {
                string translated = DataText(joStr.GetValue(node) as string);
                if (translated != null) { joStr.SetValue(node, translated); dataChanged++; }
            }
            if (joList != null) WalkData(joList.GetValue(node), visited, depth + 1);
            return;
        }
        System.Collections.IDictionary map = node as System.Collections.IDictionary;
        if (map != null) {
            List<object> keys = new List<object>();
            foreach (object k in map.Keys) keys.Add(k);
            foreach (object k in keys) {
                object v = map[k];
                string text = v as string;
                if (text != null) { string t = DataText(text); if (t != null) { map[k] = t; dataChanged++; } }
                else WalkData(v, visited, depth + 1);
            }
            return;
        }
        System.Collections.IList list = node as System.Collections.IList;
        if (list != null) {
            for (int i = 0; i < list.Count; i++) {
                object v = list[i];
                string text = v as string;
                if (text != null) { string t = DataText(text); if (t != null && !list.IsReadOnly) { list[i] = t; dataChanged++; } }
                else WalkData(v, visited, depth + 1);
            }
            return;
        }
        if (type.Assembly != typeof(jsonData).Assembly) return;
        FieldInfo[] fields;
        if (!stringFields.TryGetValue(type, out fields)) {
            List<FieldInfo> found = new List<FieldInfo>();
            foreach (FieldInfo f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (!f.IsInitOnly && (f.FieldType == typeof(string) || !f.FieldType.IsValueType)) found.Add(f);
            fields = found.ToArray(); stringFields[type] = fields;
        }
        foreach (FieldInfo f in fields) {
            object v = f.GetValue(node);
            string text = v as string;
            if (text != null) { string t = DataText(text); if (t != null) { f.SetValue(node, t); dataChanged++; } }
            else if (v != null && !(v is UnityEngine.Object)) WalkData(v, visited, depth + 1);
        }
    }
    void TranslateData(string reason) {
        if (dataTranslations.Count == 0 || jsonData.instance == null) return;
        dataPending = false;
        DateTime start = DateTime.Now;
        HashSet<object> visited = new HashSet<object>(new RefComparer());
        int before = dataChanged;
        try {
            if (dataTypes == null) {
                dataTypes = new List<Type>();
                foreach (Type t in typeof(jsonData).Assembly.GetTypes())
                    if (!t.IsGenericTypeDefinition && t.GetField("DataList", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) != null) dataTypes.Add(t);
            }
            foreach (Type t in dataTypes) {
                try {
                    FieldInfo listField = t.GetField("DataList", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    WalkData(listField.GetValue(null), visited, 0);
                } catch (Exception error) { Logger.LogWarning("[PTBR dados] " + t.Name + ": " + error.Message); }
            }
            object root = jsonData.instance;
            foreach (FieldInfo f in typeof(jsonData).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                if (f.FieldType.IsValueType || f.FieldType == typeof(string)) continue;
                try { WalkData(f.GetValue(root), visited, 0); }
                catch (Exception error) { Logger.LogWarning("[PTBR dados] " + f.Name + ": " + error.Message); }
            }
        } catch (Exception error) { Logger.LogWarning("[PTBR dados] " + error.Message); }
        if (reason == "cena" && dataChanged == before) dataStable = true;
        Logger.LogInfo("[PTBR dados] " + reason + ": " + (dataChanged - before) + " textos de dados traduzidos em " + (int)(DateTime.Now - start).TotalMilliseconds + " ms.");
        if ((reason == "cena" && dataStable) || reason == "InitLogic") { try { DumpMissing(); } catch (Exception error) { Logger.LogWarning("[PTBR dados] pendentes: " + error.Message); } }
    }
    static void InitLogicPostfix() {
        try { if (instance != null) instance.TranslateData("InitLogic"); } catch (Exception error) { instance.Logger.LogWarning("[PTBR dados] " + error.Message); }
    }

    // Short one-line labels (tabs, buttons) must fit their box: shrink the font
    // instead of wrapping a single word or spilling over the cost number beside it.
    readonly Dictionary<int, string> fitted = new Dictionary<int, string>();
    static readonly Regex latinLetter = new Regex(@"[A-Za-zÀ-ÿ]{2}");
    static readonly Regex numberLike = new Regex(@"^[\s\d+\-−/.,:%()（）]+$");
    // World labels use a Chinese font that has no ã/ç: switch those to a Latin font.
    static bool MissingGlyph(Font font, string text) {
        if (font == null) return false;
        // Decorative Chinese font (map banners): reports ã/ç as present but draws nothing.
        if (font.name.IndexOf("XINWEI", StringComparison.OrdinalIgnoreCase) >= 0 && NeedsAccent(text)) return true;
        foreach (char c in text) if (c > 127 && Char.IsLetter(c) && !(c >= '\u4e00' && c <= '\u9fff') && !font.HasCharacter(c)) return true;
        return false;
    }
    readonly Dictionary<int, int> origSize = new Dictionary<int, int>();
    readonly HashSet<string> sizeLogged = new HashSet<string>();
    void SizeLog(Text label, string value) {
        try {
            if (sizeLogged.Count > 260 || value.Length < 3) return;
            int id = label.GetInstanceID(), orig;
            if (!origSize.TryGetValue(id, out orig)) return;
            RectTransform rt = label.rectTransform;
            float ph = label.preferredHeight, pw = label.preferredWidth;
            bool tiny = label.fontSize < 12 || label.fontSize < orig * 0.6f;
            bool spill = label.verticalOverflow == VerticalWrapMode.Overflow && ph > rt.rect.height + 4f;
            bool wide = label.horizontalOverflow == HorizontalWrapMode.Overflow && pw > rt.rect.width + 4f;
            if (!tiny && !spill && !wide) return;
            if (!sizeLogged.Add(PathOf(label.transform) + value.Length)) return;
            Logger.LogInfo("[PTBR tamanho] " + (tiny ? "pequeno " : "") + (spill ? "vaza-altura " : "") + (wide ? "vaza-largura " : "") + "| rect=" + (int)rt.rect.width + "x" + (int)rt.rect.height + " pref=" + (int)pw + "x" + (int)ph + " fonte=" + orig + "->" + label.fontSize + " ajuste=" + label.resizeTextForBestFit + "(" + label.resizeTextMinSize + "-" + label.resizeTextMaxSize + ") h=" + label.horizontalOverflow + " v=" + label.verticalOverflow + " rolagem=" + (label.GetComponentInParent<ScrollRect>() != null) + " layout=" + LayoutDriven(label) + " | " + (value.Length > 40 ? value.Substring(0, 40) : value).Replace('\n', ' ') + " | " + PathOf(label.transform));
        } catch (Exception) { }
    }
    readonly HashSet<string> cutLogged = new HashSet<string>();
    void CutLog(Text label, string value) {
        try {
            if (cutLogged.Count > 60 || value.Length < 4) return;
            RectTransform rt = label.rectTransform;
            float pw = label.preferredWidth, ph = label.preferredHeight;
            bool wider = label.horizontalOverflow == HorizontalWrapMode.Overflow && pw > rt.rect.width + 2f;
            bool taller = ph > rt.rect.height + 2f && label.verticalOverflow == VerticalWrapMode.Truncate;
            if (!wider && !taller) return;
            if (!cutLogged.Add(PathOf(label.transform) + value)) return;
            Logger.LogInfo("[PTBR corte] " + (wider ? "largura " : "") + (taller ? "altura " : "") + "| rect=" + (int)rt.rect.width + "x" + (int)rt.rect.height + " pref=" + (int)pw + "x" + (int)ph + " fonte=" + label.fontSize + " alinh=" + label.alignment + " | " + (value.Length > 40 ? value.Substring(0, 40) : value).Replace('\n', ' ') + " | " + PathOf(label.transform));
        } catch (Exception) { }
    }
    readonly HashSet<string> uiCutLogged = new HashSet<string>();
    void UiCutLog(Component label, float printedW, float printedH, float boxW, float boxH, string fontName, string text) {
        try {
            if (uiCutLogged.Count > 80 || text == null || text.Length < 4) return;
            if (printedH <= boxH + 2f && printedW <= boxW + 2f) return;
            string where = PathOf(label.transform);
            if (!uiCutLogged.Add(where + text.Length)) return;
            string parentBox = "";
            Transform pt = label.transform.parent;
            if (pt != null) {
                RectTransform prt = pt as RectTransform;
                if (prt != null) parentBox = " pai=" + (int)prt.rect.width + "x" + (int)prt.rect.height;
                UIWidget pw = pt.GetComponent<UIWidget>();
                if (pw != null) parentBox += " paiNGUI=" + pw.width + "x" + pw.height;
            }
            Logger.LogInfo("[PTBR corte UI] " + label.GetType().Name + " | caixa=" + (int)boxW + "x" + (int)boxH + " impresso=" + (int)printedW + "x" + (int)printedH + parentBox + " fonte=" + fontName + " | " + (text.Length > 40 ? text.Substring(0, 40) : text).Replace('\n', ' ') + " | " + where);
        } catch (Exception) { }
    }
    static void Relayout(Component c) {
        try { LayoutRebuilder.MarkLayoutForRebuild(c.transform as RectTransform); } catch (Exception) { }
    }
    static bool NeedsAccent(string s) {
        foreach (char c in s) if (c > 127 && Char.IsLetter(c) && !(c >= '\u4e00' && c <= '\u9fff')) return true;
        return false;
    }
    static string PathOf(Transform t) {
        string p = "";
        for (int n = 0; t != null && n < 8; n++, t = t.parent) p = t.name + (p.Length > 0 ? "/" : "") + p;
        return p;
    }
    readonly HashSet<string> glyphLogged = new HashSet<string>();
    void GlyphLog(string kind, string font, string text, Transform t, bool missing) {
        bool target = text.IndexOf("Mans") >= 0 || text.IndexOf("ascens") >= 0;
        if (!missing && !target) return;
        if (glyphLogged.Count > 150) return;
        if (!glyphLogged.Add(kind + "|" + font + "|" + text)) return;
        Logger.LogInfo("[PTBR glifo] " + kind + " | fonte=" + font + " | faltando=" + missing + " | " + (text.Length > 40 ? text.Substring(0, 40) : text).Replace('\n', ' ') + " | " + PathOf(t));
    }
    static bool IsDynamicFont(object asset) {
        try {
            PropertyInfo p = asset.GetType().GetProperty("atlasPopulationMode");
            object v = p == null ? null : p.GetValue(asset, null);
            return v == null || v.ToString() == "Dynamic";
        } catch (Exception) { return true; }
    }
    static string StripAccents(string text) {
        string d = text.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder();
        foreach (char c in d) if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
    void FixTmpGlyphs(TMP_Text label) {
        try {
            string current = label.text;
            if (!String.IsNullOrEmpty(current) && !HasCjk(current) && label.characterSpacing > 5f) {
                Logger.LogInfo("[PTBR espaco] TMP characterSpacing " + label.characterSpacing + " -> 0 | " + (current.Length > 30 ? current.Substring(0, 30) : current) + " | " + PathOf(label.transform));
                label.characterSpacing = 0f;
            }
            if (String.IsNullOrEmpty(current) || HasCjk(current) || !NeedsAccent(current)) return;
            TMP_FontAsset asset = label.font;
            bool bad = false;
            if (asset != null) foreach (char c in current) if (c > 127 && Char.IsLetter(c) && !asset.HasCharacter(c)) bad = true;
            GlyphLog("TMP", asset == null ? "-" : asset.name + (IsDynamicFont(asset) ? " dinamica" : " estatica"), current, label.transform, bad);
            if (bad && asset != null && !IsDynamicFont(asset)) label.text = StripAccents(current);
        } catch (Exception) { }
    }
    void FixUILabel(UILabel label, string current) {
        try {
            bool bad = false;
            if (label.trueTypeFont != null) bad = MissingGlyph(label.trueTypeFont, current);
            else if (label.bitmapFont != null) bad = true;
            GlyphLog("UILabel", label.trueTypeFont != null ? label.trueTypeFont.name : (label.bitmapFont != null ? "bitmap" : "-"), current, label.transform, bad);
            if (bad && latin != null) { label.trueTypeFont = latin; }
        } catch (Exception) { }
    }
    // Skill/card tooltips: the name box is narrow while the row has free space, so widen it instead of shrinking the font.
    readonly HashSet<int> widened = new HashSet<int>();
    void WidenTip(Text label, string value) {
        try {
            if (numberLike.IsMatch(value)) return;
            if (PathOf(label.transform).IndexOf("SkillTip", StringComparison.Ordinal) < 0) return;
            RectTransform rt = label.rectTransform;
            if (rt.anchorMin.x != rt.anchorMax.x) return;
            if (label.alignment != TextAnchor.UpperLeft && label.alignment != TextAnchor.MiddleLeft && label.alignment != TextAnchor.LowerLeft) return;
            TextGenerationSettings settings = label.GetGenerationSettings(new Vector2(100000f, rt.rect.height));
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.resizeTextForBestFit = false;
            float preferred = label.cachedTextGeneratorForLayout.GetPreferredWidth(value, settings) / label.pixelsPerUnit;
            float width = rt.rect.width;
            if (preferred <= width) return;
            float target = Math.Min(preferred + 6f, Math.Max(width, 330f));
            if (target <= width) return;
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, target);
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x + (target - width) * rt.pivot.x, rt.anchoredPosition.y);
            Logger.LogInfo("[PTBR largura] " + width + "->" + target + " | " + value + " | " + PathOf(label.transform));
        } catch (Exception) { }
    }
    // Loading-screen tips: one wide line of long text; the best-fit pass squeezed it to 9 pt.
    // A label that really cuts its text (last words missing) is let spill vertically instead.
    readonly HashSet<int> rescued = new HashSet<int>();
    void RescueCut(Text label, string value) {
        try {
            if (value.Length < 14 || label.verticalOverflow != VerticalWrapMode.Truncate || label.horizontalOverflow != HorizontalWrapMode.Wrap) return;
            if (label.GetComponentInParent<ScrollRect>() != null) return;
            RectTransform rt = label.rectTransform;
            if (rt.rect.width < 20f || rt.rect.height < 8f) return;
            string plain = Regex.Replace(value, "<[^>]+>", "");
            TextGenerationSettings settings = label.GetGenerationSettings(rt.rect.size);
            TextGenerator generator = label.cachedTextGeneratorForLayout;
            generator.Populate(plain, settings);
            if (generator.characterCountVisible >= plain.Length - 2) return;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            if (rescued.Add(label.GetInstanceID()))
                Logger.LogInfo("[PTBR corte] liberado | rect=" + (int)rt.rect.width + "x" + (int)rt.rect.height + " visivel=" + generator.characterCountVisible + "/" + plain.Length + " | " + (plain.Length > 40 ? plain.Substring(0, 40) : plain) + " | " + PathOf(label.transform));
        } catch (Exception) { }
    }
    readonly HashSet<int> componentChecked = new HashSet<int>();
    // Some titles use a letter-spacing script made for Chinese; it ruins Latin text.
    void SpacingFix(Text label, string where) {
        try {
            if (!componentChecked.Add(label.GetInstanceID())) return;
            foreach (MonoBehaviour component in label.GetComponents<MonoBehaviour>()) {
                if (component == null || component == label) continue;
                string typeName = component.GetType().FullName;
                if (typeName.StartsWith("UnityEngine.", StringComparison.Ordinal) || typeName.StartsWith("TMPro.", StringComparison.Ordinal)) continue;
                if (typeName.IndexOf("Spacing", StringComparison.OrdinalIgnoreCase) >= 0 || typeName.IndexOf("Space", StringComparison.OrdinalIgnoreCase) >= 0) component.enabled = false;
                if (componentLogged++ < 80) Logger.LogInfo("[PTBR componente] " + typeName + (component.enabled ? "" : " (desligado)") + " | " + (label.text.Length > 30 ? label.text.Substring(0, 30) : label.text) + " | " + where);
            }
        } catch (Exception) { }
    }
    int componentLogged;
    static readonly Regex joinedNames = new Regex(@"([a-zà-ú])([A-ZÀ-Ú][a-zà-ú])");
    void LoadingFix(Text label) {
        try {
            string where = PathOf(label.transform);
            SpacingFix(label, where);
            if (shopLogged < 90 && where.IndexOf("MenPaiShop", StringComparison.Ordinal) >= 0 && shopSeen.Add(where + "|" + label.text)) ShopLog(label, where);
            if (where.EndsWith("AvatarCell(Clone)/HasAvatar/Level", StringComparison.Ordinal)) {
                // Save-slot card: the realm name sat in a 34 px wide column at 9 pt.
                RectTransform lr = label.rectTransform;
                float levelWidth = 110f;
                label.resizeTextForBestFit = false;
                label.fontSize = 17;
                label.alignment = TextAnchor.UpperLeft;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                if (Math.Abs(lr.rect.width - levelWidth) > 1f) {
                    float oldLevelWidth = lr.rect.width;
                    lr.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, levelWidth);
                    lr.anchoredPosition = new Vector2(lr.anchoredPosition.x + (levelWidth - oldLevelWidth) * lr.pivot.x, lr.anchoredPosition.y);
                }
                return;
            }
            if (where.EndsWith("/ImageName/Canvas/Text", StringComparison.Ordinal)) {
                MapBanner(label);
                return;
            }
            if (where.EndsWith("/Slot/NameText", StringComparison.Ordinal) && where.IndexOf("NPCInfoPanel", StringComparison.Ordinal) < 0) {
                SlotName(label);
                return;
            }
            if (where.EndsWith("PlayerName/Name_Text", StringComparison.Ordinal)) {
                PlayerName(label);
                return;
            }
            if (where.EndsWith("AvatarCell(Clone)/HasAvatar/subName", StringComparison.Ordinal)) {
                AvatarName(label);
                return;
            }
            if (where.EndsWith("/NameMask/NameText", StringComparison.Ordinal)) {
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 8;
                label.resizeTextMaxSize = 16;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                return;
            }
            if (where.EndsWith("UIPopTipItem(Clone)/Text", StringComparison.Ordinal)) {
                // Notification banners: the dark box is 419x50, so fit the text in at most two lines inside it.
                RectTransform pr = label.rectTransform;
                label.resizeTextForBestFit = false;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                int popSize = Math.Min(22, label.fontSize > 0 ? Math.Max(label.fontSize, 16) : 22);
                label.fontSize = popSize;
                while (popSize > 12 && label.preferredHeight > pr.rect.height + 1f) { popSize--; label.fontSize = popSize; }
                return;
            }
            if (where.EndsWith("/Slot/NameText", StringComparison.Ordinal) && where.IndexOf("NPCInfoPanel", StringComparison.Ordinal) >= 0) {
                // Equipment/technique names under the icons of the NPC panel: wrap inside the slot instead of overlapping neighbours.
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 9;
                label.resizeTextMaxSize = 15;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                label.alignment = TextAnchor.UpperCenter;
                return;
            }
            if (where.EndsWith("NpcCell(Clone)/Bg/ChengHao", StringComparison.Ordinal)) {
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 11;
                label.resizeTextMaxSize = 20;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                return;
            }
            if (where.EndsWith("UINPCXingQu/Image/Text", StringComparison.Ordinal)) {
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 10;
                label.resizeTextMaxSize = 22;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                return;
            }
            if (where.EndsWith("Tips/Bottom/领悟条件/领悟时间", StringComparison.Ordinal)) {
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 11;
                label.resizeTextMaxSize = 22;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                return;
            }
            if (where.EndsWith("Tips/Base/TypeName", StringComparison.Ordinal)) {
                // Item type under the tooltip title ("Instrumentos mágicos (médio)"): wrap inside its box instead of running off the card.
                RectTransform tr = label.rectTransform;
                label.resizeTextForBestFit = false;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                int typeSize = 22;
                label.fontSize = typeSize;
                while (typeSize > 11 && label.preferredHeight > tr.rect.height + 2f) { typeSize--; label.fontSize = typeSize; }
                return;
            }
            if (where.IndexOf("TuJianPanel", StringComparison.Ordinal) >= 0 && where.IndexOf("Dropdown", StringComparison.Ordinal) < 0 && !where.EndsWith("ReturnBtn", StringComparison.Ordinal)) {
                // Encyclopedia pages: text squeezed to a few points by a best-fit pass; give it a readable size.
                int tjId = label.GetInstanceID(), tjOriginal;
                if (!origSize.TryGetValue(tjId, out tjOriginal)) tjOriginal = label.fontSize;
                if (tujianLogged < 60) { tujianLogged++; Logger.LogInfo("[PTBR enciclopedia] fonte=" + label.fontSize + " orig=" + tjOriginal + " melhor=" + label.resizeTextForBestFit + "(" + label.resizeTextMinSize + "-" + label.resizeTextMaxSize + ") rect=" + (int)label.rectTransform.rect.width + "x" + (int)label.rectTransform.rect.height + " v=" + label.verticalOverflow + " | " + (label.text.Length > 30 ? label.text.Substring(0, 30) : label.text) + " | " + where); }
                if (label.fontSize < 14 && (label.resizeTextForBestFit || tjOriginal >= 18)) {
                    label.resizeTextForBestFit = false;
                    label.fontSize = Math.Max(18, Math.Min(tjOriginal, 24));
                    label.horizontalOverflow = HorizontalWrapMode.Wrap;
                    label.verticalOverflow = VerticalWrapMode.Overflow;
                    return;
                }
            }
            if (where.EndsWith("SayDialog/Panel/Daohao", StringComparison.Ordinal)) {
                // Long speaker titles ran leftwards over the portrait: widen the box and wrap on two lines.
                RectTransform dr = label.rectTransform;
                if (label.preferredWidth > dr.rect.width + 4f && label.text.Length > 12) {
                    float oldWidth = dr.rect.width, newWidth = Math.Max(oldWidth, 400f);
                    label.resizeTextForBestFit = false;
                    label.horizontalOverflow = HorizontalWrapMode.Wrap;
                    label.verticalOverflow = VerticalWrapMode.Overflow;
                    label.fontSize = Math.Min(label.fontSize, 22);
                    if (newWidth > oldWidth) {
                        dr.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newWidth);
                        dr.anchoredPosition = new Vector2(dr.anchoredPosition.x - (newWidth - oldWidth) * (1f - dr.pivot.x), dr.anchoredPosition.y);
                    }
                }
                return;
            }
            if (where.EndsWith("SayDialog/Panel/StoryText", StringComparison.Ordinal)) {
                int storyId = label.GetInstanceID(), storyOriginal;
                if (!originalFont.TryGetValue(storyId, out storyOriginal)) { storyOriginal = label.fontSize; originalFont[storyId] = storyOriginal; }
                int storySize = storyOriginal;
                label.fontSize = storySize;
                while (storySize > 14 && label.preferredHeight > label.rectTransform.rect.height + 2f) { storySize--; label.fontSize = storySize; }
                return;
            }
            if (where.EndsWith("Tips/Base/Name", StringComparison.Ordinal)) {
                // Item tooltip title: long Portuguese names overflowed the box on both sides.
                RectTransform nr = label.rectTransform;
                float boxWidth = nr.rect.width;
                if (boxWidth < 40f) return;
                int nameId = label.GetInstanceID(), nameOriginal;
                if (!originalFont.TryGetValue(nameId, out nameOriginal)) { nameOriginal = label.fontSize; originalFont[nameId] = nameOriginal; }
                label.resizeTextForBestFit = false;
                label.fontSize = nameOriginal;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                float wanted = label.preferredWidth;
                if (wanted <= boxWidth + 2f) return;
                int single = (int)Math.Floor(nameOriginal * boxWidth / wanted * 0.97f);
                if (single >= 20) { label.fontSize = single; return; }
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                for (int lines = Math.Min(nameOriginal, 24); lines >= 13; lines--) {
                    label.fontSize = lines;
                    if (label.preferredHeight <= lines * 2.6f) break;
                }
                return;
            }
            if (where.IndexOf("_LoadingScreen/LoadingBar/LoadingText", StringComparison.Ordinal) < 0) return;
            label.resizeTextForBestFit = false;
            label.fontSize = 16;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            if (label.rectTransform.rect.height < 40f) label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 44f);
        } catch (Exception) { }
    }
    // Map place names: the original banner is a narrow vertical flag made for one column of Chinese characters.
    // It is hidden and replaced by a horizontal plaque that grows with the (larger) Portuguese name.
    static readonly Dictionary<string, Sprite> bannerSprites = new Dictionary<string, Sprite>();
    static float BannerDist(float px, float py, float cx, float cy, float tip, float half) {
        float ex = cx - Math.Abs(px - cx), dy = Math.Abs(py - cy);
        if (ex < 0f) return -1f;
        float d = half - dy;
        if (ex < tip) { float slope = half / tip; d = Math.Min(d, (slope * ex - dy) / (float)Math.Sqrt(1f + slope * slope)); }
        return d;
    }
    static Sprite BannerSprite(bool city, int W, int H) {
        W = Math.Max(48, Math.Min(W, 1024)); H = Math.Max(24, Math.Min(H, 160));
        string key = (city ? "c" : "w") + W + "x" + H;
        Sprite found;
        if (bannerSprites.TryGetValue(key, out found) && found != null) return found;
        float u = H / 40f;
        float half = H * 0.5f, tip = half, cx = (W - 1) * 0.5f, cy = (H - 1) * 0.5f;
        Color f0 = city ? new Color(0.50f, 0.34f, 0.17f) : new Color(0.07f, 0.36f, 0.27f);
        Color f1 = city ? new Color(0.70f, 0.52f, 0.30f) : new Color(0.13f, 0.52f, 0.38f);
        Color edge = city ? new Color(0.96f, 0.88f, 0.68f) : new Color(0.72f, 0.93f, 0.80f);
        Color inner = city ? new Color(0.82f, 0.68f, 0.45f) : new Color(0.45f, 0.75f, 0.60f);
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) {
            float r = 0f, g = 0f, bl = 0f, al = 0f;
            for (int sy = 0; sy < 3; sy++) for (int sx = 0; sx < 3; sx++) {
                float px = x + (sx + 0.5f) / 3f - 0.5f, py = y + (sy + 0.5f) / 3f - 0.5f;
                float d = BannerDist(px, py, cx, cy, tip, half);
                if (d < 0f) continue;
                float t = (float)y / (H - 1);
                Color fill = Color.Lerp(f0, f1, t), c = fill;
                if (d < 1.6f * u) c = edge; else if (d < 2.6f * u) c = fill * 0.75f; else if (d < 3.5f * u) c = inner;
                float ex = cx - Math.Abs(px - cx), dy = Math.Abs(py - cy);
                if (d > 3.5f * u && Math.Abs(ex - tip * 0.62f) + dy < 3f * u) c = edge;
                r += c.r; g += c.g; bl += c.b; al += 0.94f;
            }
            if (al <= 0f) { tex.SetPixel(x, y, new Color(0f, 0f, 0f, 0f)); continue; }
            float n = al / 0.94f;
            tex.SetPixel(x, y, new Color(r / n, g / n, bl / n, al / 9f));
        }
        tex.Apply();
        found = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        bannerSprites[key] = found;
        return found;
    }
    readonly Dictionary<int, RectTransform> banners = new Dictionary<int, RectTransform>();
    readonly Dictionary<int, float> bannerHome = new Dictionary<int, float>();
    int bannerLogged;
    void MapBanner(Text label) {
        RectTransform rt = label.rectTransform;
        Transform canvas = label.transform.parent;
        if (canvas == null) return;
        Transform holder = canvas.parent != null ? canvas.parent : canvas;
        label.resizeTextForBestFit = false;
        label.fontSize = 17;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.alignment = TextAnchor.UpperCenter;
        string plain = Regex.Replace(label.text, "<[^>]+>", "");
        float oneLine = label.preferredWidth;
        float width = Math.Min(oneLine + 4f, 210f);
        if (oneLine + 4f > 210f) label.horizontalOverflow = HorizontalWrapMode.Wrap;
        // keep the text centred where the old box was centred, grow it downward
        float oldWidth = rt.rect.width;
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x + (width - oldWidth) * (rt.pivot.x - 0.5f), rt.anchoredPosition.y);
        float height = label.preferredHeight;
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        RectTransform banner;
        int id = label.GetInstanceID();
        bool city = !PathOf(label.transform).StartsWith("AllMap/", StringComparison.Ordinal);
        if (city) label.color = new Color(0.99f, 0.95f, 0.84f);
        if (!banners.TryGetValue(id, out banner) || banner == null) {
            bool raycast = false;
            foreach (Graphic g in holder.GetComponentsInChildren<Graphic>(true)) {
                if (g == label || g.name == "PTBRBanner") continue;
                if (bannerLogged < 6) { bannerLogged++; Logger.LogInfo("[PTBR mapa] grafico " + g.GetType().Name + " " + PathOf(g.transform) + " " + (int)g.rectTransform.rect.width + "x" + (int)g.rectTransform.rect.height); }
                Image im = g as Image;
                if (im != null && im.sprite != null) { raycast |= im.raycastTarget; g.enabled = false; }
            }
            foreach (SpriteRenderer sr in holder.GetComponentsInChildren<SpriteRenderer>(true)) {
                if (bannerLogged < 6) { bannerLogged++; Logger.LogInfo("[PTBR mapa] sprite " + PathOf(sr.transform) + " " + (sr.sprite != null ? sr.sprite.name : "-")); }
                if (sr.sprite != null) sr.enabled = false;
            }
            GameObject go = new GameObject("PTBRBanner", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            go.transform.SetSiblingIndex(label.transform.GetSiblingIndex());
            Image image = go.AddComponent<Image>();
            image.type = Image.Type.Simple; image.raycastTarget = raycast;
            banner = go.GetComponent<RectTransform>();
            banners[id] = banner;
        }
        banner.anchorMin = rt.anchorMin; banner.anchorMax = rt.anchorMax; banner.pivot = new Vector2(0.5f, 0.5f);
        float bw = Math.Max(width, Math.Min(label.preferredWidth, width)) + 40f, bh = height + 10f;
        float home;
        if (!bannerHome.TryGetValue(id, out home)) { home = rt.anchoredPosition.x + (0.5f - rt.pivot.x) * width; bannerHome[id] = home; }
        // On the world map the plaque grows to the left of the old flag, away from the building drawn beside it.
        float cxl = city ? home : home - (bw * 0.5f - 2f);
        rt.anchoredPosition = new Vector2(cxl - (0.5f - rt.pivot.x) * width, rt.anchoredPosition.y);
        float topy = rt.anchoredPosition.y + (1f - rt.pivot.y) * height;
        banner.sizeDelta = new Vector2(bw, bh);
        Image bannerImage = banner.GetComponent<Image>();
        if (bannerImage != null) bannerImage.sprite = BannerSprite(city, (int)(bw * 2f), (int)(bh * 2f));
        banner.anchoredPosition = new Vector2(cxl, topy - height * 0.5f);
        banner.localScale = Vector3.one;
        if (city) FitBannerColliders(holder, banner);
    }
    int colliderLogged;
    // The old flag was clicked through a collider shaped like the vertical flag; stretch it to the new plaque.
    void FitBannerColliders(Transform holder, RectTransform banner) {
        try {
            Vector3[] corners = new Vector3[4];
            banner.GetWorldCorners(corners);
            if (colliderLogged < 4) {
                colliderLogged++;
                for (Transform t = holder; t != null && t != holder.root.parent; t = t.parent) {
                    string names = "";
                    foreach (Component c in t.GetComponents<Component>()) if (c != null) names += c.GetType().Name + ",";
                    Logger.LogInfo("[PTBR mapa] clique " + PathOf(t) + " : " + names);
                    if (t.parent == null || t == holder.parent) break;
                }
            }
            foreach (Collider2D c in holder.GetComponentsInChildren<Collider2D>(true)) {
                BoxCollider2D box = c as BoxCollider2D;
                if (box == null) { if (!c.enabled) continue; c.enabled = false; box = c.gameObject.AddComponent<BoxCollider2D>(); box.isTrigger = c.isTrigger; }
                float minX = 1e9f, minY = 1e9f, maxX = -1e9f, maxY = -1e9f;
                for (int i = 0; i < 4; i++) {
                    Vector3 p = box.transform.InverseTransformPoint(corners[i]);
                    minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x); minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y);
                }
                box.offset = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
                box.size = new Vector2(maxX - minX, maxY - minY);
                box.enabled = true;
            }
            foreach (Collider c in holder.GetComponentsInChildren<Collider>(true)) {
                BoxCollider box = c as BoxCollider;
                if (box == null) { if (!c.enabled) continue; c.enabled = false; box = c.gameObject.AddComponent<BoxCollider>(); box.isTrigger = c.isTrigger; }
                float minX = 1e9f, minY = 1e9f, maxX = -1e9f, maxY = -1e9f;
                for (int i = 0; i < 4; i++) {
                    Vector3 p = box.transform.InverseTransformPoint(corners[i]);
                    minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x); minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y);
                }
                box.center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, box.center.z);
                box.size = new Vector3(maxX - minX, maxY - minY, Math.Max(box.size.z, 0.1f));
                box.enabled = true;
            }
            // The clickable box sits on the location object itself (next to ImageName): replace the old flag area by the plaque area only.
            Transform owner = holder.parent;
            if (owner != null) {
                BoxCollider ob = owner.GetComponent<BoxCollider>();
                if (ob != null) {
                    Vector3[] orig;
                    if (!originalBoxes.TryGetValue(ob.GetInstanceID(), out orig)) { orig = new Vector3[] { ob.center, ob.size }; originalBoxes[ob.GetInstanceID()] = orig; }
                    float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f;
                    for (int i = 0; i < 4; i++) {
                        Vector3 p = ob.transform.InverseTransformPoint(corners[i]);
                        minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x); minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y);
                    }
                    ob.center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, orig[0].z);
                    ob.size = new Vector3(maxX - minX, maxY - minY, orig[1].z);
                    if (colliderLogged < 8) { colliderLogged++; Logger.LogInfo("[PTBR mapa] caixa de clique " + PathOf(owner) + " " + orig[1] + " -> " + ob.size); }
                }
                BoxCollider2D ob2 = owner.GetComponent<BoxCollider2D>();
                if (ob2 != null) {
                    Vector3[] orig;
                    if (!originalBoxes.TryGetValue(ob2.GetInstanceID(), out orig)) { orig = new Vector3[] { ob2.offset, ob2.size }; originalBoxes[ob2.GetInstanceID()] = orig; }
                    float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f;
                    for (int i = 0; i < 4; i++) {
                        Vector3 p = ob2.transform.InverseTransformPoint(corners[i]);
                        minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x); minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y);
                    }
                    ob2.offset = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
                    ob2.size = new Vector2(maxX - minX, maxY - minY);
                }
            }
        } catch (Exception) { }
    }
    readonly Dictionary<int, Vector3[]> originalBoxes = new Dictionary<int, Vector3[]>();
    readonly Dictionary<int, Vector3[]> slotHome = new Dictionary<int, Vector3[]>();
    // Item names under reward/shop icons: one or two lines stay on the caption bar; longer names move under the
    // frame onto a dark plate, so they never spill over the next icon.
    void SlotName(Text label) {
        RectTransform rt = label.rectTransform;
        int id = label.GetInstanceID();
        // Sect shops: the price sits in the band under the frame, so long names grow upward over the icon instead.
        bool up = PathOf(label.transform).IndexOf("UIMenPaiShopItem", StringComparison.Ordinal) >= 0;
        Vector3[] home;
        if (!slotHome.TryGetValue(id, out home)) {
            home = new Vector3[] { rt.localPosition, new Vector3(rt.rect.width, rt.rect.height, 0f), new Vector3(rt.pivot.x, rt.pivot.y, 0f) };
            slotHome[id] = home;
        }
        float w = up ? Math.Min(home[1].x, 104f) : home[1].x;
        label.resizeTextForBestFit = false;
        label.fontSize = up ? 12 : 14;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        rt.pivot = new Vector2(home[2].x, home[2].y);
        rt.localPosition = home[0];
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, home[1].y);
        label.alignment = TextAnchor.UpperCenter;
        Transform plate = label.transform.parent != null ? label.transform.parent.Find("PTBRNamePlate") : null;
        float need = label.preferredHeight;
        if (need <= home[1].y + 2f) { if (plate != null) plate.gameObject.SetActive(false); return; }
        float h = need + 6f;
        float centerX = home[0].x + (0.5f - home[2].x) * home[1].x;
        float bottomY = home[0].y - home[2].y * home[1].y;
        if (up) {
            label.alignment = TextAnchor.LowerCenter;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.localPosition = new Vector3(centerX, bottomY + 1f, home[0].z);
        } else {
            rt.pivot = new Vector2(0.5f, 1f);
            rt.localPosition = new Vector3(centerX, bottomY - 2f, home[0].z);
        }
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
        if (plate == null && label.transform.parent != null) {
            GameObject go = new GameObject("PTBRNamePlate", typeof(RectTransform));
            go.transform.SetParent(label.transform.parent, false);
            Image im = go.AddComponent<Image>();
            im.color = new Color(0.45f, 0.62f, 0.40f, 1f); im.raycastTarget = false;
            GameObject fill = new GameObject("Fill", typeof(RectTransform));
            fill.transform.SetParent(go.transform, false);
            RectTransform fr = (RectTransform)fill.transform;
            fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = new Vector2(1.5f, 1.5f); fr.offsetMax = new Vector2(-1.5f, -1.5f);
            Image fi = fill.AddComponent<Image>();
            fi.color = new Color(0.10f, 0.20f, 0.19f, 0.96f); fi.raycastTarget = false;
            plate = go.transform;
        }
        if (plate != null) {
            plate.gameObject.SetActive(true);
            plate.SetSiblingIndex(label.transform.GetSiblingIndex());
            RectTransform pr = (RectTransform)plate;
            pr.anchorMin = rt.anchorMin; pr.anchorMax = rt.anchorMax; pr.pivot = rt.pivot;
            pr.localPosition = rt.localPosition + new Vector3(0f, up ? -1f : 2f, 0f);
            pr.localScale = Vector3.one;
            pr.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w + 6f);
            pr.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h + 2f);
        }
    }
    // Save-slot name: the old tall brush stroke was drawn for vertical Chinese text; draw a horizontal one.
    static readonly Dictionary<string, Sprite> brushSprites = new Dictionary<string, Sprite>();
    static float Hash(float x, float y) { float v = (float)Math.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f; return v - (float)Math.Floor(v); }
    static float Noise1(float x, float seed) {
        float i = (float)Math.Floor(x), f = x - i, u = f * f * (3f - 2f * f);
        return Hash(i, seed) * (1f - u) + Hash(i + 1f, seed) * u;
    }
    static float Smooth01(float v) { v = Math.Max(0f, Math.Min(1f, v)); return v * v * (3f - 2f * v); }
    // Horizontal ink-brush stroke: thick loaded start, wobbling edges, bristle streaks, and a dry tail that splits into hairs.
    static Sprite BrushSprite(bool green) {
        string key = green ? "g" : "b";
        Sprite found;
        if (brushSprites.TryGetValue(key, out found) && found != null) return found;
        int W = 512, H = 96;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
        Color c0 = green ? new Color(0.12f, 0.19f, 0.17f) : new Color(0.25f, 0.19f, 0.21f);
        Color c1 = green ? new Color(0.24f, 0.35f, 0.27f) : new Color(0.46f, 0.31f, 0.15f);
        Color c2 = green ? new Color(0.80f, 0.82f, 0.52f) : new Color(0.92f, 0.80f, 0.54f);
        for (int x = 0; x < W; x++) {
            float t = (float)x / (W - 1);
            float swell = 0.40f + 0.05f * Smooth01(t / 0.3f);
            float taper = t > 0.62f ? Smooth01((t - 0.62f) / 0.38f) : 0f;
            float half = H * (swell * (1f - 0.86f * taper));
            half += H * 0.035f * (Noise1(x / 22f, 1f) * 2f - 1f) + H * 0.012f * (Noise1(x / 5f, 2f) * 2f - 1f);
            half += H * 0.10f * Math.Max(0f, 1f - Math.Abs(t - 0.04f) / 0.045f);
            float curl = taper * taper * H * 0.20f;
            for (int y = 0; y < H; y++) {
                float dy = y - (H * 0.5f - curl);
                float edge = half - Math.Abs(dy);
                float a = Smooth01((edge + 0.8f) / 1.8f);
                // loaded, slightly torn start
                a *= Smooth01((x - (5f + 9f * Noise1(y / 3f, 5f))) / 5f);
                // bristle fingers at the tail
                float bristle = Noise1(y / 1.6f, 7f);
                float tailEnd = 0.99f - 0.30f * bristle * bristle;
                a *= 1f - Smooth01((t - (tailEnd - 0.10f)) / 0.10f);
                // dry-brush streaks along the stroke
                float streak = Noise1(y / 1.3f, 11f) * 0.5f + Noise1(y / 4f, 13f) * 0.5f;
                if (t > 0.45f) a *= 1f - Smooth01((t - 0.45f) / 0.55f) * 0.5f * (1f - streak);
                float mottle = 0.93f + 0.07f * Noise1(x / 9f + y * 0.35f, 17f);
                Color c = t < 0.62f ? Color.Lerp(c0, c1, t / 0.62f) : Color.Lerp(c1, c2, (t - 0.62f) / 0.38f);
                c.r *= mottle; c.g *= mottle; c.b *= mottle;
                c.a = Math.Max(0f, Math.Min(1f, a));
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        found = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        brushSprites[key] = found;
        return found;
    }
    readonly Dictionary<int, Vector3> brushHome = new Dictionary<int, Vector3>();
    void PlayerName(Text label) {
        Transform holder = label.transform.parent;
        if (holder == null) return;
        Image brush = null;
        foreach (Image im in holder.GetComponentsInChildren<Image>(true)) {
            RectTransform r = im.rectTransform;
            if (im.sprite != null && r.rect.height > r.rect.width * 2f && r.rect.height > 80f) { brush = im; break; }
        }
        BrushName(label, brush, true);
    }
    void AvatarName(Text label) {
        Transform holder = label.transform.parent;
        if (holder == null) return;
        Image brush = null;
        foreach (Image im in holder.GetComponentsInChildren<Image>(true))
            if (im.sprite != null && im.sprite.name.IndexOf("mingzi_bg", StringComparison.Ordinal) >= 0) { brush = im; break; }
        BrushName(label, brush, false);
    }
    void BrushName(Text label, Image brush, bool green) {
        label.resizeTextForBestFit = false;
        label.fontSize = 22;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.98f, 0.94f, 0.84f);
        float width = Math.Max(170f, Math.Min(label.preferredWidth + 70f, 250f)), height = 50f;
        if (brush == null) {
            RectTransform lr = label.rectTransform;
            lr.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            lr.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            return;
        }
        RectTransform br = brush.rectTransform;
        int id = brush.GetInstanceID();
        Vector3 topLeft;
        if (!brushHome.TryGetValue(id, out topLeft)) {
            Vector3[] c = new Vector3[4];
            br.GetWorldCorners(c);
            topLeft = br.parent.InverseTransformPoint(c[1]);
            brushHome[id] = topLeft;
            Logger.LogInfo("[PTBR nome] pincel " + PathOf(br) + " " + (int)br.rect.width + "x" + (int)br.rect.height);
        }
        brush.sprite = BrushSprite(green); brush.type = Image.Type.Simple; brush.preserveAspect = false; brush.enabled = true;
        br.pivot = new Vector2(0f, 1f);
        br.localPosition = topLeft;
        br.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        br.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        br.localRotation = Quaternion.identity;
        RectTransform tr = label.rectTransform;
        tr.pivot = new Vector2(0.5f, 0.5f);
        tr.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width - 40f);
        tr.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        tr.localRotation = Quaternion.identity;
        tr.position = br.TransformPoint(new Vector3(width * 0.45f, -height * 0.5f, 0f));
    }
    int shopLogged, tujianLogged;
    readonly HashSet<string> shopSeen = new HashSet<string>();
    void ShopLog(Text label, string where) {
        try {
            shopLogged++;
            RectTransform rt = label.rectTransform;
            Vector3[] c = new Vector3[4]; rt.GetWorldCorners(c);
            string masks = "";
            for (Transform t = label.transform.parent; t != null && t.parent != null; t = t.parent) {
                if (t.GetComponent<Mask>() != null) masks += t.name + "(Mask) ";
                if (t.GetComponent<RectMask2D>() != null) masks += t.name + "(RectMask2D) ";
            }
            Logger.LogInfo("[PTBR loja] " + where + " | '" + (label.text.Length > 30 ? label.text.Substring(0, 30) : label.text) + "' rect=" + (int)rt.rect.width + "x" + (int)rt.rect.height + " anch=" + rt.anchorMin + rt.anchorMax + " piv=" + rt.pivot + " mundo=(" + (int)c[0].x + "," + (int)c[0].y + ")-(" + (int)c[2].x + "," + (int)c[2].y + ") fonte=" + label.fontSize + " melhor=" + label.resizeTextForBestFit + " alinh=" + label.alignment + " v=" + label.verticalOverflow + " mascaras=" + masks);
            Transform cell = label.transform.parent != null && label.transform.parent.parent != null ? label.transform.parent.parent : label.transform.parent;
            if (cell != null && shopLogged < 30)
                foreach (Text t in cell.GetComponentsInChildren<Text>(true)) {
                    if (t == label) continue;
                    Vector3[] tc = new Vector3[4]; t.rectTransform.GetWorldCorners(tc);
                    Logger.LogInfo("[PTBR loja]    irmao " + PathOf(t.transform) + " | '" + (t.text.Length > 20 ? t.text.Substring(0, 20) : t.text) + "' rect=" + (int)t.rectTransform.rect.width + "x" + (int)t.rectTransform.rect.height + " mundo=(" + (int)tc[0].x + "," + (int)tc[0].y + ")-(" + (int)tc[2].x + "," + (int)tc[2].y + ") fonte=" + t.fontSize);
                }
        } catch (Exception) { }
    }
    readonly HashSet<string> guideDumped = new HashSet<string>();
    float nextGuideDump; int guideTypesLogged;
    static bool InTuJian(Component c) {
        for (Transform t = c.transform; t != null; t = t.parent)
            if (t.name.IndexOf("TuJian", StringComparison.Ordinal) >= 0 || t.name.IndexOf("HyTextSV", StringComparison.Ordinal) >= 0 || t.name.IndexOf("Guide", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
    // Every image shown in the Guides/encyclopedia window goes to Dump\Guia (cropped per sprite), to be redrawn in Portuguese.
    void GuideDump() {
        try {
            if (Time.unscaledTime < nextGuideDump || guideDumped.Count > 900) return;
            nextGuideDump = Time.unscaledTime + 1.5f;
            foreach (Image image in UnityEngine.Object.FindObjectsOfType<Image>()) {
                Sprite sp = image.sprite;
                if (sp == null || sp.texture == null || !InTuJian(image)) continue;
                if (!guideDumped.Add("S " + sp.texture.name + "/" + sp.name)) continue;
                DumpGuideSprite(sp);
            }
            foreach (RawImage raw in UnityEngine.Object.FindObjectsOfType<RawImage>()) {
                if (raw.texture == null || !InTuJian(raw) || !guideDumped.Add("T " + raw.texture.name)) continue;
                DumpGuideTexture(raw.texture);
            }
            // Other graphics (the inline pictures of the guide text are drawn by custom components).
            foreach (Graphic g in UnityEngine.Object.FindObjectsOfType<Graphic>()) {
                if (g is Text || g is Image || g is RawImage || !InTuJian(g)) continue;
                Texture tex = g.mainTexture;
                string typeName = g.GetType().Name;
                if (guideTypesLogged < 40 && guideDumped.Add("G " + typeName + "|" + (tex != null ? tex.name : "-"))) {
                    guideTypesLogged++;
                    Logger.LogInfo("[PTBR guia] grafico " + typeName + " textura=" + (tex != null ? tex.name + " " + tex.width + "x" + tex.height : "-") + " | " + PathOf(g.transform));
                }
                if (tex != null && tex.width >= 32 && tex.height >= 32 && guideDumped.Add("T " + tex.name)) DumpGuideTexture(tex);
            }
        } catch (Exception) { }
    }
    static string SafeName(string name) {
        foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        return name.Replace(".png", "");
    }
    Texture2D ReadTexture(Texture tex, Rect area) {
        RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(tex, rt);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D copy = new Texture2D((int)area.width, (int)area.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(area, 0, 0);
        copy.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return copy;
    }
    void DumpGuideSprite(Sprite sp) {
        try {
            string dir = Path.Combine(Path.Combine(pluginFolder, "Dump"), "Guia");
            Directory.CreateDirectory(dir);
            Rect nameRect = sp.textureRect;
            string spriteLabel = String.IsNullOrEmpty(sp.name) ? "sprite_" + (int)nameRect.x + "_" + (int)nameRect.y + "_" + (int)nameRect.width + "x" + (int)nameRect.height : SafeName(sp.name);
            string file = Path.Combine(dir, spriteLabel + " [" + SafeName(sp.texture.name) + "].png");
            if (File.Exists(file)) return;
            Rect r = sp.textureRect;
            if (r.width < 2f || r.height < 2f) return;
            Texture2D copy = ReadTexture(sp.texture, r);
            File.WriteAllBytes(file, ImageConversion.EncodeToPNG(copy));
            UnityEngine.Object.Destroy(copy);
        } catch (Exception) { }
    }
    void DumpGuideTexture(Texture tex) {
        try {
            string dir = Path.Combine(Path.Combine(pluginFolder, "Dump"), "Guia");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, SafeName(tex.name) + ".png");
            if (File.Exists(file)) return;
            Texture2D copy = ReadTexture(tex, new Rect(0, 0, tex.width, tex.height));
            File.WriteAllBytes(file, ImageConversion.EncodeToPNG(copy));
            UnityEngine.Object.Destroy(copy);
        } catch (Exception) { }
    }
    static bool InInfoRoot(Component c) {
        for (Transform t = c.transform; t != null; t = t.parent) if (t.name == "InfoRoot") return true;
        return false;
    }
    int infoLogged;
    readonly Dictionary<string, int> infoSect = new Dictionary<string, int>();
    readonly Dictionary<int, string> infoFit = new Dictionary<int, string>();
    readonly Dictionary<int, float> tmpOriginal = new Dictionary<int, float>();
    // Encyclopedia/guide pages lay their text out themselves: keep the original font size and let the page grow,
    // instead of shrinking or resizing boxes (that left tiny text and big gaps).
    void InfoKeep(Text label) {
        try {
            int id = label.GetInstanceID(), orig;
            if (!origSize.TryGetValue(id, out orig)) { orig = label.fontSize; origSize[id] = orig; }
            string sect = PathOf(label.transform);
            int ir = sect.IndexOf("InfoRoot/", StringComparison.Ordinal);
            sect = ir >= 0 ? sect.Substring(ir + 9, Math.Min(24, sect.Length - ir - 9)) : sect;
            int sc; infoSect.TryGetValue(sect, out sc);
            if (sc < 14 && infoLogged < 400) { infoSect[sect] = sc + 1; infoLogged++; Logger.LogInfo("[PTBR info] pos=" + (int)label.rectTransform.anchoredPosition.x + "," + (int)label.rectTransform.anchoredPosition.y + " al=" + label.alignment + " " + label.GetType().Name + " fonte=" + label.fontSize + " orig=" + orig + " melhor=" + label.resizeTextForBestFit + " rect=" + (int)label.rectTransform.rect.width + "x" + (int)label.rectTransform.rect.height + " pref=" + (int)label.preferredHeight + " v=" + label.verticalOverflow + " csf=" + (label.GetComponent<ContentSizeFitter>() != null) + " le=" + (label.GetComponent<LayoutElement>() != null) + " | " + (label.text.Length > 24 ? label.text.Substring(0, 24) : label.text) + " | " + PathOf(label.transform)); }
            label.resizeTextForBestFit = false;
            if (label.GetType() != typeof(Text) && (label.name == "InfoText1" || label.name == "InfoText") && InInfoRoot(label) && PathOf(label.transform).IndexOf("DanYaoInfo/", StringComparison.Ordinal) >= 0) {
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                PillFit(label, orig);
                LayoutRebuilder.MarkLayoutForRebuild(label.rectTransform);
                return;
            }
            if (label.name == "Text" && label.transform.parent != null && label.transform.parent.name.StartsWith("DoubleSVItem", StringComparison.Ordinal) && label.GetType() == typeof(Text)) {
                // v0.61: left column of the two-column pages: one line, shrink the font until it fits the column.
                string done;
                if (infoFit.TryGetValue(id, out done) && done == label.text) return;
                infoFit[id] = label.text;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                label.fontSize = orig;
                float colW = label.rectTransform.rect.width - 16f, wantW = label.preferredWidth;
                if (colW > 4f && wantW > colW) label.fontSize = Math.Max(14, (int)(orig * colW / wantW));
                LayoutRebuilder.MarkLayoutForRebuild(label.rectTransform);
                return;
            }
            label.fontSize = orig;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            if (label.GetType() == typeof(Text) && label.rectTransform.rect.height <= orig * 1.7f) {
                // v0.62: single-line slot (recipe rows etc.): keep one line, shrink the font to the slot width.
                string done2;
                if (!(infoFit.TryGetValue(id, out done2) && done2 == label.text)) {
                    infoFit[id] = label.text;
                    label.horizontalOverflow = HorizontalWrapMode.Overflow;
                    float w2 = label.rectTransform.rect.width - 8f, p2 = label.preferredWidth;
                    if (w2 > 4f && p2 > w2) label.fontSize = Math.Max(14, (int)(orig * w2 / p2));
                } else label.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            PillFit(label, orig);
            LayoutRebuilder.MarkLayoutForRebuild(label.rectTransform);
        } catch (Exception) { }
    }
    // v0.63: pill page (DanYaoInfo) has fixed boxes sized for Chinese: shrink the text until it fits its box,
    // so the recipe block no longer overlaps the "Info" text.
    static readonly Regex sizeCode = new Regex(@"#s(\d+)");
    void PillFit(Text label, int orig) {
        try {
            if (label.GetType() == typeof(Text)) return;
            string path = PathOf(label.transform);
            bool recipe = path.EndsWith("DanFangInfoBG/InfoText", StringComparison.Ordinal);
            bool main = path.EndsWith("DanYaoInfo/InfoText1", StringComparison.Ordinal);
            if (!recipe && !main) return;
            int id = label.GetInstanceID();
            string done;
            if (infoFit.TryGetValue(id, out done) && done == label.text) return;
            float h = label.rectTransform.rect.height;
            if (recipe) {
                string txt = label.text;
                Match m = sizeCode.Match(txt);
                int sz = m.Success ? int.Parse(m.Groups[1].Value) : 27;
                while (sz > 16 && label.preferredHeight > h + 1f) {
                    sz--;
                    txt = m.Success ? sizeCode.Replace(txt, "#s" + sz, 1) : txt;
                    label.text = txt;
                    if (!m.Success) { label.fontSize = sz; }
                }
            } else {
                label.fontSize = orig;
                int f = orig;
                while (f > 20 && label.preferredHeight > h + 1f) { f--; label.fontSize = f; }
            }
            infoFit[id] = label.text;
        } catch (Exception) { }
    }
    // v0.62: dropdown labels of the encyclopedia ("Todas as categorias"): one line, font shrunk to the box width.
    bool DropdownFit(Text label, string value) {
        try {
            if (!InTuJian(label)) return false;
            bool dd = false;
            for (Transform t = label.transform.parent; t != null; t = t.parent) if (t.name.IndexOf("Dropdown", StringComparison.Ordinal) >= 0) { dd = true; break; }
            if (!dd) return false;
            int id = label.GetInstanceID(), orig;
            if (!origSize.TryGetValue(id, out orig)) { orig = label.fontSize; origSize[id] = orig; }
            if (value != null && label.text != value) label.text = value;
            string done;
            if (infoFit.TryGetValue(id, out done) && done == label.text) return true;
            infoFit[id] = label.text;
            label.resizeTextForBestFit = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.fontSize = orig;
            float w = label.rectTransform.rect.width - 8f, pw = label.preferredWidth;
            if (w > 4f && pw > w) label.fontSize = Math.Max(10, (int)(orig * w / pw));
            return true;
        } catch (Exception) { return false; }
    }
    readonly HashSet<string> dlgLogged = new HashSet<string>();
    void DlgLog(Text label) {
        try {
            if (dlgLogged.Count > 24) return;
            string path = PathOf(label.transform);
            if (path.IndexOf("SayDialog", StringComparison.Ordinal) < 0 || path.EndsWith("Daohao", StringComparison.Ordinal) || label.text.Length < 30) return;
            if (!dlgLogged.Add(path + (label.preferredHeight > label.rectTransform.rect.height + 2f ? "X" : ""))) return;
            RectTransform rt = label.rectTransform;
            Logger.LogInfo("[PTBR dialogo] rect=" + (int)rt.rect.width + "x" + (int)rt.rect.height + " pref=" + (int)label.preferredWidth + "x" + (int)label.preferredHeight + " fonte=" + label.fontSize + " melhor=" + label.resizeTextForBestFit + "(" + label.resizeTextMinSize + "-" + label.resizeTextMaxSize + ") h=" + label.horizontalOverflow + " v=" + label.verticalOverflow + " espaco=" + label.lineSpacing + " csf=" + (label.GetComponent<ContentSizeFitter>() != null) + " paiCsf=" + (label.transform.parent != null && label.transform.parent.GetComponent<ContentSizeFitter>() != null) + " | " + path);
        } catch (Exception) { }
    }
    void FitWidth(Text label, string value) {
        try {
            WidenTip(label, value);
            if (String.IsNullOrEmpty(value) || value.IndexOf('\n') >= 0 || value.Length > 40) return;
            RectTransform rt = label.rectTransform;
            float width = rt.rect.width;
            if (width <= 4f) return;
            Vector3[] me = new Vector3[4];
            rt.GetWorldCorners(me);
            float worldWidth = me[2].x - me[0].x;
            if (worldWidth <= 0f) return;
            float scale = worldWidth / width;
            float center = (me[0].x + me[2].x) * 0.5f;
            Transform parent = rt.parent;
            // A right/left aligned title can be wider than the panel that holds it:
            // only the part inside the parent panel counts.
            RectTransform prt = parent as RectTransform;
            for (Transform walk = parent; walk != null && walk.parent != null; walk = walk.parent) {
                if (walk.GetComponent<Image>() != null && walk is RectTransform) { prt = (RectTransform)walk; break; }
                if (walk.GetComponent<Canvas>() != null) break;
            }
            if (prt != null && prt.rect.width > 4f) {
                Vector3[] pc = new Vector3[4];
                prt.GetWorldCorners(pc);
                float inside = Math.Min(me[2].x, pc[2].x) - Math.Max(me[0].x, pc[0].x);
                if (inside > 8f * scale && inside < worldWidth - 2f * scale) width = Math.Min(width, inside / scale - 4f);
            }
            if (parent != null) {
                foreach (Text other in parent.GetComponentsInChildren<Text>()) {
                    if (other == label || !other.gameObject.activeInHierarchy) continue;
                    string t = other.text;
                    if (String.IsNullOrEmpty(t) || t.Trim().Length == 0 || !numberLike.IsMatch(t)) continue;
                    Vector3[] oc = new Vector3[4];
                    other.rectTransform.GetWorldCorners(oc);
                    bool vertical = oc[1].y > me[0].y && oc[0].y < me[1].y;
                    if (!vertical || oc[0].x <= center || oc[0].x >= me[2].x) continue;
                    float avail = 2f * (oc[0].x - center) / scale - 6f;
                    if (avail > 12f && avail < width) width = avail;
                }
            }
            if (!label.resizeTextForBestFit && value.IndexOf(' ') >= 0 && rt.rect.width >= 60f && rt.rect.height >= Math.Max(9, (int)(label.fontSize * 0.6f)) * 2.1f) {
                // Room for two or more lines: wrap the text and keep the font as large as the box allows.
                int wrapBase = label.fontSize, wrapBest = -1;
                int wrapFloor = Math.Max(9, (int)(wrapBase * 0.6f));
                for (int wrapSize = wrapBase; wrapSize >= wrapFloor; wrapSize--) {
                    TextGenerationSettings wrapSettings = label.GetGenerationSettings(new Vector2(width, rt.rect.height));
                    wrapSettings.fontSize = wrapSize;
                    wrapSettings.resizeTextForBestFit = false;
                    wrapSettings.horizontalOverflow = HorizontalWrapMode.Wrap;
                    wrapSettings.verticalOverflow = VerticalWrapMode.Overflow;
                    if (label.cachedTextGeneratorForLayout.GetPreferredHeight(value, wrapSettings) / label.pixelsPerUnit <= rt.rect.height + 1f) { wrapBest = wrapSize; break; }
                }
                if (wrapBest > 0) {
                    label.fontSize = wrapBest;
                    if (label.horizontalOverflow != HorizontalWrapMode.Wrap) label.horizontalOverflow = HorizontalWrapMode.Wrap;
                    return;
                }
            }
            int baseSize = label.resizeTextForBestFit ? Math.Max(label.resizeTextMaxSize, 1) : label.fontSize;
            TextGenerationSettings settings = label.GetGenerationSettings(new Vector2(100000f, rt.rect.height));
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.resizeTextForBestFit = false;
            settings.fontSize = baseSize;
            float preferred = label.cachedTextGeneratorForLayout.GetPreferredWidth(value, settings) / label.pixelsPerUnit;
            if (preferred <= width) return;
            int size = Math.Max(9, (int)Math.Floor(baseSize * width / preferred * 0.95f));
            string abbr;
            if (size < 13 && !abbreviating && abbreviations.TryGetValue(value, out abbr)) {
                abbreviating = true;
                try { label.text = abbr; } finally { abbreviating = false; }
                FitWidth(label, abbr);
                return;
            }
            label.fontSize = size;
            if (label.resizeTextForBestFit) label.resizeTextMaxSize = Math.Min(label.resizeTextMaxSize, size);
            if (value.IndexOf(' ') < 0) label.horizontalOverflow = HorizontalWrapMode.Overflow;
        } catch (Exception) { }
    }
    // Multi-line text in a fixed box (tooltips, intro text): Portuguese is longer
    // than Chinese, so shrink the font until it fits the box height.
    // Glossary/guide pages: rows flow in a list, so make the row taller instead of shrinking the font.
    static bool InGuide(Component c) {
        for (Transform t = c.transform; t != null; t = t.parent) if (t.name.IndexOf("HyTextSV", StringComparison.Ordinal) >= 0) return true;
        return false;
    }
    readonly HashSet<int> grown = new HashSet<int>();
    void GrowToFit(Text label) {
        try {
            RectTransform rt = label.rectTransform;
            float ph = label.preferredHeight;
            float delta = ph - rt.rect.height;
            if (delta <= 2f) return;
            int key = label.GetInstanceID() * 31 + (int)ph;
            if (!grown.Add(key)) return;
            LayoutElement own = label.GetComponent<LayoutElement>();
            if (own != null) { if (own.preferredHeight > 0f) own.preferredHeight += delta; if (own.minHeight > 0f) own.minHeight += delta; }
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ph);
            ScrollRect sr = label.GetComponentInParent<ScrollRect>();
            Transform item = rt.parent;
            RectTransform irt = item as RectTransform;
            if (irt != null && (sr == null || item != sr.content)) {
                LayoutElement le = item.GetComponent<LayoutElement>();
                if (le != null) { if (le.preferredHeight > 0f) le.preferredHeight += delta; if (le.minHeight > 0f) le.minHeight += delta; }
                else if (item.GetComponent<ContentSizeFitter>() == null) irt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, irt.rect.height + delta);
            }
            if (sr != null && sr.content != null) LayoutRebuilder.MarkLayoutForRebuild(sr.content);
        } catch (Exception) { }
    }
    bool abbreviating;
    static readonly Dictionary<string, string> abbreviations = new Dictionary<string, string> {
        { "Artefatos", "Artef." }, { "Elixires", "Elix." }, { "Materiais", "Mater." }, { "Manuais", "Manual" }, { "Técnicas", "Técn." }, { "Equipamento", "Equip." }, { "Todas as categorias", "Todas" }, { "Caos Primordial", "Caos" }, { "Pensamento espiritual", "Espírito" }, { "Excluir receita", "Excluir" }
    };
    readonly Dictionary<int, int> originalFont = new Dictionary<int, int>();
    void FitHeight(Text label, string value) {
        try {
            if (String.IsNullOrEmpty(value) || (value.Length <= 40 && value.IndexOf('\n') < 0)) return;
            if (InGuide(label)) return;
            if (label.resizeTextForBestFit && label.verticalOverflow != VerticalWrapMode.Overflow) return;
            if (label.horizontalOverflow != HorizontalWrapMode.Wrap) return;
            if (label.GetComponent<ContentSizeFitter>() != null) return;
            Transform parent = label.transform.parent;
            if (parent != null && parent.GetComponent<ContentSizeFitter>() != null) return;
            RectTransform rt = label.rectTransform;
            float width = rt.rect.width, height = rt.rect.height;
            bool small = height < 30f;
            if (width < 40f || height < 10f) return;
            if (small && (value.IndexOf('\n') >= 0 || value.Length > 90)) return;
            int id = label.GetInstanceID(), original;
            if (!originalFont.TryGetValue(id, out original)) { original = label.fontSize; originalFont[id] = original; }
            int size = original;
            int minimum = small ? Math.Max(8, (int)(size * 0.45f)) : Math.Max(10, (int)(size * 0.55f));
            for (int step = 0; step < 40 && size >= minimum; step++) {
                TextGenerationSettings settings = label.GetGenerationSettings(new Vector2(width, height));
                settings.fontSize = size;
                settings.resizeTextForBestFit = false;
                settings.verticalOverflow = VerticalWrapMode.Overflow;
                float preferred = label.cachedTextGeneratorForLayout.GetPreferredHeight(value, settings) / label.pixelsPerUnit;
                if (preferred <= height + 2f) break;
                size--;
            }
            label.fontSize = Math.Max(size, minimum);
        } catch (Exception) { }
    }
    // Diagnostics: list sprite atlases seen on screen so untranslated image text can be located.
    readonly HashSet<string> seenSprites = new HashSet<string>();
    readonly HashSet<string> dumpNames = new HashSet<string>();
    string pluginFolder;
    // Copies a (possibly non-readable) texture to Dump\<name>.png so its text can be repainted.
    void DumpTexture(Texture tex) {
        try {
            string dir = Path.Combine(pluginFolder, "Dump");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, tex.name + ".png");
            if (File.Exists(file)) return;
            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(file, ImageConversion.EncodeToPNG(copy));
            UnityEngine.Object.Destroy(copy);
            Logger.LogInfo("[PTBR] Textura exportada: " + file);
        } catch (Exception error) { Logger.LogWarning("[PTBR] Exportar textura falhou: " + error.Message); }
    }
    int guideDumps, spriteRendererLogs;
    readonly HashSet<string> dumped = new HashSet<string>();
    void LogSprites() {
        if (seenSprites.Count > 1500) return;
        foreach (Image image in UnityEngine.Object.FindObjectsOfType<Image>()) {
            if (image.sprite == null || image.sprite.texture == null) continue;
            string key = image.sprite.texture.name + " / " + image.sprite.name;
            string objectName = image.gameObject.name;
            if (objectName == "Icon" || objectName == "ItemIcon" || objectName == "Background" || objectName == "Fill") continue;
            if (seenSprites.Add(key)) {
                Logger.LogInfo("[PTBR sprite] " + key + " @ " + image.gameObject.name);
                string tex = image.sprite.texture.name;
                if (tex.StartsWith("MCS_ZQ_btn_tiaoguo") || dumpNames.Contains(tex) || (tex.StartsWith("MCS_Btn_") && tex.Contains("_zi")) || (image.gameObject.name == "Text" && image.sprite.texture.width <= 300 && !HasCjk(image.sprite.texture.name) && !File.Exists(Path.Combine(Path.Combine(pluginFolder, "Texture"), tex + ".png")))) DumpTexture(image.sprite.texture);
            }
            if (guideDumps < 60 && image.GetComponentInParent<ScrollRect>() != null && image.sprite.texture.width >= 256 && dumped.Add(image.sprite.texture.name)) {
                guideDumps++;
                DumpTexture(image.sprite.texture);
            }
        }
        if (spriteRendererLogs < 120)
            foreach (SpriteRenderer sr in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>()) {
                if (sr.sprite == null || sr.sprite.texture == null || spriteRendererLogs >= 120) continue;
                string srKey = "SR " + sr.sprite.texture.name + " / " + sr.sprite.name;
                if (srKey.IndexOf("line", StringComparison.OrdinalIgnoreCase) >= 0 || srKey.IndexOf("bigmap", StringComparison.Ordinal) >= 0 || srKey.IndexOf("DLJM", StringComparison.Ordinal) >= 0 || srKey.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (!seenSprites.Add(srKey)) continue;
                spriteRendererLogs++;
                Logger.LogInfo("[PTBR sprite] " + srKey + " @ " + PathOf(sr.transform));
                if (sr.sprite.texture.width <= 2048 && dumped.Add(sr.sprite.texture.name)) DumpTexture(sr.sprite.texture);
            }
        foreach (RawImage raw in UnityEngine.Object.FindObjectsOfType<RawImage>()) {
            if (guideDumps >= 60 || raw.texture == null || raw.texture.width < 256 || raw.GetComponentInParent<ScrollRect>() == null) continue;
            if (dumped.Add(raw.texture.name)) { guideDumps++; Logger.LogInfo("[PTBR rawimage] " + raw.texture.name + " @ " + PathOf(raw.transform)); DumpTexture(raw.texture); }
        }
    }
    readonly HashSet<string> seen = new HashSet<string>();
    // Textures: Texture\<nome>.png substitui a Texture2D de mesmo nome (como o XUnity faz).
    readonly Dictionary<string, string> textureFiles = new Dictionary<string, string>();
    const float textureMark = 0.000123f;
    // Nomes com PNG traduzido: as legendas sobrepostas antigas ficam desativadas para eles.
    static readonly HashSet<string> replacedTextures = new HashSet<string>();
    float nextTexture;
    int texturesReplaced;
    void LoadTextureList(string folder) {
        string dir = Path.Combine(folder, "Texture");
        if (!Directory.Exists(dir)) return;
        foreach (string file in Directory.GetFiles(dir, "*.png")) {
            string name = Path.GetFileNameWithoutExtension(file);
            int bracket = name.IndexOf(" [");
            if (bracket > 0) name = name.Substring(0, bracket);
            textureFiles[name] = file;
            replacedTextures.Add(name);
        }
        Logger.LogInfo("[PTBR] Texturas traduzidas disponíveis: " + textureFiles.Count);
    }
    void ReplaceTextures() {
        if (textureFiles.Count == 0) return;
        foreach (Texture2D texture in Resources.FindObjectsOfTypeAll<Texture2D>()) {
            if (texture == null) continue;
            string file;
            if (!textureFiles.TryGetValue(texture.name, out file)) continue;
            // After a battle the game unloads and reloads its atlases (same name, sometimes the
            // same id) with the Chinese pixels: a replaced texture carries a tiny mip bias mark.
            if (Math.Abs(texture.mipMapBias - textureMark) < 0.0000001f) continue;
            try {
                int w = texture.width, h = texture.height;
                string name = texture.name;
                if (ImageConversion.LoadImage(texture, File.ReadAllBytes(file), false)) {
                    texture.name = name;
                    texture.mipMapBias = textureMark;
                    texturesReplaced++;
                    if (texture.width != w || texture.height != h)
                        Logger.LogWarning("[PTBR textura] tamanho diferente: " + name + " " + w + "x" + h + " -> " + texture.width + "x" + texture.height);
                    else Logger.LogInfo("[PTBR textura] " + name);
                }
            } catch (Exception error) { Logger.LogWarning("[PTBR textura] " + texture.name + ": " + error.Message); }
            try { texture.mipMapBias = textureMark; } catch { }
        }
    }
    static bool Retextured(Sprite sprite) {
        return sprite != null && sprite.texture != null && replacedTextures.Contains(sprite.texture.name);
    }
    // Text controlled by a layout (tooltips, lists) must keep its own sizing,
    // otherwise forcing best-fit/truncate makes boxes overlap.
    static bool LayoutDriven(Component label) {
        if (label.GetComponent<ContentSizeFitter>() != null || label.GetComponent<LayoutElement>() != null) return true;
        // Guide/glossary pages (scroll views, HyText markup) keep the game's font size.
        if (label.GetComponentInParent<ScrollRect>() != null) return true;
        Text markupText = label as Text;
        if (markupText != null && markupText.text != null && (markupText.text.IndexOf("#c") >= 0 || markupText.text.IndexOf("<pos") >= 0 || markupText.text.IndexOf("<sprite") >= 0 || markupText.text.IndexOf("#s") >= 0)) return true;
        Transform parent = label.transform.parent;
        if (parent == null) return false;
        if (parent.GetComponent<ContentSizeFitter>() != null) return true;
        LayoutGroup group = parent.GetComponent<LayoutGroup>();
        return group != null && (group.GetType() == typeof(VerticalLayoutGroup) || group.GetType() == typeof(HorizontalLayoutGroup));
    }
    Font latin;
    float next;
    int changed;
    bool creating;
    readonly List<KeyValuePair<string, string>> creationPhrases = new List<KeyValuePair<string, string>>();
    string CreationCaption(UnityEngine.Events.UnityEvent events) {
        if (events == null) return null;
        for (int i = 0; i < events.GetPersistentEventCount(); i++) {
            UnityEngine.Object target = events.GetPersistentTarget(i);
            if (!(target is MainUISetName) && !(target is MainUISetFace) && !(target is MainUISelectTianFu) && !(target is MainUICreateAvatar)) continue;
            switch(events.GetPersistentMethodName(i)) {
                case "NextMethod": case "NextPage": return "Próximo";
                case "ReturnMethod": case "LastPage": case "Close": return "Voltar";
                case "CreateFinsh": case "creatAvatar": return "Começar";
            }
        }
        return null;
    }
    void CaptionControl(Component control, string caption) {
        if (caption == null || control.GetComponent<PTBRGraphicLabel>() != null) return;
        Image graphic = control.GetComponent<Image>();
        if (graphic == null) graphic = control.GetComponentInChildren<Image>();
        if (graphic == null || graphic.GetComponent<PTBRGraphicLabel>() != null || Retextured(graphic.sprite)) return;
        graphic.gameObject.AddComponent<PTBRGraphicLabel>().Setup(graphic, latin, caption, true);
        Logger.LogInfo("[PTBR criação UGUI] " + control.name + " = " + caption);
    }
    static bool Compact(string value) {
        return value == "Intuição:" || value == "Sentido:" || value == "Pedras:" ||
            value == "Anos:" || value == "Raiz:" || value == "Mente:" || value == "Vel.:";
    }
    static int Limit(string value, int original) {
        if (value == "Meus mods" || value == "Oficina" || value == "Enviar mod") return Math.Min(original, 26);
        if (value == "Apagar dados") return Math.Min(original, 22);
        return original;
    }
    static int ReadableMaximum(string value, int original) {
        // A tiny original font must not remain the ceiling when Portuguese fits
        // at a larger size. Best-fit still respects the existing frame.
        int minimumCeiling = value.Length <= 28 ? 18 : 16;
        return Math.Max(Limit(value, original), minimumCeiling);
    }
    static string Key(string value) { return Regex.Replace(value ?? "", @"\s+", ""); }
    void Awake()
    {
        string folder = Path.GetDirectoryName(Info.Location);
        string path = Path.Combine(folder, "menus.tsv");
        if (!File.Exists(path)) { Logger.LogError("Dicionário ausente: " + path); enabled = false; return; }
        List<string> dictionaries = new List<string>();
        // v0.42: ajustes-*.tsv corrigem entradas que já existem (a primeira entrada carregada vence).
        string[] adjustFiles = Directory.GetFiles(folder, "ajustes-*.tsv");
        Array.Sort(adjustFiles);
        dictionaries.AddRange(adjustFiles);
        dictionaries.Add(path);
        string[] batchFiles = Directory.GetFiles(folder, "lote-*.tsv");
        Array.Sort(batchFiles, StringComparer.Ordinal);
        dictionaries.AddRange(batchFiles);
        string[] dataFiles = Directory.GetFiles(folder, "dados-*.tsv");
        Array.Sort(dataFiles);
        List<string> dataSources = new List<string>(adjustFiles);
        dataSources.AddRange(dataFiles);
        dictionaries.AddRange(dataFiles);
        foreach (string dataFile in dataSources)
        foreach (string line in File.ReadAllLines(dataFile, Encoding.UTF8)) {
            string[] parts = line.Split('\t');
            if (parts.Length != 2 || parts[0].Length == 0) continue;
            string dataKey = Key(FromFile(parts[0])), dataValue = FromFile(parts[1]);
            if (!dataTranslations.ContainsKey(dataKey)) dataTranslations[dataKey] = dataValue;
            // v0.43: o pinyin das partes de nomes vem só dos dados-*.tsv (ajustes como 一 -> "Um" não viram sobrenome).
            if (Array.IndexOf(adjustFiles, dataFile) < 0 && dataKey.Length <= 2 && HasCjk(dataKey) && pinyinValue.IsMatch(dataValue) && !namePinyin.ContainsKey(dataKey))
                namePinyin[dataKey] = dataValue;
        }
        foreach (string dictionary in dictionaries)
        foreach (string line in File.ReadAllLines(dictionary, Encoding.UTF8)) {
            string[] parts = line.Split('\t');
            if (parts.Length == 2 && parts[0].Length > 0 && !translations.ContainsKey(Key(FromFile(parts[0]))))
            {
                translations[Key(FromFile(parts[0]))] = FromFile(parts[1]);
                AddTemplate(Key(FromFile(parts[0])), FromFile(parts[1]));
            }
        }
        LoadPTBRSeaNames(folder);
        string pinyinPath = Path.Combine(folder, "pinyin.tsv");
        if (File.Exists(pinyinPath))
            foreach (string pl in File.ReadAllLines(pinyinPath, Encoding.UTF8)) {
                string[] pp = pl.Split('\t');
                if (pp.Length == 2 && pp[0].Length == 1 && pp[1].Length > 0) charPinyin[pp[0]] = pp[1];
            }
        latin = Font.CreateDynamicFontFromOSFont("Arial", 24);
        LoadTextureList(folder);
        pluginFolder = folder;
        string dumpList = Path.Combine(folder, "dump.txt");
        if (File.Exists(dumpList)) foreach (string line in File.ReadAllLines(dumpList)) if (line.Trim().Length > 0) dumpNames.Add(line.Trim());
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += delegate { nextTexture = 0f; if (!dataStable) { dataPending = true; nextData = Time.unscaledTime + 1f; } };
        Logger.LogInfo("[PTBR] Dicionário de dados do jogo: " + dataTranslations.Count + " entradas.");
        foreach (KeyValuePair<string,string> pair in translations)
            if (pair.Key.Length >= 4 && Regex.IsMatch(pair.Key, @"[\u4e00-\u9fff]")) creationPhrases.Add(pair);
        creationPhrases.Sort(delegate(KeyValuePair<string,string> a, KeyValuePair<string,string> b) { return b.Key.Length.CompareTo(a.Key.Length); });
        int templateCount = 0;
        foreach (List<Template> list in templates.Values) templateCount += list.Count;
        Logger.LogInfo("PT-BR ativo: " + translations.Count + " entradas, " + templateCount + " modelos com números. Tradução local, sem serviços externos.");
        instance = this;
        try { ApplyHooks(); } catch (Exception error) { Logger.LogWarning("[PTBR] Ganchos não aplicados: " + error.Message); }
    }
    // Pinyin of name parts (surnames and given names) from the data dictionary.
    readonly Dictionary<string, string> namePinyin = new Dictionary<string, string>();
    static readonly Regex pinyinValue = new Regex(@"^[A-Z][a-zü]+( [A-Z][a-zü]+)? ?$");
    string NameRun(string run) {
        if (run.Length < 2 || run.Length > 4) return null;
        for (int cut = 1; cut <= 2 && cut < run.Length; cut++) {
            string family, given;
            if (!namePinyin.TryGetValue(run.Substring(0, cut), out family) || !namePinyin.TryGetValue(run.Substring(cut), out given)) continue;
            given = given.Trim();
            string compact = given.Replace(" ", "").ToLowerInvariant();
            if (compact.Length == 0) continue;
            string fam = family.Replace(" ", "").Trim().ToLowerInvariant();
            if (fam.Length == 0) continue;
            return Char.ToUpperInvariant(fam[0]) + fam.Substring(1) + " " + Char.ToUpperInvariant(compact[0]) + compact.Substring(1);
        }
        return PinyinName(run);
    }
    readonly Dictionary<string, string> charPinyin = new Dictionary<string, string>();
    const string surnameChars = "赵钱孙李周吴郑王冯陈褚卫蒋沈韩杨朱秦尤许何吕施张孔曹严华魏陶姜戚谢邹喻柏窦章苏潘葛奚范彭郎鲁韦昌马苗凤方俞任袁柳鲍史唐费廉岑薛雷贺倪汤滕殷罗毕郝邬于傅皮卞齐康伍余元卜顾孟黄穆萧尹姚邵湛汪祁毛禹狄米贝臧计伏戴谈宋茅庞熊纪舒屈项祝董梁杜阮蓝闵席季麻强贾路娄危江童颜郭梅盛林刁钟徐邱骆高夏蔡田樊胡凌霍虞柯管卢莫房缪解应宗丁宣邓单杭洪包诸左崔吉龚程邢裴陆荣翁荀羊惠甄曲封储靳段富巫乌焦巴弓牧谷车侯蓬全班仰秋仲伊宫宁仇栾甘厉戎祖武符刘景詹叶幸司黎薄印宿蒲从索赖卓蔺屠蒙池乔胥能苍闻党翟谭劳姬申扶堵冉宰雍桑桂牛寿通边燕冀尚农温庄晏柴瞿阎充慕连茹习艾鱼容向易慎戈廖庾衡耿满匡寇广禄阙欧利蔚隆聂晁勾敖冷辛简饶曾沙关查荆游权盖桓公付夙姒宵嵇怀恒扁扈浦琚羽羿肖芮莘萼藤裘贡辰郁郜鞠";
    static readonly string[] compoundSurnames = { "欧阳","司马","上官","诸葛","公孙","慕容","澹台","夏侯","东方","皇甫","尉迟","令狐","长孙","宇文","司徒","轩辕","南宫","百里","独孤","端木","公羊","闻人","太史","第五","东郭","西门","南门","钟离","呼延","拓跋" };
    // Toneless pinyin for a 3-4 character personal name (surname + given name).
    string PinyinName(string run) {
        if (charPinyin.Count == 0 || run.Length < 3 || run.Length > 4) return null;
        int surnameLength = 0;
        for (int i = 0; i < compoundSurnames.Length; i++) if (run.StartsWith(compoundSurnames[i], StringComparison.Ordinal)) surnameLength = 2;
        if (surnameLength == 0 && run.Length == 3 && surnameChars.IndexOf(run[0]) >= 0) surnameLength = 1;
        if (surnameLength == 0) return null;
        StringBuilder family = new StringBuilder(), given = new StringBuilder();
        for (int i = 0; i < run.Length; i++) {
            string py;
            if (!charPinyin.TryGetValue(run.Substring(i, 1), out py)) return null;
            StringBuilder target = i < surnameLength ? family : given;
            target.Append(target.Length == 0 ? Char.ToUpperInvariant(py[0]) + py.Substring(1) : py);
        }
        return family.ToString() + " " + given.ToString();
    }
    // Text assembled by the game from several pieces (labels + values, markup such as
    // #c449491名称：#n): every Chinese run must have its own translation.
    static readonly Regex cjkRun = new Regex(@"[\u4e00-\u9fff]+");
    string ComposeRuns(string source) {
        if (source.Length > 3000) return null;
        string text = Regex.Replace(source, @"第(\d+)层", "Camada $1");
        bool failed = false;
        string result = cjkRun.Replace(text, delegate(Match m) {
            if (failed) return m.Value;
            string t;
            if (!translations.TryGetValue(m.Value, out t)) t = NameRun(m.Value);
            if (t == null) { failed = true; return m.Value; }
            int a = m.Index, b = m.Index + m.Length;
            int k = a - 1;
            while (k >= 0 && Char.IsLetterOrDigit(text[k])) k--;
            bool before = a > 0 && Char.IsLetterOrDigit(text[a - 1]) && !(k >= 0 && text[k] == '#');
            bool after = b < text.Length && Char.IsLetter(text[b]);
            return (before ? " " : "") + t + (after ? " " : "");
        });
        if (failed || result == source) return null;
        // v0.42: vírgulas/pontos chineses que sobram entre trechos já traduzidos viram pontuação latina.
        if (result.IndexOf('，') >= 0 || result.IndexOf('。') >= 0)
            result = Regex.Replace(result.Replace("，", ", ").Replace("。", ". "), @" {2,}", " ").TrimEnd();
        return result;
    }

    // Auction house and other lines the game assembles by code.
    static readonly string[] chineseNumbers = { "零","一","二","三","四","五","六","七","八","九","十" };
    static string ChineseNumber(string n) {
        for (int i = 0; i < chineseNumbers.Length; i++) if (chineseNumbers[i] == n) return i.ToString();
        if (n.Length == 2 && n[0] == '十') return "1" + ChineseNumber(n.Substring(1));
        return n;
    }
    static readonly Regex combatCast = new Regex(@"(<color=#[0-9a-fA-F]+>[^<]*</color>)?对(<color=#[0-9a-fA-F]+>)([^<]+)(</color>)释放了(<color=#[0-9a-fA-F]+>[^<]*</color>)[,，]造成(<color=#[0-9a-fA-F]+>\d+</color>)点伤害");
    static readonly Regex effGain = new Regex(@"获得【([^】]+)】[xX×*](\d+)");
    static readonly Regex effSelf = new Regex(@"对自己造成(\d+)点真实伤害");
    string EffectLines(string source) {
        string t = effGain.Replace(source, delegate(Match m) { return "Obtém 【" + Part(m.Groups[1].Value) + "】x" + m.Groups[2].Value; });
        t = effSelf.Replace(t, "Causa $1 de dano real a si mesmo");
        t = t.Replace("回合开始，", "No início do turno, ").Replace("回合结束，", "No fim do turno, ");
        return t;
    }
    string Part(string value) {
        if (!HasCjk(value)) return value;
        string t = Translate(value);
        if (t == null) t = NameRun(value);
        return t ?? value;
    }
    // Dialogue where the game already replaced {daoyou}/{qianbei}/{xiaoyou} (and sometimes
    // {FirstName} or an item name): rebuild the template key and fill the Portuguese text.
    static readonly string[] vocWords = { "道友", "前辈", "小友" };
    static readonly string[] vocHolders = { "{daoyou}", "{qianbei}", "{xiaoyou}" };
    static readonly string[] vocPortuguese = { "companheiro", "sênior", "pequeno amigo" };
    static readonly Regex latinRun = new Regex(@"[A-Za-z\u00C0-\u00FF][A-Za-z\u00C0-\u00FF'\- ]{2,}[A-Za-z\u00C0-\u00FF]");
    string Vocative(string source) {
        bool has = false;
        for (int i = 0; i < vocWords.Length; i++) if (source.IndexOf(vocWords[i], StringComparison.Ordinal) >= 0) has = true;
        if (!has) return null;
        string itemText = null;
        List<string> bases = new List<string>();
        bases.Add(source);
        MatchCollection runs = latinRun.Matches(source);
        if (runs.Count == 1) { itemText = runs[0].Value; bases.Add(source.Replace(itemText, "{item}")); }
        foreach (string basis in bases) {
            for (int w = 0; w < vocWords.Length; w++) {
                if (basis.IndexOf(vocWords[w], StringComparison.Ordinal) < 0) continue;
                for (int h = 0; h < vocHolders.Length; h++) {
                    string variant = basis.Replace(vocWords[w], vocHolders[h]);
                    int firstAt = variant.IndexOf(vocHolders[h], StringComparison.Ordinal);
                    string[] candidates = { variant, variant.Substring(0, firstAt) + "{FirstName}" + variant.Substring(firstAt) };
                    foreach (string candidate in candidates) {
                        string hit;
                        if (!translations.TryGetValue(Key(candidate), out hit)) continue;
                        string filled = hit.Replace("{FirstName}", "");
                        for (int k = 0; k < vocHolders.Length; k++) {
                            filled = filled.Replace(" de " + vocHolders[k], " do " + vocPortuguese[w]);
                            filled = filled.Replace(vocHolders[k], vocPortuguese[w]);
                        }
                        if (itemText != null) filled = filled.Replace("Este {item}", "O item {item}").Replace("Esta {item}", "O item {item}").Replace("{item}", itemText);
                        filled = Regex.Replace(filled, @"\s+([,.!?;:])", "$1");
                        filled = Regex.Replace(filled, @" {2,}", " ").Trim();
                        return filled.Length > 0 ? Char.ToUpperInvariant(filled[0]) + filled.Substring(1) : filled;
                    }
                }
            }
        }
        return null;
    }
    // ---- Templates with game placeholders ({FirstName}, {shidi}, {$name}...) matched against text
    // that the game already filled in (NPC greetings, letters, tooltips).
    class PhTemplate {
        public string key, pt, probe;
        public string[] names;
        public string pattern;
        public Regex rx;
        public int literal;
    }
    List<PhTemplate> phTemplates;
    readonly Dictionary<string, string> phCache = new Dictionary<string, string>();
    static readonly Regex phToken = new Regex(@"\{(/?)(\$?[A-Za-z][A-Za-z0-9_]*)(=[^}]*)?\}");
    static readonly string[] phVocatives = { "道友", "前辈", "小友", "师弟", "师妹", "师兄", "师姐", "兄弟", "姑娘", "公子", "小姐", "丫头", "小子", "师傅", "仙子", "少侠", "女侠", "贤弟", "师叔", "师祖", "师父", "长老" };
    static readonly string[,] phExtraWords = { {"小姐","senhorita"}, {"仙子","fada"}, {"少侠","jovem herói"}, {"女侠","heroína"}, {"贤弟","irmão"}, {"师叔","tio-mestre"}, {"师祖","mestre ancestral"}, {"师父","mestre"}, {"长老","ancião"} };
    static bool PhPronoun(string name) {
        string lower = name.ToLowerInvariant();
        return lower.Contains("daoyou") || lower.Contains("xiaoyou") || lower.Contains("qianbei") || lower.Contains("shidi") || lower.Contains("xiongdi")
            || lower.Contains("xiaozi") || lower.Contains("shixiong") || lower.Contains("gongzi");
    }
    void BuildPhTemplates() {
        phTemplates = new List<PhTemplate>();
        foreach (KeyValuePair<string, string> pair in translations) {
            string key = pair.Key;
            if (key.IndexOf('{') < 0 || key.Length < 6 || key.Length > 400 || !HasCjk(key)) continue;
            List<string> names = new List<string>();
            StringBuilder pattern = new StringBuilder("^");
            string probe = "";
            int literal = 0, cursor = 0;
            bool broken = false;
            foreach (Match m in phToken.Matches(key)) {
                string piece = key.Substring(cursor, m.Index - cursor);
                cursor = m.Index + m.Length;
                if (piece.Length > 0) { pattern.Append(Regex.Escape(piece)); literal += piece.Length; if (piece.Length > probe.Length && HasCjk(piece)) probe = piece; }
                string name = m.Groups[2].Value;
                if (m.Groups[1].Value == "/") { if (name == "color") pattern.Append("</color>"); continue; }
                if (name == "color") { pattern.Append("<color=" + Regex.Escape(m.Groups[3].Value.TrimStart('=')) + ">"); continue; }
                if (name == "w" || name == "huanhang" || name == "wc" || name == "c" || name == "nl") continue;
                names.Add(name);
                if (PhPronoun(name)) pattern.Append("(" + String.Join("|", phVocatives) + ")");
                else pattern.Append("(.{0,24}?)");
            }
            string tail = key.Substring(cursor);
            if (tail.Length > 0) { pattern.Append(Regex.Escape(tail)); literal += tail.Length; if (tail.Length > probe.Length && HasCjk(tail)) probe = tail; }
            pattern.Append("$");
            if (broken || names.Count == 0 || probe.Length < 2 || literal < 4) continue;
            PhTemplate t = new PhTemplate();
            t.key = key; t.pt = pair.Value; t.probe = probe; t.names = names.ToArray(); t.pattern = pattern.ToString(); t.literal = literal;
            phTemplates.Add(t);
        }
        phTemplates.Sort(delegate(PhTemplate a, PhTemplate b) { return b.literal.CompareTo(a.literal); });
        Logger.LogInfo("[PTBR] Modelos com marcadores: " + phTemplates.Count);
    }
    string PhWord(string zh) {
        for (int i = 0; i < genderWords.GetLength(0); i++) if (genderWords[i, 0] == zh) return genderWords[i, 1];
        for (int i = 0; i < phExtraWords.GetLength(0); i++) if (phExtraWords[i, 0] == zh) return phExtraWords[i, 1];
        return zh;
    }
    string PlaceholderTemplate(string source) {
        if (source.Length < 6 || source.Length > 600 || !HasCjk(source)) return null;
        string cached;
        if (phCache.TryGetValue(source, out cached)) return cached;
        if (phTemplates == null) BuildPhTemplates();
        StringBuilder compactBuilder = new StringBuilder();
        List<int> map = new List<int>();
        for (int i = 0; i < source.Length; i++) if (!Char.IsWhiteSpace(source[i])) { compactBuilder.Append(source[i]); map.Add(i); }
        string compact = compactBuilder.ToString();
        string found = null;
        foreach (PhTemplate t in phTemplates) {
            if (compact.IndexOf(t.probe, StringComparison.Ordinal) < 0) continue;
            if (t.rx == null) {
                try { t.rx = new Regex(t.pattern, RegexOptions.Singleline); } catch (Exception) { t.probe = "\u0000\u0000"; continue; }
            }
            Match m = t.rx.Match(compact);
            if (!m.Success) continue;
            string filled = t.pt;
            bool ok = true;
            for (int g = 0; g < t.names.Length; g++) {
                Group grp = m.Groups[g + 1];
                string value = "";
                if (grp.Length > 0) {
                    int startAt = map[grp.Index], endAt = map[grp.Index + grp.Length - 1] + 1;
                    value = source.Substring(startAt, endAt - startAt);
                }
                if (PhPronoun(t.names[g])) value = PhWord(value);
                else if (value.Length > 0 && HasCjk(value)) value = Part(value);
                int at = filled.IndexOf("{" + t.names[g] + "}", StringComparison.Ordinal);
                if (at < 0) continue;
                filled = filled.Substring(0, at) + value + filled.Substring(at + t.names[g].Length + 2);
            }
            filled = Regex.Replace(filled, @"\{w(?:=[0-9.]+)?\}", "");
            filled = filled.Replace("{huanhang}", "\n");
            filled = Regex.Replace(filled, @"\{color=([^}]*)\}", "<color=$1>").Replace("{/color}", "</color>");
            if (Regex.IsMatch(filled, @"\{\$?[A-Za-z]")) ok = false;
            if (!ok) continue;
            filled = Regex.Replace(filled, @"\s+([,.!?;:])", "$1");
            filled = Regex.Replace(filled, @" {2,}", " ").Trim();
            if (filled.Length > 0) filled = Char.ToUpperInvariant(filled[0]) + filled.Substring(1);
            found = filled;
            break;
        }
        if (phCache.Count > 6000) phCache.Clear();
        phCache[source] = found;
        return found;
    }
    // Weapon/armor names the game assembles from random syllables (金陌剑 = Jin + Mo + sword).
    static readonly string[,] gearKinds = {
        {"剑","Espada"},{"钟","Sino"},{"戒","Anel"},{"环","Anel"},{"针","Agulha"},{"盒","Caixa"},{"袍","Manto"},{"甲","Armadura"},
        {"铠","Armadura"},{"珠","Pérola"},{"令","Ordem"},{"印","Selo"},{"刀","Lâmina"},{"枪","Lança"},{"衣","Veste"} };
    static readonly string[,] gearSyllables = {
        {"凌风","Lingfeng"},{"火焰","Huoyan"},{"瞬","Shun"},{"烈","Lie"},{"金","Jin"},{"晶","Jing"},{"鑫","Xin"},{"锐","Rui"},{"木","Mu"},{"竹","Zhu"},
        {"翠","Cui"},{"碧","Bi"},{"柳","Liu"},{"荆","Jing"},{"水","Shui"},{"流","Liu"},{"浪","Lang"},{"霖","Lin"},{"雨","Yu"},{"湖","Hu"},
        {"火","Huo"},{"焰","Yan"},{"炎","Yan"},{"焱","Yan"},{"灼","Zhuo"},{"焚","Fen"},{"土","Tu"},{"尘","Chen"},{"岩","Yan"},{"障","Zhang"},
        {"砺","Li"},{"裂","Lie"},{"天","Tian"},{"幻","Huan"},{"秘","Mi"},{"惊","Jing"},{"灵","Ling"},{"锋","Feng"},{"陌","Mo"},{"罡","Gang"},
        {"晓","Xiao"},{"阙","Que"},{"宣","Xuan"},{"鼎","Ding"},{"纹","Wen"},{"丝","Si"},{"月","Yue"},{"深","Shen"},{"铃","Ling"},{"影","Ying"},
        {"飞","Fei"},{"钉","Ding"},{"镖","Biao"},{"悬","Xuan"},{"宝","Bao"},{"玉","Yu"},{"镜","Jing"},{"道","Dao"},{"灿","Can"},{"锦","Jin"},
        {"战","Zhan"},{"重","Zhong"},{"玄","Xuan"},{"铠","Kai"},{"奇","Qi"},{"邀","Yao"} };
    string GearName(string source) {
        if (source.Length < 3 || source.Length > 7) return null;
        string kind = null;
        string last = source.Substring(source.Length - 1);
        for (int i = 0; i < gearKinds.GetLength(0); i++) if (gearKinds[i, 0] == last) kind = gearKinds[i, 1];
        if (kind == null) return null;
        string rest = source.Substring(0, source.Length - 1);
        StringBuilder name = new StringBuilder();
        int pos = 0;
        while (pos < rest.Length) {
            string syllable = null;
            int used = 0;
            for (int i = 0; i < gearSyllables.GetLength(0); i++) {
                string zh = gearSyllables[i, 0];
                if (String.CompareOrdinal(rest, pos, zh, 0, zh.Length) == 0) { syllable = gearSyllables[i, 1]; used = zh.Length; break; }
            }
            if (syllable == null) return null;
            name.Append(name.Length == 0 ? syllable : syllable.ToLowerInvariant());
            pos += used;
        }
        return kind + " " + name.ToString();
    }
    static readonly Dictionary<string,string> damageElements = new Dictionary<string,string> {
        {"金","metal"},{"木","madeira"},{"水","água"},{"火","fogo"},{"土","terra"} };
    static readonly Regex clauseDamage = new Regex(@"^造成(<color=#[0-9a-fA-F]+>)?(\d+)(</color>)?点([金木水火土])系伤害$");
    static readonly Regex clauseApply = new Regex(@"^对敌方施加(<color=#[0-9a-fA-F]+>)?【([^】]+)】(</color>)?[*xX×](\d+)$");
    static readonly Regex auctionSale = new Regex(@"(<color=#[0-9a-fA-F]+>)?([^<\n以]{1,12}?)(</color>)?以(<color=#[0-9a-fA-F]+>)?(\d+)(</color>)?灵石的价格购买了(<color=#[0-9a-fA-F]+>)?([^<\n]+)(</color>)?");
    // Last resort for a personal name with no known surname: pinyin syllable by syllable.
    string NameGuess(string run) {
        if (charPinyin.Count == 0 || run.Length < 2 || run.Length > 5) return null;
        string known = PinyinName(run);
        if (known != null) return known;
        StringBuilder guessed = new StringBuilder();
        for (int i = 0; i < run.Length; i++) {
            string py;
            if (!charPinyin.TryGetValue(run.Substring(i, 1), out py)) return null;
            if (i > 0) guessed.Append(' ');
            guessed.Append(Char.ToUpperInvariant(py[0]) + py.Substring(1));
        }
        return guessed.ToString();
    }
    string EffectClauses(string source) {
        int at = source.IndexOf("造成", StringComparison.Ordinal);
        if (at < 0) at = source.IndexOf("对敌方施加", StringComparison.Ordinal);
        if (at < 0) return null;
        string prefix = source.Substring(0, at);
        if (prefix.Length > 0 && HasCjk(prefix)) { prefix = Translate(prefix); if (prefix == null) return null; }
        string[] clauses = source.Substring(at).Split(new char[] { '，', ',' });
        List<string> done = new List<string>();
        foreach (string clause in clauses) {
            Match d = clauseDamage.Match(clause.Trim());
            if (d.Success) { done.Add("causa " + d.Groups[1].Value + d.Groups[2].Value + d.Groups[3].Value + " de dano de " + damageElements[d.Groups[4].Value]); continue; }
            Match a = clauseApply.Match(clause.Trim());
            if (a.Success) {
                string effectName = Part(a.Groups[2].Value);
                if (HasCjk(effectName)) return null;
                done.Add("aplica " + a.Groups[1].Value + "【" + effectName + "】" + a.Groups[3].Value + "*" + a.Groups[4].Value + " ao inimigo");
                continue;
            }
            return null;
        }
        string joined = String.Join(", ", done.ToArray());
        joined = Char.ToUpperInvariant(joined[0]) + joined.Substring(1);
        return prefix + joined;
    }
    string Special(string source) {
        if (source.IndexOf("灵光一现", StringComparison.Ordinal) >= 0) {
            Match flash = Regex.Match(source, @"^在(\d+)年(\d+)月(\d+)日你与(.+?)论道时[，,]对([\u4e00-\u9fff])之道产生了灵光一现的感悟$");
            if (flash.Success) {
                string flashDao = DaoName(flash.Groups[5].Value + "之道");
                if (!HasCjk(flashDao)) return "No Ano " + flash.Groups[1].Value + ", " + flash.Groups[3].Value + "/" + flash.Groups[2].Value + ", ao debater o Dao com " + Part(flash.Groups[4].Value) + ", você teve um lampejo de compreensão do " + flashDao + ".";
            }
        }
        if (source.IndexOf("造成", StringComparison.Ordinal) >= 0 || source.IndexOf("对敌方施加", StringComparison.Ordinal) >= 0) {
            string effectClauses = EffectClauses(source);
            if (effectClauses != null) return effectClauses;
        }
        if (source.IndexOf('月') >= 0 && source.Length < 12) {
            Match monthsLeft = Regex.Match(Key(source), @"^剩(\d+)月$");
            if (monthsLeft.Success) return monthsLeft.Groups[1].Value == "1" ? "Resta 1 mês" : "Restam " + monthsLeft.Groups[1].Value + " meses";
        }
        if (source.Length < 12 && source.StartsWith("剩", StringComparison.Ordinal)) {
            Match daysLeft = Regex.Match(Key(source), @"^剩(\d+)[日天]$");
            if (daysLeft.Success) return daysLeft.Groups[1].Value == "1" ? "Resta 1 dia" : "Restam " + daysLeft.Groups[1].Value + " dias";
            Match yearsLeft = Regex.Match(Key(source), @"^剩(\d+)年$");
            if (yearsLeft.Success) return yearsLeft.Groups[1].Value == "1" ? "Resta 1 ano" : "Restam " + yearsLeft.Groups[1].Value + " anos";
        }
        if (source.IndexOf("感悟", StringComparison.Ordinal) >= 0 && source.Length < 30) {
            Match daoSubject = Regex.Match(Key(source), @"^对([\u4e00-\u9fff])之道的感悟$");
            if (daoSubject.Success) {
                string subjectDao = DaoName(daoSubject.Groups[1].Value + "之道");
                if (!HasCjk(subjectDao)) return "Compreensão do " + subjectDao;
            }
            Match daoLacking = Regex.Match(Key(source), @"^([\u4e00-\u9fff])之道感悟不足$");
            if (daoLacking.Success) {
                string lackingDao = DaoName(daoLacking.Groups[1].Value + "之道");
                if (!HasCjk(lackingDao)) return "Compreensão do " + lackingDao + " insuficiente";
            }
        }
        if (source.EndsWith("任务已开启", StringComparison.Ordinal)) {
            Match questStart = Regex.Match(source, @"^(<color=#[0-9a-fA-F]+>)?(.+?)(</color>)?任务已开启$");
            if (questStart.Success) {
                string questName = Part(questStart.Groups[2].Value);
                if (!HasCjk(questName)) return "Missão: " + questStart.Groups[1].Value + questName + questStart.Groups[3].Value;
            }
        }
        if (source.IndexOf("灵石的价格购买了", StringComparison.Ordinal) >= 0) {
            bool saleFailed = false;
            string sales = auctionSale.Replace(source, delegate(Match m) {
                if (saleFailed) return m.Value;
                string buyer = Part(m.Groups[2].Value);
                if (HasCjk(buyer)) buyer = NameGuess(m.Groups[2].Value);
                string saleItem = HasCjk(m.Groups[8].Value) ? Part(m.Groups[8].Value) : m.Groups[8].Value;
                if (buyer == null || HasCjk(saleItem)) { saleFailed = true; return m.Value; }
                return m.Groups[1].Value + buyer + m.Groups[3].Value + " comprou " + m.Groups[7].Value + saleItem + m.Groups[9].Value + " por " +
                    m.Groups[4].Value + m.Groups[5].Value + m.Groups[6].Value + " pedras espirituais";
            });
            if (!saleFailed && !HasCjk(sales)) return sales;
        }
        Match coloredHit = Regex.Match(source, @"^造成(<color=#[0-9a-fA-F]+>)?(\d+)(</color>)?点伤害$");
        if (coloredHit.Success) return "Causa " + coloredHit.Groups[1].Value + coloredHit.Groups[2].Value + coloredHit.Groups[3].Value + " de dano";
        if (source.IndexOf("感悟提升", StringComparison.Ordinal) >= 0) {
            Match insightUp = Regex.Match(source, @"^对(.+?)的感悟提升(.*)$", RegexOptions.Singleline);
            if (insightUp.Success) {
                string upDao = insightUp.Groups[1].Value;
                if (HasCjk(upDao)) upDao = DaoName(upDao);
                if (HasCjk(upDao)) upDao = Part(upDao);
                string upTail = insightUp.Groups[2].Value;
                if (!HasCjk(upDao) && (upTail.Length == 0 || upTail.EndsWith("点") || upTail.EndsWith("经验"))) {
                    string upAmount = upTail.EndsWith("点") ? upTail.Substring(0, upTail.Length - 1) + " pontos" : upTail.EndsWith("经验") ? upTail.Substring(0, upTail.Length - 2) + " de experiência" : "";
                    return "Aumenta a compreensão do " + upDao + (upAmount.Length > 0 ? " em " + upAmount : "");
                }
            }
        }
        if (source.IndexOf('品') >= 0) {
            Match gradedPill = Regex.Match(source, @"^服用了(\d+)颗([一二三四五六七八九])品(.+?)，(修为有所提升|延长了自身寿元)。?$");
            if (gradedPill.Success) {
                string pillName = Part(gradedPill.Groups[3].Value);
                if (!HasCjk(pillName))
                    return "Tomou " + gradedPill.Groups[1].Value + " " + pillName + " (grau " + ChineseNumber(gradedPill.Groups[2].Value) + "); " +
                        (gradedPill.Groups[4].Value == "延长了自身寿元" ? "prolongou a própria longevidade." : "o cultivo melhorou.");
            }
            Match gradedItem = Regex.Match(Key(source), @"^([一二三四五六七八九])品([一-鿿]{2,8})$");
            if (gradedItem.Success) {
                string gradedName = Part(gradedItem.Groups[2].Value);
                if (!HasCjk(gradedName)) return gradedName + " (grau " + ChineseNumber(gradedItem.Groups[1].Value) + ")";
            }
        }
        if (source.IndexOf("下一届拍卖会", StringComparison.Ordinal) >= 0) {
            Match nextAuction = Regex.Match(Key(source), @"^下一届拍卖会时间[：:](\d+)年(\d+)月(\d+)日至(\d+)年(\d+)月(\d+)日$");
            if (nextAuction.Success)
                return "Próximo leilão: Ano " + nextAuction.Groups[1].Value + ", " + nextAuction.Groups[3].Value + "/" + nextAuction.Groups[2].Value +
                    " a Ano " + nextAuction.Groups[4].Value + ", " + nextAuction.Groups[6].Value + "/" + nextAuction.Groups[5].Value;
        }
        if (source.IndexOf("对自己造成") >= 0) {
            Match selfHit = Regex.Match(source, @"^对自己造成(<color=#[0-9a-fA-F]+>)?[xX×]?(\d+)(</color>)?点真实伤害(?:[,，]灵力(<color=#[0-9a-fA-F]+>)?\+?(\d+)(</color>)?)?$");
            if (selfHit.Success) {
                string hit = "Causa " + selfHit.Groups[1].Value + "x" + selfHit.Groups[2].Value + selfHit.Groups[3].Value + " de dano real a si mesmo";
                if (selfHit.Groups[5].Success) hit += "; poder espiritual " + selfHit.Groups[4].Value + "+" + selfHit.Groups[5].Value + selfHit.Groups[6].Value;
                return hit;
            }
        }
        if (source.Length < 30 && (source.IndexOf("百万") >= 0 || source.IndexOf('万') >= 0 || source.IndexOf('亿') >= 0)) {
            string scaled = Regex.Replace(source, @"(\d+(?:\.\d+)?)(百万|万|亿)", delegate(Match m) {
                double v;
                if (!Double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return m.Value;
                string unit = m.Groups[2].Value;
                if (unit == "百万") return m.Groups[1].Value + " mi";
                if (unit == "万") return (v * 10).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " mil";
                return (v * 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " mi";
            });
            if (!HasCjk(scaled)) return scaled;
        }
        if (source.Length < 20 && source.IndexOf('第') >= 0) {
            Match whenM = Regex.Match(source, @"^第(\d+)年(\d+)月$");
            if (whenM.Success) return "Ano " + whenM.Groups[1].Value + ", mês " + whenM.Groups[2].Value;
        }
        if (source.IndexOf("坐而论道") >= 0) {
            Match talk = Regex.Match(source, @"^与(.+?)坐而论道，交流感悟，收获颇丰。$");
            if (talk.Success) return "Sentou-se para debater o Dao com " + Part(talk.Groups[1].Value) + ", trocando compreensões, e ganhou muito.";
        }
        if (source.StartsWith("不知何人将")) {
            Match forged = Regex.Match(source, @"^不知何人将(.+?)等材料炼制的(上品|中品|下品|极品)?(符器|法器|灵器|法宝)，此(.+?)铭刻(.+?)灵纹。$");
            if (forged.Success) {
                string[] mats = forged.Groups[1].Value.Split('、');
                for (int mi = 0; mi < mats.Length; mi++) mats[mi] = Part(mats[mi]);
                string matList = mats.Length > 1 ? String.Join(", ", mats, 0, mats.Length - 1) + " e " + mats[mats.Length - 1] : mats[0];
                string grade = forged.Groups[2].Value == "上品" ? "superior" : forged.Groups[2].Value == "中品" ? "médio" : forged.Groups[2].Value == "极品" ? "supremo" : "inferior";
                string kind = forged.Groups[3].Value == "符器" ? "artefato de talismã" : forged.Groups[3].Value == "法宝" ? "tesouro mágico" : "instrumento mágico";
                return "Forjado por alguém desconhecido com " + matList + ": " + kind + " " + grade + ". Traz gravado o padrão espiritual \u00ab" + Part(forged.Groups[5].Value) + "\u00bb.";
            }
        }
        if (source.StartsWith("造成伤害", StringComparison.Ordinal)) {
            Match plainHit = Regex.Match(Key(source), @"^造成伤害(\d+)点$");
            if (plainHit.Success) return "Causa " + plainHit.Groups[1].Value + " de dano";
        }
        if (source.IndexOf("情分") >= 0) {
            Match bond = Regex.Match(source, @"^你和(.+?)的情分(提升|增加|下降|降低|减少)了(.*)$", RegexOptions.Singleline);
            if (bond.Success && !HasCjk(bond.Groups[3].Value)) {
                bool grew = bond.Groups[2].Value == "提升" || bond.Groups[2].Value == "增加";
                return "Sua afinidade com " + Part(bond.Groups[1].Value) + (grew ? " aumentou" : " diminuiu") + bond.Groups[3].Value;
            }
        }
        if (source.Length >= 3 && source.Length <= 7) {
            string gear = GearName(source);
            if (gear != null) return gear;
        }
        if (source.IndexOf("悟道值满后") >= 0) {
            Match fill = Regex.Match(source, @"^悟道值满后[,，]\s*会获得悟道点[xX×]?(\d+)[,，]已获得[（(](\d+)/(\d+)[）)]$");
            if (fill.Success) return "Com a barra de Dao cheia, você ganha " + fill.Groups[1].Value + " ponto de Dao (obtidos: " + fill.Groups[2].Value + "/" + fill.Groups[3].Value + ")";
        }
        if (source.IndexOf("拍") >= 0) {
            Match lot = Regex.Match(source, @"^下面是本场拍卖会的(?:第(.*?)件拍品|(压轴拍品))，(.+?)。底价(.+?)灵石，每次加价不得少于(.+?)灵石。$", RegexOptions.Singleline);
            if (lot.Success) {
                string which = lot.Groups[2].Success && lot.Groups[2].Value.Length > 0 ? "Último item" :
                    (lot.Groups[1].Value.Length > 0 ? "Item " + ChineseNumber(lot.Groups[1].Value) : "Próximo item");
                return which + ": " + Part(lot.Groups[3].Value) + ". Lance inicial: " + lot.Groups[4].Value + " pedras; aumento mínimo: " + lot.Groups[5].Value + ".";
            }
            Match won = Regex.Match(source, @"^恭喜(.+?)(道友|前辈)?以(.+?)灵石的价格拍得这件宝物。?$", RegexOptions.Singleline);
            if (won.Success) return "Parabéns, " + Part(won.Groups[1].Value) + "! Arrematou o tesouro por " + won.Groups[3].Value + " pedras.";
        }
        Match teleport = Regex.Match(source, @"^是否花费(\d+)灵石传送至(.+?)[？?]$");
        if (teleport.Success) {
            string city = Part(teleport.Groups[2].Value);
            if (!HasCjk(city)) return "Gastar " + teleport.Groups[1].Value + " pedras espirituais para teleportar para " + city + "?";
        }
        Match daoNeed = Regex.Match(source, @"^([\u4e00-\u9fff]{1,4})之道需([\u4e00-\u9fff]{2,8})$");
        if (daoNeed.Success) {
            string needField = Part(daoNeed.Groups[1].Value), needLevel = Part(daoNeed.Groups[2].Value);
            if (!HasCjk(needField) && !HasCjk(needLevel)) return "Requer Dao de " + needField + ": " + needLevel;
        }
        Match interest = Regex.Match(source, @"^(<color=#[0-9a-fA-F]+>)?([\u4e00-\u9fff]{2,6})感兴趣的物品[：:](</color>)?([\s\S]*)$");
        if (interest.Success) {
            string interestRest = interest.Groups[4].Value;
            if (HasCjk(interestRest)) { string interestPt = Translate(interestRest); if (interestPt != null) interestRest = interestPt; }
            return interest.Groups[1].Value + "Itens de interesse de " + Part(interest.Groups[2].Value) + ":" + interest.Groups[3].Value + interestRest;
        }
        Match crafted = Regex.Match(Key(source), @"^不知何人将(.+?)等材料炼制的(.+?)[，,]此(.+?)(?:铭刻|刻铭)(.+?)灵纹[，,]灵力更加强大[，,]但对使用者也要求更高[。.]?$");
        if (crafted.Success) {
            string[] craftedMats = crafted.Groups[1].Value.Split('、');
            for (int ci = 0; ci < craftedMats.Length; ci++) craftedMats[ci] = Part(craftedMats[ci]);
            string craftedList = craftedMats.Length > 1 ? String.Join(", ", craftedMats, 0, craftedMats.Length - 1) + " e " + craftedMats[craftedMats.Length - 1] : craftedMats[0];
            return "Forjado por alguém desconhecido com " + craftedList + " e outros materiais: " + Part(crafted.Groups[2].Value) + ". Este " + Part(crafted.Groups[3].Value).ToLowerInvariant() + " tem gravado o padrão espiritual de " + Part(crafted.Groups[4].Value) + ": poder espiritual maior, mas exige mais de quem o usa.";
        }
        Match turnsLeft = Regex.Match(Key(source), @"^[（(]剩余(.+?)回合[）)]$");
        if (turnsLeft.Success) { string turnsN = ChineseNumber(turnsLeft.Groups[1].Value); return turnsN == "1" ? "(Resta 1 turno)" : "(Restam " + turnsN + " turnos)"; }
        Match insight = Regex.Match(Key(source), @"^获得新的感悟对(.+?)的思绪$");
        if (insight.Success) return "Nova compreensão: pensamento sobre o " + DaoName(insight.Groups[1].Value);
        Match range = Regex.Match(Key(source), @"^(\d+)年(\d+)月(\d+)日至(\d+)月(\d+)日$");
        if (range.Success) return "Ano " + range.Groups[1].Value + ", " + range.Groups[3].Value + "/" + range.Groups[2].Value + " a " + range.Groups[5].Value + "/" + range.Groups[4].Value;
        Match perMonth = Regex.Match(Key(source), @"^(\d+)/(月|年)$");
        if (perMonth.Success) return perMonth.Groups[1].Value + (perMonth.Groups[2].Value == "月" ? "/mês" : "/ano");
        Match gain = Regex.Match(Key(source), @"^你的(.+?)(提升|降低)了(\d+)$");
        if (gain.Success) return (gain.Groups[2].Value == "提升" ? "Aumento de " : "Redução de ") + Part(gain.Groups[1].Value).ToLowerInvariant() + ": " + (gain.Groups[2].Value == "提升" ? "+" : "-") + gain.Groups[3].Value;
        Match bid = Regex.Match(source, @"^我出价(.+?)灵石[!！]?$", RegexOptions.Singleline);
        if (bid.Success) return "Dou " + bid.Groups[1].Value + " pedras espirituais!";
        Match round = Regex.Match(Key(source), @"^第([一二三四五六七八九十]+|\d+)轮$");
        if (round.Success) return "Rodada " + ChineseNumber(round.Groups[1].Value);
        Match fee = Regex.Match(Key(source), @"^入场费(\d+)灵石，我收下啦。?$");
        if (fee.Success) return "Taxa de entrada de " + fee.Groups[1].Value + " pedras espirituais: recebida!";
        Match dissipate = Regex.Match(Key(source), @"^还需要消散(\d+)点灵气$");
        if (dissipate.Success) return "Ainda falta dissipar " + dissipate.Groups[1].Value + " de qi espiritual";
        return null;
    }
    // The game's text changed a little since the dictionary was made (a repeated or
    // missing character): accept the closest dialogue line when only 1 char in 20 differs
    // and every number is the same.
    List<string>[] fuzzyBuckets;
    readonly Dictionary<string, string> fuzzyCache = new Dictionary<string, string>();
    static string Digits(string s) { return Regex.Replace(s, @"\D", ""); }
    string Fuzzy(string key) {
        if (key == null || key.Length < 15 || key.Length > 400 || !HasCjk(key)) return null;
        string hit;
        if (fuzzyCache.TryGetValue(key, out hit)) return hit;
        if (fuzzyBuckets == null) {
            fuzzyBuckets = new List<string>[402];
            foreach (string k in translations.Keys) {
                if (k.Length < 10 || k.Length > 401 || !HasCjk(k)) continue;
                if (fuzzyBuckets[k.Length] == null) fuzzyBuckets[k.Length] = new List<string>();
                fuzzyBuckets[k.Length].Add(k);
            }
        }
        int limit = Math.Max(1, key.Length / 20);
        string digits = Digits(key);
        string best = null;
        int bestDistance = limit + 1;
        for (int len = Math.Max(10, key.Length - limit); len <= Math.Min(401, key.Length + limit) && bestDistance > 1; len++) {
            List<string> list = fuzzyBuckets[len];
            if (list == null) continue;
            foreach (string k in list) {
                if (k[0] != key[0] && k[k.Length - 1] != key[key.Length - 1]) continue;
                int d = Distance(key, k, bestDistance - 1);
                if (d >= bestDistance || Digits(k) != digits) continue;
                bestDistance = d; best = k;
                if (d <= 1) break;
            }
        }
        hit = best == null ? null : translations[best];
        if (fuzzyCache.Count > 5000) fuzzyCache.Clear();
        fuzzyCache[key] = hit;
        return hit;
    }
    static int Distance(string a, string b, int max) {
        int n = a.Length, m = b.Length;
        if (Math.Abs(n - m) > max) return max + 1;
        int[] prev = new int[m + 1], cur = new int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;
        for (int i = 1; i <= n; i++) {
            cur[0] = i;
            int rowMin = i;
            for (int j = 1; j <= m; j++) {
                int c = prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                int x = prev[j] + 1; if (x < c) c = x;
                x = cur[j - 1] + 1; if (x < c) c = x;
                cur[j] = c;
                if (c < rowMin) rowMin = c;
            }
            if (rowMin > max) return max + 1;
            int[] swap = prev; prev = cur; cur = swap;
        }
        return prev[m];
    }
    // Analysis patch: resolve complete sources before token substitutions.
    // This extension only changes text lookup/loading; it does not resize UI.
    class PTBRFormatModel {
        public Regex pattern;
        public string result;
        public string probe;
        public Dictionary<string, string> groups;
    }
    readonly List<PTBRFormatModel> ptbrFormats = new List<PTBRFormatModel>();
    readonly Dictionary<string, string> ptbrSeaNames = new Dictionary<string, string>();
    void LoadPTBRSeaNames(string folder) {
        string file = Path.Combine(folder, "nomes-maritimos.tsv");
        if (!File.Exists(file)) return;
        foreach (string line in File.ReadAllLines(file, Encoding.UTF8)) {
            string[] pair = line.Split('\t');
            if (pair.Length != 2 || pair[0].Length == 0 || HasCjk(pair[1])) continue;
            ptbrSeaNames[Key(pair[0])] = pair[1];
            ptbrSeaNames[Key(pair[1])] = pair[1];
        }
    }
    string FromPTBRSeaName(string source) {
        if (source.Length > 80) return null;
        string compact = Key(source);
        string[,] suffixes = { { "之地", "Terras de" }, { "冥眼", "Olho Sombrio de" },
            { "岛", "Ilha de" }, { "洞府", "Refúgio de" }, { "洞窟", "Caverna de" },
            { "矿脉", "Veio Mineral de" }, { "谷", "Vale de" }, { "雷灵", "Espírito do Trovão de" } };
        for (int i = 0; i < suffixes.GetLength(0); i++) {
            string previous;
            translations.TryGetValue(Key(suffixes[i, 0]), out previous);
            string[] variants = { suffixes[i, 0], previous };
            foreach (string variant in variants) {
                if (String.IsNullOrEmpty(variant)) continue;
                string suffix = Key(variant);
                if (!compact.EndsWith(suffix, StringComparison.Ordinal)) continue;
                string name;
                if (ptbrSeaNames.TryGetValue(compact.Substring(0, compact.Length - suffix.Length), out name))
                    return suffixes[i, 1] + " " + name;
            }
        }
        return null;
    }
    static readonly Regex ptbrFormatSlot = new Regex(@"(?<!\{)\{(\d+)(?:,-?\d+)?(?::[^{}]+)?\}(?!\})");
    void AddPTBRFormat(string source, string target) {
        if (!HasCjk(source) || source.Length > 6000 || !ptbrFormatSlot.IsMatch(source)) return;
        StringBuilder pattern = new StringBuilder("^");
        int cursor = 0, count = 0;
        string probe = "";
        Dictionary<string, string> groups = new Dictionary<string, string>();
        foreach (Match slotMatch in ptbrFormatSlot.Matches(source)) {
            string literal = source.Substring(cursor, slotMatch.Index - cursor);
            pattern.Append(Regex.Escape(literal));
            foreach (Match run in Regex.Matches(literal, @"[一-鿿]+"))
                if (run.Length > probe.Length) probe = run.Value;
            string number = slotMatch.Groups[1].Value;
            string group;
            if (groups.TryGetValue(number, out group)) pattern.Append(@"\k<" + group + ">");
            else {
                group = "v" + count++;
                groups[number] = group;
                pattern.Append("(?<" + group + ">.+?)");
            }
            cursor = slotMatch.Index + slotMatch.Length;
        }
        string tail = source.Substring(cursor);
        pattern.Append(Regex.Escape(tail));
        foreach (Match run in Regex.Matches(tail, @"[一-鿿]+"))
            if (run.Length > probe.Length) probe = run.Value;
        if (probe.Length < 2) return;
        foreach (Match targetSlot in ptbrFormatSlot.Matches(target))
            if (!groups.ContainsKey(targetSlot.Groups[1].Value)) return;
        pattern.Append("$");
        PTBRFormatModel model = new PTBRFormatModel();
        model.pattern = new Regex(pattern.ToString(), RegexOptions.Singleline, TimeSpan.FromMilliseconds(40));
        model.result = target;
        model.probe = probe;
        model.groups = groups;
        ptbrFormats.Add(model);
    }
    string FromPTBRFormat(string source) {
        if (source.Length > 6000 || !HasCjk(source)) return null;
        string compact = Key(source);
        foreach (PTBRFormatModel model in ptbrFormats) {
            if (compact.IndexOf(model.probe, StringComparison.Ordinal) < 0) continue;
            Match match;
            try { match = model.pattern.Match(compact); }
            catch (RegexMatchTimeoutException) { continue; }
            if (!match.Success) continue;
            return ptbrFormatSlot.Replace(model.result, delegate(Match tokenMatch) {
                // Format/alignment have already been applied by the game.
                // Copy captured text literally. Do not use it as a replacement pattern.
                return match.Groups[model.groups[tokenMatch.Groups[1].Value]].Value;
            });
        }
        return null;
    }
    void ApplyPTBRAnalysisHooks(Harmony harmony) {
        BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        MethodInfo init = AccessTools.Method(typeof(jsonData), "init", new Type[] { typeof(string), typeof(JSONObject).MakeByRefType() });
        if (init != null) harmony.Patch(init, null, new HarmonyMethod(typeof(MenusPTBR).GetMethod("PTBRJsonLoaded", flags)));
        Type symbolType = AccessTools.TypeByName("WXB.SymbolText");
        MethodInfo setter = symbolType == null ? null : AccessTools.PropertySetter(symbolType, "text");
        if (setter != null && setter.DeclaringType == symbolType)
            harmony.Patch(setter, new HarmonyMethod(typeof(MenusPTBR).GetMethod("PTBRSymbolSet", flags)));
        Type db = AccessTools.TypeByName("YSGame.TuJian.TuJianDB");
        MethodInfo dbInit = db == null ? null : AccessTools.Method(db, "InitDB");
        if (dbInit != null) harmony.Patch(dbInit, null, new HarmonyMethod(typeof(MenusPTBR).GetMethod("PTBRTuJianReady", flags)));
        Logger.LogInfo("[PTBR analise] Ganchos de JSON do Guia, cache TuJian e SymbolText configurados.");
    }
    static void PTBRJsonLoaded(string __0, ref JSONObject __1) {
        try {
            if (instance == null || __1 == null || __0 == null) return;
            string path = __0.Replace('\\', '/');
            // Only guide tables: never rewrite scripts, IDs, censorship lists or arbitrary JSON.
            if (!path.StartsWith("Effect/json/d_TuJian.py.", StringComparison.OrdinalIgnoreCase)) return;
            int before = dataChanged;
            instance.WalkData(__1, new HashSet<object>(new RefComparer()), 0);
            instance.Logger.LogInfo("[PTBR analise] JSON Guia " + path + ": " + (dataChanged - before) + " traduções antes do cache.");
        } catch (Exception error) {
            if (instance != null) instance.Logger.LogWarning("[PTBR analise] JSON Guia: " + error.Message);
        }
    }
    static bool ptbrDbProcessed;
    static void PTBRTuJianReady() {
        if (instance == null || ptbrDbProcessed) return;
        try {
            Type db = AccessTools.TypeByName("YSGame.TuJian.TuJianDB");
            if (db == null) return;
            string[] names = { "ShenTongDesc1Data", "ShenTongDesc2Data", "MiShuDesc1Data", "MiShuDesc2Data", "GongFaDesc1Data", "GongFaDesc2Data" };
            foreach (string fieldName in names) {
                FieldInfo field = AccessTools.Field(db, fieldName);
                if (field == null) continue;
                instance.PTBRTranslateCache(field.GetValue(null), 0);
            }
            ptbrDbProcessed = true;
        } catch (Exception error) { instance.Logger.LogWarning("[PTBR analise] Cache TuJian: " + error.Message); }
    }
    void PTBRTranslateCache(object node, int depth) {
        if (node == null || depth > 8) return;
        System.Collections.IDictionary map = node as System.Collections.IDictionary;
        if (map != null) {
            List<object> keys = new List<object>();
            foreach (object k in map.Keys) keys.Add(k);
            foreach (object k in keys) {
                string source = map[k] as string;
                if (source == null) { PTBRTranslateCache(map[k], depth + 1); continue; }
                string result = Translate(source);
                if (result != null) map[k] = result;
            }
            return;
        }
        System.Collections.IList list = node as System.Collections.IList;
        if (list == null || list.IsReadOnly) return;
        for (int i = 0; i < list.Count; i++) {
            string source = list[i] as string;
            if (source == null) { PTBRTranslateCache(list[i], depth + 1); continue; }
            string result = Translate(source);
            if (result != null) list[i] = result;
        }
    }
    // Use the overridden setter so SymbolText rebuilds its internal text/mesh caches.
    static void PTBRSymbolSet(UnityEngine.Object __instance, ref string value) {
        TextSetPrefix(__instance, ref value);
    }
    static bool PTBRIsSymbol(Text label) {
        return label != null && label.GetType().FullName == "WXB.SymbolText";
    }
    static readonly Regex ptbrProtected = new Regex(@"#c[0-9a-f]{6}|#n|#[sw]\d+(?:\.\d+)?|</?size\b[^>]*>|<hy\b[^>]*>", RegexOptions.IgnoreCase);
    static readonly Regex ptbrHyNameAttribute = new Regex(@"(\bt=)(.*?)(?= +[A-Za-z_][A-Za-z0-9_]*=|>)");
    static string PTBRNormalizeHy(string value) {
        if (String.IsNullOrEmpty(value) || value.IndexOf("<hy", StringComparison.OrdinalIgnoreCase) < 0) return value;
        return Regex.Replace(value, @"<hy\b[^>]*>", delegate(Match tagMatch) {
            return ptbrHyNameAttribute.Replace(tagMatch.Value, delegate(Match nameMatch) {
                return nameMatch.Groups[1].Value + nameMatch.Groups[2].Value.Replace(' ', '\u00a0');
            });
        }, RegexOptions.IgnoreCase);
    }
    static string PTBRMarkupSignature(string source) {
        StringBuilder result = new StringBuilder();
        foreach (Match tokenMatch in ptbrProtected.Matches(source)) {
            string token = tokenMatch.Value;
            if (token.StartsWith("<hy", StringComparison.OrdinalIgnoreCase))
                token = ptbrHyNameAttribute.Replace(token, "t=NAME");
            result.Append(token).Append('\n');
        }
        return result.ToString();
    }
    static bool PTBRMarkupSafe(string source, string target) {
        return target != null && PTBRMarkupSignature(source) == PTBRMarkupSignature(target);
    }
    static bool PTBRPrefabSample(string source) {
        if (source.IndexOf("年得到的", StringComparison.Ordinal) >= 0 && source.IndexOf("xxx", StringComparison.Ordinal) >= 0) return true;
        if (Regex.IsMatch(source, @"^(灵气护罩){4,}$")) return true;
        return Regex.IsMatch(source, @"^(?:【(?:主动|Ativo)】\s*相同的){4,}$");
    }
    // Set true for one diagnostic session, then restore false.
    static bool ptbrTraceResiduals = false;
    static readonly HashSet<string> ptbrResidualSeen = new HashSet<string>();
    static void PTBRTraceResidual(UnityEngine.Object owner, string value) {
        if (!ptbrTraceResiduals || instance == null || String.IsNullOrEmpty(value) || !HasCjk(value)) return;
        Component c = owner as Component;
        if (c == null || ptbrResidualSeen.Count >= 300) return;
        string path = PathOf(c.transform);
        if (!ptbrResidualSeen.Add(path + "\n" + value)) return;
        instance.Logger.LogInfo("[PTBR residual] sample=" + PTBRPrefabSample(value) + " active=" + c.gameObject.activeInHierarchy +
            " scene=" + c.gameObject.scene.name + " type=" + c.GetType().FullName + " path=" + path +
            " raw=" + value.Replace("\r", "\\r").Replace("\n", "\\n"));
    }

    // The game's rich text swallows hex digits that follow a colour code (#c449491Atributos
    // loses the "A"): a space after the code keeps the first letters.
    static readonly Regex hexGuard = new Regex(@"(#c[0-9A-Fa-f]{6})(?=[0-9A-Fa-f])"); // v0.60: nao mexer em #sNN/#wNN (antes virava "#s2 4" = fonte tamanho 2)
    string Translate(string source) {
        string r = PTBRNormalizeHy(TranslateCore(source));
        if (r != null && !PTBRMarkupSafe(source, r)) {
            r = PTBRNormalizeHy(ComposeMarkup2(source));
            if (r != null && !PTBRMarkupSafe(source, r)) r = null;
        }
        if (r != null && r.IndexOf('#') >= 0) r = hexGuard.Replace(r, "$1 ");
        return r;
    }

    // Glossary / guide pages: HyText markup (#c449491 colour codes, <pos>, <hy> links) around text.
    // Each text piece is translated on its own and the markup is kept.
    static readonly Regex markupToken = new Regex(@"(#c[0-9A-Fa-f]{6}|#[sw]\d+(?:\.\d+)?|#W|#n|<[^>]*>)");
    static readonly Regex hyTextCode = new Regex(@"#c[0-9A-Fa-f]{6}|#[sw]\d+|#n|<pos\b|<hy\b|</?size\b", RegexOptions.IgnoreCase);
    static readonly Regex posTag = new Regex(@"^<pos v=([0-9.]+)");
    static readonly Regex layerLabel = new Regex(@"^第([一二三四五六七八九十]+|\d+)层[：:]$");
    static readonly string[,] glossLabels = {
        {"名称","Nome"}, {"品级","Grau"}, {"类型","Tipo"}, {"主药","Principal"}, {"辅药","Auxiliar"}, {"药引","Catalisador"},
        {"产地","Origem"}, {"介绍","Info"}, {"境界","Reino"}, {"产出","Materiais"}, {"栖息","Habitat"}, {"属性","Atributo"},
        {"基础修炼速度","Veloc. base"}, {"修炼速度","Veloc."}, {"耐药","Tolerância"}, {"丹毒","Toxicidade"}, {"效果","Efeito"},
        {"消耗","Custo"}, {"铭文","Inscrição"} };
    string HyName(string name, ref bool failed) {
        string whole;
        if (translations.TryGetValue(Key(name), out whole) || dataTranslations.TryGetValue(Key(name), out whole)) return whole.Replace(' ', ' ');
        string[] pieces = name.Split('·');
        for (int i = 0; i < pieces.Length; i++) {
            string t;
            if (!HasCjk(pieces[i])) continue;
            if (translations.TryGetValue(Key(pieces[i]), out t) || dataTranslations.TryGetValue(Key(pieces[i]), out t)) pieces[i] = t; else failed = true;
        }
        return String.Join("·", pieces).Replace(' ', ' ');
    }
    // v0.61: card-effect clauses that reach the guide split by <hy> links ("基础木系技能，造成2点伤害，" ... "若上一个使用的是水系技能，则伤害+1").
    static readonly Regex clauseLabel = new Regex(@"^([^：:，。]{1,12}[：:])(.+)$", RegexOptions.Singleline);
    static readonly Regex clauseBasic = new Regex(@"^基础([金木水火土])系(?:技能|神通)[，,](?:造成(\d+)点伤害[，,]?)?$");
    static readonly Regex clauseDmg = new Regex(@"^造成(\d+)点伤害[，,]?$");
    static readonly Regex clauseCombo = new Regex(@"^若上一个使用的是([金木水火土])系技能[，,](.+)$", RegexOptions.Singleline);
    string EffectClause(string e) {
        e = e.Trim();
        Match m;
        m = Regex.Match(e, @"^则?伤害\+(\d+)$"); if (m.Success) return "dano +" + m.Groups[1].Value;
        if (Regex.IsMatch(e, @"^则?效果翻倍$")) return "o efeito dobra";
        m = Regex.Match(e, @"^额外提升(\d+)点伤害(。?)$"); if (m.Success) return "causa mais " + m.Groups[1].Value + " de dano" + (m.Groups[2].Success && m.Groups[2].Value.Length > 0 ? "." : "");
        if (e == "吸收一点灵气") return "absorve 1 de energia";
        m = Regex.Match(e, @"^恢复(\d+)点生命值$"); if (m.Success) return "recupera " + m.Groups[1].Value + " de vida";
        string t = Translate(e);
        return (t != null && !HasCjk(t)) ? t : null;
    }
    // Translate a whole line made of "<clause>,<link>。<link><clause>" pieces when only the clauses are plain text.
    string ClauseChain(string rest) { return null; }
    static string Element(string c) { switch (c) { case "金": return "metal"; case "木": return "madeira"; case "水": return "água"; case "火": return "fogo"; default: return "terra"; } }
    string ClauseFix(string core) {
        Match lab = clauseLabel.Match(core);
        if (lab.Success && !clauseBasic.IsMatch(core) && !clauseCombo.IsMatch(core)) {
            string rest = ClauseFix(lab.Groups[2].Value);
            if (rest == null) return null;
            string head = Translate(lab.Groups[1].Value);
            if (head == null || HasCjk(head)) return null;
            return head + rest;
        }
        Match b = clauseBasic.Match(core);
        if (b.Success) return "Habilidade básica de " + Element(b.Groups[1].Value) + ", " + (b.Groups[2].Success ? "causa " + b.Groups[2].Value + " de dano, " : "");
        Match d = clauseDmg.Match(core);
        if (d.Success) return "Causa " + d.Groups[1].Value + " de dano, ";
        Match c = clauseCombo.Match(core);
        if (c.Success) {
            string eff = EffectClause(c.Groups[2].Value);
            if (eff == null) return null;
            return " se a última habilidade usada foi de " + Element(c.Groups[1].Value) + ", " + eff;
        }
        return null;
    }
    string MarkupLine(string core) {
        Match label = Regex.Match(core, @"^(.+?)[：:]$");
        if (label.Success) {
            for (int i = 0; i < glossLabels.GetLength(0); i++) if (glossLabels[i, 0] == label.Groups[1].Value) return glossLabels[i, 1] + ":";
            Match layer = layerLabel.Match(core);
            if (layer.Success) return "Camada " + ChineseNumber(layer.Groups[1].Value) + ":";
        }
        if (Regex.IsMatch(core, @"^未知([，,]未知)*$")) return core.Replace("未知", "???").Replace("，", ", ").Replace(",", ", ").Replace(",  ", ", ");
        string t;
        if (translations.TryGetValue(Key(core), out t)) return t;
        t = FromTemplate(core);
        if (t != null) return t;
        string clause = ClauseFix(core);
        if (clause != null) return clause;
        t = Translate(core);
        if (t != null && !HasCjk(t)) return t;
        string[] sentences = Regex.Split(core, @"(?<=[。！？])");
        if (sentences.Length < 2) return null;
        StringBuilder sb = new StringBuilder();
        bool any = false;
        foreach (string sentence in sentences) {
            if (sentence.Length == 0) continue;
            string r = HasCjk(sentence) ? Translate(sentence.Trim()) : sentence;
            if (r == null) r = sentence; else any = true;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(r);
        }
        return any ? sb.ToString() : null;
    }
    // v0.61: the guide shows "境界：造成9点伤害，获得【<hy ..>】*1。..." with the number already substituted.
    // Try each whole line (label split off) against the dictionary and the number templates before
    // translating piece by piece.
    string WholeLine(string line) {
        string r;
        string key = Key(line);
        if (translations.TryGetValue(key, out r) || dataTranslations.TryGetValue(key, out r)) return r;
        return FromTemplate(line);
    }
    string LineFirst(string source) {
        string[] lines = Regex.Split(source, @"(\r\n|\r|\n)");
        StringBuilder sb = new StringBuilder();
        bool any = false;
        for (int i = 0; i < lines.Length; i++) {
            string line = lines[i];
            if (i % 2 == 1 || !HasCjk(line)) { sb.Append(line); continue; }
            Match edge = Regex.Match(line, @"^(\s*)(.*?)(\s*)$", RegexOptions.Singleline);
            string core = edge.Groups[2].Value, outLine = null;
            string whole = WholeLine(core);
            if (whole != null && !HasCjk(whole)) outLine = whole;
            else {
                Match lab = Regex.Match(core, @"^(#c[0-9A-Fa-f]{6})?([^：:，。#<\[]{1,12})[：:](#n)?(.+)$", RegexOptions.Singleline);
                if (lab.Success && HasCjk(lab.Groups[4].Value)) {
                    string rest = WholeLine(lab.Groups[4].Value);
                    if (rest == null) rest = ClauseChain(lab.Groups[4].Value);
                    string head = null;
                    for (int g = 0; g < glossLabels.GetLength(0); g++) if (glossLabels[g, 0] == lab.Groups[2].Value) head = glossLabels[g, 1];
                    if (head == null) { head = Translate(lab.Groups[2].Value + "："); if (head != null && head.EndsWith(":")) head = head.Substring(0, head.Length - 1); }
                    if (rest != null && !HasCjk(rest) && head != null && !HasCjk(head)) outLine = lab.Groups[1].Value + head + ":" + lab.Groups[3].Value + rest;
                }
            }
            if (outLine == null) { sb.Append(line); continue; }
            any = true;
            sb.Append(edge.Groups[1].Value + outLine + edge.Groups[3].Value);
        }
        return any ? sb.ToString() : null;
    }
    string ComposeMarkup(string source) {
        if (source.Length > 6000 || !hyTextCode.IsMatch(source) || !HasCjk(source)) return null;
        string first = LineFirst(source);
        if (first != null) {
            if (!HasCjk(first)) return first;
            string again = ComposeMarkup2(first);
            return again ?? first;
        }
        return ComposeMarkup2(source);
    }
    string ComposeMarkup2(string source) {
        if (!HasCjk(source)) return source;
        string[] parts = markupToken.Split(source);
        if (parts.Length < 3) return null;
        StringBuilder sb = new StringBuilder();
        bool failed = false, changedAny = false;
        int visible = 0;
        double lastV = 0;
        for (int i = 0; i < parts.Length; i++) {
            string part = parts[i];
            if (i % 2 == 1) {
                if (part.StartsWith("<hy t=")) {
                    Match hy = Regex.Match(part, @"^<hy t=([^ >]+)(.*)$", RegexOptions.Singleline);
                    if (hy.Success) {
                        string name = HyName(hy.Groups[1].Value, ref failed);
                        sb.Append("<hy t=" + name + hy.Groups[2].Value);
                        visible += name.Length; changedAny = true;
                        continue;
                    }
                }
                Match pos = posTag.Match(part);
                if (pos.Success) {
                    double v = double.Parse(pos.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    // The next column starts at a fixed position: if the text before it is longer, use a new line.
                    if ((v - lastV) * 40.0 < visible) { sb.Append('\n'); visible = 0; lastV = 0; }
                    else { sb.Append(part); visible = 0; lastV = v; }
                    continue;
                }
                sb.Append(part);
                continue;
            }
            if (part.Length == 0) continue;
            if (!HasCjk(part)) { sb.Append(part); visible += part.Length; continue; }
            string[] lines = Regex.Split(part, @"(\r\n|\r|\n)");
            for (int j = 0; j < lines.Length; j++) {
                if (j % 2 == 1) { sb.Append('\n'); visible = 0; lastV = 0; continue; }
                string line = lines[j];
                if (!HasCjk(line)) { sb.Append(line); visible += line.Length; continue; }
                Match edge = Regex.Match(line, @"^(\s*)(.*?)(\s*)$", RegexOptions.Singleline);
                string translated = MarkupLine(edge.Groups[2].Value);
                if (translated == null) { failed = true; translated = edge.Groups[2].Value; } else changedAny = true;
                if (edge.Groups[1].Value.Length == 0 && translated.Length > 0 && Char.IsLetter(translated[0]) && sb.ToString().EndsWith("】#n", StringComparison.Ordinal)) sb.Append(' ');
                sb.Append(edge.Groups[1].Value + translated + edge.Groups[3].Value);
                visible += translated.Length;
            }
        }
        if (!changedAny) return null;
        return sb.ToString();
    }
    static readonly Regex colorInner = new Regex(@"^(<color=#[0-9a-fA-F]+>)([^<]*)(</color>)$");
    string ColorPart(string colored) {
        Match m = colorInner.Match(colored);
        if (!m.Success) return colored;
        return m.Groups[1].Value + Part(m.Groups[2].Value) + m.Groups[3].Value;
    }
    static readonly Regex combatCastOnly = new Regex(@"(<color=#[0-9a-fA-F]+>[^<]*</color>)?对(<color=#[0-9a-fA-F]+>)([^<]+)(</color>)释放了(<color=#[0-9a-fA-F]+>[^<]*</color>)[,，]?");
    static readonly Regex coloredNumber = new Regex(@"(<color=#[0-9a-fA-F]+>)([0-9]+(?:\.[0-9]+)?%?)</color>");
    static readonly Regex equipLine = new Regex(@"^(<color=#[0-9a-fA-F]+>)([^<]+?)([:：])(</color>)(.+)$", RegexOptions.Singleline);
    string TranslateCore(string source)
    {
        if (String.IsNullOrEmpty(source) || source.Length > 12000) return null;
        string result;
        if (translations.TryGetValue(Key(source), out result)) return result;
        string seaName = FromPTBRSeaName(source);
        if (seaName != null) return seaName;
        string formatted = FromPTBRFormat(source);
        if (formatted != null) return formatted;
        if (source.Length <= 600 && source.IndexOf('<') < 0 && source.IndexOf('#') < 0) {
            string fragment = FromTemplate(source);
            if (fragment != null) return fragment;
            fragment = ClauseFix(source);
            if (fragment != null) return fragment;
        }
        string tokens = Tokens(source);
        if (tokens != source) {
            if (!HasCjk(tokens)) return tokens;
            string again = Translate(tokens);
            return again ?? tokens;
        }
        if (source.EndsWith("</color>") && source.IndexOf("<color") < 0) {
            string bare = Translate(source.Substring(0, source.Length - 8));
            if (bare != null) return bare;
        }
        // Typewriter effect: the game types a line by wrapping the part not yet shown in an
        // invisible <color=#FFFFFF00> tag. Translate the whole line and move the tag proportionally.
        Match hidden = Regex.Match(source, @"^([^<]*)<color=#FFFFFF00>([^<]*)(?:</color>)?$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (hidden.Success) {
            string shown = hidden.Groups[1].Value, rest = hidden.Groups[2].Value;
            string whole = Translate(shown + rest);
            if (whole != null && (shown + rest).Length > 0) {
                int cut = (int)Math.Round(whole.Length * (double)shown.Length / (shown + rest).Length);
                while (cut > 0 && cut < whole.Length && whole[cut - 1] != ' ') cut++;
                if (cut >= whole.Length) return whole;
                return whole.Substring(0, cut) + "<color=#FFFFFF00>" + whole.Substring(cut) + "</color>";
            }
        }
        // Heading in Portuguese/markup followed by a Chinese description: translate the tail only.
        Match headTail = Regex.Match(source, @"^(#c[0-9a-fA-F]+【[^】]*】#n|[^\u4e00-\u9fff]*?[.!?:] +)([\u4e00-\u9fff].*)$", RegexOptions.Singleline);
        if (headTail.Success && !HasCjk(headTail.Groups[1].Value) && headTail.Groups[1].Value.Length > 2) {
            string tailText = Translate(headTail.Groups[2].Value);
            if (tailText != null) return headTail.Groups[1].Value + tailText;
        }
        // v0.43: descrição com números coloridos ("造成<color=#FF00FF>7</color>点伤害"): traduz o
        // texto sem as cores e devolve as cores aos números, na mesma ordem.
        if (source.IndexOf("<color=", StringComparison.Ordinal) >= 0 && HasCjk(source)) {
            MatchCollection nums = coloredNumber.Matches(source);
            if (nums.Count > 0) {
                string plain = coloredNumber.Replace(source, "$2");
                if (plain.IndexOf('<') < 0) {
                    string tp = Translate(plain);
                    if (tp != null && !HasCjk(tp)) {
                        int pos = 0;
                        foreach (Match n in nums) {
                            Match at = new Regex(@"(?<![0-9])" + Regex.Escape(n.Groups[2].Value) + "(?![0-9])").Match(tp, pos);
                            if (!at.Success) break;
                            string wrapped = n.Groups[1].Value + n.Groups[2].Value + "</color>";
                            tp = tp.Substring(0, at.Index) + wrapped + tp.Substring(at.Index + at.Length);
                            pos = at.Index + wrapped.Length;
                        }
                        return tp;
                    }
                }
            }
            // Registro de equipamento: "<color=#cda14c>天锦袍:</color>回合结束，获得【护罩】x6".
            Match equip = equipLine.Match(source);
            if (equip.Success) {
                string name = Part(equip.Groups[2].Value);
                string rest = Translate(equip.Groups[5].Value);
                if (rest == null) { string eff = EffectLines(equip.Groups[5].Value); if (eff != equip.Groups[5].Value) rest = HasCjk(eff) ? ComposeRuns(eff) : eff; }
                if (rest != null && !HasCjk(name) && !HasCjk(rest))
                    return equip.Groups[1].Value + name + ":" + equip.Groups[4].Value + " " + rest.Replace(", Obtém", ", obtém").TrimStart();
            }
        }
        string markup = ComposeMarkup(source);
        if (markup != null) return markup;
        string special = Special(source);
        if (special != null) return special;
        if (source.IndexOf("释放了") >= 0 && source.IndexOf("点伤害") >= 0) {
            string combat = combatCast.Replace(source, delegate(Match m) {
                string who = m.Groups[1].Success ? ColorPart(m.Groups[1].Value) + " usou " : "Usou ";
                return who + ColorPart(m.Groups[5].Value) + " em " + m.Groups[2].Value + Part(m.Groups[3].Value) + m.Groups[4].Value + ", causando " + m.Groups[6].Value + " de dano";
            });
            if (combat != source && !HasCjk(combat)) return combat;
        }
        if (source.IndexOf("释放了", StringComparison.Ordinal) >= 0 && source.IndexOf('对') >= 0) {
            // v0.42: linha de uso de habilidade ainda sem o dano ("X对Y释放了Z,").
            string castOnly = combatCastOnly.Replace(source, delegate(Match m) {
                string who = m.Groups[1].Success ? ColorPart(m.Groups[1].Value) + " usou " : "Usou ";
                return who + ColorPart(m.Groups[5].Value) + " em " + m.Groups[2].Value + Part(m.Groups[3].Value) + m.Groups[4].Value + ",";
            });
            if (castOnly != source) {
                castOnly = castOnly.Replace("。", ".");
                if (!HasCjk(castOnly)) return castOnly;
            }
        }
        if (source.IndexOf("获得【") >= 0 || source.IndexOf("对自己造成") >= 0) {
            string effText = EffectLines(source);
            if (effText != source) {
                if (!HasCjk(effText)) return effText;
                string effCompose = ComposeRuns(effText);
                if (effCompose != null) return effCompose;
            }
        }
        Match colorHead = Regex.Match(source, @"^(<color=[^>]+>[^<]*</color>)(.+)$", RegexOptions.Singleline);
        if (colorHead.Success && HasCjk(colorHead.Groups[2].Value) && !HasCjk(colorHead.Groups[1].Value)) {
            string tail = Translate(colorHead.Groups[2].Value);
            if (tail != null) return colorHead.Groups[1].Value + tail;
        }
        string vocative = Vocative(source);
        if (vocative != null) return vocative;
        string phFilled = PlaceholderTemplate(source);
        if (phFilled != null) return phFilled;
        // Talisman/letter text arrives with {FirstName}{daoyou} already replaced ("兄弟，...").
        Match vocMatch = Regex.Match(source, @"^[\s\u3000]*([\u4e00-\u9fff]{1,4})([，,].+)$", RegexOptions.Singleline);
        if (vocMatch.Success) {
            string vocWord = vocMatch.Groups[1].Value, vocRest = vocMatch.Groups[2].Value;
            string[] vocPrefixes = { "{FirstName}{daoyou}", "{daoyou}", "{FirstName}{qianbei}", "{qianbei}", "{FirstName}{xiaoyou}", "{xiaoyou}", "{FirstName}" };
            foreach (string vocPrefix in vocPrefixes) {
                string vocResult;
                if (!translations.TryGetValue(Key(vocPrefix + vocRest), out vocResult)) continue;
                string vocPt = Part(vocWord);
                if (HasCjk(vocPt)) vocPt = "amigo";
                vocResult = vocResult.Replace("{FirstName}", "").Replace("{daoyou}", vocPt).Replace("{qianbei}", vocPt).Replace("{xiaoyou}", vocPt);
                vocResult = Regex.Replace(vocResult, @"\s+([,.!?;:])", "$1").Trim();
                return vocResult.Length > 0 ? Char.ToUpperInvariant(vocResult[0]) + vocResult.Substring(1) : vocResult;
            }
        }
        // Character tooltips concatenate colored headings, effects and descriptions.
        // Match complete chunks without stripping their original markup.
        if (creating) {
            string composed = source;
            foreach (KeyValuePair<string,string> pair in creationPhrases)
                if (composed.Contains(pair.Key)) composed = composed.Replace(pair.Key, pair.Value);
            composed = Regex.Replace(composed, @"[\u4e00-\u9fff]+[：:]?", delegate(Match m) {
                string translated;
                return translations.TryGetValue(Key(m.Value), out translated) ? translated : m.Value;
            });
            composed = Regex.Replace(composed, @"剧情模式境界达到", "Na história, alcance ");
            composed = composed.Replace("解锁", " para liberar");
            if (composed != source) return composed;
        }
        string fromTemplate = FromTemplate(source);
        if (fromTemplate != null) return fromTemplate;
        string trimmed = Key(source);
        if (trimmed.Length > 1 && (trimmed.EndsWith("：") || trimmed.EndsWith(":"))) {
            string name;
            if (translations.TryGetValue(trimmed.Substring(0, trimmed.Length - 1), out name)) return name + ":";
            string pinyinLabel = PinyinName(trimmed.Substring(0, trimmed.Length - 1));
            if (pinyinLabel != null) return pinyinLabel + ":";
        }
        Match date = Regex.Match(trimmed, @"^(\d+)年(\d+)月(\d+)日$");
        if (date.Success) return "Ano " + date.Groups[1].Value + ", " + date.Groups[3].Value + "/" + date.Groups[2].Value;
        Match minutes = Regex.Match(Key(source), @"^(\d+)分钟$");
        if (minutes.Success) return minutes.Groups[1].Value + " min";
        Match colored = Regex.Match(source, @"^(<color=[^>]+>)(.*)(</color>)$", RegexOptions.Singleline);
        if (colored.Success) {
            string inner = Translate(colored.Groups[2].Value);
            if (inner != null) return colored.Groups[1].Value + inner + colored.Groups[3].Value;
        }
        Match turn = Regex.Match(trimmed, @"^第(\d+)回合$");
        if (turn.Success) return "Turno " + turn.Groups[1].Value;
        Match owned = Regex.Match(source, @"^(.*?)\s*(?:已有[:：]|Você tem:)\s*(\d+)$", RegexOptions.Singleline);
        if (owned.Success) {
            string head = HasCjk(owned.Groups[1].Value) ? Translate(owned.Groups[1].Value) : owned.Groups[1].Value;
            if (head != null) return head + " Você tem: " + owned.Groups[2].Value;
        }
        Match progress = Regex.Match(source, @"^(.*?)\s*进度已更新$", RegexOptions.Singleline);
        if (progress.Success) {
            string head = HasCjk(progress.Groups[1].Value) ? Translate(progress.Groups[1].Value) : progress.Groups[1].Value;
            if (head != null) return head + " — progresso atualizado";
        }
        if (source.Contains("不为16:9")) {
            return source.Replace("当前分辨率", "Resolução atual ").Replace("不为16:9，可能会导致UI错误，建议调整到16:9再进行游戏", " não é 16:9; isso pode causar erros na interface. Recomendo usar 16:9.");
        }
        Match version = Regex.Match(Key(source), @"^当前版本[:：]([0-9.]+)$");
        if (version.Success) return "Versão: " + version.Groups[1].Value;
        Match favor = Regex.Match(source, @"^(.+?)对你的好感度(提升|降低)了(\d+)$");
        if (favor.Success) {
            string who = Translate(favor.Groups[1].Value) ?? favor.Groups[1].Value;
            return who + ": afinidade " + (favor.Groups[2].Value == "提升" ? "+" : "-") + favor.Groups[3].Value;
        }
        Match grade = Regex.Match(source, @"^(下品|中品|上品|极品)([^\u4e00-\u9fff]+)$");
        if (grade.Success) {
            string g = grade.Groups[1].Value == "下品" ? "inferior" : grade.Groups[1].Value == "中品" ? "médio" : grade.Groups[1].Value == "上品" ? "superior" : "supremo";
            return grade.Groups[2].Value.Trim() + " (" + g + ")";
        }
        if ((source.IndexOf('万') >= 0 || source.IndexOf('千') >= 0) && Regex.IsMatch(source, @"\d[万千]")) {
            string numbers = Regex.Replace(source, @"(\d+(?:\.\d+)?)([万千])", delegate(Match m) {
                double v = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * (m.Groups[2].Value == "千" ? 1000 : 10000);
                return v.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".");
            });
            if (!HasCjk(numbers)) return numbers;
            string again = Translate(numbers);
            return again ?? numbers;
        }
        string gendered = GenderWords(source);
        if (gendered != source) {
            if (!HasCjk(gendered)) return gendered;
            string again = ComposeRuns(gendered);
            if (again != null) return again;
        }
        if (source.Contains("【生】") || source.Contains("【克】")) {
            string keyed = source.Replace("【生】", "【Geração】").Replace("【克】", "【Controle】");
            if (!HasCjk(keyed)) return keyed;
            string again = ComposeRuns(keyed);
            if (again != null) return again;
        }
        string composedRuns = ComposeRuns(source);
        if (composedRuns != null) return composedRuns;
        if (!quietPending && seen.Count < 1500 && source.Length <= 400 && Regex.IsMatch(source, @"[\u4e00-\u9fff]") && seen.Add(source))
            Logger.LogInfo((PTBRPrefabSample(source) ? "[PTBR amostra-prefab] " : "[PTBR pendente] ") + source.Replace('\n', ' ').Replace('\r', ' '));
        return null;
    }
    static bool daoDumped; static float nextDaoDump = 40f;
    // Diagnostic: exports the game's data tables (JSONClass.*.DataDict) to Tables\<name>.tsv once, so saves can be edited with real ids.
    static string CellText(object v) {
        if (v == null) return "";
        string text;
        if (v is string) text = (string)v;
        else if (v is System.Collections.IEnumerable) {
            StringBuilder sb = new StringBuilder(); int n = 0;
            foreach (object item in (System.Collections.IEnumerable)v) { if (n++ >= 40) { sb.Append("..."); break; } if (n > 1) sb.Append(','); sb.Append(Convert.ToString(item)); }
            text = sb.ToString();
        } else text = Convert.ToString(v);
        return text.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
    }
    void DaoDump() {
        string dir = Path.Combine(pluginFolder, "Tables");
        int written = 0, empty = 0;
        foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
            if (!asm.GetName().Name.StartsWith("Assembly-CSharp")) continue;
            foreach (Type t in asm.GetTypes()) {
                System.Reflection.FieldInfo f = t.GetField("DataDict", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (f == null) continue;
                System.Collections.IDictionary map = f.GetValue(null) as System.Collections.IDictionary;
                if (map == null || map.Count == 0) { empty++; continue; }
                System.Reflection.FieldInfo[] fields = null;
                StringBuilder sb = new StringBuilder();
                foreach (System.Collections.DictionaryEntry entry in map) {
                    if (entry.Value == null) continue;
                    if (fields == null) {
                        List<System.Reflection.FieldInfo> keep = new List<System.Reflection.FieldInfo>();
                        foreach (System.Reflection.FieldInfo fi in entry.Value.GetType().GetFields())
                            if (!fi.IsStatic && !typeof(Delegate).IsAssignableFrom(fi.FieldType) && fi.Name != "DataDict" && fi.Name != "DataList") keep.Add(fi);
                        fields = keep.ToArray();
                        sb.Append("key");
                        foreach (System.Reflection.FieldInfo fi in fields) sb.Append('\t').Append(fi.Name);
                        sb.Append('\n');
                    }
                    sb.Append(Convert.ToString(entry.Key));
                    foreach (System.Reflection.FieldInfo fi in fields) sb.Append('\t').Append(CellText(fi.GetValue(entry.Value)));
                    sb.Append('\n');
                }
                if (fields == null) continue;
                if (written == 0) Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, t.Name + ".tsv"), sb.ToString(), new UTF8Encoding(false));
                written++;
            }
        }
        if (written > 0 || Time.unscaledTime > 600f) {
            daoDumped = true;
            Logger.LogInfo("[PTBR tabelas] " + written + " tabelas exportadas para " + dir + " (vazias: " + empty + ").");
        }
    }
    Text storyLabel; float nextStory, nextStoryFind; int storyLen;
    // Long dialogue lines are typed in piece by piece: keep shrinking the font while the text still overflows its box.
    void StoryTick() {
        try {
            if (Time.unscaledTime < nextStory) return;
            nextStory = Time.unscaledTime + 0.12f;
            if (storyLabel == null) {
                if (Time.unscaledTime < nextStoryFind) return;
                nextStoryFind = Time.unscaledTime + 2f;
                foreach (Text t in UnityEngine.Object.FindObjectsOfType<Text>())
                    if (t.name == "StoryText" && PathOf(t.transform).EndsWith("SayDialog/Panel/StoryText", StringComparison.Ordinal)) { storyLabel = t; break; }
                if (storyLabel == null) return;
            }
            Text l = storyLabel;
            if (!l.gameObject.activeInHierarchy) return;
            int id = l.GetInstanceID(), baseSize;
            if (!originalFont.TryGetValue(id, out baseSize)) { baseSize = l.fontSize; originalFont[id] = baseSize; }
            int len = l.text == null ? 0 : l.text.Length;
            if (len < storyLen - 2) l.fontSize = baseSize;
            storyLen = len;
            l.resizeTextForBestFit = false;
            l.verticalOverflow = VerticalWrapMode.Overflow;
            float limit = l.rectTransform.rect.height + 2f;
            int size = l.fontSize;
            while (size > 13 && l.preferredHeight > limit) { size--; l.fontSize = size; }
        } catch (Exception) { storyLabel = null; }
    }
    void Update()
    {
        StoryTick();
        GuideDump();
        if (Time.unscaledTime >= nextTexture) {
            nextTexture = Time.unscaledTime + 2f;
            try { ReplaceTextures(); } catch (Exception error) { Logger.LogWarning("[PTBR textura] " + error.Message); }
            try { LogSprites(); } catch (Exception) { }
            if (texturesReplaced > 0) { Logger.LogInfo("[PTBR] Texturas trocadas: " + texturesReplaced); texturesReplaced = 0; }
        }
        if (dataPending && Time.unscaledTime >= nextData) {
            nextData = Time.unscaledTime + 3f;
            try { TranslateData("cena"); } catch (Exception error) { Logger.LogWarning("[PTBR dados] " + error.Message); }
        }
        if (!daoDumped && Time.unscaledTime >= nextDaoDump) {
            nextDaoDump = Time.unscaledTime + 15f;
            try { DaoDump(); } catch (Exception error) { daoDumped = true; Logger.LogWarning("[PTBR dao] " + error.Message); }
        }
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.5f;
        lastScan = Time.unscaledTime;
        try {
            creating = UnityEngine.Object.FindObjectOfType<MainUICreateAvatar>() != null || UnityEngine.Object.FindObjectOfType<CreateAvatarMag>() != null;
            foreach (Text label in UnityEngine.Object.FindObjectsOfType<Text>()) {
                InputField input = label.GetComponentInParent<InputField>();
                if (input != null && input.textComponent == label) continue;
                string value = FixDuration(label, label.text, Translate(label.text));
                if (value == null && pendingStyle.Count > 0 && pendingStyle.Remove(label.GetInstanceID()) && !String.IsNullOrEmpty(label.text) && !HasCjk(label.text)) value = label.text;
                if (value == null) {
                    // Text already in Portuguese (translated in the game data): still keep it inside its box.
                    string current = label.text;
                    if (!String.IsNullOrEmpty(current) && !HasCjk(current) && latinLetter.IsMatch(current)) {
                        string last;
                        int id = label.GetInstanceID();
                        if (!fitted.TryGetValue(id, out last) || last != current) {
                            fitted[id] = current;
                            bool lacking = MissingGlyph(label.font, current);
                            GlyphLog("Text", label.font == null ? "-" : label.font.name, current, label.transform, lacking);
                            if (latin != null && lacking) label.font = latin;
                            if (current.Length < 80 && joinedNames.IsMatch(current)) {
                                // Item names assembled from separately translated parts ("Chama EspiritualLie Kong ...").
                                string spaced = joinedNames.Replace(current, "$1 $2");
                                label.text = spaced; current = spaced; fitted[id] = spaced;
                            }
                            if (!origSize.ContainsKey(id)) origSize[id] = label.fontSize;
                            if (DropdownFit(label, null)) continue;
                            if (InInfoRoot(label)) { InfoKeep(label); continue; }
                            if (current.Length <= 40) FitWidth(label, current);
                            if (InGuide(label)) GrowToFit(label); else FitHeight(label, current);
                            LoadingFix(label); RescueCut(label, current); SizeLog(label, current); DlgLog(label);
                        }
                    }
                    continue;
                }
                if (latin != null) label.font = latin;
                if (!origSize.ContainsKey(label.GetInstanceID())) origSize[label.GetInstanceID()] = label.fontSize;
                if (DropdownFit(label, value)) { changed++; touched.Add(label.GetInstanceID()); continue; }
                if (InInfoRoot(label)) { label.text = value; changed++; touched.Add(label.GetInstanceID()); InfoKeep(label); continue; }
                if (LayoutDriven(label) || label.verticalOverflow == VerticalWrapMode.Overflow) {
                    // Keep the game's layout; only shrink when a short label is wider than its box.
                    label.text = value; changed++; touched.Add(label.GetInstanceID());
                    FitWidth(label, value);
                    if (InGuide(label)) GrowToFit(label); else FitHeight(label, value);
                    LoadingFix(label); Relayout(label); CutLog(label, value); SizeLog(label, value); DlgLog(label);
                    continue;
                }
                label.fontSize = Limit(value, label.fontSize);
                label.resizeTextMaxSize = ReadableMaximum(value, label.fontSize);
                label.resizeTextMinSize = 9;
                label.resizeTextForBestFit = true;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                if (Compact(value)) {
                    label.resizeTextMaxSize = Math.Min(label.resizeTextMaxSize, 20);
                }
                if (value.StartsWith("Na história:")) {
                    label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Math.Min(label.rectTransform.rect.width, 270f));
                    label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(label.rectTransform.rect.height, 44f));
                    label.resizeTextMaxSize = 18;
                    label.resizeTextMinSize = 12;
                    label.alignment = TextAnchor.MiddleCenter;
                }
                label.text = value; changed++; touched.Add(label.GetInstanceID());
                FitWidth(label, value);
                LoadingFix(label); RescueCut(label, value); Relayout(label); CutLog(label, value); SizeLog(label, value); DlgLog(label);
            }
            pendingStyle.Clear();
            foreach (UILabel label in UnityEngine.Object.FindObjectsOfType<UILabel>()) {
                if (label.GetComponentInParent<UIInput>() != null) continue;
                string value = Translate(label.text);
                if (value == null) {
                    string current = label.text;
                    if (!String.IsNullOrEmpty(current) && !HasCjk(current) && latinLetter.IsMatch(current) && label.overflowMethod == UILabel.Overflow.ClampContent) label.overflowMethod = UILabel.Overflow.ShrinkContent;
                    if (!String.IsNullOrEmpty(current) && !HasCjk(current) && NeedsAccent(current)) FixUILabel(label, current);
                    continue;
                }
                int size = ReadableMaximum(value, label.fontSize);
                if (latin != null) { label.trueTypeFont = latin; label.fontSize = size; }
                if (label.overflowMethod == UILabel.Overflow.ClampContent) label.overflowMethod = UILabel.Overflow.ShrinkContent;
                // Long descriptions need the full box instead of a forced single line.
                if (value.Length > 60 && label.height >= size * 2) label.maxLineCount = 0;
                if (value.StartsWith("Na história:")) {
                    label.width = Math.Min(label.width, 270);
                    label.height = Math.Max(label.height, 44);
                    label.maxLineCount = 2;
                }
                label.text = value; changed++; touched.Add(label.GetInstanceID());
                UiCutLog(label, label.printedSize.x, label.printedSize.y, label.width, label.height, label.trueTypeFont == null ? "-" : label.trueTypeFont.name, value);
            }
            foreach (TMP_Text label in UnityEngine.Object.FindObjectsOfType<TMP_Text>()) {
                if (label.GetComponentInParent<TMP_InputField>() != null) continue;
                string value = FixDuration(label, label.text, Translate(label.text));
                if (InInfoRoot(label)) {
                    int tid = label.GetInstanceID(); float torig;
                    if (!tmpOriginal.TryGetValue(tid, out torig)) { torig = label.fontSize; tmpOriginal[tid] = torig; }
                    if (infoLogged < 120) { infoLogged++; Logger.LogInfo("[PTBR info] TMP fonte=" + label.fontSize + " orig=" + torig + " auto=" + label.enableAutoSizing + " rect=" + (int)label.rectTransform.rect.width + "x" + (int)label.rectTransform.rect.height + " | " + (label.text.Length > 24 ? label.text.Substring(0, 24) : label.text) + " | " + PathOf(label.transform)); }
                    if (value != null) { label.text = value; changed++; touched.Add(tid); }
                    label.enableAutoSizing = false;
                    label.fontSize = torig;
                    label.enableWordWrapping = true;
                    label.overflowMode = TextOverflowModes.Overflow;
                    continue;
                }
                if (value == null) { FixTmpGlyphs(label); continue; }
                if (LayoutDriven(label)) { label.text = value; changed++; touched.Add(label.GetInstanceID()); UiCutLog(label, label.preferredWidth, label.preferredHeight, label.rectTransform.rect.width, label.rectTransform.rect.height, label.font == null ? "-" : label.font.name, value); continue; }
                label.fontSizeMax = ReadableMaximum(value, (int)label.fontSize);
                label.fontSizeMin = 9;
                label.enableAutoSizing = true;
                label.enableWordWrapping = true;
                label.overflowMode = TextOverflowModes.Masking;
                if (value.StartsWith("Na história:")) {
                    label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Math.Min(label.rectTransform.rect.width, 270f));
                    label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(label.rectTransform.rect.height, 44f));
                    label.fontSizeMax = 18;
                    label.fontSizeMin = 12;
                }
                label.text = value; changed++; touched.Add(label.GetInstanceID());
                UiCutLog(label, label.preferredWidth, label.preferredHeight, label.rectTransform.rect.width, label.rectTransform.rect.height, label.font == null ? "-" : label.font.name, value);
            }
            foreach (Image graphic in UnityEngine.Object.FindObjectsOfType<Image>()) {
                if (graphic.sprite == null || graphic.GetComponent<PTBRGraphicLabel>() != null) continue;
                string spriteName = graphic.sprite.name;
                if ((spriteName == "MCS_DLJM_kaishi_zi_gn" || spriteName == "MCS_DLJM_kaishi_zi_or") && !Retextured(graphic.sprite)) {
                    PTBRGraphicLabel replacement = graphic.gameObject.AddComponent<PTBRGraphicLabel>();
                    replacement.Setup(graphic, latin, "Iniciar jornada", false);
                    Logger.LogInfo("[PTBR imagem] " + spriteName);
                }
            }
            foreach (FpBtn control in UnityEngine.Object.FindObjectsOfType<FpBtn>()) {
                Transform button = control.transform;
                if (button.name != "应用" || button.parent == null || button.parent.Find("音乐/Slider") == null) continue;
                if (button.GetComponent<PTBRGraphicLabel>() != null) continue;
                Image graphic = button.GetComponent<Image>();
                if (graphic == null) continue;
                PTBRGraphicLabel replacement = button.gameObject.AddComponent<PTBRGraphicLabel>();
                replacement.Setup(graphic, latin, "Aplicar", true);
                Logger.LogInfo("[PTBR imagem] Aplicar");
            }
            if (creating) {
                foreach (Button button in UnityEngine.Object.FindObjectsOfType<Button>()) CaptionControl(button, CreationCaption(button.onClick));
                foreach (FpBtn button in UnityEngine.Object.FindObjectsOfType<FpBtn>()) CaptionControl(button, CreationCaption(button.mouseUpEvent));
                foreach (UIButton button in UnityEngine.Object.FindObjectsOfType<UIButton>()) {
                    if (button.GetComponent<PTBRCreationButton>() != null) continue;
                    string caption = null;
                    foreach (EventDelegate action in button.onClick) {
                        if (!(action.target is CreateAvatarMag)) continue;
                        switch (action.methodName) {
                            case "TianFuNext": case "showLingenUI": case "showFinalUI": case "showTianfuUI": caption = "Próximo"; break;
                            case "TianFuLaset": case "showSetFace": case "ChuangjueHome": case "close": caption = "Voltar"; break;
                            case "startGameClick": case "OK": caption = "Começar"; break;
                        }
                    }
                    if (caption == null) continue;
                    UI2DSprite surface = button.tweenTarget == null ? button.GetComponent<UI2DSprite>() : button.tweenTarget.GetComponent<UI2DSprite>();
                    if (surface == null) surface = button.GetComponentInChildren<UI2DSprite>();
                    if (surface == null || Retextured(surface.sprite2D)) continue;
                    button.gameObject.AddComponent<PTBRCreationButton>().Setup(surface, latin, caption);
                    Logger.LogInfo("[PTBR criação botão] " + button.name + " = " + caption);
                }
            }
            if (hookedSay + hookedMenu > 0) { Logger.LogInfo("[PTBR] Falas: " + hookedSay + ", escolhas: " + hookedMenu); hookedSay = 0; hookedMenu = 0; }
            if (changed > 0) { Logger.LogInfo("[PTBR] Textos traduzidos: " + changed); changed = 0; }
        } catch (Exception error) { Logger.LogWarning("PT-BR: " + error.Message); next = Time.unscaledTime + 5f; }
    }
}

// A noninteractive NGUI overlay preserves the original button and its callbacks.
public class PTBRCreationButton : MonoBehaviour
{
    public void Setup(UI2DSprite surface, Font font, string caption)
    {
        GameObject panel = new GameObject("PTBR button caption");
        panel.transform.SetParent(surface.transform, false);
        panel.transform.localPosition = surface.localCenter;
        UITexture background = panel.AddComponent<UITexture>();
        background.mainTexture = Texture2D.whiteTexture;
        background.width = Math.Max(40, (int)(surface.width * 0.78f));
        background.height = Math.Max(20, (int)(surface.height * 0.62f));
        background.depth = surface.depth + 3;
        background.color = caption == "Voltar" ? new Color(0.08f,0.62f,0.65f,1f) : new Color(0.91f,0.47f,0.055f,1f);
        GameObject text = new GameObject("PTBR text");
        text.transform.SetParent(panel.transform, false);
        UILabel label = text.AddComponent<UILabel>();
        label.trueTypeFont = font;
        label.fontSize = 28;
        label.width = background.width - 8;
        label.height = background.height;
        label.maxLineCount = 1;
        label.overflowMethod = UILabel.Overflow.ShrinkContent;
        label.depth = background.depth + 1;
        label.color = new Color(1f,0.97f,0.8f,1f);
        label.text = caption;
    }
}

// Replacements are children of the original controls, so their actions and
// transforms remain owned by the game. No asset or save files are modified.
public class PTBRGraphicLabel : MonoBehaviour
{
    Image original;
    bool cover;
    public void Setup(Image graphic, Font font, string caption, bool coverCenter)
    {
        original = graphic;
        cover = coverCenter;
        GameObject holder = new GameObject("PTBR caption", typeof(RectTransform));
        holder.transform.SetParent(graphic.transform, false);
        RectTransform rect = holder.GetComponent<RectTransform>();
        rect.anchorMin = cover ? new Vector2(0.14f, 0.2f) : Vector2.zero;
        rect.anchorMax = cover ? new Vector2(0.86f, 0.8f) : Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        if (cover) {
            Image backdrop = holder.AddComponent<Image>();
            backdrop.color = caption == "Voltar" ? new Color(0.08f,0.62f,0.65f,1f) : new Color(0.91f, 0.47f, 0.055f, 1f);
            backdrop.raycastTarget = false;
        }
        GameObject textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(holder.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero; textRect.offsetMax = Vector2.zero;
        Text label = textObject.AddComponent<Text>();
        label.font = font; label.text = caption;
        label.color = cover ? new Color(1f, 0.97f, 0.8f) : new Color(0.30f, 0.39f, 0.16f);
        label.alignment = TextAnchor.MiddleCenter;
        label.fontSize = cover ? 25 : 34;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 10; label.resizeTextMaxSize = label.fontSize;
        label.raycastTarget = false;
    }
    void LateUpdate() { if (original != null && !cover) original.canvasRenderer.SetAlpha(0f); }
}


