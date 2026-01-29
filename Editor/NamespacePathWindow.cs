/*
Copyright (c) 2026 Xavier Arpa López Thomas Peter ('xavierarpa')

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NamespacePath.Editor
{
    /// <summary>
    /// EditorWindow for managing and renaming namespaces based on folder structure.
    /// </summary>
    internal sealed class NamespacePathWindow : EditorWindow
    {
        private const string WINDOW_TITLE = "Namespace Path";
        private const float MIN_WIDTH = 800f;
        private const float MIN_HEIGHT = 500f;

        // Configuration
        private DefaultAsset sourceFolder;
        private DefaultAsset searchFolder;
        private string namespacePrefix = "WonderWilds";
        private string namespaceExclude = "";
        private bool useSourceAsRoot = true;
        private bool removeDuplicates = true;

        // State
        private List<ScriptNamespaceInfo> scriptInfos = new List<ScriptNamespaceInfo>();
        private Vector2 scrollPosition;
        private Vector2 affectedScrollPosition;
        private bool isProcessing;
        private string statusMessage = "";
        private MessageType statusMessageType = MessageType.None;

        // Affected files
        private Dictionary<string, List<AffectedFileInfo>> affectedFilesMap = new Dictionary<string, List<AffectedFileInfo>>();
        private bool showAffectedPanel = false;
        private ScriptNamespaceInfo selectedScriptForAffected = null;

        // Filters
        private bool showOnlyNeedsChange = false;
        private bool showOnlyNoNamespace = false;
        private string searchFilter = "";

        // Selection
        private bool selectAll = false;

        // Styles
        private GUIStyle headerStyle;
        private GUIStyle boxStyle;
        private bool stylesInitialized = false;

        [MenuItem("Tools/Namespace Path")]
        public static void ShowWindow()
        {
            var window = GetWindow<NamespacePathWindow>(WINDOW_TITLE);
            window.minSize = new Vector2(MIN_WIDTH, MIN_HEIGHT);
            window.Show();
        }

        private void InitStyles()
        {
            if (stylesInitialized)
            {
                return;
            }

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(0, 0, 10, 10)
            };

            boxStyle = new GUIStyle("box")
            {
                padding = new RectOffset(10, 10, 10, 10),
                margin = new RectOffset(0, 0, 5, 5)
            };

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
            {
                DrawHeader();
                DrawConfiguration();
                DrawFilters();
                DrawActionButtons();
                DrawStatusMessage();
                
                if (showAffectedPanel)
                {
                    DrawAffectedFilesPanel();
                }
                else
                {
                    DrawScriptList();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Namespace Path Tool", headerStyle);
            EditorGUILayout.HelpBox(
                "This tool analyzes C# scripts and suggests namespaces based on folder structure. " +
                "It also updates 'using' references in other files.",
                MessageType.Info
            );
            EditorGUILayout.Space(5);
        }

        private void DrawConfiguration()
        {
            EditorGUILayout.BeginVertical(boxStyle);
            {
                EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                // Source folder
                EditorGUI.BeginChangeCheck();
                sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    new GUIContent("Source Folder", "Folder containing the scripts to analyze"),
                    sourceFolder,
                    typeof(DefaultAsset),
                    false
                );
                if (EditorGUI.EndChangeCheck())
                {
                    ClearResults();
                }

                // Search folder for references
                EditorGUILayout.Space(3);
                EditorGUILayout.BeginHorizontal();
                {
                    searchFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                        new GUIContent("References Folder", "Folder to search for using references. Defaults to Assets/ if empty"),
                        searchFolder,
                        typeof(DefaultAsset),
                        false
                    );
                }
                EditorGUILayout.EndHorizontal();

                // Prefix
                EditorGUILayout.Space(3);
                EditorGUI.BeginChangeCheck();
                namespacePrefix = EditorGUILayout.TextField(
                    new GUIContent("Namespace Prefix", "Prefix added to all suggested namespaces"),
                    namespacePrefix
                );
                if (EditorGUI.EndChangeCheck() && scriptInfos.Count > 0)
                {
                    RefreshSuggestions();
                }

                // Exclude patterns
                EditorGUILayout.Space(3);
                EditorGUI.BeginChangeCheck();
                namespaceExclude = EditorGUILayout.TextField(
                    new GUIContent("Exclude from Namespace", "Comma-separated parts to remove from the suggested namespace (e.g., 'Runtime,Scripts')"),
                    namespaceExclude
                );
                if (EditorGUI.EndChangeCheck() && scriptInfos.Count > 0)
                {
                    RefreshSuggestions();
                }

                // Use source as root
                EditorGUILayout.Space(3);
                EditorGUI.BeginChangeCheck();
                useSourceAsRoot = EditorGUILayout.Toggle(
                    new GUIContent("Use Source Folder as Root", 
                        "If enabled, namespace is generated from source folder. " +
                        "Otherwise, it's generated from Assets/"),
                    useSourceAsRoot
                );
                if (EditorGUI.EndChangeCheck() && scriptInfos.Count > 0)
                {
                    RefreshSuggestions();
                }

                // Remove duplicates
                EditorGUILayout.Space(3);
                EditorGUI.BeginChangeCheck();
                removeDuplicates = EditorGUILayout.Toggle(
                    new GUIContent("Remove Duplicates", 
                        "If enabled, removes duplicate parts from the namespace (right to left). " +
                        "E.g., 'A.Core.B.Core' becomes 'A.Core.B'"),
                    removeDuplicates
                );
                if (EditorGUI.EndChangeCheck() && scriptInfos.Count > 0)
                {
                    RefreshSuggestions();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawFilters()
        {
            EditorGUILayout.BeginVertical(boxStyle);
            {
                EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);
                
                EditorGUILayout.BeginHorizontal();
                {
                    searchFilter = EditorGUILayout.TextField(
                        new GUIContent("Search", "Filter by file name or namespace"),
                        searchFilter
                    );
                    
                    if (GUILayout.Button("X", GUILayout.Width(25)))
                    {
                        searchFilter = "";
                        GUI.FocusControl(null);
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                {
                    showOnlyNeedsChange = EditorGUILayout.ToggleLeft(
                        "Only show needing change",
                        showOnlyNeedsChange,
                        GUILayout.Width(250)
                    );
                    
                    showOnlyNoNamespace = EditorGUILayout.ToggleLeft(
                        "Only without namespace",
                        showOnlyNoNamespace,
                        GUILayout.Width(150)
                    );
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();
            {
                GUI.enabled = sourceFolder != null && !isProcessing;
                
                if (GUILayout.Button("Scan Scripts", GUILayout.Height(30)))
                {
                    ScanScripts();
                }

                GUI.enabled = scriptInfos.Count > 0 && scriptInfos.Any(s => s.IsSelected) && !isProcessing;
                
                if (GUILayout.Button("View Affected", GUILayout.Height(30)))
                {
                    ScanAffectedFiles();
                }
                
                if (GUILayout.Button("Apply Changes", GUILayout.Height(30)))
                {
                    ApplySelectedChanges();
                }

                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatusMessage()
        {
            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(statusMessage, statusMessageType);
            }
        }

        private void DrawScriptList()
        {
            if (scriptInfos.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(10);
            
            var filteredScripts = GetFilteredScripts();
            
            // Header with statistics
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField(
                    $"Scripts found: {scriptInfos.Count} | Showing: {filteredScripts.Count} | Selected: {scriptInfos.Count(s => s.IsSelected)}",
                    EditorStyles.miniLabel
                );

                // Selection buttons
                if (GUILayout.Button("Select All", EditorStyles.miniButtonLeft, GUILayout.Width(110)))
                {
                    foreach (var script in filteredScripts)
                    {
                        script.IsSelected = true;
                    }
                }
                
                if (GUILayout.Button("Deselect", EditorStyles.miniButtonRight, GUILayout.Width(100)))
                {
                    foreach (var script in filteredScripts)
                    {
                        script.IsSelected = false;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // Script list
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            {
                // Headers
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                {
                    EditorGUILayout.LabelField("", GUILayout.Width(20));
                    EditorGUILayout.LabelField("File", GUILayout.MinWidth(200));
                    EditorGUILayout.LabelField("Current Namespace", GUILayout.MinWidth(200));
                    EditorGUILayout.LabelField("Suggested Namespace", GUILayout.MinWidth(200));
                    EditorGUILayout.LabelField("Status", GUILayout.Width(80));
                }
                EditorGUILayout.EndHorizontal();

                foreach (var script in filteredScripts)
                {
                    DrawScriptRow(script);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawScriptRow(ScriptNamespaceInfo script)
        {
            Color originalBgColor = GUI.backgroundColor;
            
            if (script.HasTypeNameConflict)
            {
                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f); // Stronger red for conflict
            }
            else if (script.NeedsChange)
            {
                GUI.backgroundColor = new Color(1f, 0.9f, 0.7f); // Soft yellow
            }
            else if (script.HasNoNamespace)
            {
                GUI.backgroundColor = new Color(1f, 0.7f, 0.7f); // Soft red
            }

            EditorGUILayout.BeginVertical();
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                {
                    // Checkbox
                    script.IsSelected = EditorGUILayout.Toggle(script.IsSelected, GUILayout.Width(20));

                    // File (clickable)
                    if (GUILayout.Button(script.RelativePath, EditorStyles.linkLabel, GUILayout.MinWidth(200)))
                    {
                        PingScript(script.FilePath);
                    }

                    // Current namespace
                    EditorGUILayout.LabelField(
                        string.IsNullOrEmpty(script.CurrentNamespace) ? "(no namespace)" : script.CurrentNamespace,
                        GUILayout.MinWidth(200)
                    );

                    // Suggested namespace (editable)
                    EditorGUI.BeginChangeCheck();
                    script.SuggestedNamespace = EditorGUILayout.TextField(
                        script.SuggestedNamespace,
                        GUILayout.MinWidth(200)
                    );
                    if (EditorGUI.EndChangeCheck())
                    {
                        // Re-check conflict when namespace changes
                        NamespaceScanner.UpdateConflictCheck(script);
                    }

                    // Status
                    string status;
                    if (script.HasTypeNameConflict)
                    {
                        status = "⚠️ Conflict";
                    }
                    else if (script.HasNoNamespace)
                    {
                        status = "No NS";
                    }
                    else if (script.NeedsChange)
                    {
                        status = "Change";
                    }
                    else
                    {
                        status = "OK";
                    }
                    EditorGUILayout.LabelField(status, GUILayout.Width(80));
                }
                EditorGUILayout.EndHorizontal();
                
                // Show warning if there's a conflict
                if (script.HasTypeNameConflict && !string.IsNullOrEmpty(script.ConflictWarning))
                {
                    EditorGUILayout.HelpBox(script.ConflictWarning, MessageType.Warning);
                }
            }
            EditorGUILayout.EndVertical();

            GUI.backgroundColor = originalBgColor;
        }

        private List<ScriptNamespaceInfo> GetFilteredScripts()
        {
            var filtered = scriptInfos.AsEnumerable();

            if (showOnlyNeedsChange)
            {
                filtered = filtered.Where(s => s.NeedsChange);
            }

            if (showOnlyNoNamespace)
            {
                filtered = filtered.Where(s => s.HasNoNamespace);
            }

            if (!string.IsNullOrEmpty(searchFilter))
            {
                string filter = searchFilter.ToLower();
                filtered = filtered.Where(s => 
                    s.RelativePath.ToLower().Contains(filter) ||
                    s.CurrentNamespace.ToLower().Contains(filter) ||
                    s.SuggestedNamespace.ToLower().Contains(filter)
                );
            }

            return filtered.ToList();
        }

        private void ScanScripts()
        {
            if (sourceFolder == null)
            {
                SetStatus("Select a source folder first.", MessageType.Warning);
                return;
            }

            string sourcePath = AssetDatabase.GetAssetPath(sourceFolder);
            string fullSourcePath = System.IO.Path.GetFullPath(sourcePath);
            
            string rootPath;
            if (useSourceAsRoot)
            {
                rootPath = fullSourcePath;
            }
            else
            {
                rootPath = System.IO.Path.GetFullPath("Assets");
            }

            isProcessing = true;
            SetStatus("Scanning scripts...", MessageType.Info);

            try
            {
                string[] excludePatterns = ParseExcludePatterns(namespaceExclude);
                scriptInfos = NamespaceScanner.ScanScripts(fullSourcePath, namespacePrefix, rootPath, excludePatterns, removeDuplicates);
                
                int needsChange = scriptInfos.Count(s => s.NeedsChange);
                int noNamespace = scriptInfos.Count(s => s.HasNoNamespace);
                
                SetStatus(
                    $"Scan complete. {scriptInfos.Count} scripts found. " +
                    $"{needsChange} need change, {noNamespace} without namespace.",
                    MessageType.Info
                );
            }
            catch (System.Exception ex)
            {
                SetStatus($"Error during scan: {ex.Message}", MessageType.Error);
                Debug.LogException(ex);
            }
            finally
            {
                isProcessing = false;
            }
        }

        private void RefreshSuggestions()
        {
            if (sourceFolder == null)
            {
                return;
            }

            string sourcePath = AssetDatabase.GetAssetPath(sourceFolder);
            string fullSourcePath = System.IO.Path.GetFullPath(sourcePath);
            
            string rootPath;
            if (useSourceAsRoot)
            {
                rootPath = fullSourcePath;
            }
            else
            {
                rootPath = System.IO.Path.GetFullPath("Assets");
            }

            string[] excludePatterns = ParseExcludePatterns(namespaceExclude);
            foreach (var script in scriptInfos)
            {
                script.SuggestedNamespace = NamespaceScanner.GenerateSuggestedNamespace(
                    script.FilePath,
                    rootPath,
                    namespacePrefix,
                    excludePatterns,
                    removeDuplicates
                );
            }

            Repaint();
        }

        private void ApplySelectedChanges()
        {
            var selectedScripts = scriptInfos.Where(s => s.IsSelected).ToList();
            
            if (selectedScripts.Count == 0)
            {
                SetStatus("No scripts selected.", MessageType.Warning);
                return;
            }

            if (!EditorUtility.DisplayDialog(
                "Confirm Changes",
                $"{selectedScripts.Count} files will be modified and references updated.\n\n" +
                "Do you want to continue?",
                "Yes, apply changes",
                "Cancel"))
            {
                return;
            }

            isProcessing = true;
            
            // Determine search folder
            string searchPath;
            if (searchFolder != null)
            {
                searchPath = System.IO.Path.GetFullPath(AssetDatabase.GetAssetPath(searchFolder));
            }
            else
            {
                searchPath = System.IO.Path.GetFullPath("Assets");
            }

            try
            {
                var results = NamespaceRefactorer.BatchRename(
                    selectedScripts,
                    searchPath,
                    (progress, message) =>
                    {
                        EditorUtility.DisplayProgressBar("Applying changes", message, progress);
                    }
                );

                EditorUtility.ClearProgressBar();

                int successCount = results.Count(r => r.Success);
                int totalReferences = results.Sum(r => r.ReferencesUpdated);

                SetStatus(
                    $"Complete. {successCount}/{results.Count} files modified. " +
                    $"{totalReferences} references updated.",
                    successCount == results.Count ? MessageType.Info : MessageType.Warning
                );

                // Refresh AssetDatabase
                AssetDatabase.Refresh();

                // Re-scan to update the view
                ScanScripts();
            }
            catch (System.Exception ex)
            {
                EditorUtility.ClearProgressBar();
                SetStatus($"Error applying changes: {ex.Message}", MessageType.Error);
                Debug.LogException(ex);
            }
            finally
            {
                isProcessing = false;
            }
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusMessageType = type;
            Repaint();
        }

        private void ClearResults()
        {
            scriptInfos.Clear();
            affectedFilesMap.Clear();
            showAffectedPanel = false;
            statusMessage = "";
            Repaint();
        }

        private string[] ParseExcludePatterns(string excludeInput)
        {
            if (string.IsNullOrWhiteSpace(excludeInput))
            {
                return System.Array.Empty<string>();
            }

            return excludeInput
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
        }

        private void ScanAffectedFiles()
        {
            var selectedScripts = scriptInfos.Where(s => s.IsSelected).ToList();
            
            if (selectedScripts.Count == 0)
            {
                SetStatus("No scripts selected.", MessageType.Warning);
                return;
            }

            isProcessing = true;
            affectedFilesMap.Clear();

            // Determine search folder
            string searchPath;
            if (searchFolder != null)
            {
                searchPath = System.IO.Path.GetFullPath(AssetDatabase.GetAssetPath(searchFolder));
            }
            else
            {
                searchPath = System.IO.Path.GetFullPath("Assets");
            }

            try
            {
                affectedFilesMap = NamespaceRefactorer.ScanAffectedFilesForMultiple(
                    selectedScripts,
                    searchPath,
                    (progress, message) =>
                    {
                        EditorUtility.DisplayProgressBar("Scanning affected files", message, progress);
                    }
                );

                EditorUtility.ClearProgressBar();

                int totalAffected = affectedFilesMap.Values.Sum(list => list.Count);
                int scriptsWithAffected = affectedFilesMap.Count;

                if (totalAffected > 0)
                {
                    showAffectedPanel = true;
                    SetStatus(
                        $"Found {totalAffected} files that will be affected by {scriptsWithAffected} selected scripts.",
                        MessageType.Warning
                    );
                }
                else
                {
                    SetStatus(
                        "No files will be affected by the changes.",
                        MessageType.Info
                    );
                }
            }
            catch (System.Exception ex)
            {
                EditorUtility.ClearProgressBar();
                SetStatus($"Error during scan: {ex.Message}", MessageType.Error);
                Debug.LogException(ex);
            }
            finally
            {
                isProcessing = false;
            }
        }

        private void DrawAffectedFilesPanel()
        {
            EditorGUILayout.Space(10);
            
            EditorGUILayout.BeginVertical(boxStyle);
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("Affected Files", EditorStyles.boldLabel);
                    
                    if (GUILayout.Button("← Back to Scripts", GUILayout.Width(130)))
                    {
                        showAffectedPanel = false;
                    }
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.HelpBox(
                    "These files contain references to the namespaces that will be changed. " +
                    "They will be updated automatically when you apply the changes.",
                    MessageType.Info
                );
            }
            EditorGUILayout.EndVertical();

            affectedScrollPosition = EditorGUILayout.BeginScrollView(affectedScrollPosition);
            {
                foreach (var kvp in affectedFilesMap)
                {
                    string sourceFilePath = kvp.Key;
                    var affectedFiles = kvp.Value;
                    
                    // Find the corresponding script
                    var sourceScript = scriptInfos.FirstOrDefault(s => s.FilePath == sourceFilePath);
                    if (sourceScript == null)
                    {
                        continue;
                    }

                    // Source script header
                    EditorGUILayout.BeginVertical(boxStyle);
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.LabelField("📄", GUILayout.Width(20));
                            if (GUILayout.Button(sourceScript.RelativePath, EditorStyles.linkLabel))
                            {
                                PingScript(sourceScript.FilePath);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                        
                        EditorGUILayout.LabelField(
                            $"  {sourceScript.CurrentNamespace} → {sourceScript.SuggestedNamespace}",
                            EditorStyles.miniLabel
                        );
                        
                        EditorGUILayout.Space(5);
                        EditorGUILayout.LabelField($"Affected files ({affectedFiles.Count}):", EditorStyles.boldLabel);

                        // Affected files list
                        EditorGUI.indentLevel++;
                        foreach (var affected in affectedFiles)
                        {
                            DrawAffectedFileRow(affected);
                        }
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.EndVertical();
                    
                    EditorGUILayout.Space(5);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawAffectedFileRow(AffectedFileInfo affected)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            {
                // File (clickable)
                if (GUILayout.Button(affected.RelativePath, EditorStyles.linkLabel, GUILayout.MinWidth(300)))
                {
                    PingScript(affected.FilePath);
                }

                // References found
                EditorGUILayout.LabelField(
                    $"References: {affected.DisplayReferences}",
                    EditorStyles.miniLabel
                );
            }
            EditorGUILayout.EndHorizontal();
        }

        private void PingScript(string filePath)
        {
            string assetPath = filePath.Replace("\\", "/");
            string assetsFolder = Application.dataPath.Replace("\\", "/");
            
            if (assetPath.StartsWith(assetsFolder))
            {
                assetPath = "Assets" + assetPath.Substring(assetsFolder.Length);
            }

            var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }
        }
    }
}
