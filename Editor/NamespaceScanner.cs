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
using System.Text.RegularExpressions;
using UnityEngine;

namespace NamespacePath.Editor
{
    /// <summary>
    /// Scans C# scripts to detect and extract namespace information.
    /// </summary>
    internal static class NamespaceScanner
    {
        private static readonly Regex NamespaceRegex = new Regex(
            @"^\s*namespace\s+([\w\.]+)\s*\{?\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled
        );
        
        private static readonly Regex FileScopedNamespaceRegex = new Regex(
            @"^\s*namespace\s+([\w\.]+)\s*;\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled
        );

        /// <summary>
        /// Scans all C# scripts in the specified folder.
        /// </summary>
        public static List<ScriptNamespaceInfo> ScanScripts(
            string sourceFolderPath,
            string namespacePrefix,
            string rootPathForNamespace)
        {
            var results = new List<ScriptNamespaceInfo>();
            
            if (!Directory.Exists(sourceFolderPath))
            {
                Debug.LogWarning($"[NamespacePath] Folder does not exist: {sourceFolderPath}");
                return results;
            }

            string[] csFiles = Directory.GetFiles(sourceFolderPath, "*.cs", SearchOption.AllDirectories);
            
            foreach (string filePath in csFiles)
            {
                // Normalize path separators
                string normalizedPath = filePath.Replace("\\", "/");
                
                var info = AnalyzeScript(filePath, sourceFolderPath, namespacePrefix, rootPathForNamespace);
                if (info != null)
                {
                    results.Add(info);
                }
            }

            return results;
        }

        /// <summary>
        /// Analyzes an individual script and extracts its namespace information.
        /// </summary>
        private static ScriptNamespaceInfo AnalyzeScript(
            string filePath,
            string sourceFolderPath,
            string namespacePrefix,
            string rootPathForNamespace)
        {
            try
            {
                string content = File.ReadAllText(filePath);
                string currentNamespace = ExtractNamespace(content);
                
                string relativePath = GetRelativePath(filePath, sourceFolderPath);
                string suggestedNamespace = GenerateSuggestedNamespace(
                    filePath, 
                    rootPathForNamespace, 
                    namespacePrefix
                );

                // Extract types from the file
                var typeNames = ExtractTypeNames(content);
                
                // Check for name conflicts
                var (hasConflict, warning) = CheckTypeNameConflict(suggestedNamespace, typeNames);

                return new ScriptNamespaceInfo
                {
                    FilePath = filePath,
                    RelativePath = relativePath,
                    CurrentNamespace = currentNamespace ?? string.Empty,
                    SuggestedNamespace = suggestedNamespace,
                    IsSelected = false,
                    HasNamespaceConflict = false,
                    TypeNames = typeNames,
                    HasTypeNameConflict = hasConflict,
                    ConflictWarning = warning
                };
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NamespacePath] Error analyzing {filePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts the namespace from a C# file content.
        /// </summary>
        public static string ExtractNamespace(string content)
        {
            // Try file-scoped namespace first (C# 10+)
            var fileScopedMatch = FileScopedNamespaceRegex.Match(content);
            if (fileScopedMatch.Success)
            {
                return fileScopedMatch.Groups[1].Value;
            }

            // Then try traditional namespace
            var match = NamespaceRegex.Match(content);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return null;
        }

        /// <summary>
        /// Generates a suggested namespace based on the file path.
        /// </summary>
        public static string GenerateSuggestedNamespace(
            string filePath,
            string rootPath,
            string prefix)
        {
            string normalizedFilePath = filePath.Replace("\\", "/");
            string normalizedRootPath = rootPath.Replace("\\", "/");
            
            if (!normalizedRootPath.EndsWith("/"))
            {
                normalizedRootPath += "/";
            }

            // Get the name of the root (source) folder
            string rootFolderName = Path.GetFileName(normalizedRootPath.TrimEnd('/'));

            // Get the relative path from root
            string relativePath;
            if (normalizedFilePath.StartsWith(normalizedRootPath))
            {
                relativePath = normalizedFilePath.Substring(normalizedRootPath.Length);
            }
            else
            {
                // If the file is not under root, use the file name
                relativePath = Path.GetFileNameWithoutExtension(filePath);
            }

            // Remove the file name, we only want the folder structure
            string directoryPath = Path.GetDirectoryName(relativePath);
            
            // Build the base namespace including the root folder name
            string baseNamespace = string.IsNullOrEmpty(prefix) 
                ? SanitizeNamespacePart(rootFolderName)
                : $"{prefix}.{SanitizeNamespacePart(rootFolderName)}";
            
            if (string.IsNullOrEmpty(directoryPath))
            {
                // File is directly in root
                return baseNamespace;
            }

            // Convert path to namespace (replace separators with dots)
            string namespacePart = directoryPath
                .Replace("\\", ".")
                .Replace("/", ".")
                .Trim('.');

            // Sanitize each part of the namespace
            string[] parts = namespacePart.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = SanitizeNamespacePart(parts[i]);
            }
            namespacePart = string.Join(".", parts);

            // Combine baseNamespace (prefix + rootFolder) with subfolders
            return $"{baseNamespace}.{namespacePart}";
        }

        /// <summary>
        /// Sanitizes a namespace part to be a valid C# identifier.
        /// Dots are preserved as they are valid namespace separators.
        /// </summary>
        private static string SanitizeNamespacePart(string part)
        {
            if (string.IsNullOrEmpty(part))
            {
                return "_";
            }

            // Replace invalid characters, but KEEP dots
            var sanitized = Regex.Replace(part, @"[^a-zA-Z0-9_\.]", "_");
            
            // Ensure it doesn't start with a number
            if (char.IsDigit(sanitized[0]))
            {
                sanitized = "_" + sanitized;
            }

            // Ensure each dot-separated part is valid
            var parts = sanitized.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0 && char.IsDigit(parts[i][0]))
                {
                    parts[i] = "_" + parts[i];
                }
            }
            
            return string.Join(".", parts);
        }

        /// <summary>
        /// Gets the relative path of a file from a base folder.
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

            return filePath;
        }

        /// <summary>
        /// Extracts type names (classes, structs, interfaces, enums) from C# content.
        /// </summary>
        private static List<string> ExtractTypeNames(string content)
        {
            var typeNames = new List<string>();
            
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

            return typeNames;
        }

        /// <summary>
        /// Checks if the last segment of the namespace matches any type in the file.
        /// </summary>
        private static (bool hasConflict, string warning) CheckTypeNameConflict(
            string suggestedNamespace,
            List<string> typeNames)
        {
            if (string.IsNullOrEmpty(suggestedNamespace) || typeNames.Count == 0)
            {
                return (false, null);
            }

            // Get the last segment of the namespace
            string[] nsParts = suggestedNamespace.Split('.');
            string lastSegment = nsParts[nsParts.Length - 1];

            // Check if it matches any type
            foreach (string typeName in typeNames)
            {
                if (string.Equals(lastSegment, typeName, System.StringComparison.Ordinal))
                {
                    string warning = $"⚠️ Conflict: Namespace '{suggestedNamespace}' ends with '{lastSegment}' " +
                                    $"which matches type '{typeName}' in this file.";
                    return (true, warning);
                }
            }

            return (false, null);
        }

        /// <summary>
        /// Updates the conflict check for a script with a new suggested namespace.
        /// </summary>
        public static void UpdateConflictCheck(ScriptNamespaceInfo script)
        {
            var (hasConflict, warning) = CheckTypeNameConflict(script.SuggestedNamespace, script.TypeNames);
            script.HasTypeNameConflict = hasConflict;
            script.ConflictWarning = warning;
        }
    }
}
