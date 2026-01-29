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

namespace NamespacePath.Editor
{
    /// <summary>
    /// Stores information about a script and its namespace.
    /// </summary>
    internal sealed class ScriptNamespaceInfo
    {
        public string FilePath { get; set; }
        public string RelativePath { get; set; }
        public string CurrentNamespace { get; set; }
        public string SuggestedNamespace { get; set; }
        public bool IsSelected { get; set; }
        public bool HasNamespaceConflict { get; set; }
        
        /// <summary>
        /// List of types (classes, structs, etc.) defined in the file.
        /// </summary>
        public List<string> TypeNames { get; set; } = new List<string>();
        
        /// <summary>
        /// Indicates if there's a conflict between the last namespace segment and a type in the file.
        /// </summary>
        public bool HasTypeNameConflict { get; set; }
        
        /// <summary>
        /// Warning message for the conflict.
        /// </summary>
        public string ConflictWarning { get; set; }
        
        public bool NeedsChange => !string.IsNullOrEmpty(CurrentNamespace) 
                                   && CurrentNamespace != SuggestedNamespace;
        
        public bool HasNoNamespace => string.IsNullOrEmpty(CurrentNamespace);
        
        public bool HasWarning => HasTypeNameConflict || HasNamespaceConflict;
    }
}
