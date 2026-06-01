using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class EventSubscriptionAnalyzer : EditorWindow
{
    private List<Issue> _issues = new();
    private List<Issue> _ignoredIssues = new();
    private List<string> _previewFiles = new();

    private Vector2 _scroll;
    private Vector2 _settingsScroll;
    private int _selectedTab;
    private bool _showPreview;

    // Settings fields
    private string _customScanPath = "";
    private List<FolderToggle> _assetFolders = new();

    private static readonly HashSet<string> LifecycleMethods = new()
    {
        "Awake",
        "Start",
        "OnEnable",
        "OnDisable",
        "OnDestroy"
    };

    private const string IgnorePrefsKey = "EventSubscriptionAnalyzer_Ignored";
    private const string PathPrefsKey = "EventSubscriptionAnalyzer_CustomPath";
    private const string FoldersPrefsKey = "EventSubscriptionAnalyzer_SelectedFolders";

    [Serializable]
    private class FolderToggle
    {
        public string FolderName;
        public bool IsSelected;
    }

    [MenuItem("Tools/Event Subscription Analyzer")]
    private static void Open()
    {
        GetWindow<EventSubscriptionAnalyzer>("Event Analyzer");
    }

    private void OnEnable()
    {
        LoadIgnoredIssues();
        LoadSettings();
        InitializeAssetFolders();
    }

    private void OnGUI()
    {
        GUILayout.Space(10);

        if (GUILayout.Button("Analyze Project", GUILayout.Height(40)))
        {
            AnalyzeProject();
        }

        GUILayout.Space(10);

        _selectedTab = GUILayout.Toolbar(
            _selectedTab,
            new[]
            {
                $"Issues ({_issues.Count})",
                $"Ignored ({_ignoredIssues.Count})",
                "Settings"
            });

        GUILayout.Space(10);

        if (_selectedTab == 2)
        {
            DrawSettingsTab();
        }
        else
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_selectedTab == 0)
            {
                foreach (var issue in _issues.ToArray())
                {
                    DrawIssue(issue);
                }
            }
            else
            {
                foreach (var issue in _ignoredIssues.ToArray())
                {
                    DrawIgnoredIssue(issue);
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawSettingsTab()
    {
        _settingsScroll = EditorGUILayout.BeginScrollView(_settingsScroll);

        GUILayout.Label("Scan Path Settings", EditorStyles.boldLabel);

        // Custom Path Block
        EditorGUILayout.BeginHorizontal();
        _customScanPath = EditorGUILayout.TextField("Custom Scan Path", _customScanPath);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string selectedPath = EditorUtility.OpenFolderPanel("Select Scan Folder", Application.dataPath, "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                // Convert to relative path if it's inside Assets for cleaner UX
                if (selectedPath.StartsWith(Application.dataPath))
                {
                    selectedPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);
                }
                _customScanPath = selectedPath;
                SaveSettings();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("If a Custom Scan Path is provided, it will take priority. Leave it empty to use the checkmark filtering options below.", MessageType.Info);

        GUILayout.Space(15);
        GUILayout.Label("Filter Assets Subfolders", EditorStyles.boldLabel);

        if (GUILayout.Button("Refresh Root Folders", GUILayout.Width(150)))
        {
            InitializeAssetFolders();
        }

        GUILayout.Space(5);

        // Checkmark checkboxes loop
        for (int i = 0; i < _assetFolders.Count; i++)
        {
            EditorGUI.BeginChangeCheck();
            _assetFolders[i].IsSelected = EditorGUILayout.ToggleLeft($" Assets/{_assetFolders[i].FolderName}", _assetFolders[i].IsSelected);
            if (EditorGUI.EndChangeCheck())
            {
                SaveSettings();
            }
        }

        GUILayout.Space(20);
        _showPreview = EditorGUILayout.Foldout(_showPreview, "Pre-Scan Lookahead Tools", true);
        if (_showPreview)
        {
            EditorGUILayout.BeginVertical("box");
            if (GUILayout.Button("View Scripts in Selection", GUILayout.Height(25)))
            {
                _previewFiles = GetProjectFiles();
            }

            if (_previewFiles.Count > 0)
            {
                GUILayout.Space(5);
                GUILayout.Label($"Found {_previewFiles.Count} target script components matching filters:", EditorStyles.miniBoldLabel);
                foreach (var file in _previewFiles)
                {
                    EditorGUILayout.LabelField($"• {Path.GetFileName(file)}", EditorStyles.miniLabel);
                }
            }
            else
            {
                GUILayout.Label("No pre-scan run or no scripts discovered in selected configurations.", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawIssue(Issue issue)
    {
        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.LabelField(issue.Type.ToString(), EditorStyles.boldLabel);
        EditorGUILayout.LabelField("File", issue.FileName);
        EditorGUILayout.LabelField("Event", issue.EventName);
        EditorGUILayout.LabelField("Handler", issue.HandlerName);

        if (!string.IsNullOrEmpty(issue.Message))
        {
            EditorGUILayout.HelpBox(issue.Message, MessageType.Warning);
        }

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Open Script"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(issue.AssetPath);
            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset);
            }
        }

        if (GUILayout.Button("Ignore"))
        {
            _ignoredIssues.Add(issue);
            _issues.Remove(issue);
            SaveIgnoredIssues();
            GUIUtility.ExitGUI();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawIgnoredIssue(Issue issue)
    {
        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.LabelField(issue.Type.ToString(), EditorStyles.boldLabel);
        EditorGUILayout.LabelField("File", issue.FileName);
        EditorGUILayout.LabelField("Event", issue.EventName);
        EditorGUILayout.LabelField("Handler", issue.HandlerName);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Restore"))
        {
            _ignoredIssues.Remove(issue);
            SaveIgnoredIssues();
            GUIUtility.ExitGUI();
        }

        if (GUILayout.Button("Open Script"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(issue.AssetPath);
            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset);
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private List<string> GetProjectFiles()
    {
        List<string> files = new();

        // 1. Priority Route: If custom path has value
        if (!string.IsNullOrEmpty(_customScanPath))
        {
            string systemPath = _customScanPath;
            if (_customScanPath.StartsWith("Assets"))
            {
                systemPath = Path.Combine(Application.dataPath, _customScanPath.Substring(6).TrimStart('/', '\\'));
            }

            if (Directory.Exists(systemPath))
            {
                files.AddRange(Directory.GetFiles(systemPath, "*.cs", SearchOption.AllDirectories));
                return files;
            }
        }

        // 2. Fallback Route: Iterate via checkbox-selected subdirectories
        foreach (var folderData in _assetFolders)
        {
            if (!folderData.IsSelected) continue;

            string path = Path.Combine(Application.dataPath, folderData.FolderName);
            if (!Directory.Exists(path)) continue;

            files.AddRange(Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories));
        }

        // 3. Absolute catch-all: If custom path is empty and no folders are selected, fall back safely to processing root assets
        if (files.Count == 0 && _assetFolders.Count(f => f.IsSelected) == 0)
        {
            files.AddRange(Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories));
        }

        return files;
    }

    private void InitializeAssetFolders()
    {
        if (!Directory.Exists(Application.dataPath)) return;

        var directories = Directory.GetDirectories(Application.dataPath, "*", SearchOption.TopDirectoryOnly);
        List<FolderToggle> refreshedList = new();

        foreach (var dir in directories)
        {
            string folderName = Path.GetFileName(dir);
            var existing = _assetFolders.FirstOrDefault(f => f.FolderName == folderName);

            refreshedList.Add(new FolderToggle
            {
                FolderName = folderName,
                IsSelected = existing?.IsSelected ?? false
            });
        }

        _assetFolders = refreshedList;
    }

    private void AnalyzeProject()
    {
        _issues.Clear();
        var files = GetProjectFiles();

        foreach (string file in files)
        {
            AnalyzeFile(file);
        }

        RemoveIgnoredIssues();
        Debug.Log($"Event Analyzer Complete. Found {_issues.Count} issues.");
        _selectedTab = 0; // Return to standard display tab automatically when execution processes
    }

    private void RemoveIgnoredIssues()
    {
        _issues.RemoveAll(issue =>
        {
            foreach (var ignored in _ignoredIssues)
            {
                if (ignored.FileName == issue.FileName &&
                    ignored.EventName == issue.EventName &&
                    ignored.HandlerName == issue.HandlerName &&
                    ignored.Type == issue.Type)
                {
                    return true;
                }
            }
            return false;
        });
    }

    private void AnalyzeFile(string filePath)
    {
        string source = File.ReadAllText(filePath);
        var methods = ExtractAllMethods(source);
        List<EventOperation> allSubscriptions = new();

        foreach (var method in methods)
        {
            allSubscriptions.AddRange(FindEventOperations(method.Value, true, method.Key));
        }

        string assetPath = "Assets" + filePath.Replace(Application.dataPath, "").Replace("\\", "/");

        var onEnableBody = ExtractMethodBody(source, "OnEnable");
        var onDisableBody = ExtractMethodBody(source, "OnDisable");

        if (string.IsNullOrEmpty(onEnableBody) && string.IsNullOrEmpty(onDisableBody))
        {
            return;
        }

        var enableSubs = FindEventOperations(onEnableBody, true, "OnEnable");
        var enableUnsubs = FindEventOperations(onEnableBody, false, "OnEnable");
        var disableSubs = FindEventOperations(onDisableBody, true, "OnDisable");
        var disableUnsubs = FindEventOperations(onDisableBody, false, "OnDisable");

        AnalyzeSubscriptions(Path.GetFileName(filePath), assetPath, enableSubs, disableUnsubs);
        DetectDoubleSubscriptions(Path.GetFileName(filePath), assetPath, enableSubs);
        DetectDoubleSubscriptions(Path.GetFileName(filePath), assetPath, disableSubs);
        DetectDoubleUnsubscriptions(Path.GetFileName(filePath), assetPath, enableUnsubs);
        DetectDoubleUnsubscriptions(Path.GetFileName(filePath), assetPath, disableUnsubs);

        DetectConflictingOperations(Path.GetFileName(filePath), assetPath, enableSubs, enableUnsubs, "OnEnable");
        DetectConflictingOperations(Path.GetFileName(filePath), assetPath, disableSubs, disableUnsubs, "OnDisable");
        DetectExternalSubscriptions(Path.GetFileName(filePath), assetPath, allSubscriptions);
        DetectMultipleMethodSubscriptions(Path.GetFileName(filePath), assetPath, allSubscriptions);
    }

    private void AnalyzeSubscriptions(string fileName, string assetPath, List<EventOperation> subscriptions, List<EventOperation> unsubscriptions)
    {
        foreach (var sub in subscriptions)
        {
            bool found = false;
            foreach (var unsub in unsubscriptions)
            {
                if (sub.EventName == unsub.EventName && sub.HandlerName == unsub.HandlerName)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                _issues.Add(new Issue
                {
                    Type = IssueType.MissingUnsubscribe,
                    FileName = fileName,
                    AssetPath = assetPath,
                    EventName = sub.EventName,
                    HandlerName = sub.HandlerName,
                    Message = "Subscribed in OnEnable but not unsubscribed in OnDisable."
                });
            }
        }

        foreach (var unsub in unsubscriptions)
        {
            bool found = false;
            foreach (var sub in subscriptions)
            {
                if (sub.EventName == unsub.EventName && sub.HandlerName == unsub.HandlerName)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                _issues.Add(new Issue
                {
                    Type = IssueType.UnsubscribeWithoutSubscribe,
                    FileName = fileName,
                    AssetPath = assetPath,
                    EventName = unsub.EventName,
                    HandlerName = unsub.HandlerName,
                    Message = "Unsubscribed in OnDisable but never subscribed in OnEnable."
                });
            }
        }
    }

    private void DetectDoubleSubscriptions(string fileName, string assetPath, List<EventOperation> operations)
    {
        var counts = new Dictionary<string, int>();
        foreach (var op in operations)
        {
            string key = op.EventName + "|" + op.HandlerName;
            counts.TryAdd(key, 0);
            counts[key]++;
        }

        foreach (var pair in counts)
        {
            if (pair.Value > 1)
            {
                string[] split = pair.Key.Split('|');
                _issues.Add(new Issue
                {
                    Type = IssueType.DoubleSubscribe,
                    FileName = fileName,
                    AssetPath = assetPath,
                    EventName = split[0],
                    HandlerName = split[1],
                    Message = "Multiple subscriptions detected."
                });
            }
        }
    }

    private void DetectDoubleUnsubscriptions(string fileName, string assetPath, List<EventOperation> operations)
    {
        var counts = new Dictionary<string, int>();
        foreach (var op in operations)
        {
            string key = op.EventName + "|" + op.HandlerName;
            counts.TryAdd(key, 0);
            counts[key]++;
        }

        foreach (var pair in counts)
        {
            if (pair.Value > 1)
            {
                string[] split = pair.Key.Split('|');
                _issues.Add(new Issue
                {
                    Type = IssueType.DoubleUnsubscribe,
                    FileName = fileName,
                    AssetPath = assetPath,
                    EventName = split[0],
                    HandlerName = split[1],
                    Message = "Multiple unsubscriptions detected."
                });
            }
        }
    }

    private void DetectConflictingOperations(string fileName, string assetPath, List<EventOperation> subscriptions, List<EventOperation> unsubscriptions, string methodName)
    {
        foreach (var sub in subscriptions)
        {
            foreach (var unsub in unsubscriptions)
            {
                if (sub.EventName == unsub.EventName && sub.HandlerName == unsub.HandlerName)
                {
                    _issues.Add(new Issue
                    {
                        Type = IssueType.SubscribeAndUnsubscribeSameMethod,
                        FileName = fileName,
                        AssetPath = assetPath,
                        EventName = sub.EventName,
                        HandlerName = sub.HandlerName,
                        Message = $"Subscribed and unsubscribed in {methodName}."
                    });
                }
            }
        }
    }

    private void DetectExternalSubscriptions(string fileName, string assetPath, List<EventOperation> allSubscriptions)
    {
        foreach (var op in allSubscriptions)
        {
            if (!LifecycleMethods.Contains(op.MethodName))
            {
                _issues.Add(new Issue
                {
                    Type = IssueType.SubscriptionOutsideLifecycle,
                    FileName = fileName,
                    AssetPath = assetPath,
                    EventName = op.EventName,
                    HandlerName = op.HandlerName,
                    Message = $"Subscription found in '{op.MethodName}'. Manual verification recommended."
                });
            }
        }
    }

    private void DetectMultipleMethodSubscriptions(string fileName, string assetPath, List<EventOperation> allSubscriptions)
    {
        Dictionary<string, HashSet<string>> map = new();
        foreach (var op in allSubscriptions)
        {
            string key = $"{op.EventName}|{op.HandlerName}";
            if (!map.ContainsKey(key))
            {
                map[key] = new HashSet<string>();
            }
            map[key].Add(op.MethodName);
        }

        foreach (var pair in map)
        {
            if (pair.Value.Count <= 1) continue;

            string[] split = pair.Key.Split('|');
            _issues.Add(new Issue
            {
                Type = IssueType.SubscriptionInMultipleMethods,
                FileName = fileName,
                AssetPath = assetPath,
                EventName = split[0],
                HandlerName = split[1],
                Message = "Subscription found in multiple methods:\n" + string.Join(", ", pair.Value)
            });
        }
    }

    private List<EventOperation> FindEventOperations(string methodBody, bool subscribe, string methodName)
    {
        List<EventOperation> list = new();
        if (string.IsNullOrEmpty(methodBody)) return list;

        string op = subscribe ? @"\+=" : @"\-=";
        Regex regex = new($@"([\w\.]+)\s*{op}\s*([\w]+)");
        MatchCollection matches = regex.Matches(methodBody);

        foreach (Match match in matches)
        {
            string eventName = match.Groups[1].Value;
            string handlerName = match.Groups[2].Value;
            string fullMatch = match.Value;

            if (fullMatch.Contains("ToString") || fullMatch.Contains("Append") || fullMatch.Contains("Concat"))
                continue;

            if (!char.IsLetter(handlerName[0]) || !handlerName.Any(char.IsLetter) || char.IsLower(handlerName[0]))
                continue;

            if (int.TryParse(handlerName, out _) || float.TryParse(handlerName, out _))
                continue;

            list.Add(new EventOperation
            {
                EventName = eventName,
                HandlerName = handlerName,
                MethodName = methodName
            });
        }

        return list;
    }

    private string ExtractMethodBody(string source, string methodName)
    {
        int methodIndex = source.IndexOf($"void {methodName}", StringComparison.Ordinal);
        if (methodIndex < 0) return string.Empty;

        int openBrace = source.IndexOf('{', methodIndex);
        if (openBrace < 0) return string.Empty;

        int depth = 1;
        for (int i = openBrace + 1; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            if (source[i] == '}') depth--;

            if (depth == 0)
            {
                return source.Substring(openBrace + 1, i - openBrace - 1);
            }
        }

        return string.Empty;
    }

    private void SaveSettings()
    {
        EditorPrefs.SetString(PathPrefsKey, _customScanPath);

        // Serialize selected checkmarks to string array format representation
        var selectedFolders = _assetFolders.Where(f => f.IsSelected).Select(f => f.FolderName).ToArray();
        string json = JsonUtility.ToJson(new StringArrayWrapper { Items = selectedFolders });
        EditorPrefs.SetString(FoldersPrefsKey, json);
    }

    private void LoadSettings()
    {
        _customScanPath = EditorPrefs.GetString(PathPrefsKey, "");

        if (EditorPrefs.HasKey(FoldersPrefsKey))
        {
            string json = EditorPrefs.GetString(FoldersPrefsKey);
            var wrapper = JsonUtility.FromJson<StringArrayWrapper>(json);
            if (wrapper?.Items != null)
            {
                _assetFolders.Clear();
                foreach (var item in wrapper.Items)
                {
                    _assetFolders.Add(new FolderToggle { FolderName = item, IsSelected = true });
                }
            }
        }
    }

    private void SaveIgnoredIssues()
    {
        var wrapper = new IgnoreWrapper { Issues = _ignoredIssues };
        string json = JsonUtility.ToJson(wrapper);
        EditorPrefs.SetString(IgnorePrefsKey, json);
    }

    private void LoadIgnoredIssues()
    {
        if (!EditorPrefs.HasKey(IgnorePrefsKey)) return;

        string json = EditorPrefs.GetString(IgnorePrefsKey);
        var wrapper = JsonUtility.FromJson<IgnoreWrapper>(json);

        if (wrapper?.Issues != null)
        {
            _ignoredIssues.Clear();
            _ignoredIssues.AddRange(wrapper.Issues);
        }
    }

    private Dictionary<string, string> ExtractAllMethods(string source)
    {
        Dictionary<string, string> methods = new();
        Regex methodRegex = new(@"(?:public|private|protected|internal)?\s*(?:virtual\s+)?(?:override\s+)?(?:IEnumerator|void|bool|int|float|string|\w+)\s+(\w+)\s*\([^)]*\)\s*\{");
        MatchCollection matches = methodRegex.Matches(source);

        foreach (Match match in matches)
        {
            string methodName = match.Groups[1].Value;
            int openBrace = source.IndexOf('{', match.Index);
            int depth = 1;

            for (int i = openBrace + 1; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                if (source[i] == '}') depth--;

                if (depth == 0)
                {
                    methods[methodName] = source.Substring(openBrace + 1, i - openBrace - 1);
                    break;
                }
            }
        }

        return methods;
    }

    [Serializable]
    public class IgnoreWrapper
    {
        public List<Issue> Issues = new();
    }

    [Serializable]
    private class StringArrayWrapper
    {
        public string[] Items;
    }

    [Serializable]
    public class EventOperation
    {
        public string EventName;
        public string HandlerName;
        public string MethodName;
    }

    [Serializable]
    public class Issue
    {
        public IssueType Type;
        public string FileName;
        public string AssetPath;
        public string EventName;
        public string HandlerName;
        public string Message;
    }

    public enum IssueType
    {
        MissingUnsubscribe,
        UnsubscribeWithoutSubscribe,
        DoubleSubscribe,
        DoubleUnsubscribe,
        SubscribeAndUnsubscribeSameMethod,
        SubscriptionInMultipleMethods,
        SubscriptionOutsideLifecycle
    }
}