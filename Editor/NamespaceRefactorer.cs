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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace NamespacePath.Editor
{
    /// <summary>
    /// Performs namespace refactoring in C# scripts.
    /// </summary>
    internal static class NamespaceRefactorer
    {
        private static readonly Regex NamespaceDeclarationRegex = new Regex(
            @"(^\s*namespace\s+)([\w\.]+)(\s*\{?\s*$)",
            RegexOptions.Multiline | RegexOptions.Compiled
        );
        
        private static readonly Regex FileScopedNamespaceRegex = new Regex(
            @"(^\s*namespace\s+)([\w\.]+)(\s*;\s*$)",
            RegexOptions.Multiline | RegexOptions.Compiled
        );

        private static readonly Regex UsingStatementRegex = new Regex(
            @"(^\s*using\s+)([\w\.]+)(\s*;\s*$)",
            RegexOptions.Multiline | RegexOptions.Compiled
        );

        /// <summary>
        /// Renames the namespace in a file and updates references in other files.
        /// </summary>
        public static NamespaceRenameResult RenameNamespace(
            ScriptNamespaceInfo scriptInfo,
            string searchFolderPath)
        {
            var result = new NamespaceRenameResult
            {
                OldNamespace = scriptInfo.CurrentNamespace,
                NewNamespace = scriptInfo.SuggestedNamespace,
                FilesModified = 0,
                ReferencesUpdated = 0,
                Success = false
            };

            try
            {
                // Extract types from the file being modified
                var typeNames = ExtractTypeNames(scriptInfo.FilePath);
                
                // 1. Rename the namespace in the source file
                if (!RenameNamespaceInFile(scriptInfo.FilePath, scriptInfo.CurrentNamespace, scriptInfo.SuggestedNamespace))
                {
                    result.ErrorMessage = $"Could not modify namespace in {scriptInfo.FilePath}";
                    return result;
                }
                result.FilesModified++;

                // 2. Update references in other files
                // Only update files that actually use types from this script
                if (!string.IsNullOrEmpty(scriptInfo.CurrentNamespace) && typeNames.Count > 0)
                {
                    int referencesUpdated = UpdateReferencesForTypes(
                        searchFolderPath,
                        scriptInfo.CurrentNamespace,
                        scriptInfo.SuggestedNamespace,
                        typeNames,
                        scriptInfo.FilePath
                    );
                    result.ReferencesUpdated = referencesUpdated;
                }

                result.Success = true;
            }
            catch (System.Exception ex)
            {
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[NamespacePath] Error durante el refactoring: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Updates references only in files that use the specific types.
        /// </summary>
        private static int UpdateReferencesForTypes(
            string searchFolderPath,
            string oldNamespace,
            string newNamespace,
            List<string> typeNames,
            string excludeFilePath)
        {
            int updatedCount = 0;

            if (!Directory.Exists(searchFolderPath))
            {
                return 0;
            }

            string[] csFiles = Directory.GetFiles(searchFolderPath, "*.cs", SearchOption.AllDirectories);

            foreach (string filePath in csFiles)
            {
                // Saltar el archivo que acabamos de modificar
                if (Path.GetFullPath(filePath) == Path.GetFullPath(excludeFilePath))
                {
                    continue;
                }

                if (UpdateFileReferencesForTypes(filePath, oldNamespace, newNamespace, typeNames))
                {
                    updatedCount++;
                }
            }

            return updatedCount;
        }

        /// <summary>
        /// Updates references in a specific file only if it uses the given types.
        /// - Adds using for new namespace if needed
        /// - Updates fully-qualified references
        /// - Does NOT remove old using (may be used by other types)
        /// </summary>
        private static bool UpdateFileReferencesForTypes(
            string filePath, 
            string oldNamespace, 
            string newNamespace,
            List<string> typeNames)
        {
            try
            {
                string content = File.ReadAllText(filePath);
                var lines = content.Split('\n').ToList();
                bool modified = false;
                bool usesTypes = false;
                bool hasOldUsing = false;
                bool hasNewUsing = false;
                int lastUsingIndex = -1;
                int oldUsingIndex = -1;

                // First pass: detect what the file contains
                for (int i = 0; i < lines.Count; i++)
                {
                    string trimmedLine = lines[i].TrimStart();
                    
                    if (trimmedLine.StartsWith("using "))
                    {
                        lastUsingIndex = i;
                        
                        // Verify exact using for old namespace
                        var oldUsingMatch = Regex.Match(trimmedLine, $@"^using\s+{Regex.Escape(oldNamespace)}\s*;");
                        if (oldUsingMatch.Success)
                        {
                            hasOldUsing = true;
                            oldUsingIndex = i;
                        }
                        
                        // Verify using for new namespace
                        var newUsingMatch = Regex.Match(trimmedLine, $@"^using\s+{Regex.Escape(newNamespace)}\s*;");
                        if (newUsingMatch.Success)
                        {
                            hasNewUsing = true;
                        }
                    }
                }

                // Second pass: check if it uses the types and update FQ references
                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i];
                    string trimmedLine = line.TrimStart();
                    
                    // Ignore namespace and using lines
                    if (trimmedLine.StartsWith("namespace ") || trimmedLine.StartsWith("using "))
                    {
                        continue;
                    }

                    // Search for fully-qualified references and update them
                    foreach (string typeName in typeNames)
                    {
                        var fqRegex = new Regex($@"\b{Regex.Escape(oldNamespace)}\.{Regex.Escape(typeName)}\b");
                        if (fqRegex.IsMatch(line))
                        {
                            lines[i] = fqRegex.Replace(lines[i], $"{newNamespace}.{typeName}");
                            modified = true;
                            usesTypes = true;
                            Debug.Log($"[NamespacePath] Actualizado FQ {oldNamespace}.{typeName} -> {newNamespace}.{typeName} en {filePath}");
                        }
                    }

                    // Search for direct use of types (if it has old namespace using)
                    if (hasOldUsing)
                    {
                        foreach (string typeName in typeNames)
                        {
                            var typeRegex = new Regex($@"\b{Regex.Escape(typeName)}\b");
                            if (typeRegex.IsMatch(line))
                            {
                                usesTypes = true;
                                break;
                            }
                        }
                    }
                }

                // If it uses types and has old using but not new, add new using
                if (usesTypes && hasOldUsing && !hasNewUsing && lastUsingIndex >= 0)
                {
                    // Find correct place to insert (after last using)
                    string newUsingLine = $"using {newNamespace};";
                    lines.Insert(lastUsingIndex + 1, newUsingLine);
                    modified = true;
                    // Adjust old using index if we inserted before
                    if (oldUsingIndex > lastUsingIndex)
                    {
                        oldUsingIndex++;
                    }
                    Debug.Log($"[NamespacePath] Añadido using {newNamespace} en {filePath}");
                }

                if (modified)
                {
                    string newContent = string.Join("\n", lines);
                    
                    // Check if old using is still needed
                    newContent = CleanupUnusedUsing(newContent, oldNamespace);
                    
                    // Remove duplicates
                    newContent = RemoveDuplicateUsings(newContent);
                    
                    File.WriteAllText(filePath, newContent);
                    return true;
                }

                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NamespacePath] Error al actualizar referencias en {filePath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if a using is still used in the file and removes it if not.
        /// </summary>
        private static string CleanupUnusedUsing(string content, string namespaceToCheck)
        {
            var lines = content.Split('\n').ToList();
            int usingLineIndex = -1;
            
            // Find the using line
            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].TrimStart();
                var usingMatch = Regex.Match(trimmed, $@"^using\s+{Regex.Escape(namespaceToCheck)}\s*;");
                if (usingMatch.Success)
                {
                    usingLineIndex = i;
                    break;
                }
            }
            
            if (usingLineIndex == -1)
            {
                // No using for that namespace
                return content;
            }
            
            // Check if namespace is used anywhere in the code
            bool isUsed = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                
                // Ignorar líneas de namespace y using
                if (trimmed.StartsWith("namespace ") || trimmed.StartsWith("using "))
                {
                    continue;
                }
                
                // Buscar cualquier referencia al namespace (fully-qualified)
                var fqRegex = new Regex($@"\b{Regex.Escape(namespaceToCheck)}\.");
                if (fqRegex.IsMatch(line))
                {
                    isUsed = true;
                    break;
                }
            }
            
            // If no fully-qualified reference found, the using might still be needed
            // for types used without qualification. We need to verify this differently.
            // For now, we only remove the using if it's a namespace that no longer exists in the project.
            // This is safer than removing usings that might be needed.
            
            // To be conservative, we do NOT automatically remove the using
            // because we can't know for certain if there are other types in that namespace
            // that the file might be using.
            
            // Only remove if namespace is very specific (many segments)
            // and not a common/root namespace
            int segments = namespaceToCheck.Split('.').Length;
            if (segments >= 4 && !isUsed)
            {
                // Very specific namespace and not used FQ, might be safe to remove
                // But still, be conservative and leave it
                Debug.Log($"[NamespacePath] Using '{namespaceToCheck}' might not be needed in the file, but kept for safety.");
            }
            
            return content;
        }

        /// <summary>
        /// Renames the namespace in a specific file.
        /// </summary>
        private static bool RenameNamespaceInFile(string filePath, string oldNamespace, string newNamespace)
        {
            try
            {
                string content = File.ReadAllText(filePath);
                string newContent = content;

                // Try to replace file-scoped namespace first
                if (FileScopedNamespaceRegex.IsMatch(content))
                {
                    newContent = FileScopedNamespaceRegex.Replace(content, m =>
                    {
                        if (m.Groups[2].Value == oldNamespace)
                        {
                            return $"{m.Groups[1].Value}{newNamespace}{m.Groups[3].Value}";
                        }
                        return m.Value;
                    });
                }
                else
                {
                    // Replace traditional namespace
                    newContent = NamespaceDeclarationRegex.Replace(content, m =>
                    {
                        if (m.Groups[2].Value == oldNamespace)
                        {
                            return $"{m.Groups[1].Value}{newNamespace}{m.Groups[3].Value}";
                        }
                        return m.Value;
                    });
                }

                if (newContent != content)
                {
                    File.WriteAllText(filePath, newContent);
                    return true;
                }

                // If there was no namespace, add one
                if (string.IsNullOrEmpty(oldNamespace))
                {
                    return AddNamespaceToFile(filePath, newNamespace);
                }

                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NamespacePath] Error al modificar {filePath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Adds a namespace to a file that doesn't have one.
        /// </summary>
        private static bool AddNamespaceToFile(string filePath, string newNamespace)
        {
            try
            {
                string content = File.ReadAllText(filePath);
                
                // Find where using statements end
                var lines = content.Split('\n');
                int insertIndex = 0;
                bool foundUsing = false;
                
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmedLine = lines[i].Trim();
                    if (trimmedLine.StartsWith("using ") && trimmedLine.EndsWith(";"))
                    {
                        foundUsing = true;
                        insertIndex = i + 1;
                    }
                    else if (foundUsing && !string.IsNullOrWhiteSpace(trimmedLine))
                    {
                        break;
                    }
                    else if (!foundUsing && !string.IsNullOrWhiteSpace(trimmedLine) && 
                             !trimmedLine.StartsWith("//") && !trimmedLine.StartsWith("/*") &&
                             !trimmedLine.StartsWith("#"))
                    {
                        insertIndex = i;
                        break;
                    }
                }

                // Insert file-scoped namespace
                var linesList = new List<string>(lines);
                linesList.Insert(insertIndex, "");
                linesList.Insert(insertIndex + 1, $"namespace {newNamespace};");
                linesList.Insert(insertIndex + 2, "");

                string newContent = string.Join("\n", linesList);
                File.WriteAllText(filePath, newContent);
                
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NamespacePath] Error al agregar namespace a {filePath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Updates using references in all files in the search folder.
        /// </summary>
        private static int UpdateUsingReferences(
            string searchFolderPath,
            string oldNamespace,
            string newNamespace,
            string excludeFilePath)
        {
            int updatedCount = 0;

            if (!Directory.Exists(searchFolderPath))
            {
                return 0;
            }

            string[] csFiles = Directory.GetFiles(searchFolderPath, "*.cs", SearchOption.AllDirectories);

            foreach (string filePath in csFiles)
            {
                // Saltar el archivo que acabamos de modificar
                if (Path.GetFullPath(filePath) == Path.GetFullPath(excludeFilePath))
                {
                    continue;
                }

                if (UpdateUsingInFile(filePath, oldNamespace, newNamespace))
                {
                    updatedCount++;
                }
            }

            return updatedCount;
        }

        /// <summary>
        /// Updates using references in a specific file.
        /// Does NOT modify namespace declarations in the file.
        /// </summary>
        private static bool UpdateUsingInFile(string filePath, string oldNamespace, string newNamespace)
        {
            try
            {
                string content = File.ReadAllText(filePath);
                var lines = content.Split('\n');
                bool modified = false;
                
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmedLine = line.TrimStart();
                    
                    // IGNORE namespace declaration lines - NEVER modify them
                    if (trimmedLine.StartsWith("namespace "))
                    {
                        continue;
                    }
                    
                    // Only process using lines
                    if (trimmedLine.StartsWith("using "))
                    {
                        // More flexible regex to capture using statements
                        var usingMatch = Regex.Match(line, @"^(\s*using\s+)([\w\.]+)(\s*;.*)$");
                        if (usingMatch.Success)
                        {
                            string usingNamespace = usingMatch.Groups[2].Value;
                            
                            // Exact match of complete namespace
                            if (usingNamespace == oldNamespace)
                            {
                                lines[i] = $"{usingMatch.Groups[1].Value}{newNamespace}{usingMatch.Groups[3].Value}";
                                modified = true;
                                Debug.Log($"[NamespacePath] Updated using in {filePath}: {oldNamespace} -> {newNamespace}");
                            }
                            // Sub-namespace match (e.g.: OldNamespace.SubPart where we change OldNamespace)
                            else if (usingNamespace.StartsWith(oldNamespace + "."))
                            {
                                string subPart = usingNamespace.Substring(oldNamespace.Length);
                                lines[i] = $"{usingMatch.Groups[1].Value}{newNamespace}{subPart}{usingMatch.Groups[3].Value}";
                                modified = true;
                                Debug.Log($"[NamespacePath] Updated using (sub) in {filePath}: {usingNamespace} -> {newNamespace}{subPart}");
                            }
                        }
                    }
                }
                
                // Second pass: search for fully qualified references in code
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmedLine = line.TrimStart();
                    
                    // Ignore namespace and using lines
                    if (trimmedLine.StartsWith("namespace ") || trimmedLine.StartsWith("using "))
                    {
                        continue;
                    }
                    
                    // Buscar referencias fully qualified (ej: OldNamespace.ClassName)
                    // Debe ser palabra completa seguida de punto
                    var fullyQualifiedRegex = new Regex($@"\b{Regex.Escape(oldNamespace)}\.(\w+)");
                    if (fullyQualifiedRegex.IsMatch(line))
                    {
                        string oldLine = lines[i];
                        lines[i] = fullyQualifiedRegex.Replace(line, $"{newNamespace}.$1");
                        if (oldLine != lines[i])
                        {
                            modified = true;
                            Debug.Log($"[NamespacePath] Updated FQ reference in {filePath}");
                        }
                    }
                }

                if (modified)
                {
                    // Remove duplicate usings before saving
                    string newContent = RemoveDuplicateUsings(string.Join("\n", lines));
                    File.WriteAllText(filePath, newContent);
                    return true;
                }

                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NamespacePath] Error updating references in {filePath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Removes duplicate using lines from C# content.
        /// </summary>
        private static string RemoveDuplicateUsings(string content)
        {
            var lines = content.Split('\n');
            var seenUsings = new HashSet<string>();
            var result = new List<string>();
            
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                
                // If it's a using statement
                if (trimmed.StartsWith("using ") && trimmed.EndsWith(";"))
                {
                    // Normalize for comparison (remove extra spaces)
                    string normalized = Regex.Replace(trimmed, @"\s+", " ");
                    
                    if (seenUsings.Contains(normalized))
                    {
                        // Duplicate, don't add
                        Debug.Log($"[NamespacePath] Removed duplicate using: {trimmed}");
                        continue;
                    }
                    
                    seenUsings.Add(normalized);
                }
                
                result.Add(line);
            }
            
            return string.Join("\n", result);
        }

        /// <summary>
        /// Performs batch renaming of multiple scripts.
        /// </summary>
        public static List<NamespaceRenameResult> BatchRename(
            List<ScriptNamespaceInfo> scripts,
            string searchFolderPath,
            System.Action<float, string> progressCallback = null)
        {
            var results = new List<NamespaceRenameResult>();
            int total = scripts.Count;
            int current = 0;

            // Collect all types we're moving and from which namespace
            var movedTypesPerNamespace = new Dictionary<string, HashSet<string>>();
            var modifiedFiles = new HashSet<string>();
            
            foreach (var script in scripts)
            {
                if (!script.IsSelected || string.IsNullOrEmpty(script.SuggestedNamespace))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(script.CurrentNamespace) && 
                    script.CurrentNamespace != script.SuggestedNamespace)
                {
                    // Extract types from script
                    var types = ExtractTypeNames(script.FilePath);
                    
                    if (!movedTypesPerNamespace.ContainsKey(script.CurrentNamespace))
                    {
                        movedTypesPerNamespace[script.CurrentNamespace] = new HashSet<string>();
                    }
                    
                    foreach (var type in types)
                    {
                        movedTypesPerNamespace[script.CurrentNamespace].Add(type);
                    }
                }
            }

            // Process each script
            foreach (var script in scripts)
            {
                current++;
                
                if (!script.IsSelected)
                {
                    continue;
                }

                float progress = (float)current / total * 0.8f; // 80% for rename
                progressCallback?.Invoke(progress, $"Processing: {script.RelativePath}");

                var result = RenameNamespace(script, searchFolderPath);
                results.Add(result);
            }

            // Collect modified files for cleanup
            string[] csFiles = Directory.GetFiles(searchFolderPath, "*.cs", SearchOption.AllDirectories);
            
            // Final pass: clean up unused usings in files that might have them
            progressCallback?.Invoke(0.85f, "Cleaning up unused usings...");
            
            foreach (var kvp in movedTypesPerNamespace)
            {
                string oldNamespace = kvp.Key;
                var movedTypes = kvp.Value;
                
                CleanupUnusedUsingsInFiles(csFiles, oldNamespace, movedTypes.ToList(), searchFolderPath);
            }
            
            progressCallback?.Invoke(1f, "Completed");

            return results;
        }

        /// <summary>
        /// Cleans up usings that are no longer needed after moving types.
        /// </summary>
        private static void CleanupUnusedUsingsInFiles(
            string[] files, 
            string namespaceToCheck, 
            List<string> movedTypes,
            string searchFolderPath)
        {
            foreach (string filePath in files)
            {
                try
                {
                    string content = File.ReadAllText(filePath);
                    
                    // Check if it has using of the namespace
                    var usingRegex = new Regex($@"^\s*using\s+{Regex.Escape(namespaceToCheck)}\s*;", RegexOptions.Multiline);
                    if (!usingRegex.IsMatch(content))
                    {
                        continue; // No using, skip
                    }
                    
                    // Check if file uses any type from that namespace that we did NOT move
                    bool usesOtherTypesFromNamespace = false;
                    var lines = content.Split('\n');
                    
                    // Search for fully-qualified references to namespace (that are not moved types)
                    var fqRegex = new Regex($@"\b{Regex.Escape(namespaceToCheck)}\.(\w+)");
                    foreach (var line in lines)
                    {
                        string trimmed = line.TrimStart();
                        if (trimmed.StartsWith("namespace ") || trimmed.StartsWith("using "))
                        {
                            continue;
                        }
                        
                        var matches = fqRegex.Matches(line);
                        foreach (Match match in matches)
                        {
                            string typeName = match.Groups[1].Value;
                            if (!movedTypes.Contains(typeName))
                            {
                                // Found a type we did NOT move, using is still needed
                                usesOtherTypesFromNamespace = true;
                                break;
                            }
                        }
                        
                        if (usesOtherTypesFromNamespace)
                        {
                            break;
                        }
                    }
                    
                    if (usesOtherTypesFromNamespace)
                    {
                        continue; // Using is still needed
                    }
                    
                    // Search for types used directly (without FQ)
                    // We need to know what other types exist in that namespace
                    // To be safe, search if there's ANY identifier that could be a type from that namespace
                    
                    // Get all types that exist in that namespace by scanning searchFolder
                    var typesInNamespace = GetTypesInNamespace(searchFolderPath, namespaceToCheck);
                    
                    // Remove moved types
                    var remainingTypes = typesInNamespace.Except(movedTypes).ToList();
                    
                    // If no types remain in namespace, we can remove the using
                    if (remainingTypes.Count == 0)
                    {
                        // Remove the using
                        content = RemoveUsing(content, namespaceToCheck);
                        File.WriteAllText(filePath, content);
                        Debug.Log($"[NamespacePath] Removed unnecessary using '{namespaceToCheck}' from {filePath}");
                        continue;
                    }
                    
                    // Check if it uses any of the remaining types
                    bool usesRemainingTypes = false;
                    foreach (var line in lines)
                    {
                        string trimmed = line.TrimStart();
                        if (trimmed.StartsWith("namespace ") || trimmed.StartsWith("using "))
                        {
                            continue;
                        }
                        
                        foreach (string typeName in remainingTypes)
                        {
                            var typeRegex = new Regex($@"\b{Regex.Escape(typeName)}\b");
                            if (typeRegex.IsMatch(line))
                            {
                                usesRemainingTypes = true;
                                break;
                            }
                        }
                        
                        if (usesRemainingTypes)
                        {
                            break;
                        }
                    }
                    
                    if (!usesRemainingTypes)
                    {
                        // Doesn't use any type from namespace, remove using
                        content = RemoveUsing(content, namespaceToCheck);
                        File.WriteAllText(filePath, content);
                        Debug.Log($"[NamespacePath] Removed unnecessary using '{namespaceToCheck}' from {filePath}");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[NamespacePath] Error cleaning usings in {filePath}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets all types defined in a specific namespace.
        /// </summary>
        private static List<string> GetTypesInNamespace(string searchFolder, string targetNamespace)
        {
            var types = new List<string>();
            
            try
            {
                string[] csFiles = Directory.GetFiles(searchFolder, "*.cs", SearchOption.AllDirectories);
                
                foreach (string filePath in csFiles)
                {
                    string content = File.ReadAllText(filePath);
                    
                    // Check if file has this namespace
                    var nsRegex = new Regex($@"^\s*namespace\s+{Regex.Escape(targetNamespace)}\s*[{{\n;]", RegexOptions.Multiline);
                    if (!nsRegex.IsMatch(content))
                    {
                        continue;
                    }
                    
                    // Extract types from file
                    var fileTypes = ExtractTypeNames(filePath);
                    types.AddRange(fileTypes);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[NamespacePath] Error getting types from {targetNamespace}: {ex.Message}");
            }
            
            return types.Distinct().ToList();
        }

        /// <summary>
        /// Removes a specific using from content.
        /// </summary>
        private static string RemoveUsing(string content, string namespaceToRemove)
        {
            var lines = content.Split('\n').ToList();
            
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                string trimmed = lines[i].TrimStart();
                var usingMatch = Regex.Match(trimmed, $@"^using\s+{Regex.Escape(namespaceToRemove)}\s*;");
                if (usingMatch.Success)
                {
                    lines.RemoveAt(i);
                    break; // Only remove one occurrence
                }
            }
            
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Scans and returns files that will be affected by the namespace change.
        /// </summary>
        public static List<AffectedFileInfo> ScanAffectedFiles(
            ScriptNamespaceInfo scriptInfo,
            string searchFolderPath)
        {
            var affectedFiles = new List<AffectedFileInfo>();
            
            if (string.IsNullOrEmpty(scriptInfo.CurrentNamespace) || 
                !Directory.Exists(searchFolderPath))
            {
                return affectedFiles;
            }

            string[] csFiles = Directory.GetFiles(searchFolderPath, "*.cs", SearchOption.AllDirectories);
            string oldNamespace = scriptInfo.CurrentNamespace;

            // Extract type/class names from original file
            var typeNames = ExtractTypeNames(scriptInfo.FilePath);

            foreach (string filePath in csFiles)
            {
                // Skip the file itself
                if (Path.GetFullPath(filePath) == Path.GetFullPath(scriptInfo.FilePath))
                {
                    continue;
                }

                var affectedInfo = CheckFileForReferences(filePath, oldNamespace, typeNames, searchFolderPath);
                if (affectedInfo != null && affectedInfo.AffectedReferences.Count > 0)
                {
                    affectedFiles.Add(affectedInfo);
                }
            }

            return affectedFiles;
        }

        /// <summary>
        /// Scans affected files for multiple selected scripts.
        /// </summary>
        public static Dictionary<string, List<AffectedFileInfo>> ScanAffectedFilesForMultiple(
            List<ScriptNamespaceInfo> scripts,
            string searchFolderPath,
            System.Action<float, string> progressCallback = null)
        {
            var result = new Dictionary<string, List<AffectedFileInfo>>();
            int total = scripts.Count;
            int current = 0;

            foreach (var script in scripts)
            {
                current++;
                float progress = (float)current / total;
                progressCallback?.Invoke(progress, $"Scanning: {script.RelativePath}");

                if (!script.IsSelected || string.IsNullOrEmpty(script.CurrentNamespace))
                {
                    continue;
                }

                var affectedFiles = ScanAffectedFiles(script, searchFolderPath);
                if (affectedFiles.Count > 0)
                {
                    result[script.FilePath] = affectedFiles;
                }
            }

            return result;
        }

        /// <summary>
        /// Extracts class, struct, interface and enum names from a C# file.
        /// </summary>
        private static List<string> ExtractTypeNames(string filePath)
        {
            var typeNames = new List<string>();
            
            try
            {
                string content = File.ReadAllText(filePath);
                
                // Regex to find type declarations
                var typeRegex = new Regex(
                    @"(?:public|internal|private|protected)?\s*(?:static|sealed|abstract|partial)?\s*(?:class|struct|interface|enum)\s+(\w+)",
                    RegexOptions.Compiled
                );

                var matches = typeRegex.Matches(content);
                foreach (Match match in matches)
                {
                    if (match.Groups[1].Success)
                    {
                        typeNames.Add(match.Groups[1].Value);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[NamespacePath] Error extracting types from {filePath}: {ex.Message}");
            }

            return typeNames;
        }

        /// <summary>
        /// Checks if a file contains references to the given namespace or types.
        /// </summary>
        private static AffectedFileInfo CheckFileForReferences(
            string filePath,
            string oldNamespace,
            List<string> typeNames,
            string basePath)
        {
            try
            {
                string content = File.ReadAllText(filePath);
                var references = new List<string>();
                bool hasUsingOfNamespace = false;

                // Check if it has using of the namespace
                var usingMatch = Regex.Match(content, $@"using\s+{Regex.Escape(oldNamespace)}\s*;");
                hasUsingOfNamespace = usingMatch.Success;

                // Search for fully-qualified references to namespace (e.g.: WonderWilds.Core.SomeType)
                // This always needs update
                var fullyQualifiedMatches = Regex.Matches(content, $@"\b{Regex.Escape(oldNamespace)}\.(\w+)");
                foreach (Match match in fullyQualifiedMatches)
                {
                    string typeName = match.Groups[1].Value;
                    // Verify that the type is one of the types from the file we're changing
                    if (typeNames.Contains(typeName))
                    {
                        string reference = $"{oldNamespace}.{typeName}";
                        if (!references.Contains(reference))
                        {
                            references.Add($"FQ: {reference}");
                        }
                    }
                }

                // Only search for type usage if there's using of namespace AND defined types
                if (hasUsingOfNamespace && typeNames.Count > 0)
                {
                    foreach (string typeName in typeNames)
                    {
                        // Search for type usage as complete word
                        // Exclude lines that are namespace or using declarations
                        var lines = content.Split('\n');
                        foreach (var line in lines)
                        {
                            string trimmedLine = line.TrimStart();
                            
                            // Ignore namespace and using lines
                            if (trimmedLine.StartsWith("namespace ") || trimmedLine.StartsWith("using "))
                            {
                                continue;
                            }
                            
                            // Search for type usage
                            var typeUsageRegex = new Regex($@"\b{Regex.Escape(typeName)}\b");
                            if (typeUsageRegex.IsMatch(line))
                            {
                                string refKey = $"Type: {typeName}";
                                if (!references.Contains(refKey))
                                {
                                    references.Add(refKey);
                                }
                                break; // One match is enough
                            }
                        }
                    }
                }

                // Only mark as affected if it actually uses the types
                // Do NOT mark just for having the using
                if (references.Count > 0)
                {
                    // Add using as informative reference only if there are types used
                    if (hasUsingOfNamespace)
                    {
                        references.Insert(0, $"using {oldNamespace}");
                    }
                    
                    string relativePath = GetRelativePath(filePath, basePath);
                    return new AffectedFileInfo
                    {
                        FilePath = filePath,
                        RelativePath = relativePath,
                        AffectedReferences = references
                    };
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[NamespacePath] Error checking {filePath}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets the relative path of a file.
        /// </summary>
        private static string GetRelativePath(string filePath, string basePath)
        {
            string normalizedFile = filePath.Replace("\\", "/");
            string normalizedBase = basePath.Replace("\\", "/");
            
            if (!normalizedBase.EndsWith("/"))
            {
                normalizedBase += "/";
            }

            if (normalizedFile.StartsWith(normalizedBase))
            {
                return normalizedFile.Substring(normalizedBase.Length);
            }

            // Try with Assets/
            string assetsPath = UnityEngine.Application.dataPath.Replace("\\", "/");
            if (normalizedFile.StartsWith(assetsPath))
            {
                return "Assets" + normalizedFile.Substring(assetsPath.Length);
            }

            return filePath;
        }
    }
}
